using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WMC.Core;

/// <summary>Which hive of the image a key of a .reg file ends up in.</summary>
public enum RegTarget { Software, System, Default, DefaultUser }

public sealed record RegText(string[] Lines, bool Unicode);

public sealed record RegAnalysis(
    int Keys, IReadOnlyDictionary<RegTarget, int> Targets, int Skipped, IReadOnlyList<string> SkippedKeys)
{
    public string TargetsText => Targets.Count == 0
        ? "-"
        : string.Join(", ", Targets.OrderBy(t => t.Key).Select(t => $"{RegFile.DisplayName(t.Key)} x{t.Value}"));
}

/// <summary>
/// Reads .reg files and rewrites their key paths so they can be imported into hives of the mounted image
/// that are loaded under HKEY_LOCAL_MACHINE\WMC_*:
///   HKLM\SOFTWARE -> SOFTWARE hive        HKLM\SYSTEM (CurrentControlSet = the image's control set) -> SYSTEM hive
///   HKCR          -> SOFTWARE\Classes     HKCU -> default user profile (Users\Default\NTUSER.DAT)
///   HKU\.DEFAULT  -> DEFAULT hive (system account)
/// Anything else (SAM, SECURITY, HARDWARE, other users) is skipped together with its values.
/// </summary>
public static class RegFile
{
    public const string SoftwareHive = "WMC_SOFTWARE";
    public const string SystemHive = "WMC_SYSTEM";
    public const string DefaultHive = "WMC_DEFAULT";
    public const string UserHive = "WMC_NTUSER";

    private static readonly Encoding Ansi = GetAnsi();

    private static readonly Regex Header = new(
        @"^\[(-?)\s*(HKEY_LOCAL_MACHINE|HKLM|HKEY_CURRENT_USER|HKCU|HKEY_USERS|HKU|HKEY_CLASSES_ROOT|HKCR)(?:\\(.*))?\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Encoding GetAnsi()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch
        {
            return Encoding.Latin1;
        }
    }

    public static string HiveName(RegTarget t) => t switch
    {
        RegTarget.Software => SoftwareHive,
        RegTarget.System => SystemHive,
        RegTarget.Default => DefaultHive,
        _ => UserHive,
    };

    public static string DisplayName(RegTarget t) => t switch
    {
        RegTarget.Software => "SOFTWARE",
        RegTarget.System => "SYSTEM",
        RegTarget.Default => "DEFAULT",
        _ => "Default user",
    };

    // ------------------------------------------------------------------ reading / writing

    /// <summary>Reads a .reg file (UTF-16 "Version 5.00" or ANSI "REGEDIT4"). Throws if it is not a .reg file.</summary>
    public static RegText Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        string text;
        bool unicode;

        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(b, 2, b.Length - 2);
            unicode = true;
        }
        else if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(b, 3, b.Length - 3);
            unicode = true;   // written back as UTF-16, which reg.exe understands
        }
        else
        {
            text = Ansi.GetString(b);
            unicode = false;
        }

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        string first = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        if (!first.Equals("Windows Registry Editor Version 5.00", StringComparison.OrdinalIgnoreCase)
            && !first.Equals("REGEDIT4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This does not look like a .reg file (the first line should be \"Windows Registry Editor Version 5.00\" or \"REGEDIT4\").");

        return new RegText(lines, unicode);
    }

    public static void Write(string path, IEnumerable<string> lines, bool unicode)
    {
        string text = string.Join("\r\n", lines) + "\r\n";
        File.WriteAllText(path, text, unicode ? new UnicodeEncoding(false, true) : Ansi);   // UTF-16 LE with BOM, or ANSI
    }

    // ------------------------------------------------------------------ rewriting

    /// <summary>
    /// Rewrites every key header to the loaded hive of the image and drops keys that cannot be imported
    /// (with the value lines that follow them). Also used to analyze a file before importing.
    /// </summary>
    public static (List<string> Lines, RegAnalysis Analysis) Transform(
        IEnumerable<string> lines, string controlSet = "ControlSet001")
    {
        var output = new List<string>();
        var counts = new Dictionary<RegTarget, int>();
        var skippedKeys = new List<string>();
        int keys = 0, skipped = 0;
        bool skipping = false;

        foreach (var raw in lines)
        {
            string t = raw.Trim();

            if (t.StartsWith('['))
            {
                var m = Header.Match(t);
                if (m.Success && TryMap(m.Groups[2].Value, m.Groups[3].Value, m.Groups[1].Value == "-", controlSet,
                                         out var target, out var path))
                {
                    keys++;
                    counts[target] = counts.GetValueOrDefault(target) + 1;
                    output.Add($@"[{m.Groups[1].Value}HKEY_LOCAL_MACHINE\{path}]");
                    skipping = false;
                    continue;
                }

                skipped++;
                if (skippedKeys.Count < 5) skippedKeys.Add(t);
                skipping = true;
                continue;
            }

            if (skipping && t.Length > 0) continue;   // value lines of a skipped key
            output.Add(raw);
        }

        return (output, new RegAnalysis(keys, counts, skipped, skippedKeys));
    }

    private static bool TryMap(
        string root, string rest, bool delete, string controlSet, out RegTarget target, out string path)
    {
        target = default;
        path = "";
        rest = rest.TrimEnd('\\');

        switch (root.ToUpperInvariant())
        {
            case "HKEY_LOCAL_MACHINE":
            case "HKLM":
            {
                string first = Split(rest, out string tail);
                if (first.Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase))
                {
                    target = RegTarget.Software;
                    path = Join(SoftwareHive, tail);
                }
                else if (first.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase))
                {
                    string second = Split(tail, out string tail2);
                    if (second.Equals("CurrentControlSet", StringComparison.OrdinalIgnoreCase)) second = controlSet;
                    target = RegTarget.System;
                    path = Join(SystemHive, Join(second, tail2));
                }
                else return false;
                break;
            }

            case "HKEY_CURRENT_USER":
            case "HKCU":
                target = RegTarget.DefaultUser;
                path = Join(UserHive, rest);
                break;

            case "HKEY_USERS":
            case "HKU":
            {
                string first = Split(rest, out string tail);
                if (!first.Equals(".DEFAULT", StringComparison.OrdinalIgnoreCase)) return false;
                target = RegTarget.Default;
                path = Join(DefaultHive, tail);
                break;
            }

            case "HKEY_CLASSES_ROOT":
            case "HKCR":
                target = RegTarget.Software;
                path = Join(SoftwareHive, Join("Classes", rest));
                break;

            default:
                return false;
        }

        // Never delete a whole hive (or the whole Classes tree) from a .reg file.
        if (delete && IsHiveRoot(path)) return false;
        return true;
    }

    private static bool IsHiveRoot(string path) =>
        path is SoftwareHive or SystemHive or DefaultHive or UserHive
        || path.Equals(SoftwareHive + @"\Classes", StringComparison.OrdinalIgnoreCase);

    private static string Split(string s, out string tail)
    {
        int i = s.IndexOf('\\');
        tail = i < 0 ? "" : s[(i + 1)..];
        return i < 0 ? s : s[..i];
    }

    private static string Join(string a, string b) =>
        a.Length == 0 ? b : b.Length == 0 ? a : a + "\\" + b;
}
