using System.Text.Json;
using System.Text.Json.Serialization;

namespace WMC.Core;

/// <summary>Um app adicionado à imagem (instalado em Program Files e registrado no registry offline).</summary>
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
    /// <summary>Caminho do executável principal, relativo à pasta do app.</summary>
    public string MainExe { get; set; } = "";
    public bool StartMenuShortcut { get; set; } = true;
    public bool DesktopShortcut { get; set; }
    /// <summary>Preenchido na instalação: caminho dentro da imagem (ex.: C:\Program Files\MeuApp).</summary>
    public string InstalledPath { get; set; } = "";
}

public record WimEdition(int Index, string Name);

/// <summary>Estado de um projeto WMC. Fica salvo em wmc-project.json dentro da pasta de trabalho.</summary>
public class WmcProject
{
    private const string FileName = "wmc-project.json";

    public string WorkDir { get; set; } = "";
    public string IsoPath { get; set; } = "";
    public string VolumeLabel { get; set; } = "WMC_WINDOWS";
    public bool Extracted { get; set; }
    public bool Mounted { get; set; }
    public int ImageIndex { get; set; }
    public List<AppInfo> Apps { get; set; } = new();

    [JsonIgnore] public string IsoDir => Path.Combine(WorkDir, "iso");
    [JsonIgnore] public string MountDir => Path.Combine(WorkDir, "mount");
    [JsonIgnore] public string SourcesDir => Path.Combine(IsoDir, "sources");

    /// <summary>install.wim (preferido) ou install.esd, se existir.</summary>
    [JsonIgnore]
    public string? ImageFile
    {
        get
        {
            var wim = Path.Combine(SourcesDir, "install.wim");
            if (File.Exists(wim)) return wim;
            var esd = Path.Combine(SourcesDir, "install.esd");
            return File.Exists(esd) ? esd : null;
        }
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
