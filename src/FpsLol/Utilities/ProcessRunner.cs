using System.Diagnostics;
using System.Text;

namespace FpsLol.Utilities;

public sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
    public string Combined => (StandardOutput + Environment.NewLine + StandardError).Trim();
}

/// <summary>Runs trusted Windows system tools (from System32 only) without showing a console window.</summary>
public static class ProcessRunner
{
    public static string SystemTool(string exe) => Path.Combine(Environment.SystemDirectory, exe);

    public static ProcessOutput Run(string fileName, string arguments, int timeoutMs = 30_000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignored */ }
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {timeoutMs / 1000}s.");
        }
        return new ProcessOutput(process.ExitCode, stdout.Result, stderr.Result);
    }

    public static void OpenShell(string target, string? arguments = null)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? string.Empty })?.Dispose();
    }
}
