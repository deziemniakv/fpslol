namespace FpsLol.Models;

public enum OperationStatus
{
    Success,
    Failed,
    Skipped,
    NotSupported,
}

/// <summary>Outcome of any user-visible operation. FPS.LOL never reports success unless it was verified.</summary>
public sealed record OperationResult(string Title, OperationStatus Status, string Message, string? Details = null)
{
    public static OperationResult Ok(string title, string message) => new(title, OperationStatus.Success, message);
    public static OperationResult Fail(string title, string message, string? details = null) => new(title, OperationStatus.Failed, message, details);
    public static OperationResult Skip(string title, string message) => new(title, OperationStatus.Skipped, message);
    public static OperationResult Unsupported(string title, string message) => new(title, OperationStatus.NotSupported, message);

    public bool IsSuccess => Status == OperationStatus.Success;

    public string StatusText => Status switch
    {
        OperationStatus.Success => "Success",
        OperationStatus.Failed => "Failed",
        OperationStatus.Skipped => "Skipped",
        _ => "Not supported",
    };
}
