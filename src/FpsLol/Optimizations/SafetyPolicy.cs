using System.Text.RegularExpressions;
using FpsLol.SystemIntegration;

namespace FpsLol.Optimizations;

/// <summary>
/// Hard allow-list of everything FPS.LOL is permitted to modify. Any action outside this list is refused,
/// both in the normal process and in the elevated helper. FPS.LOL is not a registry cleaner:
/// it never deletes keys, only individual values it created or changed.
/// </summary>
public static partial class SafetyPolicy
{
    private static readonly string[] CurrentUserPrefixes =
    [
        @"Software\Microsoft\GameBar",
        @"System\GameConfigStore",
        @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
        @"Software\Microsoft\DirectX\UserGpuPreferences",
        @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
        @"Software\Microsoft\Windows\CurrentVersion\Search",
        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\",
        @"Software\Microsoft\Windows\CurrentVersion\Run",
    ];

    private static readonly string[] LocalMachinePrefixes =
    [
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\",
    ];

    private static readonly string[] UsersPrefixes =
    [
        // Delivery Optimization user setting lives in the NetworkService hive (same as the Settings app).
        @"S-1-5-20\Software\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Settings",
    ];

    public static readonly IReadOnlySet<string> AllowedServices =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiagTrack" };

    /// <summary>Services FPS.LOL will never touch, even if a future bug tried to.</summary>
    private static readonly HashSet<string> ForbiddenServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "WdNisSvc", "SecurityHealthService", "wscsvc", "mpssvc", "BFE", "wuauserv", "UsoSvc",
        "WaaSMedicSvc", "TrustedInstaller", "CryptSvc", "RpcSs", "EventLog", "Dhcp", "Dnscache", "SamSs",
    };

    [GeneratedRegex(@"^SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\[^\\]+\.exe\\PerfOptions$", RegexOptions.IgnoreCase)]
    private static partial Regex IfeoPerfOptions();

    public static string? CheckRegistry(RegRoot root, string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
            return "Invalid registry path.";

        if (root == RegRoot.LocalMachine && IfeoPerfOptions().IsMatch(path))
        {
            return string.Equals(name, "CpuPriorityClass", StringComparison.OrdinalIgnoreCase)
                ? null
                : "Only CpuPriorityClass may be changed in Image File Execution Options.";
        }

        var prefixes = root switch
        {
            RegRoot.CurrentUser => CurrentUserPrefixes,
            RegRoot.LocalMachine => LocalMachinePrefixes,
            _ => UsersPrefixes,
        };
        return prefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            ? null
            : $"FPS.LOL safety policy does not allow changes to {RegistryUtil.Describe(root, path, name)}.";
    }

    public static string? CheckService(string name)
    {
        if (ForbiddenServices.Contains(name)) return $"Service '{name}' is security- or update-critical and is never modified.";
        return AllowedServices.Contains(name) ? null : $"Service '{name}' is not on the FPS.LOL allow-list.";
    }
}
