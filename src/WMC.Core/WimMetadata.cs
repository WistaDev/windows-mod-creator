using System.Text;
using System.Xml.Linq;

namespace WMC.Core;

/// <summary>Metadata of one image (edition) as stored in the WIM's XML data.</summary>
public sealed class ImageMeta
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string DisplayDescription { get; init; } = "";
    public string Flags { get; init; } = "";
    public string EditionId { get; init; } = "";
}

/// <summary>A pending edit of one image: new values, position in the list (= new index) and extra properties.</summary>
public sealed class ImageEdit
{
    public ImageMeta Original { get; }
    public int OriginalIndex => Original.Index;

    public string Name { get; set; }
    public string Description { get; set; }
    public string DisplayName { get; set; }
    public string DisplayDescription { get; set; }
    public string Flags { get; set; }

    /// <summary>One KEY=VALUE per line, e.g. WINDOWS/PRODUCTNAME=My Windows. Keys follow wimlib's image-property syntax.</summary>
    public string ExtraProperties { get; set; } = "";

    public ImageEdit(ImageMeta original)
    {
        Original = original;
        Name = original.Name;
        Description = original.Description;
        DisplayName = original.DisplayName;
        DisplayDescription = original.DisplayDescription;
        Flags = original.Flags;
    }

    /// <summary>Properties that differ from the original. Empty fields are treated as "leave unchanged".</summary>
    public IEnumerable<KeyValuePair<string, string>> Changes()
    {
        if (Name.Length > 0 && Name != Original.Name) yield return new("NAME", Name);
        if (Description.Length > 0 && Description != Original.Description) yield return new("DESCRIPTION", Description);
        if (DisplayName.Length > 0 && DisplayName != Original.DisplayName) yield return new("DISPLAYNAME", DisplayName);
        if (DisplayDescription.Length > 0 && DisplayDescription != Original.DisplayDescription) yield return new("DISPLAYDESCRIPTION", DisplayDescription);
        if (Flags.Length > 0 && Flags != Original.Flags) yield return new("FLAGS", Flags);

        foreach (var raw in ExtraProperties.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) throw new FormatException($"Invalid property line (expected KEY=VALUE): {line}");
            yield return new(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
    }

    public bool IsDirty => Changes().Any();
}

/// <summary>
/// Reads and edits the image metadata of the project's install.wim / install.esd.
/// Reading is done directly from the WIM header + XML. Property changes use wimlib-imagex;
/// reordering/removing editions (and ESD conversion) rebuild the image with DISM.
/// </summary>
public static class WimMetadata
{
    // ------------------------------------------------------------------ reading

    public static async Task<List<ImageMeta>> ReadAsync(string imageFile, CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(() => Read(imageFile), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Fallback: DISM only exposes index and name.
            var editions = await WimService.GetEditionsAsync(imageFile, null, ct);
            return editions.Select(e => new ImageMeta { Index = e.Index, Name = e.Name }).ToList();
        }
    }

