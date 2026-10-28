namespace WMC.Core;

public sealed record RegImportResult(
    IReadOnlyList<string> ImportedFiles, int KeysImported, int KeysSkipped, string? UnloadWarning);

/// <summary>
/// Imports .reg files into the registry hives of the mounted image. reg.exe can only import into the live registry,
/// so the needed hives are loaded under HKLM\WMC_*, the key paths of the .reg file are rewritten to them
/// (see <see cref="RegFile"/>), the rewritten copy is imported, and the hives are unloaded again.
/// </summary>
public static class RegImporter
{
    private static string HiveFile(string mountFull, RegTarget target) => target switch
    {
        RegTarget.Software => Path.Combine(mountFull, "Windows", "System32", "config", "SOFTWARE"),
        RegTarget.System => Path.Combine(mountFull, "Windows", "System32", "config", "SYSTEM"),
        RegTarget.Default => Path.Combine(mountFull, "Windows", "System32", "config", "DEFAULT"),
        _ => Path.Combine(mountFull, "Users", "Default", "NTUSER.DAT"),
    };

    public static async Task<RegImportResult> ImportAsync(
        string mountDir, IReadOnlyList<string> regFiles, IProgress<string> log, CancellationToken ct = default)
    {
        if (regFiles.Count == 0) throw new ArgumentException("Add at least one .reg file.");
        if (!Directory.Exists(Path.Combine(mountDir, "Windows")))
            throw new InvalidOperationException("The image does not look mounted (no Windows folder in the mount folder).");

        string mountFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountDir));

        // 1) Read and analyze everything first, so a bad file fails before the image is touched.
        var files = new List<(string Path, RegText Text)>();
        var needed = new HashSet<RegTarget>();
        foreach (var f in regFiles)
        {
            RegText text;
            try { text = RegFile.Read(f); }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"{Path.GetFileName(f)}: {e.Message}", e);
            }

            files.Add((f, text));
            foreach (var t in RegFile.Transform(text.Lines).Analysis.Targets.Keys) needed.Add(t);
        }

        if (needed.Count == 0)
            throw new InvalidOperationException(
                "Nothing to import: none of the keys can go into an image. Supported: HKLM\\SOFTWARE, HKLM\\SYSTEM, " +
                "HKCR, HKCU (= default user) and HKU\\.DEFAULT.");

        // 2) Load only the hives that are used, import, and always unload.
        var hives = new Dictionary<RegTarget, OfflineHive>();
        var imported = new List<string>();
        int keys = 0, skipped = 0;
        string? unloadWarning = null;
        string tmp = Path.Combine(Path.GetTempPath(), "wmc-import-" + Guid.NewGuid().ToString("N") + ".reg");

        try
        {
            foreach (var target in needed.OrderBy(t => t))
            {
                ct.ThrowIfCancellationRequested();
                string file = HiveFile(mountFull, target);
                log.Report($"Loading the {RegFile.DisplayName(target)} hive ({Path.GetRelativePath(mountFull, file)})...");
                hives[target] = await OfflineHive.LoadAsync(file, RegFile.HiveName(target), log, ct);
            }

            // CurrentControlSet does not exist offline: use the image's current control set.
            string controlSet = "ControlSet001";
            if (hives.TryGetValue(RegTarget.System, out var system))
            {
                using var select = system.Root.OpenSubKey("Select");
                if (select?.GetValue("Current") is int current && current > 0) controlSet = $"ControlSet{current:000}";
            }

            foreach (var (path, text) in files)
            {
                ct.ThrowIfCancellationRequested();
                var (lines, analysis) = RegFile.Transform(text.Lines, controlSet);
                string name = Path.GetFileName(path);

                if (analysis.Keys == 0)
                {
                    log.Report($"{name}: nothing importable, skipped.");
                    skipped += analysis.Skipped;
                    continue;
                }

                log.Report($"Importing {name}: {analysis.Keys} key(s)" +
                           (analysis.Skipped > 0 ? $", skipping {analysis.Skipped} unsupported" : "") + " ...");
                foreach (var s in analysis.SkippedKeys) log.Report("  skipped: " + s);

                RegFile.Write(tmp, lines, text.Unicode);
                try
                {
                    await ProcessRunner.RunOrThrowAsync("reg.exe", $"import \"{tmp}\" /reg:64", log, ct);
                }
                catch (InvalidOperationException e)
                {
                    throw new InvalidOperationException(
                        $"{name} could not be imported ({e.Message}). Files imported before it: {imported.Count}. See the log.", e);
                }

                imported.Add(path);
                keys += analysis.Keys;
                skipped += analysis.Skipped;
            }
        }
        finally
        {
            // Dispose closes our handles and unloads the hive; a hive that stays loaded blocks the unmount.
            foreach (var h in hives.Values.Reverse())
            {
                try { h.Dispose(); }
                catch (Exception e)
                {
                    unloadWarning ??= e.Message;
                    log.Report("WARNING: " + e.Message);
                }
            }

            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* temp file: not worth failing for */ }
        }

        log.Report($"Imported {imported.Count} file(s), {keys} key(s); {skipped} key(s) skipped.");
        return new RegImportResult(imported, keys, skipped, unloadWarning);
    }
}
