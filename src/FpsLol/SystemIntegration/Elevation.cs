using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace FpsLol.SystemIntegration;

public static class Elevation
{
    public const int ErrorCancelled = 1223;

    public static bool IsElevated { get; } = CheckElevated();

    public static string CurrentUserSid { get; } = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;

    private static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Starts a new elevated instance of FPS.LOL. Returns false if the user declined the UAC prompt.
    /// The caller is responsible for shutting down the current instance.
    /// </summary>
    public static bool TryRestartElevated(string extraArguments = "")
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"--wait-for {Environment.ProcessId} {extraArguments}".Trim(),
        };
        try
        {
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }
}
