using System.Text.RegularExpressions;

namespace WMC.Core;

/// <summary>Extracts the contents of a Windows ISO into a working folder.</summary>
public static class IsoService
{
    public static async Task ExtractAsync(
        string isoPath, string destDir, IProgress<string> log, CancellationToken ct = default)
    {
        if (!File.Exists(isoPath)) throw new FileNotFoundException("ISO not found.", isoPath);
        Directory.CreateDirectory(destDir);

        string iso = isoPath.Replace("'", "''");
        log.Report("Mounting ISO...");
        var (code, output) = await ProcessRunner.RunPowerShellAsync(
            $"$i = Mount-DiskImage -ImagePath '{iso}' -PassThru; ($i | Get-Volume).DriveLetter", log, ct);

        string? drive = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .LastOrDefault(l => Regex.IsMatch(l, "^[A-Za-z]$"));

        if (code != 0 || drive is null)
            throw new InvalidOperationException("Could not mount the ISO or find its drive letter.");

        try
        {
            log.Report($"Copying files from {drive}:\\ ...");
            string dest = Path.TrimEndingDirectorySeparator(destDir);
            // robocopy: exit codes 0..7 mean success (with or without files copied).
            await ProcessRunner.RunOrThrowAsync("robocopy.exe",
                $"{drive}:\\ \"{dest}\" /E /NJH /NJS /NP /NDL", log, ct, c => c < 8);
        }
        finally
        {
            await ProcessRunner.RunPowerShellAsync($"Dismount-DiskImage -ImagePath '{iso}'", null, CancellationToken.None);
        }

        // Files coming from read-only media: make them editable.
        foreach (var f in Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* ignore */ }
        }

        log.Report("ISO extracted.");
    }
}
