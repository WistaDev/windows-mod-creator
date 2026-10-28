namespace WMC.Core;

/// <summary>
/// Folders next to WMC.exe:
///   tools\  external programs (oscdimg.exe, wimlib-imagex.exe + libwim-15.dll, ...)
///   logs\   one log file per session
/// </summary>
public static class AppPaths
{
    public static string BaseDir => AppContext.BaseDirectory;
    public static string ToolsDir => Path.Combine(BaseDir, "tools");
    public static string LogsDir => Path.Combine(BaseDir, "logs");

    /// <summary>
    /// Looks for a program in tools\, then in each folder directly under tools\ (e.g. tools\wimlib\),
    /// then next to WMC.exe. Does not look at PATH.
    /// </summary>
    public static string? FindBundledTool(string exeName)
    {
        var dirs = new List<string> { ToolsDir };
        try
        {
            if (Directory.Exists(ToolsDir)) dirs.AddRange(Directory.EnumerateDirectories(ToolsDir));
        }
        catch { /* unreadable tools folder: fall through to the other locations */ }
        dirs.Add(BaseDir);

        return dirs.Select(d => Path.Combine(d, exeName)).FirstOrDefault(File.Exists);
    }

    public static string? FindOnPath(string exeName)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim().Trim('"'), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* invalid PATH entry */ }
        }
        return null;
    }
}
