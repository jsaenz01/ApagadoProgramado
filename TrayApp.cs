using System.Diagnostics;
using System.Media;

namespace ApagadoProgramado;

static class TrayApp
{
    public static void RunTray()
    {
        // "Local\" => un mutex por sesión de Windows: cada usuario conectado tiene su propio icono.
        using var mutex = new Mutex(true, @"Local\ApagadoProgramado.Tray", out bool isFirst);
        if (!isFirst) return;

        using var ctx = new TrayContext();
        Application.Run(ctx);

        // Esta copia (p. ej. la del Escritorio) cede el relevo a la instalada para dejar libre su .exe.
        // Se libera antes el mutex: la nueva instancia lo necesita para arrancar.
        if (ctx.HandOff)
        {
            mutex.ReleaseMutex();
            Process.Start(new ProcessStartInfo(Installer.TargetExe, "--minimized")
            {
                UseShellExecute = false,
                WorkingDirectory = Installer.TargetDir,
            });
        }
    }

    /// <summary>Panel de configuración: proceso aparte, siempre elevado (administrador).</summary>
    public static int RunPanel()
    {
        if (!Installer.IsAdmin()) return 5;
        using var mutex = new Mutex(true, @"Local\ApagadoProgramado.Panel", out bool isFirst);
        if (!isFirst) return 0;

        using var icon = MakeIcon();
        Application.Run(new ConfigForm(icon));
        return 0;
    }

    public static Icon MakeIcon()
    {
        using var s = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico")!;
        return new Icon(s, SystemInformation.SmallIconSize);
    }
}

sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    readonly ToolStripMenuItem _toggleCancel;
    readonly Icon _icon = TrayApp.MakeIcon();

    WarningForm? _warning;
    DateTime? _warnedFor;
    int _ticks;
    bool _serviceMissing;

    /// <summary>True si el icono debe cerrarse y ser sustituido por el de la copia instalada.</summary>
    public bool HandOff { get; private set; }

    public TrayContext()
    {
        var isAdmin = Installer.IsAdminUser();
        var menu = new ContextMenuStrip();
        if (isAdmin)
            menu.Items.Add("Configurar...", null, (_, _) => Installer.OpenPanel());
        _toggleCancel = new ToolStripMenuItem("Cancelar el próximo apagado", null, (_, _) => ToggleCancel());
        menu.Items.Add(_toggleCancel);

        _tray = new NotifyIcon { Icon = _icon, ContextMenuStrip = menu, Visible = true, Text = "Apagado programado" };
        if (isAdmin) _tray.DoubleClick += (_, _) => Installer.OpenPanel();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    void ToggleCancel()
    {
        var next = Schedule.Next(Store.Load(), DateTime.Now);
        if (next is not { } n) return;
        if (Store.GetCancelled() == n)
        {
            Store.ClearCancel();
            _warnedFor = null;
        }
        else
        {
            Store.Cancel(n);
            _tray.ShowBalloonTip(3000, "Apagado programado", $"Se ha cancelado el apagado de las {n:HH:mm}.", ToolTipIcon.Info);
        }
        Tick();
    }

    void Tick()
    {
        // Sin servicio instalado (o deshabilitado) no hay apagado: ni icono ni avisos. El proceso sigue
        // vivo y oculto para que el icono reaparezca al instalar el servicio sin cerrar sesión.
        if (_ticks++ % 2 == 0)
        {
            var state = Installer.GetServiceState();
            if (state != Installer.ServiceState.NotInstalled && !Installer.RunningFromTarget() && File.Exists(Installer.TargetExe))
            {
                HandOff = true;
                ExitThread();
                return;
            }
            _serviceMissing = state is Installer.ServiceState.NotInstalled or Installer.ServiceState.Disabled;
            _tray.Visible = !_serviceMissing;
        }
        if (_serviceMissing)
        {
            _warning?.Close();
            return;
        }

        var cfg = Store.Load();
        var now = DateTime.Now;
        var next = Schedule.Next(cfg, now);
        bool cancelled = next.HasValue && Store.GetCancelled() == next;

        _toggleCancel.Enabled = next.HasValue;
        _toggleCancel.Text = cancelled ? "Reactivar el próximo apagado" : "Cancelar el próximo apagado";
        var text = next is { } nx
            ? (cancelled ? $"Apagado cancelado ({nx:ddd HH:mm})" : $"Próximo apagado: {nx:ddd HH:mm}")
            : "Apagado programado: sin configurar";
        _tray.Text = text.Length > 63 ? text[..63] : text;

        if (next is { } n && !cancelled && cfg.WarningMinutes > 0
            && now >= n.AddMinutes(-cfg.WarningMinutes) && _warnedFor != n)
        {
            _warnedFor = n;
            ShowWarning(n);
        }
    }

    void ShowWarning(DateTime occurrence)
    {
        SystemSounds.Exclamation.Play();
        _warning = new WarningForm(occurrence, _icon);
        _warning.FormClosed += (_, _) => _warning = null;
        _warning.Cancelled += () =>
            _tray.ShowBalloonTip(3000, "Apagado programado", "Apagado cancelado.", ToolTipIcon.Info);
        _warning.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}

sealed class WarningForm : Form
{
    readonly DateTime _occurrence;
    readonly Label _countdown = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public event Action? Cancelled;

    public WarningForm(DateTime occurrence, Icon icon)
    {
        _occurrence = occurrence;
        Text = "Apagado programado";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 170);

        Controls.Add(new Label
        {
            Text = $"El equipo se apagará a las {occurrence:HH:mm}.\nSe cerrarán todas las aplicaciones sin guardar.",
            Location = new Point(20, 15), Size = new Size(380, 45),
        });
        _countdown.Font = new Font(Font.FontFamily, 20f, FontStyle.Bold);
        _countdown.Location = new Point(20, 65);
        _countdown.Size = new Size(380, 40);
        Controls.Add(_countdown);

        var cancel = new Button { Text = "Cancelar apagado", Location = new Point(150, 120), Size = new Size(150, 32) };
        var close = new Button { Text = "Cerrar aviso", Location = new Point(310, 120), Size = new Size(95, 32) };
        cancel.Click += (_, _) =>
        {
            Store.Cancel(_occurrence);
            Cancelled?.Invoke();
            Close();
        };
        close.Click += (_, _) => Close();
        Controls.AddRange([cancel, close]);
        AcceptButton = cancel;

        _timer.Tick += (_, _) => Refresh_();
        _timer.Start();
        Refresh_();
    }

    void Refresh_()
    {
        var now = DateTime.Now;
        var remaining = _occurrence - now;
        var cfg = Store.Load();
        // Se cierra solo si pasó la hora, otro usuario canceló o se cambió la configuración.
        if (remaining <= TimeSpan.Zero || Store.GetCancelled() == _occurrence
            || Schedule.Next(cfg, now) != _occurrence)
        {
            Close();
            return;
        }
        _countdown.Text = $"Apagando en {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
