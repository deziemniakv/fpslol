using System.Windows;
using System.Windows.Media.Animation;

namespace FpsLol.Controls;

/// <summary>Global animation policy driven by Settings → Appearance (animation intensity / reduce animations).</summary>
public static class Motion
{
    private static double _intensity = 1.0;

    public static bool Reduced { get; private set; }

    /// <summary>0 = animations off, 1 = full.</summary>
    public static double Intensity => Reduced ? 0 : _intensity;

    public static bool Enabled => Intensity > 0.01;

    public static event EventHandler? Changed;

    public static void Configure(double intensityPercent, bool reduce)
    {
        _intensity = Math.Clamp(intensityPercent / 100.0, 0, 1);
        Reduced = reduce;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Scales a nominal duration by the intensity (lower intensity → shorter, subtler motion).</summary>
    public static Duration Duration(double milliseconds) =>
        new(TimeSpan.FromMilliseconds(Enabled ? milliseconds * (0.45 + 0.55 * Intensity) : 0));

    /// <summary>Scales a travel distance (e.g. slide offset in pixels).</summary>
    public static double Distance(double pixels) => pixels * Intensity;

    public static IEasingFunction EaseOut { get; } = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static IEasingFunction EaseInOut { get; } = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });

    private static IEasingFunction Freeze(CubicEase e)
    {
        e.Freeze();
        return e;
    }
}
