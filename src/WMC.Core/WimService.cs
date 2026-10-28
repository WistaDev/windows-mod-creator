using System.Text.RegularExpressions;

namespace WMC.Core;

/// <summary>DISM operations: list editions, mount and unmount the image.</summary>
public static class WimService
{
    public static async Task<List<WimEdition>> GetEditionsAsync(
        string imageFile, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var (code, output) = await ProcessRunner.CaptureAsync(
            "dism.exe", $"/English /Get-WimInfo /WimFile:\"{imageFile}\"", null, ct);
        if (code != 0) throw new InvalidOperationException("DISM failed to read the image:\n" + output);

        var list = new List<WimEdition>();
        int? index = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var m = Regex.Match(line, @"^Index\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) { index = int.Parse(m.Groups[1].Value); continue; }

            m = Regex.Match(line, @"^Name\s*:\s*(.+)$", RegexOptions.IgnoreCase);
            if (m.Success && index is not null)
            {
                list.Add(new WimEdition(index.Value, m.Groups[1].Value.Trim()));
                index = null;
            }
        }
        return list;
    }

    /// <summary>
    /// Prepares (converts ESD to WIM if needed) and mounts the chosen edition at <see cref="WmcProject.MountDir"/>.
    /// </summary>
    public static async Task MountAsync(
        WmcProject project, int index, IProgress<string> log, CancellationToken ct = default)
    {
        string image = project.ImageFile ?? throw new InvalidOperationException("No WIM/ESD image found in the extracted ISO.");

        if (image.EndsWith(".esd", StringComparison.OrdinalIgnoreCase))
        {
            // ESD is read-only: export the chosen edition to an editable WIM.
            string wim = Path.ChangeExtension(image, ".wim");
            if (File.Exists(wim))
                throw new InvalidOperationException(
                    $"Cannot convert {Path.GetFileName(image)}: {Path.GetFileName(wim)} already exists in the same folder. Select that WIM instead, or remove it.");
            log.Report($"{Path.GetFileName(image)} is an ESD: converting the selected edition to {Path.GetFileName(wim)} (this can take a while)...");
            await ProcessRunner.RunOrThrowAsync("dism.exe",
                $"/Export-Image /SourceImageFile:\"{image}\" /SourceIndex:{index} /DestinationImageFile:\"{wim}\" /Compress:max /CheckIntegrity",
                log, ct);
            File.Delete(image);
            image = wim;
            index = 1;
            project.SelectedImage = Path.GetRelativePath(project.IsoDir, wim);
        }

        File.SetAttributes(image, FileAttributes.Normal);

        if (Directory.Exists(project.MountDir) && Directory.EnumerateFileSystemEntries(project.MountDir).Any())
            throw new InvalidOperationException(
                "The mount folder is not empty. If there is a stale mount, run 'dism /Cleanup-Wim' or discard it first.");
        Directory.CreateDirectory(project.MountDir);

        log.Report("Mounting image...");
        await ProcessRunner.RunOrThrowAsync("dism.exe",
            $"/Mount-Image /ImageFile:\"{image}\" /Index:{index} /MountDir:\"{project.MountDir}\"", log, ct);

        project.ImageIndex = index;
        project.Mounted = true;
        project.Save();
    }

    public static async Task UnmountAsync(
        WmcProject project, bool commit, IProgress<string> log, CancellationToken ct = default)
    {
        log.Report(commit ? "Unmounting and committing changes..." : "Unmounting and discarding changes...");
        await ProcessRunner.RunOrThrowAsync("dism.exe",
            $"/Unmount-Image /MountDir:\"{project.MountDir}\" /{(commit ? "Commit" : "Discard")}", log, ct);

        project.Mounted = false;
        project.Save();
    }
}
