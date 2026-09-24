using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.SystemIntegration;

namespace FpsLol.Optimizations.Privileged;

public interface IPrivilegedExecutor
{
    /// <summary>Runs jobs with administrator rights. Shows exactly one UAC prompt per call when FPS.LOL is not elevated.</summary>
    Task<IReadOnlyList<WorkerJobResult>> RunAsync(IReadOnlyList<WorkerJob> jobs, CancellationToken ct = default);
}

public sealed class PrivilegedExecutor(ILogService log) : IPrivilegedExecutor
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<WorkerJobResult>> RunAsync(IReadOnlyList<WorkerJob> jobs, CancellationToken ct = default)
    {
        if (jobs.Count == 0) return [];

        if (Elevation.IsElevated)
            return await Task.Run(() => jobs.Select(j => JobRunner.Run(j, userMatches: true)).ToList(), ct);

        await _gate.WaitAsync(ct);
        try
        {
            return await RunElevatedAsync(jobs, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<WorkerJobResult>> RunElevatedAsync(IReadOnlyList<WorkerJob> jobs, CancellationToken ct)
    {
        var pipeName = "fpslol-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Process? helper;
        try
        {
            log.Info($"Requesting administrator privileges for {jobs.Count} operation(s).");
            helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--elevated-worker {pipeName} {Environment.ProcessId}",
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Elevation.ErrorCancelled)
        {
            log.Warn("Administrator permission was declined.");
            return AllWith(jobs, OperationStatus.Skipped, "Administrator permission was declined. Nothing was changed.");
        }

        if (helper is null)
            return AllWith(jobs, OperationStatus.Failed, "Could not start the elevated helper.");

        using (helper)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                var connect = server.WaitForConnectionAsync(timeout.Token);
                var exited = helper.WaitForExitAsync(timeout.Token);
                if (await Task.WhenAny(connect, exited) != connect)
                    return AllWith(jobs, OperationStatus.Failed, "The elevated helper exited unexpectedly.");
                await connect;

                if (!NativeMethods.GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientPid) || clientPid != helper.Id)
                    return AllWith(jobs, OperationStatus.Failed, "Security check failed: unexpected process connected to the helper channel.");

                await PipeFraming.WriteAsync(server, new WorkerRequest { UserSid = Elevation.CurrentUserSid, Jobs = jobs.ToList() }, timeout.Token);
                var response = await PipeFraming.ReadAsync<WorkerResponse>(server, timeout.Token);

                // Map results back by key; anything missing is reported as a failure (never as success).
                return jobs.Select(j => response.Results.FirstOrDefault(r => r.Key == j.Key)
                                        ?? WorkerJobResult.For(j, OperationStatus.Failed, "No result was returned for this operation.")).ToList();
            }
            catch (OperationCanceledException)
            {
                return AllWith(jobs, OperationStatus.Failed, "The elevated operation timed out.");
            }
            catch (Exception ex)
            {
                log.Error("Elevated helper communication failed.", ex);
                return AllWith(jobs, OperationStatus.Failed, "Communication with the elevated helper failed.", ex.Message);
            }
        }
    }

    private static List<WorkerJobResult> AllWith(IReadOnlyList<WorkerJob> jobs, OperationStatus status, string message, string? details = null) =>
        jobs.Select(j => WorkerJobResult.For(j, status, message, details)).ToList();
}
