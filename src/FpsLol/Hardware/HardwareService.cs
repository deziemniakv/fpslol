using System.Management;
using System.Net.NetworkInformation;
using FpsLol.Hardware.Native;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.SystemIntegration;

namespace FpsLol.Hardware;

public interface IHardwareService
{
    HardwareInfo? Cached { get; }
    Task<HardwareInfo> GetAsync();
    GpuSchedulingInfo GetGpuScheduling();
}

public sealed record GpuSchedulingInfo(bool Supported, bool EnabledNow);

/// <summary>Static hardware inventory via DXGI, WMI and the registry. Unknown values stay "N/A".</summary>
public sealed class HardwareService(ILogService log) : IHardwareService
{
    private Task<HardwareInfo>? _pending;

    public HardwareInfo? Cached { get; private set; }

    public Task<HardwareInfo> GetAsync() => _pending ??= Task.Run(Detect);

    public GpuSchedulingInfo GetGpuScheduling()
    {
        try
        {
            var readings = D3dkmt.Query(includeScheduling: true);
            var supported = readings.Any(r => r.HwSchSupported == true);
            var enabled = readings.Any(r => r.HwSchEnabled == true);
            return new GpuSchedulingInfo(supported, enabled);
        }
        catch (Exception ex)
        {
            log.Warn("Could not query GPU scheduling capabilities.", ex);
            return new GpuSchedulingInfo(false, false);
        }
    }

    private HardwareInfo Detect()
    {
        log.Info("Hardware detection started");
        var info = new HardwareInfo();

        info = Safe(info, i => DetectCpu(i));
        info = Safe(info, i => DetectGpu(i));
        info = Safe(info, i => DetectMemory(i));
        info = Safe(info, i => DetectBoard(i));
        info = Safe(info, i => DetectStorage(i));
        info = Safe(info, i => i with { NetworkAdapter = PrimaryAdapterName() });

        log.Info($"{WindowsInfo.Current.Long} detected");
        log.Info($"CPU detected: {info.CpuName} ({info.CpuCores}C/{info.CpuThreads}T)");
        foreach (var gpu in info.Gpus) log.Info($"GPU detected: {gpu.Name}");
        log.Info($"RAM detected: {info.RamTotalBytes / (1024 * 1024 * 1024.0):F1} GB");

        Cached = info;
        return info;
    }

    private HardwareInfo Safe(HardwareInfo info, Func<HardwareInfo, HardwareInfo> step)
    {
        try
        {
            return step(info);
        }
        catch (Exception ex)
        {
            log.Warn("A hardware detection step failed; affected values will show N/A.", ex);
            return info;
        }
    }

    private static HardwareInfo DetectCpu(HardwareInfo info)
    {
        string? name = null;
        int cores = 0, threads = 0;
        double? clock = null;
        foreach (var mo in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            name ??= (mo["Name"] as string)?.Trim();
            cores += Convert.ToInt32(mo["NumberOfCores"] ?? 0);
            threads += Convert.ToInt32(mo["NumberOfLogicalProcessors"] ?? 0);
            clock ??= mo["MaxClockSpeed"] is { } c ? Convert.ToDouble(c) : null;
        }
        return info with
        {
            CpuName = name ?? "N/A",
            CpuCores = cores,
            CpuThreads = threads == 0 ? Environment.ProcessorCount : threads,
            CpuBaseClockMhz = clock,
        };
    }

    private static HardwareInfo DetectGpu(HardwareInfo info)
    {
        var drivers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mo in Query("SELECT Name, DriverVersion FROM Win32_VideoController"))
        {
            if (mo["Name"] is string n && mo["DriverVersion"] is string d) drivers[n.Trim()] = d;
        }

        var gpus = Dxgi.EnumerateAdapters()
            .Select(a => new GpuInfo(a.Name, VendorName(a.VendorId), a.DedicatedVideoMemory,
                drivers.TryGetValue(a.Name, out var drv) ? drv : null, a.LuidKey))
            .ToList();
        return info with { Gpus = gpus };
    }

    private static string VendorName(uint vendorId) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 or 0x1022 => "AMD",
        0x8086 => "Intel",
        0x5143 => "Qualcomm",
        _ => "Unknown",
    };

    private static HardwareInfo DetectMemory(HardwareInfo info)
    {
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        NativeMethods.GlobalMemoryStatusEx(ref status);

        int modules = 0;
        int? speed = null;
        foreach (var mo in Query("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory"))
        {
            modules++;
            var s = Convert.ToInt32(mo["ConfiguredClockSpeed"] ?? mo["Speed"] ?? 0);
            if (s > 0) speed = speed is null ? s : Math.Min(speed.Value, s);
        }
        return info with { RamTotalBytes = status.ullTotalPhys, RamModules = modules, RamSpeedMhz = speed };
    }

    private static HardwareInfo DetectBoard(HardwareInfo info)
    {
        string board = "N/A", bios = "N/A";
        foreach (var mo in Query("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
        {
            board = $"{(mo["Manufacturer"] as string)?.Trim()} {(mo["Product"] as string)?.Trim()}".Trim();
            break;
        }
        foreach (var mo in Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
        {
            var date = mo["ReleaseDate"] is string d && d.Length >= 8 ? $" ({d[..4]}-{d[4..6]}-{d[6..8]})" : string.Empty;
            bios = $"{(mo["Manufacturer"] as string)?.Trim()} {(mo["SMBIOSBIOSVersion"] as string)?.Trim()}{date}".Trim();
            break;
        }
        return info with
        {
            Motherboard = string.IsNullOrWhiteSpace(board) ? "N/A" : board,
            Bios = string.IsNullOrWhiteSpace(bios) ? "N/A" : bios,
        };
    }

    private static HardwareInfo DetectStorage(HardwareInfo info)
    {
        var list = new List<StorageInfo>();
        try
        {
            foreach (var mo in Query("SELECT FriendlyName, Size, MediaType, BusType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                var media = Convert.ToInt32(mo["MediaType"] ?? 0) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Disk" };
                var bus = Convert.ToInt32(mo["BusType"] ?? 0) switch
                {
                    17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", _ => "Other",
                };
                if (bus == "NVMe" && media == "Disk") media = "SSD";
                list.Add(new StorageInfo((mo["FriendlyName"] as string ?? "Disk").Trim(), Convert.ToUInt64(mo["Size"] ?? 0UL), media, bus));
            }
        }
        catch
        {
            foreach (var mo in Query("SELECT Model, Size, InterfaceType FROM Win32_DiskDrive"))
                list.Add(new StorageInfo((mo["Model"] as string ?? "Disk").Trim(), Convert.ToUInt64(mo["Size"] ?? 0UL), "Disk", mo["InterfaceType"] as string ?? "Other"));
        }
        return info with { Storage = list };
    }

    public static string PrimaryAdapterName()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
            .FirstOrDefault();
        return nic?.Description ?? "N/A";
    }

    private static IEnumerable<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        foreach (var mo in results)
        {
            using (mo) yield return mo;
        }
    }
}
