using FpsLol.Logging;
using FpsLol.SystemIntegration;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace FpsLol.Hardware;

public sealed record FpsSample(double Fps, double FrameTimeMs, double? OnePercentLowFps, int FramesInWindow);

public interface IFpsMonitorService
{
    bool IsSupported { get; }
    string? UnsupportedReason { get; }
    bool IsRunning { get; }
    int? TargetPid { get; }
    event EventHandler<FpsSample>? Sampled;
    void Start(int pid);
    void Stop();
}

/// <summary>
/// Frame-rate measurement from ETW "Present" events of the DXGI and Direct3D 9 runtimes — the same
/// public, read-only mechanism used by PresentMon. No injection, no hooks, no drivers.
/// Requires administrator rights (ETW real-time sessions). Vulkan/OpenGL titles that do not present
/// through DXGI are not measured.
/// </summary>
public sealed class FpsMonitorService(ILogService log) : IFpsMonitorService, IDisposable
{
    private const string SessionName = "FPS.LOL-FrameMonitor";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42;
    private const int DxgiPresentMpoStart = 55;
    private const int D3D9PresentStart = 1;

    private readonly object _gate = new();
    private readonly Queue<double> _timestamps = new();   // ms, relative to session start
    private readonly Queue<double> _frameTimes = new();   // last ~2000 frame intervals
    private TraceEventSession? _session;
    private Thread? _thread;
    private Timer? _timer;
    private double _lastTimestamp = -1;
    private double _latestTimestamp;

    public bool IsSupported => Elevation.IsElevated;

    public string? UnsupportedReason => IsSupported ? null
        : "Frame-rate capture uses Windows performance tracing (ETW), which requires administrator privileges. Restart FPS.LOL as administrator to enable it.";

    public bool IsRunning => _session is not null;

    public int? TargetPid { get; private set; }

    public event EventHandler<FpsSample>? Sampled;

    public void Start(int pid)
    {
        Stop();
        if (!IsSupported) throw new InvalidOperationException(UnsupportedReason);

        TargetPid = pid;
        lock (_gate)
        {
            _timestamps.Clear();
            _frameTimes.Clear();
            _lastTimestamp = -1;
        }

        _session = new TraceEventSession(SessionName) { StopOnDispose = true };
        _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue);
        _session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, ulong.MaxValue);
        _session.Source.AllEvents += OnEvent;

        var session = _session;
        _thread = new Thread(() =>
        {
            try
            {
                session.Source.Process();
            }
            catch (Exception ex)
            {
                log.Warn("Frame monitor stopped unexpectedly.", ex);
            }
        })
        { IsBackground = true, Name = "FPS.LOL ETW" };
        _thread.Start();

        _timer = new Timer(_ => Publish(), null, 1000, 1000);
        log.Info($"Frame monitor started for PID {pid}");
    }

    private void OnEvent(TraceEvent e)
    {
        if (e.ProcessID != TargetPid) return;
        var id = (int)e.ID;
        var isPresent = (e.ProviderGuid == DxgiProvider && (id == DxgiPresentStart || id == DxgiPresentMpoStart))
                        || (e.ProviderGuid == D3D9Provider && id == D3D9PresentStart);
        if (!isPresent) return;

        var t = e.TimeStampRelativeMSec;
        lock (_gate)
        {
            if (_lastTimestamp >= 0)
            {
                var delta = t - _lastTimestamp;
                if (delta > 0 && delta < 1000)
                {
                    _frameTimes.Enqueue(delta);
                    while (_frameTimes.Count > 2000) _frameTimes.Dequeue();
                }
            }
            _lastTimestamp = t;
            _latestTimestamp = t;
            _timestamps.Enqueue(t);
            while (_timestamps.Count > 0 && t - _timestamps.Peek() > 1000) _timestamps.Dequeue();
        }
    }

    private void Publish()
    {
        FpsSample sample;
        lock (_gate)
        {
            // Drop frames older than 1 s relative to the newest event; if no frames arrived, report 0.
            while (_timestamps.Count > 0 && _latestTimestamp - _timestamps.Peek() > 1000) _timestamps.Dequeue();
            var frames = _timestamps.Count;
            var recent = _frameTimes.TakeLast(Math.Max(frames, 1)).ToList();
            var avgFrameTime = recent.Count > 0 ? recent.Average() : 0;
            double? low = null;
            if (_frameTimes.Count >= 100)
            {
                var worst = _frameTimes.OrderByDescending(x => x).Take(Math.Max(1, _frameTimes.Count / 100)).Average();
                low = worst > 0 ? 1000 / worst : null;
            }
            sample = new FpsSample(avgFrameTime > 0 ? 1000 / avgFrameTime : 0, avgFrameTime, low, frames);
            if (frames == 0) sample = new FpsSample(0, 0, low, 0);
        }
        Sampled?.Invoke(this, sample);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        if (_session is not null)
        {
            try
            {
                _session.Source.AllEvents -= OnEvent;
                _session.Dispose();
            }
            catch (Exception ex)
            {
                log.Debug("Error while stopping ETW session: " + ex.Message);
            }
            _session = null;
            log.Info("Frame monitor stopped");
        }
        _thread = null;
        TargetPid = null;
    }

    public void Dispose() => Stop();
}
