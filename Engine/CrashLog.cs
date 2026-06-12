namespace ExoProxy.Engine;

// Writes the full exception (type, message, stack trace, inner exceptions)
// to Logs/crash_*.log so a fatal error is never reduced to ex.Message.
public static class CrashLog
{
    public static string Write(Exception ex)
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.WriteAllText(path, $"{DateTime.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}");
            return path;
        }
        catch
        {
            return "(crash log could not be written)";
        }
    }
}
