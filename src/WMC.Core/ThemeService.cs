using Microsoft.Win32;

namespace WMC.Core;

/// <summary>A registry value in a Themes key whose data is the path of a .theme file.</summary>
public sealed record ThemeValue(string KeyPath, string Name, string Data, RegistryValueKind Kind, bool Exists = true);

/// <summary>A value to change (key path under HKLM\SOFTWARE of the offline hive + value name).</summary>
public sealed record ThemeTarget(string KeyPath, string Name);

/// <summary>
/// Changes the default theme of the mounted image by pointing the values of
/// HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Themes (InstallTheme, ...) to a .theme file found in the image.
/// The native 64-bit key (Microsoft\Windows\CurrentVersion\Themes) is listed too, but only changed if you pick it.
/// </summary>
public static class ThemeService
{
    public const string WowKey = @"WOW6432Node\Microsoft\Windows\CurrentVersion\Themes";
    public const string NativeKey = @"Microsoft\Windows\CurrentVersion\Themes";
    public const string InstallThemeValue = "InstallTheme";

    private static string SoftwareHive(string mountDir) =>
        Path.Combine(mountDir, "Windows", "System32", "config", "SOFTWARE");

    /// <summary>String values of the Themes keys whose data ends with .theme (raw, with %SystemRoot% unexpanded).</summary>
    public static async Task<List<ThemeValue>> GetValuesAsync(
        string mountDir, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var list = new List<ThemeValue>();

        using (var hive = await OfflineHive.LoadAsync(SoftwareHive(mountDir), "WMC_SOFTWARE", log, ct))
        {
            foreach (var path in new[] { WowKey, NativeKey })
            {
                using var key = hive.Root.OpenSubKey(path);
                if (key is null) continue;

                foreach (var name in key.GetValueNames())
                {
                    RegistryValueKind kind = key.GetValueKind(name);
                    if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) continue;

                    // DoNotExpandEnvironmentNames: never expand %SystemRoot% with the HOST's folder.
                    string data = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                    if (data.EndsWith(".theme", StringComparison.OrdinalIgnoreCase))
                        list.Add(new ThemeValue(path, name, data, kind));
                }
            }
        }

        // Always offer the WOW6432Node InstallTheme, even when this image does not have it yet.
        if (!list.Any(v => v.KeyPath == WowKey && v.Name.Equals(InstallThemeValue, StringComparison.OrdinalIgnoreCase)))
            list.Insert(0, new ThemeValue(WowKey, InstallThemeValue, "", RegistryValueKind.ExpandString, Exists: false));

        return list;
    }

    /// <summary>
    /// .theme files in the image (Windows\Resources and its subfolders), as seen inside Windows,
    /// plus any extra in-image paths (for example files added from the Files tab) that exist.
    /// </summary>
    public static List<string> FindThemes(string mountDir, IEnumerable<string>? extraImagePaths = null)
    {
        string mountFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir));
        var found = new List<string>();

        string resources = Path.Combine(mountFull, "Windows", "Resources");
        if (Directory.Exists(resources))
        {
            try
            {
                foreach (var f in EnumerateThemes(resources, 3))
                    found.Add(ImagePaths.ToImagePath(mountFull, f));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* list what we got */ }
        }

        foreach (var p in extraImagePaths ?? Enumerable.Empty<string>())
        {
            if (!p.EndsWith(".theme", StringComparison.OrdinalIgnoreCase)) continue;
            try { if (ImagePaths.Exists(mountFull, p)) found.Add(p); }
            catch (InvalidOperationException) { /* goes through a link: ignore */ }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    private static IEnumerable<string> EnumerateThemes(string dir, int depth)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*.theme")) yield return f;
        if (depth <= 0) yield break;

        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            if (new DirectoryInfo(d).LinkTarget is not null) continue;   // never follow a link out of the image
            foreach (var f in EnumerateThemes(d, depth - 1)) yield return f;
        }
    }

    /// <summary>
    /// Text to store in the registry. REG_EXPAND_SZ values keep using %SystemRoot%;
    /// REG_SZ values (which Windows would not expand) get the literal path.
    /// </summary>
    public static string ToStoredPath(string inImagePath, RegistryValueKind kind)
    {
        string p = ImagePaths.ExpandVariables(inImagePath.Trim().Trim('"')).Replace('/', '\\');
        const string windows = @"C:\Windows\";

        if (kind == RegistryValueKind.ExpandString && p.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            return @"%SystemRoot%\" + p[windows.Length..];
        return p;
    }

    /// <summary>Points the given values at <paramref name="themeInImage"/>. Returns how many values were written.</summary>
    public static async Task<int> SetAsync(
        string mountDir, string themeInImage, IReadOnlyList<ThemeTarget> targets,
        IProgress<string> log, CancellationToken ct = default)
    {
        if (targets.Count == 0) throw new ArgumentException("Check at least one value to change.");
        themeInImage = (themeInImage ?? "").Trim();
        if (!themeInImage.EndsWith(".theme", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a .theme file.");
        if (!Directory.Exists(Path.Combine(mountDir, "Windows")))
            throw new InvalidOperationException("The image does not look mounted (no Windows folder in the mount folder).");

        string mountFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir));
        if (!ImagePaths.Exists(mountFull, themeInImage))
            throw new FileNotFoundException("The theme file was not found in the image: " + themeInImage);

        int changed = 0;
        using (var hive = await OfflineHive.LoadAsync(SoftwareHive(mountDir), "WMC_SOFTWARE", log, ct))
        {
            foreach (var t in targets)
            {
                ct.ThrowIfCancellationRequested();

                using var key = hive.Root.CreateSubKey(t.KeyPath);
                string? oldRaw = key.GetValue(t.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                RegistryValueKind kind = oldRaw is null ? RegistryValueKind.ExpandString : key.GetValueKind(t.Name);

                string stored = ToStoredPath(themeInImage, kind);
                key.SetValue(t.Name, stored, kind);
                log.Report($@"HKLM\SOFTWARE\{t.KeyPath}\{t.Name}: {oldRaw ?? "(not set)"} -> {stored}");
                changed++;
            }
        }

        log.Report($"Theme set in {changed} value(s).");
        return changed;
    }
}
