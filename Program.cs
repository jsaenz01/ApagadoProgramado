using System.Diagnostics;
using System.ServiceProcess;

namespace ApagadoProgramado;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var mode = args.FirstOrDefault()?.ToLowerInvariant();
        switch (mode)
        {
            case "--service":
                ServiceBase.Run(new ShutdownService());
                return 0;
            case "--panel":
                ApplicationConfiguration.Initialize();
                return TrayApp.RunPanel();
            case "--install":
            {
                ApplicationConfiguration.Initialize();
                bool wasAdmin = Installer.IsAdmin();
                int rc = Installer.Install();
                // Si venimos de una instancia sin privilegios, abrimos la app ya instalada con el usuario actual.
                if (rc == 0 && !wasAdmin && File.Exists(Installer.TargetExe))
                    Process.Start(new ProcessStartInfo(Installer.TargetExe, "--minimized") { UseShellExecute = true });
                return rc;
            }
            case "--uninstall":
                ApplicationConfiguration.Initialize();
                return Installer.Uninstall(deleteProgram: !args.Contains("--keep-files", StringComparer.OrdinalIgnoreCase));
            default:
                // "--minimized" es el inicio automático al iniciar sesión: solo el icono, sin panel.
                ApplicationConfiguration.Initialize();
                if (mode != "--minimized")
                {
                    // Lanzamiento manual: abre el panel (el UAC salta solo) y deja el icono en la bandeja.
                    if (Installer.IsAdminUser()) Installer.OpenPanel();
                    else MessageBox.Show("Solo los administradores pueden configurar el apagado programado.",
                        "Apagado programado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                TrayApp.RunTray(waitForPrevious: mode == "--minimized");
                return 0;
        }
    }
}
