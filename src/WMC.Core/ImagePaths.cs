namespace WMC.Core;

/// <summary>Looks at paths as seen inside the mounted image (C:\Windows\...) without ever leaving the mount folder.</summary>
internal static class ImagePaths
{
    /// <summary>Expands the few variables that are the same in every image. Never uses the host's environment.</summary>
    public static string ExpandVariables(string s) => s
        .Replace("%SystemRoot%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
        .Replace("%windir%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
        .Replace("%SystemDrive%", "C:", StringComparison.OrdinalIgnoreCase)
        .Replace("%ProgramFiles%", @"C:\Program Files", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if the file exists in the image. A bare name (explorer.exe) is looked up in Windows\ and Windows\System32\.
    /// Throws if the path goes through a link, because that could point outside the image.
    /// </summary>
    public static bool Exists(string mountFull, string path)
    {
        foreach (var host in HostCandidates(mountFull, path))
        {
            FileInjector.EnsureNoLinks(mountFull, host);
            if (File.Exists(host)) return true;
        }
        return false;
    }

    /// <summary>The path as seen inside Windows for something under the mount folder.</summary>
    public static string ToImagePath(string mountFull, string hostPath) =>
        @"C:\" + Path.GetRelativePath(mountFull, hostPath);

    private static IEnumerable<string> HostCandidates(string mountFull, string path)
    {
        path = path.Trim().Trim('"').Replace('/', '\\');
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') path = path[2..];   // drop the drive letter
        bool rooted = path.StartsWith('\\');
        path = path.TrimStart('\\');

        if (path.Length == 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) yield break;

        if (!rooted && !path.Contains('\\'))
        {
            // Bare name: Windows looks for it in the Windows and System32 folders.
            yield return Path.Combine(mountFull, "Windows", path);
            yield return Path.Combine(mountFull, "Windows", "System32", path);
            yield break;
        }

        string full = Path.GetFullPath(Path.Combine(mountFull, path));
        if (full.StartsWith(mountFull + "\\", StringComparison.OrdinalIgnoreCase)) yield return full;
    }
}
