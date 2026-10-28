using System.Text.Json;
using System.Text.Json.Serialization;

namespace WMC.Core;

/// <summary>An app published into the image (installed under Program Files and registered in the offline registry).</summary>
public class AppInfo
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "1.0";
    /// <summary>Optional icon (.ico/.exe/.dll). Relative to the app folder, or an absolute path on this PC (it gets copied into the app folder).</summary>
    public string IconPath { get; set; } = "";
    /// <summary>Optional uninstall command (path + optional arguments). Same path rules as <see cref="IconPath"/>.</summary>
    public string UninstallerPath { get; set; } = "";
    /// <summary>Optional modify command (path + optional arguments). Same path rules as <see cref="IconPath"/>.</summary>
    public string ModifyPath { get; set; } = "";
    public string SourceDir { get; set; } = "";
    /// <summary>Path of the main executable, relative to the app folder.</summary>
    public string MainExe { get; set; } = "";
    public bool StartMenuShortcut { get; set; } = true;
    public bool DesktopShortcut { get; set; }
    /// <summary>Filled in on install: path inside the image (e.g. C:\Program Files\MyApp).</summary>
    public string InstalledPath { get; set; } = "";
}

/// <summary>A file or folder copied into the image from the Files tab.</summary>
public class AddedFile
{
    public string Source { get; set; } = "";
    /// <summary>Full path as seen inside the installed Windows, e.g. C:\Users\Public\Desktop\README.md.</summary>
    public string Destination { get; set; } = "";
}

public record WimEdition(int Index, string Name);

/// <summary>State of a WMC project. Saved as wmc-project.json inside the working folder.</summary>
public class WmcProject
{
    private const string FileName = "wmc-project.json";

    public string WorkDir { get; set; } = "";
    public string IsoPath { get; set; } = "";
    public string VolumeLabel { get; set; } = "WMC_WINDOWS";
    public bool Extracted { get; set; }
    public bool Mounted { get; set; }
    public int ImageIndex { get; set; }

    /// <summary>The WIM/ESD being modified, relative to <see cref="IsoDir"/> (e.g. sources\install.wim).</summary>
    public string SelectedImage { get; set; } = "";

    public List<AppInfo> Apps { get; set; } = new();
    public List<AddedFile> Files { get; set; } = new();

    [JsonIgnore] public string IsoDir => Path.Combine(WorkDir, "iso");
    [JsonIgnore] public string MountDir => Path.Combine(WorkDir, "mount");

    /// <summary>
    /// Full path of the selected image. Falls back to the first image found
    /// (install.wim, install.esd, boot.wim, then the rest) when nothing valid is selected.
    /// </summary>
    [JsonIgnore]
    public string? ImageFile
    {
        get
        {
            if (SelectedImage.Length > 0)
            {
                var selected = Path.Combine(IsoDir, SelectedImage);
                if (File.Exists(selected)) return selected;
            }

            var first = FindImages().FirstOrDefault();
            return first is null ? null : Path.Combine(IsoDir, first);
        }
    }

    /// <summary>All .wim / .esd files in the extracted ISO, relative to <see cref="IsoDir"/>, most useful first.</summary>
    public List<string> FindImages()
    {
        if (!Directory.Exists(IsoDir)) return new();

        static int Rank(string rel) => Path.GetFileName(rel).ToLowerInvariant() switch
        {
            "install.wim" => 0,
            "install.esd" => 1,
            "boot.wim" => 2,
            _ => 3,
        };

        return Directory.EnumerateFiles(IsoDir, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                string ext = Path.GetExtension(f);
                return ext.Equals(".wim", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".esd", StringComparison.OrdinalIgnoreCase);
            })
            .Select(f => Path.GetRelativePath(IsoDir, f))
            .OrderBy(Rank)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Save()
    {
        Directory.CreateDirectory(WorkDir);
        File.WriteAllText(Path.Combine(WorkDir, FileName),
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static WmcProject? Load(string workDir)
    {
        var file = Path.Combine(workDir, FileName);
        if (!File.Exists(file)) return null;
        var p = JsonSerializer.Deserialize<WmcProject>(File.ReadAllText(file));
        if (p is not null) p.WorkDir = workDir;
        return p;
    }
}
