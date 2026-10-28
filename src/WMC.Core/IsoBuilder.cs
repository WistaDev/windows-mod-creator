using System.Text.RegularExpressions;

namespace WMC.Core;

/// <summary>Builds the bootable ISO (BIOS + UEFI) with oscdimg from the Windows ADK.</summary>
public static class IsoBuilder
{
    public static async Task<string?> FindOscdimgAsync()
    {
        // 1) A copy placed in the tools folder next to WMC.exe wins (portable, no ADK needed).
        string? bundled = AppPaths.FindBundledTool("oscdimg.exe");
        if (bundled is not null) return bundled;

        // 2) The installed Windows ADK.
        string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        string adk = Path.Combine(pf86, "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools");

        foreach (var arch in new[] { "amd64", "x86", "arm64" })
        {
            string candidate = Path.Combine(adk, arch, "Oscdimg", "oscdimg.exe");
            if (File.Exists(candidate)) return candidate;
        }

        var (code, output) = await ProcessRunner.CaptureAsync("where.exe", "oscdimg.exe");
        if (code == 0)
        {
            var first = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(File.Exists);
            if (first is not null) return first;
        }
        return null;
    }

    public static async Task BuildAsync(
        string isoDir, string outputIso, string volumeLabel, IProgress<string> log, CancellationToken ct = default)
    {
        string oscdimg = await FindOscdimgAsync() ?? throw new FileNotFoundException(
            "oscdimg.exe not found. Copy oscdimg.exe into the 'tools' folder next to WMC.exe (from a PC with the Windows ADK), " +
            "or install the ADK with Deployment Tools: https://learn.microsoft.com/windows-hardware/get-started/adk-install");

        string etfsboot = Path.Combine(isoDir, "boot", "etfsboot.com");
        string efisys = Path.Combine(isoDir, "efi", "microsoft", "boot", "efisys.bin");
        if (!File.Exists(efisys))
            throw new FileNotFoundException("efisys.bin not found in the image (efi\\microsoft\\boot).", efisys);

        // Two boot entries (BIOS + UEFI) when etfsboot.com exists; UEFI only otherwise.
        string bootData = File.Exists(etfsboot)
            ? $"-bootdata:2#p0,e,b\"{etfsboot}\"#pEF,e,b\"{efisys}\""
            : $"-bootdata:1#pEF,e,b\"{efisys}\"";

        string label = Regex.Replace(volumeLabel, @"[^A-Za-z0-9_]", "_");
        if (label.Length == 0) label = "WMC_WINDOWS";
        if (label.Length > 32) label = label[..32];

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputIso))!);
        if (File.Exists(outputIso)) File.Delete(outputIso);

        log.Report("Building ISO...");
        await ProcessRunner.RunOrThrowAsync(oscdimg,
            $"-m -o -u2 -udfver102 -l{label} {bootData} \"{Path.TrimEndingDirectorySeparator(isoDir)}\" \"{outputIso}\"",
            log, ct);

        log.Report("ISO ready: " + outputIso);
    }
}
