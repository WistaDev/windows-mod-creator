using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WMC.Core;

/// <summary>
/// Publishes an app into the mounted image: copies it to Program Files and registers it in the
/// offline registry (Uninstall entry with uninstaller/modify commands, App Paths) plus optional shortcuts.
/// </summary>
public static class AppInstaller
{
    private sealed record Command(string InImagePath, string Args)
    {
        public string Full => Args.Length == 0 ? $"\"{InImagePath}\"" : $"\"{InImagePath}\" {Args}";
    }

    public static async Task InstallAsync(
        string mountDir, AppInfo app, IProgress<string> log, CancellationToken ct = default)
    {
        if (!Directory.Exists(app.SourceDir)) throw new DirectoryNotFoundException(app.SourceDir);

        string exeSource = Path.Combine(app.SourceDir, app.MainExe);
        if (!File.Exists(exeSource)) throw new FileNotFoundException("Main executable not found.", exeSource);

        string safeName = SanitizeFileName(app.Name);
        if (safeName.Length == 0) throw new ArgumentException("Invalid application name.");

        // x64 images have SysWOW64; 32-bit apps go to Program Files (x86).
        bool imageIs64 = Directory.Exists(Path.Combine(mountDir, "Windows", "SysWOW64"));
        bool appIs32 = imageIs64 && PeInfo.Is64Bit(exeSource) == false;
        string programFilesName = appIs32 ? "Program Files (x86)" : "Program Files";

        string targetDirOnHost = Path.Combine(mountDir, programFilesName, safeName);
        string installedPath = $@"C:\{programFilesName}\{safeName}";
        string installedExe = Path.Combine(installedPath, app.MainExe);

        // 1) Copy files
        log.Report($"Copying {app.Name} to {installedPath} ...");
        long bytes = await Task.Run(() => CopyDirectory(app.SourceDir, targetDirOnHost, ct), ct);
        app.InstalledPath = installedPath;

        // 2) Resolve optional icon / uninstaller / modify command against the copied files
        string? icon = ResolveFile(app.IconPath, "icon", app, targetDirOnHost, installedPath, log);
        Command? uninstall = ResolveCommand(app.UninstallerPath, "uninstaller", app, targetDirOnHost, installedPath, log);
        Command? modify = ResolveCommand(app.ModifyPath, "modify command", app, targetDirOnHost, installedPath, log);

        // 3) Offline registry
        log.Report("Registering in the image registry...");
        string softwareHive = Path.Combine(mountDir, "Windows", "System32", "config", "SOFTWARE");
        using (var hive = await OfflineHive.LoadAsync(softwareHive, "WMC_SOFTWARE", log, ct))
        {
            // 32-bit apps on an x64 image live under WOW6432Node.
            string prefix = appIs32 ? @"WOW6432Node\" : "";
            string id = Regex.Replace(safeName, @"\s+", "_");

            using (var un = hive.Root.CreateSubKey(prefix + @"Microsoft\Windows\CurrentVersion\Uninstall\" + id))
            {
                un.SetValue("DisplayName", app.Name);
                un.SetValue("DisplayVersion", app.Version);
                un.SetValue("Publisher", app.Publisher);
                un.SetValue("InstallLocation", installedPath);
                un.SetValue("DisplayIcon", icon ?? installedExe);
                un.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, bytes / 1024), RegistryValueKind.DWord);
                un.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                if (uninstall is not null) un.SetValue("UninstallString", uninstall.Full);
                else un.SetValue("NoRemove", 1, RegistryValueKind.DWord);

                if (modify is not null) un.SetValue("ModifyPath", modify.Full);
                else un.SetValue("NoModify", 1, RegistryValueKind.DWord);
            }

            string exeName = Path.GetFileName(app.MainExe);
            using (var ap = hive.Root.CreateSubKey(prefix + @"Microsoft\Windows\CurrentVersion\App Paths\" + exeName))
            {
                ap.SetValue("", installedExe);
                ap.SetValue("Path", Path.GetDirectoryName(installedExe) ?? installedPath);
            }
        }

        // 4) Shortcuts
        if (app.StartMenuShortcut)
        {
            string dir = Path.Combine(mountDir, "ProgramData", "Microsoft", "Windows", "Start Menu", "Programs");
            CreateShortcut(Path.Combine(dir, safeName + ".lnk"), installedExe, icon, log);
        }
        if (app.DesktopShortcut)
        {
            string dir = Path.Combine(mountDir, "Users", "Public", "Desktop");
            CreateShortcut(Path.Combine(dir, safeName + ".lnk"), installedExe, icon, log);
        }

        log.Report($"{app.Name} published" + (uninstall is null ? " (no uninstaller)." : " (with uninstaller)."));
    }

    // ------------------------------------------------------------------ path resolution

    private static string? ResolveFile(
        string input, string label, AppInfo app, string targetDirOnHost, string installedPath, IProgress<string> log)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string path = input.Trim().Trim('"');
        string? result = MapToImage(path, app, targetDirOnHost, installedPath);
        if (result is null) log.Report($"Warning: {label} not found, ignored: {path}");
        return result;
    }

    private static Command? ResolveCommand(
        string input, string label, AppInfo app, string targetDirOnHost, string installedPath, IProgress<string> log)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var (path, args) = SplitCommand(input.Trim(), app.SourceDir);
        string? result = MapToImage(path, app, targetDirOnHost, installedPath);
        if (result is null)
        {
            log.Report($"Warning: {label} not found, ignored: {path}");
            return null;
        }
        return new Command(result, args);
    }

