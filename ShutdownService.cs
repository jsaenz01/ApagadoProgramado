using System.Diagnostics;
using System.ServiceProcess;

namespace ApagadoProgramado;

public sealed class ShutdownService : ServiceBase
{
    public const string Name = "ApagadoProgramado";

    // Margen para no apagar por una ocurrencia pasada (p. ej. al arrancar el equipo después de la hora).
    static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(60);

    System.Threading.Timer? _timer;
    DateTime? _fired;

    public ShutdownService()
    {
        ServiceName = Name;
        CanStop = true;
    }

    protected override void OnStart(string[] args)
    {
        try { Store.ApplyPermissions(); }
        catch (Exception ex) { Store.Log("No se pudieron aplicar los permisos: " + ex.Message); }
        Store.Log("Servicio iniciado");
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    protected override void OnStop()
    {
        _timer?.Dispose();
        Store.Log("Servicio detenido");
    }

    void Tick()
    {
        try
        {
            var cfg = Store.Load();
            var now = DateTime.Now;
            if (Schedule.Latest(cfg, now) is not { } occ) return;
            if (now - occ > Tolerance || occ < cfg.ModifiedAt || occ == _fired) return;

            _fired = occ;
            if (Store.GetCancelled() == occ)
            {
                Store.Log($"Apagado de {occ:HH:mm} cancelado por el usuario");
                return;
            }

            Store.Log($"Apagando el equipo ({occ:yyyy-MM-dd HH:mm})");
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                Arguments = "/s /f /t 0 /c \"Apagado programado\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Store.Log("Error: " + ex.Message);
        }
    }
}
