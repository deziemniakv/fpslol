using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Hardware;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.Utilities;

namespace FpsLol.ViewModels;

public sealed record ProcessChoice(int Pid, string Name)
{
    public string Display => $"{Name}  ·  PID {Pid}";
}

public sealed partial class PerformanceViewModel : PageViewModel
{
    private const int FpsHistoryLength = 60;
    private readonly IMonitoringService _monitoring;
    private readonly IHardwareService _hardware;
    private readonly IFpsMonitorService _fps;
    private readonly IProcessService _processes;
    private readonly IDialogService _dialogs;
    private readonly Queue<double> _fpsHistory = new();
    private readonly Queue<double> _frameTimeHistory = new();
    private bool _visible;

    public PerformanceViewModel(IMonitoringService monitoring, IHardwareService hardware, IFpsMonitorService fps,
        IProcessService processes, IDialogService dialogs)
    {
        _monitoring = monitoring;
        _hardware = hardware;
        _fps = fps;
        _processes = processes;
        _dialogs = dialogs;
        monitoring.Updated += (_, m) => { if (_visible) Ui.Post(() => Apply(m)); };
        fps.Sampled += (_, s) => Ui.Post(() => ApplyFps(s));
    }

    public override string Title => "Performance";
    public override string Subtitle => "Real-time CPU, GPU, memory and frame-rate monitor";

    [ObservableProperty] private string _cpuName = Format.NotAvailable;
    [ObservableProperty] private string _gpuName = Format.NotAvailable;
    [ObservableProperty] private string _cpuText = Format.NotAvailable;
    [ObservableProperty] private string _cpuClockText = Format.NotAvailable;
    [ObservableProperty] private string _cpuTempText = Format.NotAvailable;
    [ObservableProperty] private string _cpuTempNote = "Windows does not expose a driver-free CPU temperature sensor.";
    [ObservableProperty] private string _gpuText = Format.NotAvailable;
    [ObservableProperty] private string _gpuTempText = Format.NotAvailable;
    [ObservableProperty] private string _vramText = Format.NotAvailable;
    [ObservableProperty] private string _ramText = Format.NotAvailable;
    [ObservableProperty] private string _ramDetail = Format.NotAvailable;
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _gpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _ramHistory = [];

    public ObservableCollection<ProcessChoice> Candidates { get; } = [];
    [ObservableProperty] private ProcessChoice? _selectedCandidate;
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _fpsText = "—";
    [ObservableProperty] private string _frameTimeText = "—";
    [ObservableProperty] private string _lowText = "—";
    [ObservableProperty] private string _captureStatus = "Select a running game and start the capture.";
    [ObservableProperty] private IReadOnlyList<double> _fpsSeries = [];
    [ObservableProperty] private IReadOnlyList<double> _frameTimeSeries = [];

    public bool FpsSupported => _fps.IsSupported;
    public string? FpsUnsupportedReason => _fps.UnsupportedReason;

    public override async Task OnNavigatedToAsync()
    {
        _visible = true;
        var hw = await _hardware.GetAsync();
        CpuName = hw.CpuName;
        GpuName = hw.PrimaryGpu?.Name ?? Format.NotAvailable;
        if (_monitoring.Latest is { } m) Apply(m);
        await RefreshCandidatesAsync();
    }

    public override void OnNavigatedFrom() => _visible = false;

    private void Apply(MetricsSnapshot m)
    {
        CpuText = Format.Percent(m.CpuUsage);
        CpuClockText = Format.Mhz(m.CpuClockMhz);
        CpuTempText = Format.Temperature(m.CpuTemperature);
        if (m.CpuTemperatureSource is { } src) CpuTempNote = $"Source: {src} (motherboard sensor, not the CPU die).";
        GpuText = Format.Percent(m.GpuUsage);
        GpuTempText = Format.Temperature(m.GpuTemperature);
        VramText = m.GpuMemoryUsedBytes is { } used
            ? m.GpuMemoryTotalBytes is { } total ? $"{Format.Bytes(used)} / {Format.Bytes(total, 0)}" : Format.Bytes(used)
            : Format.NotAvailable;
        RamText = Format.Percent(m.RamUsage);
        RamDetail = $"{Format.Bytes(m.RamUsedBytes)} used · {Format.Bytes(m.RamAvailableBytes)} available";
        CpuHistory = _monitoring.CpuHistory;
        GpuHistory = _monitoring.GpuHistory;
        RamHistory = _monitoring.RamHistory;
    }

    [RelayCommand]
    private async Task RefreshCandidatesAsync()
    {
        var list = await Task.Run(() => _processes.Snapshot()
            .Where(p => p.HasWindow && p.Protection == SystemIntegration.ProcessProtection.None && p.Pid != Environment.ProcessId)
            .OrderByDescending(p => p.MemoryBytes)
            .Select(p => new ProcessChoice(p.Pid, p.Name))
            .ToList());
        var keep = SelectedCandidate?.Pid;
        Candidates.Clear();
        foreach (var c in list) Candidates.Add(c);
        SelectedCandidate = Candidates.FirstOrDefault(c => c.Pid == keep) ?? Candidates.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ToggleCaptureAsync()
    {
        if (IsCapturing)
        {
            _fps.Stop();
            IsCapturing = false;
            CaptureStatus = "Capture stopped.";
            return;
        }

        if (!_fps.IsSupported)
        {
            await _dialogs.ShowInfoAsync("Administrator required", _fps.UnsupportedReason ?? string.Empty);
            return;
        }
        if (SelectedCandidate is not { } target)
        {
            CaptureStatus = "Start a game first, then refresh the list.";
            return;
        }

        try
        {
            _fpsHistory.Clear();
            _frameTimeHistory.Clear();
            _fps.Start(target.Pid);
            IsCapturing = true;
            CaptureStatus = $"Measuring {target.Name}. DirectX (DXGI/D3D9) presents are counted; Vulkan/OpenGL titles may show 0.";
        }
        catch (Exception ex)
        {
            IsCapturing = false;
            await _dialogs.ShowErrorAsync("Frame capture failed", "Windows performance tracing could not be started.", ex.Message);
        }
    }

    private void ApplyFps(FpsSample s)
    {
        if (!IsCapturing) return;
        FpsText = s.FramesInWindow == 0 ? "0" : s.Fps.ToString("F0");
        FrameTimeText = s.FramesInWindow == 0 ? "—" : $"{s.FrameTimeMs:F2} ms";
        LowText = s.OnePercentLowFps is { } low ? low.ToString("F0") : "—";
        Push(_fpsHistory, s.Fps);
        Push(_frameTimeHistory, s.FrameTimeMs);
        FpsSeries = _fpsHistory.ToArray();
        FrameTimeSeries = _frameTimeHistory.ToArray();
    }

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > FpsHistoryLength) q.Dequeue();
    }
}
