using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FpsLol.Hardware.Native;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.SystemIntegration;

namespace FpsLol.Hardware;

public interface IMonitoringService
{
    MetricsSnapshot? Latest { get; }
    IReadOnlyList<double> CpuHistory { get; }
    IReadOnlyList<double> GpuHistory { get; }
    IReadOnlyList<double> RamHistory { get; }
    bool IsPaused { get; set; }
    event EventHandler<MetricsSnapshot>? Updated;
    void Start();
}

/// <summary>
/// Samples real-time metrics once per second using PDH counters (same sources as Task Manager),
/// D3DKMT for GPU temperature and GlobalMemoryStatusEx for memory.
/// </summary>
public sealed partial class MonitoringService(ILogService log, IHardwareService hardware) : IMonitoringService, IDisposable
{
    public const int HistoryLength = 60;

    private readonly object _gate = new();
    private readonly Queue<double> _cpu = new(), _gpu = new(), _ram = new();
    private CancellationTokenSource? _cts;

    private PdhQuery? _pdh;
    private IntPtr _cpuUtility, _cpuTime, _cpuPerformance, _gpuEngine, _gpuMemory, _processes;
    private int _tick;
    private double? _cachedCpuTemp;

    public MetricsSnapshot? Latest { get; private set; }
    public bool IsPaused { get; set; }

    public IReadOnlyList<double> CpuHistory { get { lock (_gate) return _cpu.ToArray(); } }
    public IReadOnlyList<double> GpuHistory { get { lock (_gate) return _gpu.ToArray(); } }
    public IReadOnlyList<double> RamHistory { get { lock (_gate) return _ram.ToArray(); } }

    public event EventHandler<MetricsSnapshot>? Updated;

    [GeneratedRegex(@"luid_0x([0-9a-f]+)_0x([0-9a-f]+)_phys_\d+(?:_eng_\d+_engtype_(.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex GpuInstance();

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => LoopAsync(token), token);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            InitCounters();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (IsPaused) continue;
                try
                {
                    var snapshot = Sample();
                    Latest = snapshot;
                    Updated?.Invoke(this, snapshot);
                }
                catch (Exception ex)
                {
                    log.Warn("Metrics sampling failed.", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void InitCounters()
    {
        _pdh = new PdhQuery();
        _cpuUtility = _pdh.Add(@"\Processor Information(_Total)\% Processor Utility");
        _cpuTime = _cpuUtility == IntPtr.Zero ? _pdh.Add(@"\Processor(_Total)\% Processor Time") : IntPtr.Zero;
        _cpuPerformance = _pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
        _gpuEngine = _pdh.Add(@"\GPU Engine(*)\Utilization Percentage");
        _gpuMemory = _pdh.Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
        _processes = _pdh.Add(@"\System\Processes");
        _pdh.Collect(); // rate counters need two samples
        if (_gpuEngine == IntPtr.Zero) log.Warn("GPU engine performance counters are not available on this system.");
    }

    private MetricsSnapshot Sample()
    {
        _tick++;
        var pdh = _pdh!;
        pdh.Collect();

        var hw = hardware.Cached;
        var primary = hw?.PrimaryGpu;

        // CPU
        double? cpu = pdh.Get(_cpuUtility != IntPtr.Zero ? _cpuUtility : _cpuTime);
        if (cpu is { } c) cpu = Math.Clamp(c, 0, 100);
        double? clock = null;
        if (pdh.Get(_cpuPerformance) is { } perf && hw?.CpuBaseClockMhz is { } baseClock && perf > 0)
            clock = baseClock * perf / 100.0;

        // GPU utilisation: per adapter, the busiest engine type (3D, Compute, VideoDecode...) — Task Manager's method.
        double? gpu = null;
        if (_gpuEngine != IntPtr.Zero)
        {
            var perType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (instance, value) in pdh.GetArray(_gpuEngine))
            {
                var m = GpuInstance().Match(instance);
                if (!m.Success) continue;
                var key = LuidKey(m);
                if (primary is not null && !string.Equals(key, primary.LuidKey, StringComparison.OrdinalIgnoreCase)) continue;
                var type = key + "|" + m.Groups[3].Value;
                perType[type] = perType.GetValueOrDefault(type) + value;
            }
            gpu = perType.Count > 0 ? Math.Clamp(perType.Values.Max(), 0, 100) : 0;
        }

        ulong? vramUsed = null;
        if (_gpuMemory != IntPtr.Zero)
        {
            foreach (var (instance, value) in pdh.GetArray(_gpuMemory))
            {
                var m = GpuInstance().Match(instance);
                if (!m.Success) continue;
                if (primary is null || string.Equals(LuidKey(m), primary.LuidKey, StringComparison.OrdinalIgnoreCase))
                {
                    vramUsed = (vramUsed ?? 0) + (ulong)Math.Max(0, value);
                    if (primary is not null) break;
                }
            }
        }

        double? gpuTemp = null;
        try
        {
            var readings = D3dkmt.Query(includeScheduling: false);
            var reading = primary is null ? readings.FirstOrDefault() : readings.FirstOrDefault(r => r.LuidKey == primary.LuidKey);
            gpuTemp = reading?.TemperatureC;
        }
        catch
        {
            // Temperature not reported by this driver.
        }

        // CPU temperature: Windows exposes no reliable, driver-free CPU sensor. ACPI thermal zones are read
        // only when elevated and are labelled as such (they measure the motherboard zone, not the CPU die).
        if (Elevation.IsElevated && _tick % 5 == 1) _cachedCpuTemp = ReadAcpiTemperature();

        // Memory
        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        NativeMethods.GlobalMemoryStatusEx(ref mem);

        var snapshot = new MetricsSnapshot
        {
            Timestamp = DateTime.Now,
            CpuUsage = cpu,
            CpuClockMhz = clock,
            CpuTemperature = _cachedCpuTemp,
            CpuTemperatureSource = _cachedCpuTemp is null ? null : "ACPI thermal zone",
            GpuUsage = gpu,
            GpuTemperature = gpuTemp,
            GpuMemoryUsedBytes = vramUsed,
            GpuMemoryTotalBytes = primary?.DedicatedMemoryBytes is > 0 ? primary.DedicatedMemoryBytes : null,
            RamTotalBytes = mem.ullTotalPhys,
            RamAvailableBytes = mem.ullAvailPhys,
            ProcessCount = pdh.Get(_processes) is { } p ? (int)p : null,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
        };

        lock (_gate)
        {
            Push(_cpu, snapshot.CpuUsage ?? 0);
            Push(_gpu, snapshot.GpuUsage ?? 0);
            Push(_ram, snapshot.RamUsage);
        }
        return snapshot;
    }

    private static string LuidKey(Match m) =>
        $"{Convert.ToUInt32(m.Groups[1].Value, 16):X8}_{Convert.ToUInt32(m.Groups[2].Value, 16):X8}";

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > HistoryLength) q.Dequeue();
    }

    private static double? ReadAcpiTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double? max = null;
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var c = Convert.ToDouble(mo["CurrentTemperature"]) / 10.0 - 273.15;
                    if (c is > 5 and < 125) max = max is null ? c : Math.Max(max.Value, c);
                }
            }
            return max;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _pdh?.Dispose();
    }
}
