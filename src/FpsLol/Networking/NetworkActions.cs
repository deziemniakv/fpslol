using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FpsLol.Optimizations;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.Networking;

// ============================================================================ DNS

public sealed class DnsAction : SystemAction
{
    public string InterfaceId { get; set; } = string.Empty;   // "{GUID}"
    public string AdapterName { get; set; } = string.Empty;
    public AddressFamily Family { get; set; } = AddressFamily.InterNetwork;

    /// <summary>Empty = obtain DNS automatically (DHCP / router).</summary>
    public string[] Servers { get; set; } = [];

    public override bool RequiresAdmin => true;

    public override string Describe() => Servers.Length == 0
        ? $"Use automatic {FamilyName(Family)} DNS on {AdapterName}"
        : $"Set {FamilyName(Family)} DNS on {AdapterName} to {string.Join(", ", Servers)}";

    public override string? Validate() => DnsUtil.Validate(InterfaceId, Family, Servers);

    public override ActionSnapshot Capture() => new DnsSnapshot
    {
        InterfaceId = InterfaceId,
        AdapterName = AdapterName,
        Family = Family,
        Servers = DnsUtil.ReadStaticServers(InterfaceId, Family),
    };

    public override void Execute() => DnsUtil.Apply(InterfaceId, Family, Servers);

    public override bool Verify() => DnsUtil.ReadStaticServers(InterfaceId, Family).SequenceEqual(Servers, StringComparer.OrdinalIgnoreCase);

    internal static string FamilyName(AddressFamily f) => f == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
}

public sealed class DnsSnapshot : ActionSnapshot
{
    public string InterfaceId { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public AddressFamily Family { get; set; } = AddressFamily.InterNetwork;
    public string[] Servers { get; set; } = [];

    public override bool RequiresAdmin => true;

    public override string Describe() => Servers.Length == 0
        ? $"Restore automatic {DnsAction.FamilyName(Family)} DNS on {AdapterName}"
        : $"Restore {DnsAction.FamilyName(Family)} DNS on {AdapterName} to {string.Join(", ", Servers)}";

    public override string? Validate() => DnsUtil.Validate(InterfaceId, Family, Servers);

    public override void Restore() => DnsUtil.Apply(InterfaceId, Family, Servers);

    public override bool VerifyRestored() => DnsUtil.ReadStaticServers(InterfaceId, Family).SequenceEqual(Servers, StringComparer.OrdinalIgnoreCase);
}

public static class DnsUtil
{
    private static string InterfaceKey(string interfaceId, AddressFamily family) =>
        (family == AddressFamily.InterNetworkV6 ? @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\" : @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\")
        + interfaceId;

    /// <summary>Statically configured DNS servers; empty when DNS is obtained automatically.</summary>
    public static string[] ReadStaticServers(string interfaceId, AddressFamily family)
    {
        var raw = RegistryUtil.ReadString(RegRoot.LocalMachine, InterfaceKey(interfaceId, family), "NameServer");
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string? Validate(string interfaceId, AddressFamily family, string[] servers)
    {
        if (!Guid.TryParse(interfaceId.Trim('{', '}'), out _)) return "Invalid network interface id.";
        if (servers.Length > 2) return "At most two DNS servers are supported.";
        foreach (var s in servers)
        {
            if (!IPAddress.TryParse(s, out var ip) || ip.AddressFamily != family)
                return $"'{s}' is not a valid {DnsAction.FamilyName(family)} address.";
        }
        return null;
    }

    public static void Apply(string interfaceId, AddressFamily family, string[] servers)
    {
        var index = ResolveIndex(interfaceId, family)
                    ?? throw new InvalidOperationException("The network adapter is not available (disconnected or removed).");
        var netsh = ProcessRunner.SystemTool("netsh.exe");
        var proto = family == AddressFamily.InterNetworkV6 ? "ipv6" : "ipv4";

        ProcessOutput output = servers.Length == 0
            ? ProcessRunner.Run(netsh, $"interface {proto} set dnsservers name={index} source=dhcp")
            : ProcessRunner.Run(netsh, $"interface {proto} set dnsservers name={index} source=static address={servers[0]} register=primary validate=no");
        if (!output.Succeeded) throw new InvalidOperationException("netsh failed: " + output.Combined);

        for (int i = 1; i < servers.Length; i++)
        {
            output = ProcessRunner.Run(netsh, $"interface {proto} add dnsservers name={index} address={servers[i]} index={i + 1} validate=no");
            if (!output.Succeeded) throw new InvalidOperationException("netsh failed: " + output.Combined);
        }
    }

    public static int? ResolveIndex(string interfaceId, AddressFamily family)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => string.Equals(n.Id, interfaceId, StringComparison.OrdinalIgnoreCase));
        if (nic is null) return null;
        var props = nic.GetIPProperties();
        try
        {
            return family == AddressFamily.InterNetworkV6 ? props.GetIPv6Properties()?.Index : props.GetIPv4Properties()?.Index;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}

// ============================================================================ TCP receive window auto-tuning

public sealed class TcpAutoTuningAction : SystemAction
{
    public int Level { get; set; } = TcpSettings.Normal;

    public override bool RequiresAdmin => true;

    public override string Describe() => $"Set TCP receive window auto-tuning to {TcpSettings.Name(Level)}";

    public override string? Validate() => Level is >= 0 and <= 4 ? null : "Invalid auto-tuning level.";

    public override ActionSnapshot Capture() => new TcpAutoTuningSnapshot
    {
        Level = TcpSettings.ReadAutoTuningLevel() ?? throw new InvalidOperationException("Could not read the current auto-tuning level."),
    };

    public override void Execute() => TcpSettings.SetAutoTuningLevel(Level);

    public override bool Verify() => TcpSettings.ReadAutoTuningLevel() == Level;
}

public sealed class TcpAutoTuningSnapshot : ActionSnapshot
{
    public int Level { get; set; }

    public override bool RequiresAdmin => true;

    public override string Describe() => $"Restore TCP auto-tuning to {TcpSettings.Name(Level)}";

    public override string? Validate() => Level is >= 0 and <= 4 ? null : "Invalid auto-tuning level.";

    public override void Restore() => TcpSettings.SetAutoTuningLevel(Level);

    public override bool VerifyRestored() => TcpSettings.ReadAutoTuningLevel() == Level;
}

public static class TcpSettings
{
    public const int Normal = 3;
    private static readonly string[] Names = ["disabled", "highlyrestricted", "restricted", "normal", "experimental"];

    public static string Name(int level) => level >= 0 && level < Names.Length ? Names[level] : "unknown";

    /// <summary>Reads AutoTuningLevelLocal of the "Internet" TCP template (locale independent, no admin needed).</summary>
    public static int? ReadAutoTuningLevel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\StandardCimv2",
                "SELECT AutoTuningLevelLocal FROM MSFT_NetTCPSetting WHERE SettingName='Internet'");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    if (mo["AutoTuningLevelLocal"] is { } v) return Convert.ToInt32(v);
                }
            }
        }
        catch
        {
            // Not available (e.g. WMI repository issue) — caller reports N/A.
        }
        return null;
    }

    public static void SetAutoTuningLevel(int level)
    {
        var output = ProcessRunner.Run(ProcessRunner.SystemTool("netsh.exe"), $"interface tcp set global autotuninglevel={Name(level)}");
        if (!output.Succeeded) throw new InvalidOperationException("netsh failed: " + output.Combined);
    }
}

