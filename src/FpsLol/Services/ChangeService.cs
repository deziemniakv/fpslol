using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.Optimizations.Privileged;

namespace FpsLol.Services;

public interface IChangeService
{
    /// <summary>
    /// Applies change requests: backup → (restore point) → apply with verification and rollback → record history.
    /// Administrator changes are batched into a single UAC prompt.
    /// </summary>
    Task<ExecutionReport> ExecuteAsync(IReadOnlyList<ChangeRequest> requests, bool createRestorePoint, string label, CancellationToken ct = default);

    /// <summary>Restores recorded changes (newest first). Returns one result per record.</summary>
    Task<IReadOnlyList<(ChangeRecord Record, OperationResult Result)>> RestoreAsync(IReadOnlyList<ChangeRecord> records, CancellationToken ct = default);

    Task<OperationResult> RestoreBackupAsync(BackupSet backup, CancellationToken ct = default);
}

public sealed class ChangeService(
    ILogService log,
    IHistoryService history,
    IBackupService backups,
    IPrivilegedExecutor privileged) : IChangeService
{
    public async Task<ExecutionReport> ExecuteAsync(IReadOnlyList<ChangeRequest> requests, bool createRestorePoint, string label, CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var results = new Dictionary<ChangeRequest, ChangeResult>();

        // 1. Pre-flight: safety policy.
        var runnable = new List<ChangeRequest>();
        foreach (var request in requests)
        {
            var violation = request.Actions.Select(a => a.Validate()).FirstOrDefault(v => v is not null);
            if (request.Actions.Count == 0)
                results[request] = new(request, OperationResult.Skip(request.Title, "Already in the recommended state."), null);
            else if (violation is not null)
                results[request] = new(request, OperationResult.Fail(request.Title, "Blocked by the FPS.LOL safety policy.", violation), null);
            else
                runnable.Add(request);
        }

        // 2. FPS.LOL configuration backup (always, before any change).
        string? backupFile = runnable.Count > 0 ? await Task.Run(() => backups.Create(label, runnable), ct) : null;

        // 3. Administrator batch (restore point first), one UAC prompt.
        var adminRequests = runnable.Where(r => r.RequiresAdmin).ToList();
        var userRequests = runnable.Where(r => !r.RequiresAdmin).ToList();
        OperationResult? restorePoint = null;

        var jobs = new List<WorkerJob>();
        RestorePointJob? rpJob = null;
        if (createRestorePoint && runnable.Count > 0)
        {
            rpJob = new RestorePointJob { Title = "System Restore point", Description = $"FPS.LOL: {label}" };
            jobs.Add(rpJob);
        }
        var jobMap = new Dictionary<string, ChangeRequest>();
        foreach (var request in adminRequests)
        {
            var job = new ApplyJob { Title = request.Title, Actions = request.Actions.ToList() };
            jobMap[job.Key] = request;
            jobs.Add(job);
        }

        if (jobs.Count > 0)
        {
            var jobResults = await privileged.RunAsync(jobs, ct);
            foreach (var jr in jobResults)
            {
                if (rpJob is not null && jr.Key == rpJob.Key)
                {
                    restorePoint = new OperationResult("System Restore point", jr.Status, jr.Message, jr.Details);
                    continue;
                }
                if (jobMap.TryGetValue(jr.Key, out var request))
                    results[request] = ToChangeResult(request, jr.Status, jr.Message, jr.Details, jr.Snapshots, sessionId);
            }
        }

        // 4. Per-user changes, in-process.
        foreach (var request in userRequests)
        {
            var outcome = await Task.Run(() => TransactionRunner.Apply(request.Actions), ct);
            results[request] = ToChangeResult(request, outcome.Status, outcome.Message, outcome.Details, outcome.Snapshots, sessionId);
        }

        // 5. History + logging.
        var records = results.Values.Where(r => r.Record is not null).Select(r => r.Record!).ToList();
        if (records.Count > 0) history.Add(records);

        foreach (var r in results.Values)
        {
            var level = r.Result.Status switch
            {
                OperationStatus.Success => LogLevel.Success,
                OperationStatus.Failed => LogLevel.Error,
                _ => LogLevel.Warning,
            };
            log.Write(level, $"{r.Request.Title}: {r.Result.StatusText} — {r.Result.Message}");
            if (r.Result.Details is not null) log.Debug(r.Result.Details);
        }
        if (restorePoint is not null) log.Info($"Restore point: {restorePoint.StatusText} — {restorePoint.Message}");

        var ordered = requests.Select(r => results[r]).ToList();
        return new ExecutionReport(ordered, restorePoint, backupFile);
    }

    private static ChangeResult ToChangeResult(ChangeRequest request, OperationStatus status, string message, string? details,
        List<ActionSnapshot> snapshots, string sessionId)
    {
        var result = new OperationResult(request.Title, status, message, details);
        if (status != OperationStatus.Success) return new ChangeResult(request, result, null);

        var record = new ChangeRecord
        {
            SessionId = sessionId,
            SourceId = request.SourceId,
            Title = request.Title,
            Category = request.Category,
            Before = request.Before,
            After = request.After,
            Snapshots = snapshots,
        };
        return new ChangeResult(request, result, record);
    }

    public async Task<IReadOnlyList<(ChangeRecord Record, OperationResult Result)>> RestoreAsync(IReadOnlyList<ChangeRecord> records, CancellationToken ct = default)
    {
        var ordered = records.Where(r => !r.Restored).OrderByDescending(r => r.Timestamp).ToList();
        var results = new Dictionary<Guid, OperationResult>();

        var adminJobs = new Dictionary<string, ChangeRecord>();
        var jobs = new List<WorkerJob>();
        foreach (var record in ordered.Where(r => r.RequiresAdmin))
        {
            var job = new RestoreJob { Title = "Restore " + record.Title, Snapshots = record.Snapshots };
            adminJobs[job.Key] = record;
            jobs.Add(job);
        }

        if (jobs.Count > 0)
        {
            foreach (var jr in await privileged.RunAsync(jobs, ct))
            {
                if (adminJobs.TryGetValue(jr.Key, out var record))
                    results[record.Id] = new OperationResult(record.Title, jr.Status, jr.Message, jr.Details);
            }
        }

        foreach (var record in ordered.Where(r => !r.RequiresAdmin))
        {
            var outcome = await Task.Run(() => TransactionRunner.Restore(record.Snapshots), ct);
            results[record.Id] = new OperationResult(record.Title, outcome.Status, outcome.Message, outcome.Details);
        }

        var restored = ordered.Where(r => results.TryGetValue(r.Id, out var res) && res.IsSuccess).Select(r => r.Id).ToList();
        if (restored.Count > 0) history.MarkRestored(restored);

        foreach (var record in ordered)
        {
            var res = results.GetValueOrDefault(record.Id) ?? OperationResult.Fail(record.Title, "No result.");
            log.Write(res.IsSuccess ? LogLevel.Success : LogLevel.Error, $"Restore {record.Title}: {res.StatusText} — {res.Message}");
        }

        return ordered.Select(r => (r, results.GetValueOrDefault(r.Id) ?? OperationResult.Fail(r.Title, "No result."))).ToList();
    }

    public async Task<OperationResult> RestoreBackupAsync(BackupSet backup, CancellationToken ct = default)
    {
        var snapshots = backup.Items.SelectMany(i => i.Snapshots).ToList();
        if (snapshots.Count == 0) return OperationResult.Skip("Backup", "This backup contains no restorable values.");

        var admin = snapshots.Where(s => s.RequiresAdmin).ToList();
        var user = snapshots.Where(s => !s.RequiresAdmin).ToList();
        var errors = new List<string>();

        if (admin.Count > 0)
        {
            var res = await privileged.RunAsync([new RestoreJob { Title = "Restore backup", Snapshots = admin }], ct);
            if (res[0].Status != OperationStatus.Success) errors.Add(res[0].Message + (res[0].Details is null ? string.Empty : Environment.NewLine + res[0].Details));
        }
        if (user.Count > 0)
        {
            var outcome = await Task.Run(() => TransactionRunner.Restore(user), ct);
            if (outcome.Status != OperationStatus.Success) errors.Add(outcome.Message + Environment.NewLine + outcome.Details);
        }

        log.Write(errors.Count == 0 ? LogLevel.Success : LogLevel.Error, $"Backup from {backup.Timestamp:g} restore: {(errors.Count == 0 ? "Success" : "Failed")}");
        return errors.Count == 0
            ? OperationResult.Ok("Backup", "All values from the backup were restored and verified.")
            : OperationResult.Fail("Backup", "Some values could not be restored.", string.Join(Environment.NewLine, errors));
    }
}
