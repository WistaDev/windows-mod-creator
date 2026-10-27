using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace WMC.Core;

/// <summary>Executa ferramentas externas (DISM, robocopy, oscdimg, reg, PowerShell) e captura a saída.</summary>
public static class ProcessRunner
{
    private static readonly Encoding OemEncoding = GetOemEncoding();

    private static Encoding GetOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    public static async Task<(int ExitCode, string Output)> CaptureAsync(
        string fileName, string arguments, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = OemEncoding,
            StandardErrorEncoding = OemEncoding,
        };

        var sb = new StringBuilder();
        var gate = new object();

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (gate) sb.AppendLine(e.Data);
            if (!string.IsNullOrWhiteSpace(e.Data)) log?.Report(e.Data);
        }
        p.OutputDataReceived += OnLine;
        p.ErrorDataReceived += OnLine;

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct);
        // Garante que os últimos eventos de saída foram entregues.
        p.WaitForExit();

        return (p.ExitCode, sb.ToString());
    }

    /// <summary>Runs a tool and throws if the exit code is not accepted by <paramref name="isOk"/> (default: only 0).</summary>
    public static async Task RunOrThrowAsync(
        string fileName, string arguments, IProgress<string>? log = null,
        CancellationToken ct = default, Func<int, bool>? isOk = null)
    {
        log?.Report($"> {Path.GetFileName(fileName)} {arguments}");
        var (code, _) = await CaptureAsync(fileName, arguments, log, ct);
        bool ok = isOk?.Invoke(code) ?? code == 0;
        if (!ok)
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} exited with code {code}.");
    }

    public static async Task<(int ExitCode, string Output)> RunPowerShellAsync(
        string script, IProgress<string>? log = null, CancellationToken ct = default)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return await CaptureAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", log, ct);
    }
}
