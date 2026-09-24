using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.Networking;

public sealed record AdapterInfo(
    string Id,
    string Name,
    string Description,
    string Type,
    string Status,
    long SpeedBps,
    IReadOnlyList<string> IPv4,
    IReadOnlyList<string> IPv6,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    bool DnsAutomatic,
    bool HasIPv6,
    string Mac)
{
    public string Display => $"{Name} — {Description}";
    public string SpeedText => Format.LinkSpeed(SpeedBps);
    public string IPv4Text => IPv4.Count == 0 ? Format.NotAvailable : string.Join(", ", IPv4);
    public string IPv6Text => IPv6.Count == 0 ? Format.NotAvailable : string.Join(Environment.NewLine, IPv6);
    public string GatewayText => Gateways.Count == 0 ? Format.NotAvailable : string.Join(", ", Gateways);
    public string DnsText => DnsServers.Count == 0 ? Format.NotAvailable : string.Join(", ", DnsServers);
    public string DnsModeText => DnsAutomatic ? "Automatic (DHCP)" : "Manual";
}

public sealed record LatencyResult(string Label, string Target, int Sent, int Received, double? Min, double? Avg, double? Max, double? Jitter)
{
    public double LossPercent => Sent == 0 ? 0 : (Sent - Received) * 100.0 / Sent;
    public string AvgText => Avg is { } a ? $"{a:F0} ms" : "Timeout";
    public string RangeText => Min is { } mi && Max is { } ma ? $"{mi:F0}–{ma:F0} ms" : Format.NotAvailable;
    public string JitterText => Jitter is { } j ? $"{j:F1} ms" : Format.NotAvailable;
    public string LossText => $"{LossPercent:F0}%";
    public string Tone => Received == 0 ? "danger" : LossPercent > 0 || Avg > 80 ? "warning" : "success";
}

public sealed record DnsPreset(string Name, string Description, string[] IPv4, string[] IPv6)
{
    public bool IsAutomatic => IPv4.Length == 0;
    public string ServersText => IsAutomatic ? "Provided by your router / ISP" : string.Join(", ", IPv4);
}

public interface INetworkService
{
    IReadOnlyList<DnsPreset> DnsPresets { get; }
    IReadOnlyList<AdapterInfo> GetAdapters();
    Task<LatencyResult> MeasureAsync(string label, string target, int count, CancellationToken ct = default);
    OperationResult FlushDns();
    ChangeRequest BuildDnsChange(AdapterInfo adapter, DnsPreset preset);
}

public sealed class NetworkService(ILogService log) : INetworkService
{
    public IReadOnlyList<DnsPreset> DnsPresets { get; } =
    [
        new("Automatic", "Use the DNS servers provided by your router or ISP (Windows default).", [], []),
        new("Cloudflare", "Fast, privacy-focused public resolver.", ["1.1.1.1", "1.0.0.1"], ["2606:4700:4700::1111", "2606:4700:4700::1001"]),
        new("Google", "Google Public DNS.", ["8.8.8.8", "8.8.4.4"], ["2001:4860:4860::8888", "2001:4860:4860::8844"]),
        new("Quad9", "Security-focused resolver that blocks known malicious domains.", ["9.9.9.9", "149.112.112.112"], ["2620:fe::fe", "2620:fe::9"]),
    ];

    public IReadOnlyList<AdapterInfo> GetAdapters()
    {
        var list = new List<AdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                var props = nic.GetIPProperties();
                var v4 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToList();
                var v6 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.Address.ToString()).ToList();
                var gw = props.GatewayAddresses.Select(g => g.Address).Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)).Select(a => a.ToString()).ToList();
                var dns = props.DnsAddresses.Select(d => d.ToString()).ToList();
                var auto = DnsUtil.ReadStaticServers(nic.Id, AddressFamily.InterNetwork).Length == 0;
                bool hasV6;
                try { hasV6 = nic.Supports(NetworkInterfaceComponent.IPv6); } catch { hasV6 = false; }

                list.Add(new AdapterInfo(nic.Id, nic.Name, nic.Description, TypeName(nic.NetworkInterfaceType),
                    nic.OperationalStatus.ToString(), nic.OperationalStatus == OperationalStatus.Up ? nic.Speed : 0,
                    v4, v6, gw, dns, auto, hasV6, FormatMac(nic.GetPhysicalAddress())));
            }
            catch (Exception ex)
            {
                log.Debug($"Skipping adapter {nic.Name}: {ex.Message}");
            }
        }
        return list
            .OrderByDescending(a => a.Status == "Up")
            .ThenByDescending(a => a.Gateways.Count > 0)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<LatencyResult> MeasureAsync(string label, string target, int count, CancellationToken ct = default)
    {
        var times = new List<double>();
        using var ping = new Ping();
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(target, 1000);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            catch (PingException)
            {
                // counted as lost
            }
            await Task.Delay(150, ct);
        }

        double? jitter = null;
        if (times.Count > 1) jitter = times.Zip(times.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        var result = new LatencyResult(label, target, count, times.Count,
            times.Count > 0 ? times.Min() : null, times.Count > 0 ? times.Average() : null, times.Count > 0 ? times.Max() : null, jitter);
        log.Info($"Latency {label} ({target}): avg {result.AvgText}, loss {result.LossText}");
        return result;
    }

    public OperationResult FlushDns()
    {
        const string title = "Flush DNS cache";
        try
        {
            if (NativeMethods.DnsFlushResolverCache())
            {
                log.Success("DNS resolver cache flushed");
                return OperationResult.Ok(title, "The DNS resolver cache was cleared.");
            }
            return OperationResult.Fail(title, "Windows refused to clear the DNS cache.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(title, "Could not clear the DNS cache.", ex.Message);
        }
    }

    public ChangeRequest BuildDnsChange(AdapterInfo adapter, DnsPreset preset)
    {
        var actions = new List<Optimizations.SystemAction>
        {
            new DnsAction { InterfaceId = adapter.Id, AdapterName = adapter.Name, Family = AddressFamily.InterNetwork, Servers = preset.IPv4 },
        };
        if (adapter.HasIPv6 && (preset.IsAutomatic || preset.IPv6.Length > 0))
            actions.Add(new DnsAction { InterfaceId = adapter.Id, AdapterName = adapter.Name, Family = AddressFamily.InterNetworkV6, Servers = preset.IPv6 });

        return new ChangeRequest
        {
            SourceId = "dns:" + adapter.Id,
            Title = $"DNS on {adapter.Name}",
            Category = "Network",
            Before = adapter.DnsAutomatic ? "Automatic" : adapter.DnsText,
            After = preset.IsAutomatic ? "Automatic" : $"{preset.Name} ({preset.ServersText})",
            Actions = actions,
        };
    }

    private static string TypeName(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "Ethernet",
        NetworkInterfaceType.Ppp => "PPP",
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Mobile broadband",
        _ => t.ToString(),
    };

    private static string FormatMac(PhysicalAddress mac)
    {
        var bytes = mac.GetAddressBytes();
        return bytes.Length == 0 ? Format.NotAvailable : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }
}
