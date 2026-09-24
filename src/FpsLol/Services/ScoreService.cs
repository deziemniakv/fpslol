using FpsLol.Models;

namespace FpsLol.Services;

public sealed record ScoreItem(string Title, string Detail, int Points, int Max, string? Advice)
{
    public bool Passed => Points >= Max;
    public bool Partial => Points > 0 && Points < Max;
    public string Tone => Passed ? "success" : Partial ? "warning" : "danger";
    public string PointsText => $"{Points}/{Max}";
}

public sealed record ScoreReport(int Score, string Headline, IReadOnlyList<ScoreItem> Items, int AvailableOptimizations)
{
    public string AvailableText => AvailableOptimizations switch
    {
        0 => "No optimizations available",
        1 => "1 optimization available",
        _ => $"{AvailableOptimizations} optimizations available",
    };
}

public interface IScoreService
{
    ScoreReport Compute(IReadOnlyList<TweakStatus> tweaks, MetricsSnapshot? metrics, int enabledStartupApps);
}

/// <summary>
/// Deterministic System Score computed only from real, detected settings and state.
/// Settings that are not supported on this PC are excluded from the maximum, so they never cost points.
/// </summary>
public sealed class ScoreService : IScoreService
{
    private static readonly (string Id, int Weight)[] Weights =
    [
        ("game-mode", 15),
        ("power-plan", 15),
        ("background-recording", 12),
        ("hags", 8),
        ("windowed-optimizations", 5),
        ("game-dvr", 5),
        ("tcp-autotuning", 5),
        ("adapter-power-saving", 5),
        ("background-apps", 4),
        ("mouse-acceleration", 3),
    ];

    public ScoreReport Compute(IReadOnlyList<TweakStatus> tweaks, MetricsSnapshot? metrics, int enabledStartupApps)
    {
        var items = new List<ScoreItem>();

        foreach (var (id, weight) in Weights)
        {
            var status = tweaks.FirstOrDefault(t => t.Definition.Id == id);
            if (status is null || !status.Detection.Supported || status.Detection.StateUnknown) continue;
            var ok = status.Detection.IsOptimal;
            items.Add(new ScoreItem(
                status.Definition.Name,
                $"Current: {status.Detection.Current} · Recommended: {status.Detection.Recommended}",
                ok ? weight : 0, weight,
                ok ? null : "Available in Optimize and Tweaks."));
        }

        // Startup load
        var startupPoints = enabledStartupApps switch { <= 5 => 10, <= 10 => 6, <= 15 => 3, _ => 0 };
        items.Add(new ScoreItem("Startup apps", $"{enabledStartupApps} apps start with Windows", startupPoints, 10,
            startupPoints < 10 ? "Disable apps you do not need at startup in the Startup section." : null));

        // Memory headroom (current usage)
        if (metrics is not null && metrics.RamTotalBytes > 0)
        {
            var usage = metrics.RamUsage;
            var memPoints = usage < 70 ? 5 : usage < 85 ? 2 : 0;
            items.Add(new ScoreItem("Memory headroom", $"{usage:F0}% of RAM in use", memPoints, 5,
                memPoints < 5 ? "Close memory-heavy background apps before gaming (see Processes)." : null));
        }

        // System drive free space
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            var freePct = drive.TotalSize > 0 ? drive.AvailableFreeSpace * 100.0 / drive.TotalSize : 100;
            var diskPoints = freePct >= 15 ? 5 : freePct >= 8 ? 2 : 0;
            items.Add(new ScoreItem("System drive space", $"{freePct:F0}% free on {drive.Name.TrimEnd('\\')}", diskPoints, 5,
                diskPoints < 5 ? "Low free space slows shader caching and updates. Use Cleanup." : null));
        }
        catch
        {
            // Drive info unavailable — excluded from the score.
        }

        var max = items.Sum(i => i.Max);
        var score = max == 0 ? 0 : (int)Math.Round(items.Sum(i => i.Points) * 100.0 / max);
        var available = tweaks.Count(t => t.Detection.Supported && !t.Detection.IsOptimal && !t.Detection.StateUnknown);

        var headline = score switch
        {
            >= 90 => "Your system is optimized for gaming.",
            >= 75 => "Your system is in good shape for gaming.",
            >= 55 => "A few settings are holding back performance.",
            _ => "Several settings are holding back performance.",
        };
        return new ScoreReport(score, headline, items.OrderBy(i => i.Passed).ThenByDescending(i => i.Max).ToList(), available);
    }
}
