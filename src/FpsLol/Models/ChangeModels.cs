using FpsLol.Optimizations;

namespace FpsLol.Models;

/// <summary>One logical change requested by the user (a tweak, a profile setting, a DNS change...).</summary>
public sealed class ChangeRequest
{
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public string Before { get; init; } = string.Empty;
    public string After { get; init; } = string.Empty;
    public RestartRequirement Restart { get; init; }
    public required IReadOnlyList<SystemAction> Actions { get; init; }
    public bool RequiresAdmin => Actions.Any(a => a.RequiresAdmin);
}

/// <summary>A change recorded in the Restore Center with everything needed to undo it.</summary>
public sealed class ChangeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SessionId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public List<ActionSnapshot> Snapshots { get; set; } = [];
    public bool Restored { get; set; }
    public DateTime? RestoredAt { get; set; }
    public bool RequiresAdmin => Snapshots.Any(s => s.RequiresAdmin);
}

public sealed record ChangeResult(ChangeRequest Request, OperationResult Result, ChangeRecord? Record);

public sealed record ExecutionReport(
    IReadOnlyList<ChangeResult> Changes,
    OperationResult? RestorePoint,
    string? BackupFile)
{
    public bool RestartRequired => Changes.Any(c => c.Result.IsSuccess && c.Request.Restart == RestartRequirement.Restart);
    public bool SignOutRequired => Changes.Any(c => c.Result.IsSuccess && c.Request.Restart == RestartRequirement.SignOut);
    public int Count(OperationStatus s) => Changes.Count(c => c.Result.Status == s);
}

/// <summary>An FPS.LOL configuration backup written before a batch of changes.</summary>
public sealed class BackupSet
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Label { get; set; } = string.Empty;
    public List<BackupItem> Items { get; set; } = [];
}

public sealed class BackupItem
{
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public List<ActionSnapshot> Snapshots { get; set; } = [];
}
