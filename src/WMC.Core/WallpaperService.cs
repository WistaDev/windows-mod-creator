namespace WMC.Core;

/// <summary>
/// Replaces the default wallpaper of the mounted image: C:\Windows\Web\Wallpaper\Windows\img0.jpg,
/// optionally also the resolution variants in C:\Windows\Web\4K\Wallpaper\Windows\img0_*.jpg
/// (Windows picks those on most screens).
/// </summary>
public static class WallpaperService
{
    public const string TargetInImage = @"C:\Windows\Web\Wallpaper\Windows\img0.jpg";

    /// <summary>Returns how many files were replaced.</summary>
    public static async Task<int> SetAsync(
        string mountDir, string jpgPath, bool replace4kVariants, IProgress<string> log, CancellationToken ct = default)
    {
        if (!Directory.Exists(Path.Combine(mountDir, "Windows")))
            throw new InvalidOperationException("The image does not look mounted (no Windows folder in the mount folder).");
        ValidateJpeg(jpgPath);

        string mountFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir));

        var targets = new List<string>
        {
            Path.Combine(mountFull, "Windows", "Web", "Wallpaper", "Windows", "img0.jpg"),
        };

        if (replace4kVariants)
        {
            string dir4k = Path.Combine(mountFull, "Windows", "Web", "4K", "Wallpaper", "Windows");
            FileInjector.EnsureNoLinks(mountFull, dir4k);

            var variants = Directory.Exists(dir4k)
                ? Directory.EnumerateFiles(dir4k, "img0_*.jpg").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

            if (variants.Count == 0) log.Report("No 4K variants (img0_*.jpg) found in this image; only img0.jpg is replaced.");
            targets.AddRange(variants);
        }

        // robocopy cannot rename, so each target gets its own correctly named temporary copy.
        string tmpDir = Path.Combine(Path.GetTempPath(), "wmc-wallpaper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                FileInjector.EnsureNoLinks(mountFull, target);

                string name = Path.GetFileName(target);
                string targetDir = Path.GetDirectoryName(target)!;
                string tmpFile = Path.Combine(tmpDir, name);
                File.Copy(jpgPath, tmpFile, overwrite: true);

                if (File.Exists(target))
                {
                    try { File.SetAttributes(target, FileAttributes.Normal); } catch { /* robocopy /B still tries */ }
                }

                log.Report("Replacing " + @"C:\" + Path.GetRelativePath(mountFull, target));
                // Backup mode (/B): works on TrustedInstaller-owned files without changing permissions in the image.
                await ProcessRunner.RunOrThrowAsync("robocopy.exe",
                    $"{FileInjector.Q(tmpDir)} {FileInjector.Q(targetDir)} \"{name}\" /B /IS /IT /COPY:DAT /R:0 /W:0 /NJH /NJS /NP /NDL",
                    log, ct, code => code < 8);

                File.Delete(tmpFile);
            }
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* temp folder: not worth failing for */ }
        }

        log.Report($"Default wallpaper replaced ({targets.Count} file(s)).");
        return targets.Count;
    }

    private static void ValidateJpeg(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Image not found.", path);

        if (!Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .jpg images are accepted (the file becomes img0.jpg).");

        var head = new byte[3];
        using var fs = File.OpenRead(path);
        if (fs.Read(head, 0, head.Length) != head.Length || head[0] != 0xFF || head[1] != 0xD8 || head[2] != 0xFF)
            throw new InvalidOperationException("The file is not a real JPEG (a renamed PNG or BMP will not work). Convert it to JPG first.");
    }
}
