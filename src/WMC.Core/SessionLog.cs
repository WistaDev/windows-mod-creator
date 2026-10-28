using System.Text;

namespace WMC.Core;

/// <summary>Writes the session log to logs\wmc-yyyyMMdd-HHmmss.log next to WMC.exe. Never throws.</summary>
public static class SessionLog
{
    private const int KeepFiles = 20;
    private static readonly object Gate = new();
    private static bool _initialized;

    public static string FilePath { get; } =
        Path.Combine(AppPaths.LogsDir, $"wmc-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                if (!_initialized)
                {
                    Directory.CreateDirectory(AppPaths.LogsDir);
                    Prune();
                    _initialized = true;
                }

                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the app (for example, a read-only install folder).
        }
    }

    /// <summary>Keeps only the newest log files.</summary>
    private static void Prune()
    {
        var old = new DirectoryInfo(AppPaths.LogsDir).GetFiles("wmc-*.log")
            .OrderByDescending(f => f.Name)
            .Skip(KeepFiles);

        foreach (var f in old)
        {
            try { f.Delete(); } catch { /* in use or protected: leave it */ }
        }
    }
}
