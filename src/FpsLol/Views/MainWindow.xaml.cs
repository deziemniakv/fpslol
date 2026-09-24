using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FpsLol.Controls;
using FpsLol.Hardware;
using FpsLol.Services;
using FpsLol.ViewModels;

namespace FpsLol.Views;

public partial class MainWindow : Window
{
    private readonly ITrayService _tray;
    private readonly ISettingsService _settings;
    private readonly IMonitoringService _monitoring;
    private readonly MainViewModel _vm;
    private bool _exiting;
    private bool _trayHintShown;

    public MainWindow(MainViewModel vm, ITrayService tray, ISettingsService settings, IThemeService theme,
        INavigationService navigation, IMonitoringService monitoring, IToastService toasts)
    {
        InitializeComponent();
        _vm = vm;
        _tray = tray;
        _settings = settings;
        _monitoring = monitoring;
        DataContext = vm;

        theme.AttachWindow(this);
        _tray.Initialize(ShowFromTray, () =>
        {
            _exiting = true;
            App.ExitApplication();
        });

        toasts.Shown += (_, t) =>
        {
            if (!IsVisible && _settings.Current.Notifications) _tray.Notify(t.Title, t.Message, t.Tone == "danger");
        };

        StateChanged += (_, _) =>
        {
            UpdateMaximizedState();
            // No live metrics are needed while minimized; resume when the window is restored.
            _monitoring.IsPaused = WindowState == WindowState.Minimized || !IsVisible;
            StartAmbientMotion();
        };
        Loaded += (_, _) => UpdateMaximizedState();
        Activated += (_, _) => StartAmbientMotion();
        Deactivated += (_, _) => StopAmbientMotion();
        Motion.Changed += (_, _) => StartAmbientMotion();

        var start = vm.NavItems.FirstOrDefault(n => string.Equals(n.Title, Program.StartPage, StringComparison.OrdinalIgnoreCase));
        navigation.Navigate(start?.PageType ?? typeof(DashboardViewModel));
        vm.IsFirstRunVisible = !settings.Current.FirstRunCompleted || Program.ShowWelcome;
    }

    // ------------------------------------------------------------------ Tray behaviour

    public void HideToTray()
    {
        _tray.SetVisible(true);
        Hide();
        _monitoring.IsPaused = true;
        if (!_trayHintShown && _settings.Current.Notifications)
        {
            _tray.Notify("FPS.LOL is still running", "FPS.LOL was minimized to the tray. Right-click the icon to exit.", false);
            _trayHintShown = true;
        }
    }

    public void ShowFromTray()
    {
        _monitoring.IsPaused = false;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _tray.SetVisible(false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        base.OnClosed(e);
        App.ExitApplication();
    }

    // ------------------------------------------------------------------ Caption buttons

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>A maximized chrome-less window overhangs the screen by the resize frame; compensate so nothing is clipped.</summary>
    private void UpdateMaximizedState()
    {
        if (WindowState == WindowState.Maximized)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0) dpi = 96;
            var px = GetSystemMetricsForDpi(32 /* SM_CXSIZEFRAME */, dpi) + GetSystemMetricsForDpi(92 /* SM_CXPADDEDBORDER */, dpi);
            var dip = px * 96.0 / dpi;
            Root.Margin = new Thickness(dip);
            MaximizeIcon.Data = (Geometry)FindResource("Icon.Restore");
            MaximizeButton.ToolTip = "Restore down";
        }
        else
        {
            Root.Margin = new Thickness(0);
            MaximizeIcon.Data = (Geometry)FindResource("Icon.Square");
            MaximizeButton.ToolTip = "Maximize";
        }
    }

    /// <summary>
    /// Very slow drift of the ambient light pools — the "liquid" part of the glass. It runs at a low frame rate
    /// and only while the window is the active window, so FPS.LOL costs (almost) nothing while you play.
    /// </summary>
    private void StartAmbientMotion()
    {
        StopAmbientMotion();
        if (!Motion.Enabled || !IsActive || WindowState == WindowState.Minimized) return;

        LightAShift.BeginAnimation(TranslateTransform.XProperty, Drift(LightAShift.X, 140, 24), HandoffBehavior.SnapshotAndReplace);
        LightBShift.BeginAnimation(TranslateTransform.YProperty, Drift(LightBShift.Y, -160, 30), HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation Drift(double from, double to, int seconds)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = Motion.EaseInOut,
        };
        Timeline.SetDesiredFrameRate(a, 12); // imperceptible for a slow drift, a fraction of the render cost
        return a;
    }

    private void StopAmbientMotion()
    {
        // Freeze the lights where they are (no jump back to the origin).
        var ax = LightAShift.X;
        var by = LightBShift.Y;
        LightAShift.BeginAnimation(TranslateTransform.XProperty, null);
        LightBShift.BeginAnimation(TranslateTransform.YProperty, null);
        LightAShift.X = ax;
        LightBShift.Y = by;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
