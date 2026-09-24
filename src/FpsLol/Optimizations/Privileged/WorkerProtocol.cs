using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FpsLol.Models;
using FpsLol.Utilities;

namespace FpsLol.Optimizations.Privileged;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$job")]
[JsonDerivedType(typeof(ApplyJob), "apply")]
[JsonDerivedType(typeof(RestoreJob), "restore")]
[JsonDerivedType(typeof(RestorePointJob), "restore-point")]
[JsonDerivedType(typeof(CleanupJob), "cleanup")]
[JsonDerivedType(typeof(KillProcessJob), "kill")]
public abstract class WorkerJob
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
}

public sealed class ApplyJob : WorkerJob
{
    public List<SystemAction> Actions { get; set; } = [];
}

public sealed class RestoreJob : WorkerJob
{
    public List<ActionSnapshot> Snapshots { get; set; } = [];
}

public sealed class RestorePointJob : WorkerJob
{
    public string Description { get; set; } = "FPS.LOL optimization";
}

public sealed class CleanupJob : WorkerJob
{
    public List<string> CategoryIds { get; set; } = [];
}

public sealed class KillProcessJob : WorkerJob
{
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
}

public sealed class WorkerJobResult
{
    public string Key { get; set; } = string.Empty;
    public OperationStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public List<ActionSnapshot> Snapshots { get; set; } = [];
    public long BytesFreed { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesSkipped { get; set; }

    public static WorkerJobResult For(WorkerJob job, OperationStatus status, string message, string? details = null) =>
        new() { Key = job.Key, Status = status, Message = message, Details = details };
}

public sealed class WorkerRequest
{
    public string UserSid { get; set; } = string.Empty;
    public List<WorkerJob> Jobs { get; set; } = [];
}

public sealed class WorkerResponse
{
    public List<WorkerJobResult> Results { get; set; } = [];
}

/// <summary>Length-prefixed JSON frames over the named pipe.</summary>
public static class PipeFraming
{
    private const int MaxFrame = 16 * 1024 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonStore.Options));
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrame) throw new InvalidDataException("Invalid frame length.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(body, JsonStore.Options) ?? throw new InvalidDataException("Empty frame.");
    }
}
