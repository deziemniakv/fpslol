using FpsLol.Optimizations;

namespace FpsLol.Models;

public enum RiskLevel
{
    Safe,
    Moderate,
    Advanced,
}

public enum RestartRequirement
{
    None,
    RestartGame,
    SignOut,
    Restart,
}

public enum TweakCategory
{
    WindowsGaming,
    Input,
    Gpu,
    Power,
    Network,
    BackgroundProcesses,
    PrivacyServices,
    VisualEffects,
}

public static class CategoryNames
{
    public static string Of(TweakCategory c) => c switch
    {
        TweakCategory.WindowsGaming => "Windows Gaming",
        TweakCategory.Input => "Input",
        TweakCategory.Gpu => "GPU",
        TweakCategory.Power => "Power",
        TweakCategory.Network => "Network",
        TweakCategory.BackgroundProcesses => "Background Processes",
        TweakCategory.PrivacyServices => "Privacy & Background Services",
        TweakCategory.VisualEffects => "Visual Effects",
        _ => c.ToString(),
    };

    public static string Of(RestartRequirement r) => r switch
    {
        RestartRequirement.RestartGame => "Restart games",
        RestartRequirement.SignOut => "Sign out required",
        RestartRequirement.Restart => "Restart required",
        _ => "No restart",
    };
}

public sealed record TweakDetection(
    bool Supported,
    string Current,
    string Recommended,
    bool IsOptimal,
    string? UnsupportedReason = null,
    bool StateUnknown = false)
{
    public static TweakDetection Of(string current, string recommended, bool optimal) => new(true, current, recommended, optimal);

    public static TweakDetection NotSupported(string reason) => new(false, "N/A", "N/A", false, reason);

    public static TweakDetection Unknown(string recommended, string reason) => new(true, "Unknown", recommended, false, reason, StateUnknown: true);
}

/// <summary>Everything a tweak needs to decide whether it applies to this PC.</summary>
public sealed record TweakContext(
    int Build,
    bool IsElevated,
    bool HasBattery,
    bool ModernStandby,
    bool GpuSchedulingSupported,
    bool GpuSchedulingEnabledNow,
    string? PrimaryAdapterName,
    string? PrimaryAdapterPnpId);

public sealed class TweakDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required TweakCategory Category { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Safe;
    public RestartRequirement Restart { get; init; } = RestartRequirement.None;
    public bool RequiresAdmin { get; init; }
    public int MinBuild { get; init; } = 10240;

    /// <summary>Whether One-Click Optimize pre-selects this tweak (only conservative, broadly beneficial ones).</summary>
    public Func<TweakContext, bool> Preselect { get; init; } = _ => true;

    public required Func<TweakContext, TweakDetection> Detect { get; init; }

    /// <summary>Actions that bring the setting to the recommended state.</summary>
    public required Func<TweakContext, IReadOnlyList<SystemAction>> Apply { get; init; }

    /// <summary>Actions that reset the setting to the Windows default (used when there is no FPS.LOL history).</summary>
    public required Func<TweakContext, IReadOnlyList<SystemAction>> Defaults { get; init; }

    public string CategoryName => CategoryNames.Of(Category);
}
