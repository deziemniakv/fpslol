using System.ComponentModel;
using System.Diagnostics;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Optimizations.Privileged;
using FpsLol.SystemIntegration;

namespace FpsLol.Services;

public sealed record ProcessInfo(int Pid, string Name, double? CpuPercent, long MemoryBytes, ProcessProtection Protection, int SessionId, bool HasWindow)
{
    public bool CanEnd => Protection == ProcessProtection.None;
    public string ProtectionLabel => Protection switch
    {
        ProcessProtection.Protected => "PROTECTED",
        ProcessProtection.System => "SYSTEM",
        _ => string.Empty,
    };
}

public interface IProcessService
{
    IReadOnlyList<ProcessInfo> Snapshot();
    Task<OperationResult> EndAsync(ProcessInfo process);
    string? GetPath(int pid);
}

public sealed class ProcessService(ILogService log, IPrivilegedExecutor privileged) : IProcessService
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (string Name, TimeSpan Cpu)> _previous = [];
    private readonly Dictionary<(int, string), ProcessProtection> _protection = [];
    private readonly Dictionary<int, string?> _paths = [];
    private long _previousTimestamp;

    public IReadOnlyList<ProcessInfo> Snapshot()
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = _previousTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_previousTimestamp, now).TotalMilliseconds;
            _previousTimestamp = now;

            var result = new List<ProcessInfo>();
            var seen = new HashSet<int>();
            var windowed = NativeMethods.VisibleWindowProcessIds();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        var pid = p.Id;
                        var name = pid == 0 ? "Idle" : p.ProcessName;
                        seen.Add(pid);

                        double? cpu = null;
                        try
                        {
                            var total = p.TotalProcessorTime;
                            if (elapsed > 0 && _previous.TryGetValue(pid, out var prev) && prev.Name == name)
                                cpu = Math.Clamp((total - prev.Cpu).TotalMilliseconds / (elapsed * Environment.ProcessorCount) * 100, 0, 100);
                            _previous[pid] = (name, total);
                        }
                        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                        {
                            // Access denied for protected processes — CPU shown as N/A.
                        }

                        var session = p.SessionId;
                        if (!_protection.TryGetValue((pid, name), out var protection))
                        {
                            protection = ProcessGuard.Classify(pid, name, session);
                            _protection[(pid, name)] = protection;
                        }

                        result.Add(new ProcessInfo(pid, name, cpu, p.WorkingSet64, protection, session, windowed.Contains(pid)));
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited while enumerating.
                    }
                }
            }

            foreach (var gone in _previous.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _previous.Remove(gone);
                _paths.Remove(gone);
            }
            foreach (var key in _protection.Keys.Where(k => !seen.Contains(k.Item1)).ToList()) _protection.Remove(key);
            return result;
        }
    }

    public string? GetPath(int pid)
    {
        lock (_gate)
        {
            if (!_paths.TryGetValue(pid, out var path))
            {
                path = ProcessGuard.GetImagePath(pid);
                _paths[pid] = path;
            }
            return path;
        }
    }

    public async Task<OperationResult> EndAsync(ProcessInfo process)
    {
        var title = $"End {process.Name}";
        if (!process.CanEnd)
            return OperationResult.Skip(title, $"{process.Name} is a {process.ProtectionLabel.ToLowerInvariant()} process and cannot be ended.");

        try
        {
            await Task.Run(() => ProcessGuard.Kill(process.Pid, process.Name));
            log.Info($"Process ended: {process.Name} (PID {process.Pid})");
            return OperationResult.Ok(title, $"{process.Name} was ended.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5 && !Elevation.IsElevated)
        {
            // Elevated process — ask for administrator rights for this single operation.
            var res = (await privileged.RunAsync([new KillProcessJob { Title = title, Pid = process.Pid, ProcessName = process.Name }]))[0];
            return new OperationResult(title, res.Status, res.Message, res.Details);
        }
        catch (ArgumentException)
        {
            return OperationResult.Skip(title, $"{process.Name} is no longer running.");
        }
        catch (Exception ex)
        {
            log.Error($"Could not end {process.Name}.", ex);
            return OperationResult.Fail(title, $"{process.Name} could not be ended.", ex.Message);
        }
    }
}
