using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Optimizations.Privileged;
using FpsLol.Storage;
using FpsLol.SystemIntegration;

namespace FpsLol.Services;

public sealed record CleanupSummary(long BytesFreed, int FilesDeleted, int FilesSkipped, IReadOnlyList<OperationResult> Results);

public interface ICleanupService
{
    Task<IReadOnlyList<CleanupScan>> ScanAsync(IEnumerable<CleanupCategory> categories, CancellationToken ct = default);
    Task<CleanupSummary> CleanAsync(IEnumerable<CleanupCategory> categories, CancellationToken ct = default);
}

public sealed class CleanupService(ILogService log, IPrivilegedExecutor privileged) : ICleanupService
{
    public Task<IReadOnlyList<CleanupScan>> ScanAsync(IEnumerable<CleanupCategory> categories, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<CleanupScan>>(() =>
        {
            log.Info("Cleanup scan started");
            var list = categories.Select(c => CleanupEngine.Scan(c, ct)).ToList();
            log.Info($"Cleanup scan finished: {Utilities.Format.Bytes(list.Sum(s => s.Bytes))} can be cleaned");
            return list;
        }, ct);

    public async Task<CleanupSummary> CleanAsync(IEnumerable<CleanupCategory> categories, CancellationToken ct = default)
    {
        var list = categories.ToList();
        var results = new List<OperationResult>();
        long freed = 0;
        int deleted = 0, skipped = 0;

        var admin = list.Where(c => c.RequiresAdmin && !Elevation.IsElevated).ToList();
        var local = list.Except(admin).ToList();

        foreach (var category in local)
        {
            try
            {
                var run = await Task.Run(() => CleanupEngine.Clean(category, ct), ct);
                freed += run.BytesFreed;
                deleted += run.FilesDeleted;
                skipped += run.FilesSkipped;
                results.Add(OperationResult.Ok(category.Name, $"Removed {run.FilesDeleted} files ({Utilities.Format.Bytes(run.BytesFreed)})"
                                                                + (run.FilesSkipped > 0 ? $", {run.FilesSkipped} in use were skipped." : ".")));
            }
            catch (Exception ex)
            {
                results.Add(OperationResult.Fail(category.Name, "Cleanup failed.", ex.Message));
            }
        }

        if (admin.Count > 0)
        {
            var job = new CleanupJob { Title = "Cleanup", CategoryIds = admin.Select(c => c.Id).ToList() };
            var res = (await privileged.RunAsync([job], ct))[0];
            freed += res.BytesFreed;
            deleted += res.FilesDeleted;
            skipped += res.FilesSkipped;
            var names = string.Join(", ", admin.Select(a => a.Name));
            results.Add(res.Status == OperationStatus.Success
                ? OperationResult.Ok(names, $"Removed {res.FilesDeleted} files ({Utilities.Format.Bytes(res.BytesFreed)}).")
                : new OperationResult(names, res.Status, res.Message, res.Details));
        }

        log.Write(results.All(r => r.IsSuccess) ? LogLevel.Success : LogLevel.Warning,
            $"Cleanup finished: {Utilities.Format.Bytes(freed)} freed, {deleted} files removed, {skipped} skipped");
        return new CleanupSummary(freed, deleted, skipped, results);
    }
}
