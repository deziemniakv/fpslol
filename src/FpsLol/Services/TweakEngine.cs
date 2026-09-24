using FpsLol.Hardware;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Networking;
using FpsLol.Optimizations;
using FpsLol.SystemIntegration;

namespace FpsLol.Services;

public sealed record TweakStatus(TweakDefinition Definition, TweakDetection Detection, bool HasHistory);

public interface ITweakEngine
{
    IReadOnlyList<TweakDefinition> All { get; }
    Task<TweakContext> GetContextAsync(bool refresh = false);
    Task<IReadOnlyList<TweakStatus>> DetectAllAsync();
    TweakStatus Detect(TweakDefinition tweak, TweakContext ctx);
    ChangeRequest BuildApply(TweakDefinition tweak, TweakContext ctx, TweakDetection detection);
    ChangeRequest BuildDefaults(TweakDefinition tweak, TweakContext ctx, TweakDetection detection);
    event EventHandler? StateChanged;
    void NotifyStateChanged();
}

public sealed class TweakEngine(ILogService log, IHardwareService hardware, IHistoryService history) : ITweakEngine
{
    private TweakContext? _context;

    public IReadOnlyList<TweakDefinition> All => TweakCatalog.All;

    public event EventHandler? StateChanged;

    public void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async Task<TweakContext> GetContextAsync(bool refresh = false)
    {
        if (_context is not null && !refresh) return _context;
        _context = await Task.Run(() =>
        {
            var power = PowerPlans.GetDeviceInfo();
            var sched = hardware.GetGpuScheduling();
            var adapter = NetworkAdapters.GetPrimaryPhysical();
            return new TweakContext(
                WindowsInfo.Current.Build,
                Elevation.IsElevated,
                power.HasBattery,
                power.ModernStandby,
                sched.Supported,
                sched.EnabledNow,
                adapter?.Description,
                adapter?.PnpDeviceId);
        });
        return _context;
    }

    public async Task<IReadOnlyList<TweakStatus>> DetectAllAsync()
    {
        var ctx = await GetContextAsync(refresh: true);
        return await Task.Run(() => All.Select(t => Detect(t, ctx)).ToList());
    }

    public TweakStatus Detect(TweakDefinition tweak, TweakContext ctx)
    {
        TweakDetection detection;
        if (ctx.Build < tweak.MinBuild)
        {
            detection = TweakDetection.NotSupported($"Requires Windows build {tweak.MinBuild} or newer (this PC runs build {ctx.Build}).");
        }
        else
        {
            try
            {
                detection = tweak.Detect(ctx);
            }
            catch (Exception ex)
            {
                log.Warn($"Could not read the state of \"{tweak.Name}\".", ex);
                detection = TweakDetection.NotSupported("The current state of this setting could not be read: " + TransactionRunner.Describe(ex));
            }
        }
        log.Debug($"{tweak.Name}: {detection.Current}{(detection.IsOptimal ? " (optimal)" : string.Empty)}");
        return new TweakStatus(tweak, detection, history.ActiveFor(tweak.Id).Count > 0);
    }

    public ChangeRequest BuildApply(TweakDefinition tweak, TweakContext ctx, TweakDetection detection) => new()
    {
        SourceId = tweak.Id,
        Title = tweak.Name,
        Category = tweak.CategoryName,
        Before = detection.Current,
        After = detection.Recommended,
        Restart = tweak.Restart,
        Actions = tweak.Apply(ctx),
    };

    public ChangeRequest BuildDefaults(TweakDefinition tweak, TweakContext ctx, TweakDetection detection) => new()
    {
        SourceId = tweak.Id,
        Title = tweak.Name + " (Windows default)",
        Category = tweak.CategoryName,
        Before = detection.Current,
        After = "Windows default",
        Restart = tweak.Restart,
        Actions = tweak.Defaults(ctx),
    };
}