// ============================================================================ Adapter power management

/// <summary>"Allow the computer to turn off this device to save power" for a network adapter.</summary>
public sealed class AdapterPowerAction : SystemAction
{
    public string PnpDeviceId { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public bool AllowPowerOff { get; set; }

    public override bool RequiresAdmin => true;

    public override string Describe() => $"{(AllowPowerOff ? "Allow" : "Prevent")} Windows turning off {AdapterName} to save power";

    public override string? Validate() => AdapterPowerUtil.Validate(PnpDeviceId);

    public override ActionSnapshot Capture() => new AdapterPowerSnapshot
    {
        PnpDeviceId = PnpDeviceId,
        AdapterName = AdapterName,
        AllowPowerOff = AdapterPowerUtil.Read(PnpDeviceId) ?? throw new InvalidOperationException("This adapter does not expose power management settings."),
    };

    public override void Execute() => AdapterPowerUtil.Write(PnpDeviceId, AllowPowerOff);

    public override bool Verify() => AdapterPowerUtil.Read(PnpDeviceId) == AllowPowerOff;
}

public sealed class AdapterPowerSnapshot : ActionSnapshot
{
    public string PnpDeviceId { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public bool AllowPowerOff { get; set; }

    public override bool RequiresAdmin => true;

    public override string Describe() => $"Restore power management on {AdapterName}";

    public override string? Validate() => AdapterPowerUtil.Validate(PnpDeviceId);

    public override void Restore() => AdapterPowerUtil.Write(PnpDeviceId, AllowPowerOff);

    public override bool VerifyRestored() => AdapterPowerUtil.Read(PnpDeviceId) == AllowPowerOff;
}

public static class AdapterPowerUtil
{
    public static string? Validate(string pnpId) =>
        pnpId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) || pnpId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
            ? null
            : "Power management can only be changed for physical PCI/USB network adapters.";

    private static ManagementObject? Find(string pnpId)
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, Enable FROM MSPower_DeviceEnable");
        foreach (ManagementObject mo in searcher.Get())
        {
            var instance = mo["InstanceName"] as string ?? string.Empty;
            if (instance.StartsWith(pnpId, StringComparison.OrdinalIgnoreCase)) return mo;
            mo.Dispose();
        }
        return null;
    }

    public static bool? Read(string pnpId)
    {
        try
        {
            using var mo = Find(pnpId);
            return mo?["Enable"] is bool b ? b : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string pnpId, bool allowPowerOff)
    {
        using var mo = Find(pnpId) ?? throw new InvalidOperationException("This adapter does not expose power management settings.");
        mo["Enable"] = allowPowerOff;
        mo.Put();
    }
}
