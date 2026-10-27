using Microsoft.Win32;

namespace WMC.Core;

/// <summary>
/// Carrega um hive do registry da imagem montada (ex.: Windows\System32\config\SOFTWARE)
/// em HKEY_LOCAL_MACHINE\&lt;nome&gt; e descarrega no Dispose.
/// O processo precisa ser 64-bit e estar elevado.
/// </summary>
public sealed class OfflineHive : IDisposable
{
    private readonly string _name;
    private bool _disposed;

    public RegistryKey Root { get; }

    private OfflineHive(string name, RegistryKey root)
    {
        _name = name;
        Root = root;
    }

    public static async Task<OfflineHive> LoadAsync(
        string hiveFile, string name, IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (!File.Exists(hiveFile)) throw new FileNotFoundException("Registry hive not found.", hiveFile);

        await ProcessRunner.RunOrThrowAsync("reg.exe", $"load \"HKLM\\{name}\" \"{hiveFile}\"", log, ct);

        var root = Registry.LocalMachine.OpenSubKey(name, writable: true)
                   ?? throw new InvalidOperationException($"Hive loaded but HKLM\\{name} could not be opened.");
        return new OfflineHive(name, root);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Root.Close();
        Root.Dispose();

        // Handles pendentes impedem o unload; força a coleta e tenta algumas vezes.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", $"unload \"HKLM\\{_name}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit();
            if (p.ExitCode == 0) return;

            Thread.Sleep(500);
        }

        throw new InvalidOperationException(
            $"Could not unload HKLM\\{_name}. Unload it manually with: reg unload HKLM\\{_name}");
    }
}
