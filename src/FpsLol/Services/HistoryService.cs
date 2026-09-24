using FpsLol.Models;
using FpsLol.Utilities;

namespace FpsLol.Services;

public interface IHistoryService
{
    IReadOnlyList<ChangeRecord> Records { get; }
    event EventHandler? Changed;
    void Add(IEnumerable<ChangeRecord> records);
    void MarkRestored(IEnumerable<Guid> ids);
    IReadOnlyList<ChangeRecord> ActiveFor(string sourceId);
    void ClearRestored();
}

/// <summary>Persistent change history (Restore Center). Stored in %LOCALAPPDATA%\FPS.LOL\history.json.</summary>
public sealed class HistoryService : IHistoryService
{
    private readonly object _gate = new();
    private readonly List<ChangeRecord> _records;

    public HistoryService()
    {
        _records = JsonStore.Load(AppPaths.HistoryFile, () => new List<ChangeRecord>());
    }

    public IReadOnlyList<ChangeRecord> Records
    {
        get { lock (_gate) return _records.OrderByDescending(r => r.Timestamp).ToList(); }
    }

    public event EventHandler? Changed;

    public void Add(IEnumerable<ChangeRecord> records)
    {
        lock (_gate)
        {
            _records.AddRange(records);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkRestored(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        lock (_gate)
        {
            foreach (var r in _records.Where(r => set.Contains(r.Id)))
            {
                r.Restored = true;
                r.RestoredAt = DateTime.Now;
            }
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<ChangeRecord> ActiveFor(string sourceId)
    {
        lock (_gate)
            return _records.Where(r => !r.Restored && r.SourceId == sourceId).OrderByDescending(r => r.Timestamp).ToList();
    }

    public void ClearRestored()
    {
        lock (_gate)
        {
            _records.RemoveAll(r => r.Restored);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Persist() => JsonStore.Save(AppPaths.HistoryFile, _records);
}
