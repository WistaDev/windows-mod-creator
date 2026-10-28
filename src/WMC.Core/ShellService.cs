using Microsoft.Win32;

namespace WMC.Core;

/// <summary>
/// Reads and changes the default shell of the mounted image:
/// HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon, value "Shell" (offline SOFTWARE hive).
/// </summary>
public static class ShellService
{
    public const string DefaultShell = "explorer.exe";
    private const string WinlogonKey = @"Microsoft\Windows NT\CurrentVersion\Winlogon";

    private static string SoftwareHive(string mountDir) =>
        Path.Combine(mountDir, "Windows", "System32", "config", "SOFTWARE");

    /// <summary>Current value of Winlogon\Shell in the image, or null if it is not set.</summary>
    public static async Task<string?> GetAsync(
        string mountDir, IProgress<string>? log = null, CancellationToken ct = default)
    {
        using var hive = await OfflineHive.LoadAsync(SoftwareHive(mountDir), "WMC_SOFTWARE", log, ct);
        using var key = hive.Root.OpenSubKey(WinlogonKey);
        return key?.GetValue("Shell") as string;
    }

    /// <summary>
    /// Sets Winlogon\Shell. The program must exist in the image: either a bare name resolved in Windows\ or
    /// Windows\System32\ (explorer.exe), or a path as seen inside Windows (C:\Program Files\MyShell\shell.exe).
    /// Arguments are allowed. Returns the value written.
    /// </summary>
    public static async Task<string> SetAsync(
        string mountDir, string command, IProgress<string> log, CancellationToken ct = default)
    {
        command = (command ?? "").Trim();
        if (command.Length == 0) throw new ArgumentException("Enter the shell to use.");
        if (!Directory.Exists(Path.Combine(mountDir, "Windows")))
            throw new InvalidOperationException("The image does not look mounted (no Windows folder in the mount folder).");

        string mountFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir));
        string exe = FindExecutable(mountFull, command)
            ?? throw new FileNotFoundException(
                $"The shell program was not found in the image: {command}\r\n" +
                "Publish it in the Apps tab or add it in the Files tab first, then use its path as seen inside Windows " +
                @"(for example C:\Program Files\MyShell\shell.exe).");

        log.Report($"Shell program found in the image: {exe}");

        string? previous;
        using (var hive = await OfflineHive.LoadAsync(SoftwareHive(mountDir), "WMC_SOFTWARE", log, ct))
        using (var key = hive.Root.CreateSubKey(WinlogonKey))
        {
            previous = key.GetValue("Shell") as string;
            key.SetValue("Shell", command, RegistryValueKind.String);
        }

        log.Report($"Winlogon Shell: {previous ?? "(not set)"} -> {command}");
        return command;
    }

    /// <summary>Finds the executable part of a command line ("path args") in the image. Returns it, or null.</summary>
    private static string? FindExecutable(string mountFull, string command)
    {
        command = ImagePaths.ExpandVariables(command);

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 1)
            {
                string quoted = command[1..end];
                return ImagePaths.Exists(mountFull, quoted) ? quoted : null;
            }
        }

        // Unquoted: try the whole text first, then drop trailing tokens (they are arguments).
        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int n = tokens.Length; n >= 1; n--)
        {
            string candidate = string.Join(' ', tokens.Take(n));
            if (ImagePaths.Exists(mountFull, candidate)) return candidate;
        }
        return null;
    }
}
