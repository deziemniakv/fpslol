namespace FpsLol.Models;

public enum PriorityOption
{
    Default,
    AboveNormal,
    High,
}

public sealed record DetectedGame(string Name, string Source, string InstallDir, string? ExecutablePath);

public sealed class GameProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = "Manual";
    public string InstallDir { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public PriorityOption Priority { get; set; } = PriorityOption.Default;
    public bool HighPerformanceGpu { get; set; }
    public bool DisableFullscreenOptimizations { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime Added { get; set; } = DateTime.Now;
    public DateTime? LastApplied { get; set; }

    public string ExecutableName => Path.GetFileName(ExecutablePath);
}

public sealed record ProfileState(PriorityOption Priority, bool HighPerformanceGpu, bool FullscreenOptimizationsDisabled);
