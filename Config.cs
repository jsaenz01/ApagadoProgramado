using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace ApagadoProgramado;

public sealed class Config
{
    public bool Enabled { get; set; }
    public TimeSpan Time { get; set; } = new(23, 0, 0);
    public List<DayOfWeek> Days { get; set; } = Enum.GetValues<DayOfWeek>().ToList();
    public int WarningMinutes { get; set; } = 3;

    // Un apagado solo es válido si la configuración se guardó antes de su hora,
    // así guardar a las 23:00:30 no apaga el equipo de inmediato.
    public DateTime ModifiedAt { get; set; } = DateTime.MinValue;

    public void Normalize()
    {
        WarningMinutes = Math.Clamp(WarningMinutes, 0, 120);
        Days = Days.Distinct().ToList();
        Time = new TimeSpan(Time.Hours, Time.Minutes, 0);
    }
}

public static class Schedule
{
    /// <summary>Primera ocurrencia estrictamente posterior a <paramref name="after"/>.</summary>
    public static DateTime? Next(Config c, DateTime after)
    {
        if (!c.Enabled || c.Days.Count == 0) return null;
        for (int d = 0; d <= 7; d++)
        {
            var date = after.Date.AddDays(d);
            if (!c.Days.Contains(date.DayOfWeek)) continue;
            var occ = date + c.Time;
            if (occ > after) return occ;
        }
        return null;
    }

    /// <summary>Última ocurrencia anterior o igual a <paramref name="atOrBefore"/>.</summary>
    public static DateTime? Latest(Config c, DateTime atOrBefore)
    {
        if (!c.Enabled || c.Days.Count == 0) return null;
        for (int d = 0; d <= 7; d++)
        {
            var date = atOrBefore.Date.AddDays(-d);
            if (!c.Days.Contains(date.DayOfWeek)) continue;
            var occ = date + c.Time;
            if (occ <= atOrBefore) return occ;
        }
        return null;
    }
}

public static class Store
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ApagadoProgramado");

    static string ConfigPath => Path.Combine(Dir, "config.json");
    // La cancelación vive en su propia carpeta: es lo único que pueden escribir los usuarios normales.
    static string CancelDir => Path.Combine(Dir, "cancel");
    static string CancelPath => Path.Combine(CancelDir, "cancel.txt");
    static string LogPath => Path.Combine(Dir, "log.txt");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Solo administradores y SYSTEM pueden modificar la configuración; los usuarios solo la leen.
    /// Debe ejecutarse con privilegios (servicio o instalador).
    /// </summary>
    public static void ApplyPermissions()
    {
        var dir = Directory.CreateDirectory(Dir);
        var sec = NewSecurity();
        Allow(sec, WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute);
        dir.SetAccessControl(sec);

        // Los usuarios pueden crear, modificar y borrar el archivo de cancelación, pero no la carpeta en sí:
        // sin el derecho Delete sobre ella (y sin DeleteSubdirectoriesAndFiles) solo un administrador puede borrarla.
        var cancelDir = Directory.CreateDirectory(CancelDir);
        sec = NewSecurity();
        Allow(sec, WellKnownSidType.BuiltinUsersSid,
            FileSystemRights.ReadAndExecute | FileSystemRights.CreateFiles |
            FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes,
            InheritanceFlags.None, PropagationFlags.None);
        Allow(sec, WellKnownSidType.BuiltinUsersSid, FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly);
        cancelDir.SetAccessControl(sec);
    }

    static DirectorySecurity NewSecurity()
    {
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        Allow(sec, WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl);
        Allow(sec, WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl);
        return sec;
    }

    static void Allow(DirectorySecurity sec, WellKnownSidType sid, FileSystemRights rights,
        InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
        PropagationFlags propagation = PropagationFlags.None) =>
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
            inheritance, propagation, AccessControlType.Allow));

    /// <summary>Recrea la carpeta de cancelación (con sus permisos) si alguien la ha borrado. Solo con privilegios.</summary>
    public static void EnsureCancelDir()
    {
        if (!Directory.Exists(CancelDir)) ApplyPermissions();
    }

    public static Config Load()
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new Config();
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath)) ?? new Config();
            }
            catch (IOException) { Thread.Sleep(50); }
            catch (JsonException) { break; }
        }
        return new Config();
    }

    public static void Save(Config c)
    {
        c.Normalize();
        c.ModifiedAt = DateTime.Now;
        Directory.CreateDirectory(Dir);
        WriteAtomic(ConfigPath, JsonSerializer.Serialize(c, Json));
    }

    public static DateTime? GetCancelled()
    {
        try
        {
            if (!File.Exists(CancelPath)) return null;
            return DateTime.TryParse(File.ReadAllText(CancelPath).Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var d) ? d : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Marca la ocurrencia como cancelada. Devuelve false si no se pudo (p. ej. la carpeta no existe y el usuario no puede crearla).</summary>
    public static bool Cancel(DateTime occurrence)
    {
        try
        {
            // La carpeta la crea el servicio con permisos especiales; si falta, el usuario no puede recrearla.
            if (!Directory.Exists(CancelDir)) return false;
            WriteAtomic(CancelPath, occurrence.ToString("o", CultureInfo.InvariantCulture));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void ClearCancel()
    {
        try { File.Delete(CancelPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > 200_000) fi.Delete();
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static void WriteAtomic(string path, string content)
    {
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
