using FpsLol.Models;
using FpsLol.Storage;
using FpsLol.SystemIntegration;

namespace FpsLol.Optimizations.Privileged;

/// <summary>Executes worker jobs. Runs either in-process (when FPS.LOL is elevated) or inside the elevated helper.</summary>
public static class JobRunner
{
    public static WorkerJobResult Run(WorkerJob job, bool userMatches)
    {
        try
        {
            return job switch
            {
                ApplyJob apply => RunApply(apply, userMatches),
                RestoreJob restore => RunRestore(restore, userMatches),
                RestorePointJob rp => FromResult(job, RestorePoints.Create(rp.Description)),
                CleanupJob cleanup => RunCleanup(cleanup),
                KillProcessJob kill => RunKill(kill),
                _ => WorkerJobResult.For(job, OperationStatus.NotSupported, "Unknown operation."),
            };
        }
        catch (Exception ex)
        {
            return WorkerJobResult.For(job, OperationStatus.Failed, "Unexpected error.", ex.ToString());
        }
    }

    private static WorkerJobResult RunApply(ApplyJob job, bool userMatches)
    {
        if (!userMatches && job.Actions.Any(a => !a.RequiresAdmin))
            return WorkerJobResult.For(job, OperationStatus.Failed,
                "Per-user settings cannot be changed from a different administrator account.");
        var outcome = TransactionRunner.Apply(job.Actions);
        return new WorkerJobResult
        {
            Key = job.Key,
            Status = outcome.Status,
            Message = outcome.Message,
            Details = outcome.Details,
            Snapshots = outcome.Snapshots,
        };
    }

    private static WorkerJobResult RunRestore(RestoreJob job, bool userMatches)
    {
        if (!userMatches && job.Snapshots.Any(s => !s.RequiresAdmin))
            return WorkerJobResult.For(job, OperationStatus.Failed,
                "Per-user settings cannot be restored from a different administrator account.");
        var outcome = TransactionRunner.Restore(job.Snapshots);
        return WorkerJobResult.For(job, outcome.Status, outcome.Message, outcome.Details);
    }

    private static WorkerJobResult RunCleanup(CleanupJob job)
    {
        long freed = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var id in job.CategoryIds)
        {
            var category = CleanupEngine.Find(id);
            if (category is null)
            {
                errors.Add($"Unknown cleanup category '{id}'.");
                continue;
            }
            try
            {
                var run = CleanupEngine.Clean(category);
                freed += run.BytesFreed;
                deleted += run.FilesDeleted;
                skipped += run.FilesSkipped;
            }
            catch (Exception ex)
            {
                errors.Add($"{category.Name}: {TransactionRunner.Describe(ex)}");
            }
        }

        return new WorkerJobResult
        {
            Key = job.Key,
            Status = errors.Count == 0 ? OperationStatus.Success : (deleted > 0 ? OperationStatus.Success : OperationStatus.Failed),
            Message = errors.Count == 0 ? "Cleanup completed." : "Cleanup completed with errors.",
            Details = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
            BytesFreed = freed,
            FilesDeleted = deleted,
            FilesSkipped = skipped,
        };
    }

    private static WorkerJobResult RunKill(KillProcessJob job)
    {
        ProcessGuard.Kill(job.Pid, job.ProcessName);
        return WorkerJobResult.For(job, OperationStatus.Success, $"{job.ProcessName} was ended.");
    }

    private static WorkerJobResult FromResult(WorkerJob job, OperationResult result) =>
        WorkerJobResult.For(job, result.Status, result.Message, result.Details);
}