    private static List<ImageMeta> Read(string imageFile)
    {
        using var fs = new FileStream(imageFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var header = new byte[0x60];
        if (fs.Read(header, 0, header.Length) != header.Length || Encoding.ASCII.GetString(header, 0, 5) != "MSWIM")
            throw new InvalidDataException("Not a WIM file.");

        // XmlData resource header: bytes 72..95 = size(7 bytes)+flags(1), offset(8), original size(8).
        ulong sizeAndFlags = BitConverter.ToUInt64(header, 0x48);
        long size = (long)(sizeAndFlags & 0x00FF_FFFF_FFFF_FFFF);
        byte flags = (byte)(sizeAndFlags >> 56);
        long offset = BitConverter.ToInt64(header, 0x50);

        if ((flags & 0x04) != 0) throw new NotSupportedException("The XML data is compressed.");
        if (size <= 0 || size > 64 * 1024 * 1024) throw new InvalidDataException("Unexpected XML size.");

        var buf = new byte[size];
        fs.Position = offset;
        fs.ReadExactly(buf, 0, buf.Length);

        string xml = Encoding.Unicode.GetString(buf).TrimStart('﻿');
        var doc = XDocument.Parse(xml);

        var list = new List<ImageMeta>();
        foreach (var img in doc.Root!.Elements("IMAGE"))
        {
            list.Add(new ImageMeta
            {
                Index = (int?)img.Attribute("INDEX") ?? list.Count + 1,
                Name = (string?)img.Element("NAME") ?? "",
                Description = (string?)img.Element("DESCRIPTION") ?? "",
                DisplayName = (string?)img.Element("DISPLAYNAME") ?? "",
                DisplayDescription = (string?)img.Element("DISPLAYDESCRIPTION") ?? "",
                Flags = (string?)img.Element("FLAGS") ?? "",
                EditionId = (string?)img.Element("WINDOWS")?.Element("EDITIONID") ?? "",
            });
        }
        return list.OrderBy(m => m.Index).ToList();
    }

    // ------------------------------------------------------------------ wimlib

    public const string WimlibHelp =
        "wimlib-imagex.exe not found. Download the Windows binaries from https://wimlib.net/ and extract them " +
        "(wimlib-imagex.exe + libwim-15.dll) into the 'tools\\wimlib' folder next to WMC.exe, or add them to PATH.";

    public static string? FindWimlib() =>
        AppPaths.FindBundledTool("wimlib-imagex.exe") ?? AppPaths.FindOnPath("wimlib-imagex.exe");

    // ------------------------------------------------------------------ applying

    /// <summary>True when applying the edits needs a full rebuild of the image (reorder, remove or ESD source).</summary>
    public static bool NeedsRebuild(string imageFile, IReadOnlyList<ImageEdit> edits, int originalCount) =>
        imageFile.EndsWith(".esd", StringComparison.OrdinalIgnoreCase)
        || edits.Count != originalCount
        || !edits.Select(e => e.OriginalIndex).SequenceEqual(Enumerable.Range(1, edits.Count));

    public static async Task ApplyAsync(
        WmcProject project, IReadOnlyList<ImageEdit> edits, int originalCount,
        IProgress<string> log, CancellationToken ct = default)
    {
        if (project.Mounted) throw new InvalidOperationException("Unmount the image before editing its metadata.");
        if (edits.Count == 0) throw new InvalidOperationException("The image must keep at least one edition.");

        string image = project.ImageFile ?? throw new InvalidOperationException("No WIM/ESD image found in the extracted ISO.");
        bool rebuild = NeedsRebuild(image, edits, originalCount);

        if (rebuild && Path.GetFileName(image).Equals("boot.wim", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Reordering or removing indexes in boot.wim would reset its boot index and break Windows Setup. " +
                "Only property changes are supported for boot.wim.");
        bool needProps = edits.Any(e => e.IsDirty);

        string? wimlib = null;
        if (needProps) wimlib = FindWimlib() ?? throw new FileNotFoundException(WimlibHelp);

        // 1) Reorder / remove / convert ESD: re-export the kept editions in the new order.
        if (rebuild)
        {
            string target = Path.ChangeExtension(image, ".wim");
            if (!target.Equals(image, StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                throw new InvalidOperationException(
                    $"{Path.GetFileName(target)} already exists next to {Path.GetFileName(image)}; select that WIM instead, or remove it.");
            string tmp = Path.Combine(Path.GetDirectoryName(image)!, Path.GetFileNameWithoutExtension(image) + ".rebuild.wim");
            if (File.Exists(tmp)) File.Delete(tmp);

            try
            {
                for (int i = 0; i < edits.Count; i++)
                {
                    log.Report($"Exporting edition {edits[i].OriginalIndex} as index {i + 1} ({i + 1}/{edits.Count})...");
                    await ProcessRunner.RunOrThrowAsync("dism.exe",
                        $"/Export-Image /SourceImageFile:\"{image}\" /SourceIndex:{edits[i].OriginalIndex} " +
                        $"/DestinationImageFile:\"{tmp}\" /Compress:max /CheckIntegrity", log, ct);
                }
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
                throw;
            }

            File.SetAttributes(image, FileAttributes.Normal);
            File.Delete(image);
            File.Move(tmp, target, overwrite: true);
            image = target;
            project.SelectedImage = Path.GetRelativePath(project.IsoDir, target);
            project.Save();
        }

        // 2) Property changes via wimlib-imagex (new index = position in the list).
        for (int i = 0; i < edits.Count; i++)
        {
            var changes = edits[i].Changes().ToList();
            if (changes.Count == 0) continue;

            var args = new StringBuilder($"info \"{image}\" {i + 1}");
            foreach (var (key, value) in changes)
                args.Append($" --image-property \"{key}={value.Replace("\"", "\\\"")}\"");

            log.Report($"Updating index {i + 1}: {string.Join(", ", changes.Select(c => c.Key))}");
            await ProcessRunner.RunOrThrowAsync(wimlib!, args.ToString(), log, ct);
        }

        log.Report("Metadata updated.");
    }
}
