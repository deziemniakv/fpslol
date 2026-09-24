using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Hardware;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;
using FpsLol.ViewModels.Items;

namespace FpsLol.ViewModels;

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly IHardwareService _hardware;
    private readonly IMonitoringService _monitoring;
    private readonly ISystemScanService _scan;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly OptimizeViewModel _optimize;
    private bool _visible;

    public DashboardViewModel(IHardwareService hardware, IMonitoringService monitoring, ISystemScanService scan,
        INavigationService navigation, IDialogService dialogs, OptimizeViewModel optimize)
    {
        _hardware = hardware;
        _monitoring = monitoring;
        _scan = scan;
        _navigation = navigation;
        _dialogs = dialogs;
        _optimize = optimize;

        monitoring.Updated += (_, m) => { if (_visible) Ui.Post(() => ApplyMetrics(m)); };
        scan.Scanned += (_, r) => Ui.Post(() => ApplyScan(r));
    }

    public override string Title => "Dashboard";
    public override string Subtitle => "Performance status";

    public string WindowsText => WindowsInfo.Current.Long;

    [ObservableProperty] private HardwareInfo? _hardwareInfo;
    [ObservableProperty] private bool _hardwareLoading = true;

    [ObservableProperty] private string _cpuUsageText = Format.NotAvailable;
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private string _cpuClockText = Format.NotAvailable;
    [ObservableProperty] private string _cpuTempText = Format.NotAvailable;
    [ObservableProperty] private string _gpuUsageText = Format.NotAvailable;
    [ObservableProperty] private double _gpuUsage;
    [ObservableProperty] private string _gpuTempText = Format.NotAvailable;
    [ObservableProperty] private string _vramText = Format.NotAvailable;
    [ObservableProperty] private double _ramUsage;
    [ObservableProperty] private string _ramUsageText = Format.NotAvailable;
    [ObservableProperty] private string _ramUsedText = Format.NotAvailable;
    [ObservableProperty] private string _ramAvailableText = Format.NotAvailable;
    [ObservableProperty] private string _uptimeText = Format.NotAvailable;
    [ObservableProperty] private string _processCountText = Format.NotAvailable;
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _gpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _ramHistory = [];

    [ObservableProperty] private string _startupAppsText = Format.NotAvailable;
    [ObservableProperty] private bool _scoreLoading = true;
    [ObservableProperty] private int _score;
    [ObservableProperty] private string _scoreHeadline = "Analyzing your system…";
    [ObservableProperty] private string _availableText = string.Empty;
    [ObservableProperty] private int _availableCount;
    [ObservableProperty] private string _lastScanText = string.Empty;

    public ObservableCollection<StatusItem> GamingStatus { get; } = [];

    public string GpuName => HardwareInfo?.PrimaryGpu?.Name ?? Format.NotAvailable;
    public string CpuDetail => HardwareInfo is null ? string.Empty : $"{HardwareInfo.CpuCores}C / {HardwareInfo.CpuThreads}T";
    public string GpuDetail => HardwareInfo?.PrimaryGpu is { } g ? $"{g.Vendor} · {Format.Bytes(g.DedicatedMemoryBytes, 0)} VRAM" : string.Empty;
    public string RamDetail => HardwareInfo is null ? string.Empty
        : $"{Format.Bytes(HardwareInfo.RamTotalBytes, 0)}{(HardwareInfo.RamSpeedMhz is { } s ? $" · {s} MT/s" : string.Empty)}{(HardwareInfo.RamModules > 0 ? $" · {HardwareInfo.RamModules} modules" : string.Empty)}";
    public string StorageText => HardwareInfo is null || HardwareInfo.Storage.Count == 0 ? Format.NotAvailable
        : string.Join(Environment.NewLine, HardwareInfo.Storage.Select(s => $"{s.Model} · {Format.Bytes(s.SizeBytes, 0)} {s.BusType} {s.MediaType}"));

    partial void OnHardwareInfoChanged(HardwareInfo? value)
    {
        OnPropertyChanged(nameof(GpuName));
        OnPropertyChanged(nameof(CpuDetail));
        OnPropertyChanged(nameof(GpuDetail));
        OnPropertyChanged(nameof(RamDetail));
        OnPropertyChanged(nameof(StorageText));
    }

    public override async Task OnNavigatedToAsync()
    {
        _visible = true;
        if (_monitoring.Latest is { } m) ApplyMetrics(m);

        if (HardwareInfo is null)
        {
            HardwareInfo = await _hardware.GetAsync();
            HardwareLoading = false;
        }

        ApplyScan(await _scan.EnsureAsync());
    }

    public override void OnNavigatedFrom() => _visible = false;

    private void ApplyMetrics(MetricsSnapshot m)
    {
        CpuUsage = m.CpuUsage ?? 0;
        CpuUsageText = Format.Percent(m.CpuUsage);
        CpuClockText = Format.Mhz(m.CpuClockMhz);
        CpuTempText = Format.Temperature(m.CpuTemperature);
        GpuUsage = m.GpuUsage ?? 0;
        GpuUsageText = Format.Percent(m.GpuUsage);
        GpuTempText = Format.Temperature(m.GpuTemperature);
        VramText = m.GpuMemoryUsedBytes is { } used
            ? m.GpuMemoryTotalBytes is { } total ? $"{Format.Bytes(used)} / {Format.Bytes(total, 0)}" : Format.Bytes(used)
            : Format.NotAvailable;
        RamUsage = m.RamUsage;
        RamUsageText = Format.Percent(m.RamUsage);
        RamUsedText = Format.Bytes(m.RamUsedBytes);
        RamAvailableText = Format.Bytes(m.RamAvailableBytes);
        UptimeText = Format.Duration(m.Uptime);
        ProcessCountText = m.ProcessCount?.ToString() ?? Format.NotAvailable;
        CpuHistory = _monitoring.CpuHistory;
        GpuHistory = _monitoring.GpuHistory;
        RamHistory = _monitoring.RamHistory;
    }

    private void ApplyScan(ScanResult r)
    {
        Score = r.Score.Score;
        ScoreHeadline = r.Score.Headline;
        AvailableCount = r.Score.AvailableOptimizations;
        AvailableText = r.Score.AvailableText;
        StartupAppsText = r.StartupEnabled.ToString();
        LastScanText = $"Last scan {r.Timestamp:HH:mm}";
        GamingStatus.Clear();
        foreach (var item in GamingInfo.BuildStatus(r.Tweaks)) GamingStatus.Add(item);
        ScoreLoading = false;
    }

    [RelayCommand]
    private void OptimizeNow()
    {
        _navigation.Navigate<OptimizeViewModel>();
        _ = _optimize.StartScanAsync();
    }

    [RelayCommand]
    private void ShowScoreDetails()
    {
        if (_scan.Last is { } last) _ = _dialogs.ShowAsync(new ScoreDetailsDialogViewModel(last.Score));
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        ScoreLoading = true;
        await _scan.ScanAsync();
    }
}

public sealed partial class ScoreDetailsDialogViewModel(ScoreReport report) : DialogViewModel
{
    public ScoreReport Report { get; } = report;
    public int Score => Report.Score;
    public string Headline => Report.Headline;
    public IReadOnlyList<ScoreItem> Items => Report.Items;
    public int Earned => Report.Items.Sum(i => i.Points);
    public int Max => Report.Items.Sum(i => i.Max);
    public string Summary => $"{Earned} of {Max} points from {Report.Items.Count} checks · {Report.AvailableText}";
}
