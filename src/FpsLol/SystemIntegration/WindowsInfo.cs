using FpsLol.Utilities;

namespace FpsLol.SystemIntegration;

/// <summary>Accurate Windows version information (ProductName alone reports "Windows 10" on Windows 11).</summary>
public sealed record WindowsInfo(string Name, string Edition, string DisplayVersion, int Build, int Revision)
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public bool IsWindows11 => Build >= 22000;

    public string FullBuild => $"{Build}.{Revision}";

    public string Short => string.IsNullOrEmpty(DisplayVersion) ? Name : $"{Name} {DisplayVersion}";

    public string Long => $"{Name} {Edition} {DisplayVersion} (build {FullBuild})".Replace("  ", " ");

    public static WindowsInfo Current { get; } = Detect();

    private static WindowsInfo Detect()
    {
        int build = Environment.OSVersion.Version.Build;
        if (int.TryParse(RegistryUtil.ReadString(RegRoot.LocalMachine, CurrentVersionKey, "CurrentBuildNumber"), out var regBuild))
            build = regBuild;

        int ubr = (int)(RegistryUtil.ReadNumber(RegRoot.LocalMachine, CurrentVersionKey, "UBR") ?? 0);
        var display = RegistryUtil.ReadString(RegRoot.LocalMachine, CurrentVersionKey, "DisplayVersion")
                      ?? RegistryUtil.ReadString(RegRoot.LocalMachine, CurrentVersionKey, "ReleaseId") ?? string.Empty;
        var edition = RegistryUtil.ReadString(RegRoot.LocalMachine, CurrentVersionKey, "EditionID") ?? string.Empty;
        edition = edition switch
        {
            "Core" => "Home",
            "CoreSingleLanguage" => "Home",
            "Professional" => "Pro",
            "ProfessionalWorkstation" => "Pro for Workstations",
            "Enterprise" => "Enterprise",
            "Education" => "Education",
            _ => edition,
        };
        var name = build >= 22000 ? "Windows 11" : build >= 10240 ? "Windows 10" : "Windows";
        return new WindowsInfo(name, Format.OrNa(edition) == Format.NotAvailable ? string.Empty : edition, display, build, ubr);
    }
}
