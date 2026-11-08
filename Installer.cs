using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace ApagadoProgramado;

public static class Installer
{
    const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "ApagadoProgramado";

    // El servicio corre como SYSTEM: el ejecutable debe estar en una carpeta que solo administradores puedan modificar.
    public static string TargetDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ApagadoProgramado");
    public static string TargetExe => Path.Combine(TargetDir, "ApagadoProgramado.exe");

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// True si el usuario es administrador, aunque su proceso no esté elevado (UAC):
    /// un administrador con el token filtrado tiene TokenElevationType = Limited.
    /// </summary>
    public static bool IsAdminUser()
    {
        if (IsAdmin()) return true;
        using var id = WindowsIdentity.GetCurrent();
        return GetTokenInformation(id.AccessToken.DangerousGetHandle(), TokenElevationType,
                   out int type, sizeof(int), out _) && type == ElevationLimited;
    }

    const int TokenElevationType = 18;
    const int ElevationLimited = 3;

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

    /// <summary>Estado del servicio, o null si no está instalado.</summary>
    public static ServiceControllerStatus? ServiceStatus()
    {
        try
        {
            using var sc = new ServiceController(ShutdownService.Name);
            return sc.Status;
        }
        catch (InvalidOperationException) { return null; }
    }

    public enum ServiceState { NotInstalled, Running, Stopped, Disabled }

    public static ServiceState GetServiceState()
    {
        try
        {
            using var sc = new ServiceController(ShutdownService.Name);
            if (sc.StartType == ServiceStartMode.Disabled) return ServiceState.Disabled;
            return sc.Status == ServiceControllerStatus.Running ? ServiceState.Running : ServiceState.Stopped;
        }
        catch (InvalidOperationException) { return ServiceState.NotInstalled; }
    }

    /// <summary>Abre el panel de configuración en un proceso elevado (pide UAC si hace falta), sin esperar a que termine.</summary>
    public static void OpenPanel()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = "--panel",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception) { } // el usuario rechazó el UAC
    }

    public static int RunElevated(string arg)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = arg,
                UseShellExecute = true,
                Verb = "runas",
            })!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception) { return 1223; } // el usuario rechazó el UAC
    }

    /// <summary>Instala el servicio; si se pasa configuración, la guarda (y queda activa) en el mismo paso.</summary>
    public static int Install(Config? cfg = null)
    {
        if (!IsAdmin()) return RunElevated("--install");
        try
        {
            Store.ApplyPermissions();
            if (cfg is not null) Store.Save(cfg);
            StopService();

            var self = Environment.ProcessPath!;
            Directory.CreateDirectory(TargetDir);
            if (!string.Equals(Path.GetFullPath(self), TargetExe, StringComparison.OrdinalIgnoreCase))
                CopyOverRunning(self, TargetExe);

            var binPath = $"\\\"{TargetExe}\\\" --service";
            if (ServiceStatus() is null)
                Sc($"create {ShutdownService.Name} binPath= \"{binPath}\" start= auto DisplayName= \"Apagado programado\"");
            else
                Sc($"config {ShutdownService.Name} binPath= \"{binPath}\" start= auto");
            Sc($"description {ShutdownService.Name} \"Apaga el equipo a la hora configurada\"");
            Sc($"failure {ShutdownService.Name} reset= 86400 actions= restart/5000/restart/5000/restart/5000");

            using (var run = Registry.LocalMachine.CreateSubKey(RunKey))
                run.SetValue(RunValue, $"\"{TargetExe}\" --minimized");

            using var sc = new ServiceController(ShutdownService.Name);
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));

            MessageBox.Show("Instalado correctamente.\nEl servicio apagará el equipo aunque no haya nadie con la sesión iniciada.",
                "Apagado programado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo instalar:\n" + ex.Message, "Apagado programado",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>Elimina el servicio y el inicio automático; con deleteProgram también borra el programa de Program Files.</summary>
    public static int Uninstall(bool deleteProgram = true)
    {
        if (!IsAdmin()) return RunElevated(deleteProgram ? "--uninstall" : "--uninstall --keep-files");
        try
        {
            StopService();
            if (ServiceStatus() is not null) Sc($"delete {ShutdownService.Name}");
            using (var run = Registry.LocalMachine.OpenSubKey(RunKey, writable: true))
                run?.DeleteValue(RunValue, throwOnMissingValue: false);

            bool pending = deleteProgram && !DeleteProgram();
            var msg = "Desinstalado. La configuración se conserva en " + Store.Dir + ".\n" +
                (!deleteProgram ? "El programa se conserva en " + TargetDir + "."
                : pending ? "El programa se borrará de " + TargetDir + " en unos segundos."
                : "El programa se ha borrado de " + TargetDir + ".");
            MessageBox.Show(msg, "Apagado programado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo desinstalar:\n" + ex.Message, "Apagado programado",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>True si este proceso se ejecuta desde la carpeta instalada (no se puede borrar mientras siga abierto).</summary>
    public static bool RunningFromTarget() => IsInTarget(Environment.ProcessPath);

    static bool IsInTarget(string? path) =>
        path is not null && Path.GetFullPath(path).StartsWith(TargetDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Borra la carpeta del programa. Devuelve false si queda pendiente (se está ejecutando desde ella) y se borrará al salir.</summary>
    static bool DeleteProgram()
    {
        // Un directorio de trabajo dentro de la carpeta (nuestro o del cmd que lanzamos) impediría borrarla.
        try { Environment.CurrentDirectory = Environment.SystemDirectory; } catch (IOException) { }

        // El servicio puede tardar en liberar el .exe tras detenerse, así que se reintenta.
        for (int i = 0; i < 10; i++)
        {
            KillProcessesInTarget();
            if (!Directory.Exists(TargetDir)) return true;
            try
            {
                Directory.Delete(TargetDir, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Thread.Sleep(500);
        }

        // Windows no deja borrar un .exe en ejecución: un cmd oculto reintenta hasta que este proceso termine.
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c for /l %i in (1,1,60) do (rmdir /s /q \"{TargetDir}\" 2>nul & if not exist \"{TargetDir}\" exit /b & ping -n 2 127.0.0.1 >nul)",
            WorkingDirectory = Environment.SystemDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        return false;
    }

    /// <summary>Termina todos los procesos (servicio, iconos de bandeja de cualquier sesión, paneles) que ejecutan algo de la carpeta instalada.</summary>
    static void KillProcessesInTarget()
    {
        // No se filtra por nombre exacto: un exe renombrado a ".exe.old" (ver CopyOverRunning) tiene otro nombre de proceso.
        var prefix = Path.GetFileNameWithoutExtension(TargetExe);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId || !p.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || !IsInTarget(p.MainModule?.FileName)) continue;
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
    }

    // Un .exe en ejecución no se puede sobrescribir (los iconos de la bandeja de otras sesiones lo usan), pero sí renombrar.
    static void CopyOverRunning(string source, string target)
    {
        try { File.Copy(source, target, overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var old = target + ".old";
            try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            File.Move(target, old, overwrite: true);
            File.Copy(source, target, overwrite: true);
        }
    }

    static void StopService()
    {
        if (ServiceStatus() is not { } st || st == ServiceControllerStatus.Stopped) return;
        using var sc = new ServiceController(ShutdownService.Name);
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
    }

    static void Sc(string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"sc {args.Split(' ')[0]} falló ({p.ExitCode}): {output.Trim()}");
    }
}
