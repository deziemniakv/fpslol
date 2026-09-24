namespace FpsLol.Models;

public sealed record GpuInfo(string Name, string Vendor, ulong DedicatedMemoryBytes, string? DriverVersion, string LuidKey);

public sealed record StorageInfo(string Model, ulong SizeBytes, string MediaType, string BusType);

public sealed record HardwareInfo
{
    public string CpuName { get; init; } = "N/A";
    public int CpuCores { get; init; }
    public int CpuThreads { get; init; }
    public double? CpuBaseClockMhz { get; init; }

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public GpuInfo? PrimaryGpu => Gpus.OrderByDescending(g => g.DedicatedMemoryBytes).FirstOrDefault();

    public ulong RamTotalBytes { get; init; }
    public int? RamSpeedMhz { get; init; }
    public int RamModules { get; init; }

    public string Motherboard { get; init; } = "N/A";
    public string Bios { get; init; } = "N/A";
    public IReadOnlyList<StorageInfo> Storage { get; init; } = [];
    public string NetworkAdapter { get; init; } = "N/A";
}

public sealed record MetricsSnapshot
{
    public DateTime Timestamp { get; init; }

    public double? CpuUsage { get; init; }
    public double? CpuClockMhz { get; init; }
    public double? CpuTemperature { get; init; }
    public string? CpuTemperatureSource { get; init; }

    public double? GpuUsage { get; init; }
    public double? GpuTemperature { get; init; }
    public ulong? GpuMemoryUsedBytes { get; init; }
    public ulong? GpuMemoryTotalBytes { get; init; }

    public ulong RamTotalBytes { get; init; }
    public ulong RamAvailableBytes { get; init; }
    public ulong RamUsedBytes => RamTotalBytes - RamAvailableBytes;
    public double RamUsage => RamTotalBytes == 0 ? 0 : RamUsedBytes * 100.0 / RamTotalBytes;

    public int? ProcessCount { get; init; }
    public TimeSpan Uptime { get; init; }
}