    /// <summary>Splits "path args" into its parts. Handles quoted paths and unquoted paths followed by /switch or -switch.</summary>
    private static (string Path, string Args) SplitCommand(string input, string sourceDir)
    {
        if (input[0] == '"')
        {
            int end = input.IndexOf('"', 1);
            if (end > 0) return (input[1..end], input[(end + 1)..].Trim());
        }

        if (File.Exists(input) || File.Exists(Path.Combine(sourceDir, input))) return (input, "");

        var m = Regex.Match(input, @"\s[/-]");
        if (m.Success) return (input[..m.Index], input[(m.Index + 1)..].Trim());

        return (input, "");
    }

    /// <summary>
    /// Maps a user-supplied path to its location inside the image (C:\Program Files\...).
    /// Accepts: an in-image path, a path relative to the app folder, or a file elsewhere on this PC
    /// (which is copied into the app folder). Returns null if the file can't be found.
    /// </summary>
    private static string? MapToImage(string path, AppInfo app, string targetDirOnHost, string installedPath)
    {
        // Already an in-image path (C:\Program Files\App\x.exe)
        if (path.StartsWith(installedPath, StringComparison.OrdinalIgnoreCase))
        {
            string rel = path[installedPath.Length..].TrimStart('\\');
            return File.Exists(Path.Combine(targetDirOnHost, rel)) ? path : null;
        }

        // Relative to the app folder
        if (!Path.IsPathRooted(path))
            return File.Exists(Path.Combine(app.SourceDir, path)) ? Path.Combine(installedPath, path) : null;

        // Absolute path on this PC
        if (!File.Exists(path)) return null;

        string full = Path.GetFullPath(path);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(app.SourceDir)) + "\\";
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(installedPath, full[root.Length..]);   // inside the app folder: already copied

        // Outside the app folder: copy it next to the app so it exists in the image.
        string name = Path.GetFileName(full);
        File.Copy(full, Path.Combine(targetDirOnHost, name), overwrite: true);
        return Path.Combine(installedPath, name);
    }

    // ------------------------------------------------------------------ helpers

    private static void CreateShortcut(string lnkPath, string targetInImage, string? icon, IProgress<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic lnk = shell.CreateShortcut(lnkPath);
        lnk.TargetPath = targetInImage;                       // path as seen INSIDE the installed Windows
        lnk.WorkingDirectory = Path.GetDirectoryName(targetInImage) ?? "";
        lnk.IconLocation = (icon ?? targetInImage) + ",0";
        lnk.Save();

        log.Report("Shortcut created: " + lnkPath);
    }

    private static long CopyDirectory(string source, string dest, CancellationToken ct)
    {
        long total = 0;
        Directory.CreateDirectory(dest);

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string target = Path.Combine(dest, Path.GetRelativePath(source, file));
            File.Copy(file, target, overwrite: true);
            total += new FileInfo(target).Length;
        }
        return total;
    }

    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.Trim())
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return sb.ToString().Trim('.', ' ');
    }
}
