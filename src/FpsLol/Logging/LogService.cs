using System.Text;
using FpsLol.Utilities;

namespace FpsLol.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error,
}

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message, string? Details)
{
    public string LevelText => Level switch
    {
        LogLevel.Warning => "WARN",
        _ => Level.ToString().ToUpperInvariant(),
    };

    public string Formatted => $"[{Timestamp:HH:mm:ss}] {LevelText} {Message}";

    public override string ToString() => Details is null ? Formatted : Formatted + Environment.NewLine + Details;
}

public interface ILogService
{
    bool FileLoggingEnabled { get; set; }
    bool DebugEnabled { get; set; }
    string CurrentLogFile { get; }
    IReadOnlyList<LogEntry> Recent();
    event EventHandler<LogEntry>? EntryAdded;
    void Write(LogLevel level, string message, Exception? exception = null);
}

/// <summary>Thread-safe logger: human-readable daily log files plus an in-memory ring for the UI.</summary>
public sealed class LogService : ILogService
{
    private const int MemoryCapacity = 1500;
    private readonly object _gate = new();
    private readonly LinkedList<LogEntry> _recent = new();
    private readonly string _suffix;

    public LogService(string suffix = "")
    {
        _suffix = suffix;
    }

    public bool FileLoggingEnabled { get; set; } = true;
    public bool DebugEnabled { get; set; }
    public string CurrentLogFile => Path.Combine(AppPaths.Logs, $"fpslol-{DateTime.Now:yyyyMMdd}{_suffix}.log");

    public event EventHandler<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Recent()
    {
        lock (_gate) return _recent.ToList();
    }

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (level == LogLevel.Debug && !DebugEnabled) return;

        string? details = exception is null ? null : (DebugEnabled ? exception.ToString() : $"{exception.GetType().Name}: {exception.Message}");
        var entry = new LogEntry(DateTime.Now, level, message, details);

        lock (_gate)
        {
            _recent.AddLast(entry);
            while (_recent.Count > MemoryCapacity) _recent.RemoveFirst();

            if (FileLoggingEnabled)
            {
                try
                {
                    File.AppendAllText(CurrentLogFile, entry + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Logging must never crash the app (e.g. disk full / file locked).
                }
            }
        }

        EntryAdded?.Invoke(this, entry);
    }
}

public static class LogExtensions
{
    public static void Debug(this ILogService log, string message) => log.Write(LogLevel.Debug, message);
    public static void Info(this ILogService log, string message) => log.Write(LogLevel.Info, message);
    public static void Success(this ILogService log, string message) => log.Write(LogLevel.Success, message);
    public static void Warn(this ILogService log, string message, Exception? ex = null) => log.Write(LogLevel.Warning, message, ex);
    public static void Error(this ILogService log, string message, Exception? ex = null) => log.Write(LogLevel.Error, message, ex);
}
