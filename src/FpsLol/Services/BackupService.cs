using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.Utilities;

namespace FpsLol.Services;

public sealed record BackupFileInfo(string Path, DateTime Timestamp, string Label, int Items);

public interface IBackupService
{
    string? Create(string label, IReadOnlyList<ChangeRequest> requests);
    IReadOnlyList<BackupFileInfo> List();
    BackupSet? Load(string path);
}

/// <summary>
/// Writes an FPS.LOL configuration backup (current value of every setting about to be changed)
/// before any batch of changes. Independent of the history file and of Windows System Restore.
/// </summary>
public sealed class BackupService(ILogService log) : IBackupService
{
    public string? Create(string label, IReadOnlyList<ChangeRequest> requests)
    {
        try
        {
            var set = new BackupSet { Label = label };
            foreach (var request in requests)
            {
                var item = new BackupItem { Title = request.Title, State = request.Before };
                foreach (var action in request.Actions)
                {
                    try
                    {
                        item.Snapshots.Add(action.Capture());
                    }
                    catch
                    {
                        // Some values (e.g. admin-only) cannot be read yet; the transaction captures them again before changing.
                    }
                }
                set.Items.Add(item);
            }

            var path = Path.Combine(AppPaths.Backups, $"backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            JsonStore.Save(path, set);
            log.Info($"Configuration backup created: {Path.GetFileName(path)}");
            return path;
        }
        catch (Exception ex)
        {
            log.Error("Could not create the configuration backup.", ex);
            return null;
        }
    }

    public IReadOnlyList<BackupFileInfo> List()
    {
        var result = new List<BackupFileInfo>();
        foreach (var file in Directory.EnumerateFiles(AppPaths.Backups, "backup-*.json").OrderByDescending(f => f))
        {
            var set = Load(file);
            if (set is not null) result.Add(new BackupFileInfo(file, set.Timestamp, set.Label, set.Items.Count));
        }
        return result;
    }

    public BackupSet? Load(string path) => JsonStore.Load<BackupSet?>(path, () => null);
}
