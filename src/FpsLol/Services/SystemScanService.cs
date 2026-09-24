using FpsLol.Hardware;
using FpsLol.Logging;
using FpsLol.Models;

namespace FpsLol.Services;

public sealed record ScanResult(IReadOnlyList<TweakStatus> Tweaks, ScoreReport Score, int StartupEnabled, DateTime Timestamp);

public interface ISystemScanService
{
    ScanResult? Last { get; }
    event EventHandler<ScanResult>? Scanned;
    Task<ScanResult> ScanAsync();

    /// <summary>Returns the last result, joins a scan in progress, or starts one.</summary>
    Task<ScanResult> EnsureAsync();
}

/// <summary>Single source of truth for "what is the current state of this PC": tweak detection + score.</summary>
public sealed class SystemScanService(
    ILogService log,
    ITweakEngine tweaks,
    IStartupService startup,
    IMonitoringService monitoring,
    IScoreService score,
    ISettingsService settings) : ISystemScanService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<ScanResult>? _running;

    public ScanResult? Last { get; private set; }

    public event EventHandler<ScanResult>? Scanned;

    public Task<ScanResult> EnsureAsync()
    {
        if (Last is { } last) return Task.FromResult(last);
        return _running is { IsCompleted: false } running ? running : ScanAsync();
    }

    public Task<ScanResult> ScanAsync()
    {
        var task = ScanCoreAsync();
        _running = task;
        return task;
    }

    private async Task<ScanResult> ScanCoreAsync()
    {
        await _gate.WaitAsync();
        try
        {
            log.Info("System scan started");
            var statuses = await tweaks.DetectAllAsync();
            var startupCount = await Task.Run(startup.CountEnabled);
            var report = score.Compute(statuses, monitoring.Latest, startupCount);

            foreach (var s in statuses.Where(s => s.Definition.Category is TweakCategory.WindowsGaming or TweakCategory.Power or TweakCategory.Gpu))
                log.Info($"{s.Definition.Name}: {s.Detection.Current}");
            log.Info($"System score: {report.Score}/100 — {report.AvailableText}");

            settings.Current.LastScore = report.Score;
            settings.Save();

            Last = new ScanResult(statuses, report, startupCount, DateTime.Now);
            Scanned?.Invoke(this, Last);
            return Last;
        }
        finally
        {
            _gate.Release();
        }
    }
}
