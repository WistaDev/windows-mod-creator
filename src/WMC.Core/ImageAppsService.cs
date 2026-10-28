namespace WMC.Core;

public enum ImageAppKind { Appx, Capability }

/// <summary>Something found in the mounted image that DISM can remove.</summary>
/// <param name="Id">PackageName (AppX) or capability identity.</param>
/// <param name="Note">Non-empty when other apps depend on it.</param>
public sealed record ImageApp(ImageAppKind Kind, string Id, string DisplayName, string Version, string Note);

public sealed record RemoveResult(int Removed, List<string> Failed);

/// <summary>
/// Lists and removes apps of the mounted image with DISM:
/// provisioned AppX packages (Store apps) and installed Windows capabilities (Paint, Steps Recorder, ...).
/// </summary>
public static class ImageAppsService
{
    private const string CoreNote = "Core component: other apps depend on it";

    private static readonly string[] CorePrefixes =
    {
        "Microsoft.NET.Native.Framework",
        "Microsoft.NET.Native.Runtime",
        "Microsoft.VCLibs",
        "Microsoft.UI.Xaml",
        "Microsoft.Services.Store.Engagement",
        "Microsoft.WindowsStore",
    };

    public static async Task<List<ImageApp>> ScanAsync(
        string mountDir, bool includeCapabilities, IProgress<string> log, CancellationToken ct = default)
    {
        string image = $"/Image:\"{Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir))}\"";
        var apps = new List<ImageApp>();

        log.Report("Listing provisioned AppX packages...");
        var (code, output) = await ProcessRunner.CaptureAsync("dism.exe", $"/English {image} /Get-ProvisionedAppxPackages", null, ct);
        if (code == 0) apps.AddRange(ParseAppx(output));
        else log.Report($"Could not list AppX packages (DISM exit code {code}): {LastLine(output)}");

        if (includeCapabilities)
        {
            log.Report("Listing installed Windows capabilities (this can take a minute)...");
            (code, output) = await ProcessRunner.CaptureAsync("dism.exe", $"/English {image} /Get-Capabilities", null, ct);
            if (code == 0) apps.AddRange(ParseCapabilities(output));
            else log.Report($"Could not list capabilities (DISM exit code {code}): {LastLine(output)}");
        }

        log.Report($"Found {apps.Count} removable item(s).");
        return apps;
    }

    /// <summary>Removes the given items one by one; a failure does not stop the others.</summary>
    public static async Task<RemoveResult> RemoveAsync(
        string mountDir, IReadOnlyList<ImageApp> apps, IProgress<string> log, CancellationToken ct = default)
    {
        string image = $"/Image:\"{Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir))}\"";
        int removed = 0;
        var failed = new List<string>();

        foreach (var app in apps)
        {
            ct.ThrowIfCancellationRequested();

            string operation = app.Kind == ImageAppKind.Appx
                ? $"/Remove-ProvisionedAppxPackage /PackageName:{app.Id}"
                : $"/Remove-Capability /CapabilityName:{app.Id}";

            log.Report($"Removing {app.DisplayName} ...");
            var (code, output) = await ProcessRunner.CaptureAsync("dism.exe", $"/English {image} {operation}", log, ct);

            if (code == 0) removed++;
            else failed.Add($"{app.DisplayName} (DISM exit code {code}: {LastLine(output)})");
        }

        return new RemoveResult(removed, failed);
    }

    // ------------------------------------------------------------------ parsing DISM output

    /// <summary>Parses "dism /Get-ProvisionedAppxPackages" (English): DisplayName / Version / ... / PackageName blocks.</summary>
    public static List<ImageApp> ParseAppx(string output)
    {
        var list = new List<ImageApp>();
        string? name = null, version = null;

        foreach (var raw in output.Split('\n'))
        {
            string line = raw.Trim();

            if (TryField(line, "DisplayName", out var v)) { name = v; version = null; }
            else if (TryField(line, "Version", out v)) version = v;
            else if (TryField(line, "PackageName", out v))
            {
                string display = name ?? v;
                list.Add(new ImageApp(ImageAppKind.Appx, v, display, version ?? "", NoteFor(display)));
                name = null;
                version = null;
            }
        }

        return list.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Parses "dism /Get-Capabilities" (English); keeps only the capabilities whose State is Installed.</summary>
    public static List<ImageApp> ParseCapabilities(string output)
    {
        var list = new List<ImageApp>();
        string? id = null;

        foreach (var raw in output.Split('\n'))
        {
            string line = raw.Trim();

            if (TryField(line, "Capability Identity", out var v)) id = v;
            else if (id is not null && TryField(line, "State", out v))
            {
                if (v.Equals("Installed", StringComparison.OrdinalIgnoreCase))
                {
                    int lastTilde = id.LastIndexOf('~');
                    string display = lastTilde > 0 ? id[..lastTilde].TrimEnd('~') : id;
                    string version = lastTilde > 0 ? id[(lastTilde + 1)..] : "";
                    list.Add(new ImageApp(ImageAppKind.Capability, id, display, version, ""));
                }
                id = null;
            }
        }

        return list.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NoteFor(string name) =>
        CorePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? CoreNote : "";

    /// <summary>True for lines like "Field : value" (spaces around the colon are optional).</summary>
    private static bool TryField(string line, string field, out string value)
    {
        value = "";
        if (!line.StartsWith(field, StringComparison.OrdinalIgnoreCase)) return false;

        string rest = line[field.Length..].TrimStart();
        if (!rest.StartsWith(':')) return false;

        value = rest[1..].Trim();
        return value.Length > 0;
    }

    private static string LastLine(string output) =>
        output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
}
