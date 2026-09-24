using System.IO.Pipes;
using FpsLol.Logging;
using FpsLol.SystemIntegration;

namespace FpsLol.Optimizations.Privileged;

/// <summary>
/// Entry point of the short-lived elevated helper ("FPS.LOL.exe --elevated-worker &lt;pipe&gt; &lt;parentPid&gt;").
/// It has no UI, connects back to the parent over a named pipe, verifies the parent's identity,
/// runs the requested jobs (all validated by <see cref="SafetyPolicy"/>) and exits.
/// </summary>
public static class ElevatedWorker
{
    public static int Run(string pipeName, int parentPid)
    {
        var log = new LogService("-elevated");
        log.Info($"Elevated helper started (parent PID {parentPid}).");
        try
        {
            if (!pipeName.StartsWith("fpslol-", StringComparison.Ordinal)) return 2;

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(15_000);

            if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != parentPid)
            {
                log.Error("Refused request: pipe server is not the FPS.LOL instance that requested elevation.");
                return 3;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var request = PipeFraming.ReadAsync<WorkerRequest>(pipe, cts.Token).GetAwaiter().GetResult();
            var userMatches = string.Equals(request.UserSid, Elevation.CurrentUserSid, StringComparison.OrdinalIgnoreCase);

            var response = new WorkerResponse();
            foreach (var job in request.Jobs)
            {
                var result = JobRunner.Run(job, userMatches);
                log.Write(result.Status == Models.OperationStatus.Success ? LogLevel.Success : LogLevel.Warning,
                    $"{job.Title}: {result.Status} — {result.Message}");
                response.Results.Add(result);
            }

            PipeFraming.WriteAsync(pipe, response, cts.Token).GetAwaiter().GetResult();
            pipe.WaitForPipeDrain();
            return 0;
        }
        catch (Exception ex)
        {
            log.Error("Elevated helper failed.", ex);
            return 1;
        }
    }
}
