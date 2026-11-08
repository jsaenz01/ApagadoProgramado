using System.Globalization;

namespace ApagadoProgramado;

/// <summary>Panel de configuración. Se ejecuta siempre elevado, así que escribe directamente en la carpeta protegida.</summary>
sealed class ConfigForm : Form
{
    static readonly (DayOfWeek Day, string Label)[] Days =
    [
        (DayOfWeek.Monday, "Lun"), (DayOfWeek.Tuesday, "Mar"), (DayOfWeek.Wednesday, "Mié"),
        (DayOfWeek.Thursday, "Jue"), (DayOfWeek.Friday, "Vie"), (DayOfWeek.Saturday, "Sáb"),
        (DayOfWeek.Sunday, "Dom"),
    ];

    readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    readonly NumericUpDown _warn = new() { Minimum = 0, Maximum = 120, Width = 60 };
    readonly Dictionary<DayOfWeek, CheckBox> _days = new();
    readonly Label _next = new() { AutoSize = true };
    readonly Label _service = new() { AutoSize = true };
    readonly Button _action = new() { Size = new Size(200, 32) };
    readonly CheckBox _deleteProgram = new()
    {
        Text = "Borrar programa de Program Files",
        Checked = true,
        AutoSize = false,
        Size = new Size(140, 34),
        Visible = false,
    };

    Installer.ServiceState _state;
    bool _dirty;

    public ConfigForm(Icon icon)
    {
        Text = "Apagado programado";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(470, 280);

        Controls.Add(new Label { Text = "Hora de apagado:", Location = new Point(20, 24), AutoSize = true });
        _time.Location = new Point(190, 20);
        Controls.Add(_time);

        var daysPanel = new FlowLayoutPanel { Location = new Point(20, 58), Size = new Size(430, 32) };
        foreach (var (day, label) in Days)
        {
            var cb = new CheckBox { Text = label, AutoSize = true };
            cb.CheckedChanged += (_, _) => MarkDirty();
            _days[day] = cb;
            daysPanel.Controls.Add(cb);
        }
        Controls.Add(daysPanel);

        Controls.Add(new Label { Text = "Avisar con antelación (minutos):", Location = new Point(20, 109), AutoSize = true });
        _warn.Location = new Point(250, 105);
        Controls.Add(_warn);
        Controls.Add(new Label { Text = "0 = sin aviso", Location = new Point(320, 109), AutoSize = true, ForeColor = SystemColors.GrayText });

        _next.Location = new Point(20, 150);
        Controls.Add(_next);
        _service.Location = new Point(20, 180);
        Controls.Add(_service);

        _action.Location = new Point(20, 225);
        _action.Click += (_, _) => OnAction();
        Controls.Add(_action);

        _deleteProgram.Location = new Point(228, 224);
        Controls.Add(_deleteProgram);

        var close = new Button { Text = "Cerrar", Location = new Point(375, 225), Size = new Size(80, 32) };
        close.Click += (_, _) => Close();
        Controls.Add(close);
        CancelButton = close;

        var cfg = Store.Load();
        _time.Value = DateTime.Today + cfg.Time;
        _warn.Value = Math.Clamp(cfg.WarningMinutes, 0, 120);
        foreach (var (day, cb) in _days) cb.Checked = cfg.Days.Contains(day);
        _time.ValueChanged += (_, _) => MarkDirty();
        _warn.ValueChanged += (_, _) => MarkDirty();
        _dirty = false;
        RefreshStatus();
    }

    void MarkDirty()
    {
        _dirty = true;
        RefreshStatus();
    }

    void OnAction()
    {
        switch (ActionKind())
        {
            case Kind.Uninstall:
                var deleting = _deleteProgram.Checked;
                var extra = deleting ? "\nTambién se borrará el programa de Program Files (el panel se cerrará)." : "";
                if (MessageBox.Show("Se eliminará el servicio y el equipo dejará de apagarse automáticamente." + extra + "\n¿Desinstalarlo?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var rc = Installer.Uninstall(deleting);
                // Si el panel corre desde Program Files, debe cerrarse para que se pueda borrar su .exe.
                if (rc == 0 && deleting && Installer.RunningFromTarget()) { Close(); return; }
                break;
            case Kind.Save:
                if (BuildConfig() is { } saved && TrySave(saved)) _dirty = false;
                break;
            default:
                if (BuildConfig() is { } cfg && Installer.Install(cfg) == 0) _dirty = false;
                break;
        }
        RefreshStatus();
    }

    // Instalado y sin cambios pendientes => desinstalar; instalado con cambios => guardar; si no => instalar.
    enum Kind { Install, Save, Uninstall }

    Kind ActionKind() =>
        _state != Installer.ServiceState.Running ? Kind.Install
        : _dirty || !Store.Load().Enabled ? Kind.Save
        : Kind.Uninstall;

    Config? BuildConfig()
    {
        var days = _days.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList();
        if (days.Count == 0)
        {
            MessageBox.Show("Selecciona al menos un día.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return new Config
        {
            Enabled = true,
            Time = _time.Value.TimeOfDay,
            Days = days,
            WarningMinutes = (int)_warn.Value,
        };
    }

    bool TrySave(Config cfg)
    {
        try
        {
            Store.Save(cfg);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("No se pudo guardar la configuración:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    void RefreshStatus()
    {
        _state = Installer.GetServiceState();
        bool running = _state == Installer.ServiceState.Running;

        var next = running ? Schedule.Next(Store.Load(), DateTime.Now) : null;
        _next.Text = next is { } n
            ? "Próximo apagado: " + n.ToString("dddd d MMMM, HH:mm", CultureInfo.CurrentCulture)
            : "Sin apagado programado";

        _service.Text = _state switch
        {
            Installer.ServiceState.NotInstalled => "Servicio: no instalado (el equipo no se apagará)",
            Installer.ServiceState.Running => "Servicio: instalado y en ejecución",
            Installer.ServiceState.Disabled => "Servicio: deshabilitado (el equipo no se apagará)",
            _ => "Servicio: detenido (el equipo no se apagará)",
        };

        _action.Text = ActionKind() switch
        {
            Kind.Uninstall => "Desinstalar servicio",
            Kind.Save => "Guardar cambios",
            _ => _state == Installer.ServiceState.NotInstalled ? "Instalar servicio" : "Reparar servicio",
        };
        _deleteProgram.Visible = ActionKind() == Kind.Uninstall;
    }
}
