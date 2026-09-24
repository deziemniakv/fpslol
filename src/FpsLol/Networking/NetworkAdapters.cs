using System.Management;
using System.Net.NetworkInformation;

namespace FpsLol.Networking;

public sealed record AdapterIdentity(string InterfaceId, string Name, string Description, string? PnpDeviceId);

public static class NetworkAdapters
{
    /// <summary>The physical adapter currently carrying the default route (the one games use).</summary>
    public static AdapterIdentity? GetPrimaryPhysical()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                                or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT)
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any)))
                .ThenByDescending(n => n.Speed)
                .FirstOrDefault();
            return nic is null ? null : new AdapterIdentity(nic.Id, nic.Name, nic.Description, GetPnpDeviceId(nic.Id));
        }
        catch
        {
            return null;
        }
    }

    public static string? GetPnpDeviceId(string interfaceId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE GUID = '" + interfaceId.Replace("'", string.Empty) + "'");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo) return mo["PNPDeviceID"] as string;
            }
        }
        catch
        {
            // ignored — adapter power features will report "not supported"
        }
        return null;
    }
}
