using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FpsLol.Controls;
using FpsLol.SystemIntegration;

namespace FpsLol.Services;

public interface IThemeService
{
    void Apply(AppSettings settings);
    void AttachWindow(Window window);
}

/// <summary>
/// Applies the Liquid Glass look: the DWM acrylic backdrop (real blur of what is behind the window),
/// a tint layer and glass surface brushes whose translucency follows the "Liquid Glass intensity" setting.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const int BackdropNone = 1;
    private const int BackdropAcrylic = 3;
    private readonly List<Window> _windows = [];
    private AppSettings? _settings;

    public static bool BackdropSupported => WindowsInfo.Current.Build >= 22523;

    public void AttachWindow(Window window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) ApplyBackdrop(window);
        else window.SourceInitialized += (_, _) => ApplyBackdrop(window);
    }

    public void Apply(AppSettings settings)
    {
        _settings = settings;
        Motion.Configure(settings.AnimationIntensity, settings.ReduceAnimations);

        var g = Math.Clamp(settings.GlassIntensity / 100.0, 0, 1);
        var blur = settings.BlurEnabled && BackdropSupported;
        var res = Application.Current.Resources;

        // Tint over the acrylic backdrop: more intensity = more see-through glass.
        res["Brush.WindowTint"] = Freeze(new SolidColorBrush(blur
            ? Color.FromArgb((byte)(255 * (0.93 - 0.30 * g)), 0x0A, 0x0A, 0x0C)
            : Color.FromRgb(0x0A, 0x0A, 0x0C)));

        res["Brush.Glass"] = Freeze(Vertical(Alpha(0.035 + 0.06 * g), Alpha(0.015 + 0.03 * g)));
        res["Brush.GlassHover"] = Freeze(new SolidColorBrush(Color.FromArgb(Alpha(0.07 + 0.05 * g), 0xFF, 0xFF, 0xFF)));
        res["Brush.GlassBorder"] = Freeze(Vertical(Alpha(0.09 + 0.12 * g), Alpha(0.025 + 0.03 * g)));
        res["Brush.Sidebar"] = Freeze(new SolidColorBrush(Color.FromArgb(Alpha(0.02 + 0.04 * g), 0xFF, 0xFF, 0xFF)));

        foreach (var w in _windows) ApplyBackdrop(w);
    }

    private void ApplyBackdrop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = 2; // DWMWCP_ROUND
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        if (!BackdropSupported) return;

        var blur = _settings?.BlurEnabled ?? true;
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = blur ? Colors.Transparent : Color.FromRgb(0x0A, 0x0A, 0x0C);

        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int backdrop = blur ? BackdropAcrylic : BackdropNone;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }

    private static byte Alpha(double fraction) => (byte)Math.Clamp(Math.Round(fraction * 255), 0, 255);

    private static LinearGradientBrush Vertical(byte top, byte bottom) => new(
        Color.FromArgb(top, 0xFF, 0xFF, 0xFF), Color.FromArgb(bottom, 0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(0, 1));

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
