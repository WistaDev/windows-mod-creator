namespace WMC.Core;

/// <summary>
/// Copies files and folders to any location inside the mounted image (e.g. a README.md on the desktop).
/// Copies run through robocopy in backup mode (/B), so protected folders such as System32 work
/// without changing any permissions in the image.
/// </summary>
public static class FileInjector
{
    public static readonly string[] CommonDestinations =
    {
        @"C:\Users\Public\Desktop",
        @"C:\Users\Default\Desktop",
        @"C:\Users\Default\Documents",
        @"C:\ProgramData",
        @"C:\Program Files",
        @"C:\Windows",
        @"C:\Windows\System32",
        @"C:\",
    };

    /// <summary>
    /// Maps a destination folder given as seen inside Windows (C:\Users\Public\Desktop) to its location in the mount folder.
    /// Throws if the result would leave the mount folder.
    /// </summary>
    public static string ResolveDestination(string mountDir, string destInImage, out string inImagePath)
    {
        string mountFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir));

        string d = (destInImage ?? "").Trim().Trim('"').Replace('/', '\\');
        if (d.Length >= 2 && char.IsLetter(d[0]) && d[1] == ':') d = d[2..];   // drop the drive letter
        d = d.Trim('\\');

        char[] invalid = Path.GetInvalidPathChars().Concat(new[] { '*', '?', ':' }).ToArray();
        if (d.IndexOfAny(invalid) >= 0)
            throw new ArgumentException("The destination folder contains invalid characters.");

        string full = Path.GetFullPath(Path.Combine(mountFull, d));
        bool inside = full.Equals(mountFull, StringComparison.OrdinalIgnoreCase)
                      || full.StartsWith(mountFull + "\\", StringComparison.OrdinalIgnoreCase);
        if (!inside)
            throw new InvalidOperationException("The destination must stay inside the image.");

        string rel = Path.GetRelativePath(mountFull, full);
        inImagePath = rel == "." ? @"C:\" : @"C:\" + rel;
        return full;
    }

    public static async Task<List<AddedFile>> AddAsync(
        string mountDir, IReadOnlyList<string> sources, string destDirInImage, bool overwrite,
        IProgress<string> log, CancellationToken ct = default)
    {
        if (sources.Count == 0) throw new ArgumentException("Choose at least one file or folder.");
        if (!Directory.Exists(Path.Combine(mountDir, "Windows")))
            throw new InvalidOperationException("The image does not look mounted (no Windows folder in the mount folder).");

        string mountFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir));
        string destDir = ResolveDestination(mountDir, destDirInImage, out string destDisplay);
        EnsureNoLinks(mountFull, destDir);

        var added = new List<AddedFile>();

        foreach (var raw in sources)
        {
            ct.ThrowIfCancellationRequested();

            string src = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw.Trim().Trim('"')));
            string name = Path.GetFileName(src);
            if (name.Length == 0) throw new ArgumentException("Cannot add a drive root: " + src);

            string target = Path.Combine(destDir, name);
            string inImage = destDisplay.TrimEnd('\\') + "\\" + name;
            EnsureNoLinks(mountFull, target);

            if (File.Exists(src))
            {
                if (Directory.Exists(target))
                    throw new InvalidOperationException($"A folder named '{name}' already exists in {destDisplay}.");

                if (File.Exists(target))
                {
                    if (!overwrite) { log.Report($"Skipped (already exists): {inImage}"); continue; }
                    try { File.SetAttributes(target, FileAttributes.Normal); } catch { /* robocopy /B still tries */ }
                }

                log.Report($"Adding {name} -> {destDisplay}");
                await ProcessRunner.RunOrThrowAsync("robocopy.exe",
                    $"{Q(Path.GetDirectoryName(src)!)} {Q(destDir)} \"{name}\" /B /IS /IT /COPY:DAT /R:0 /W:0 /NJH /NJS /NP /NDL",
                    log, ct, code => code < 8);
            }
            else if (Directory.Exists(src))
            {
                // Never copy a folder that contains the mount folder (it would copy the image into itself).
                if ((mountFull + "\\").StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("That folder contains the mount folder; choose a different folder.");
                if (File.Exists(target))
                    throw new InvalidOperationException($"A file named '{name}' already exists in {destDisplay}.");

                log.Report($"Adding folder {name} -> {destDisplay}");
                string keepExisting = overwrite ? "" : " /XC /XN /XO";
                await ProcessRunner.RunOrThrowAsync("robocopy.exe",
                    $"{Q(src)} {Q(target)} /E /B /IS /IT /COPY:DAT /R:0 /W:0 /NJH /NJS /NP /NDL{keepExisting}",
                    log, ct, code => code < 8);
            }
            else
            {
                throw new FileNotFoundException("Source not found.", src);
            }

            added.Add(new AddedFile { Source = src, Destination = inImage });
        }

        log.Report($"Done: {added.Count} item(s) added.");
        return added;
    }

    /// <summary>Quotes a path for a command line; a trailing backslash would otherwise escape the closing quote.</summary>
    private static string Q(string path) => path.EndsWith('\\') ? $"\"{path}.\"" : $"\"{path}\"";

    /// <summary>
    /// A mounted image contains junctions/symlinks (e.g. "Documents and Settings") whose targets are absolute paths.
    /// Writing through one would land on the HOST's folders, so refuse any path that goes through a link.
    /// </summary>
    private static void EnsureNoLinks(string mountFull, string target)
    {
        string rel = Path.GetRelativePath(mountFull, target);
        if (rel == ".") return;

        string cur = mountFull;
        foreach (var part in rel.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            cur = Path.Combine(cur, part);

            FileAttributes attrs;
            try { attrs = File.GetAttributes(cur); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { break; }   // the rest does not exist yet

            if ((attrs & FileAttributes.ReparsePoint) == 0) continue;

            FileSystemInfo info = (attrs & FileAttributes.Directory) != 0 ? new DirectoryInfo(cur) : new FileInfo(cur);
            if (info.LinkTarget is not null)
                throw new InvalidOperationException(
                    $"'{part}' is a symbolic link or junction in the image. Writing through it could modify folders outside the image. " +
                    "Use the real folder instead (for example C:\\Users instead of C:\\Documents and Settings).");
        }
    }
}
