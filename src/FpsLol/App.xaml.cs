using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FpsLol.Hardware;
using FpsLol.Logging;
using FpsLol.Networking;
using FpsLol.Optimizations.Privileged;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using FpsLol.ViewModels;
using FpsLol.Views;
using Microsoft.Extensions.DependencyInjection;

namespace FpsLol;

public partial class App : Application
{
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateRegistration;
    private ServiceProvider? _services;

    public static IServiceProvider Services => ((App)Current)._services!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ConfigureServices();
        var log = _services.GetRequiredService<ILogService>();
        var settings = _services.GetRequiredService<ISettingsService>();
        log.FileLoggingEnabled = settings.Current.LoggingEnabled;
        log.DebugEnabled = settings.Current.DebugMode;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Error("Fatal unhandled exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("Unobserved background task exception.", args.Exception);
            args.SetObserved();
        };

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        log.Info($"FPS.LOL {version} started ({(Elevation.IsElevated ? "Administrator" : "Standard user")}, {WindowsInfo.Current.Long})");

        _services.GetRequiredService<IThemeService>().Apply(settings.Current);
        _services.GetRequiredService<IMonitoringService>().Start();
        _ = _services.GetRequiredService<IHardwareService>().GetAsync();
        _ = _services.GetRequiredService<ISystemScanService>().EnsureAsync();

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        if (Program.StartMinimized && settings.Current.MinimizeToTray) window.HideToTray();
        else window.Show();

        // Second instances signal this event to bring the existing window to the front.
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ActivateEventName);
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(_activateEvent,
            (_, _) => Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray()), null, -1, false);
    }

    private static ServiceProvider ConfigureServices()
    {
        var s = new ServiceCollection();

        // Infrastructure
        s.AddSingleton<ILogService>(_ => new LogService());
        s.AddSingleton<ISettingsService, SettingsService>();
        s.AddSingleton<IThemeService, ThemeService>();
        s.AddSingleton<INavigationService, NavigationService>();
        s.AddSingleton<IDialogService, DialogService>();
        s.AddSingleton<IToastService, ToastService>();
        s.AddSingleton<ITrayService, TrayService>();

        // System / optimization
        s.AddSingleton<IPrivilegedExecutor, PrivilegedExecutor>();
        s.AddSingleton<IHistoryService, HistoryService>();
        s.AddSingleton<IBackupService, BackupService>();
        s.AddSingleton<IChangeService, ChangeService>();
        s.AddSingleton<ITweakEngine, TweakEngine>();
        s.AddSingleton<ITweakActionService, TweakActionService>();
        s.AddSingleton<IScoreService, ScoreService>();
        s.AddSingleton<ISystemScanService, SystemScanService>();
        s.AddSingleton<IProcessService, ProcessService>();
        s.AddSingleton<IStartupService, StartupService>();
        s.AddSingleton<ICleanupService, CleanupService>();
        s.AddSingleton<IPowerService, PowerService>();
        s.AddSingleton<INetworkService, NetworkService>();
        s.AddSingleton<IGameLibraryService, GameLibraryService>();
        s.AddSingleton<IGameProfileService, GameProfileService>();

        // Hardware
        s.AddSingleton<IHardwareService, HardwareService>();
        s.AddSingleton<IMonitoringService, MonitoringService>();
        s.AddSingleton<IFpsMonitorService, FpsMonitorService>();

        // View models (pages keep their state while the app runs)
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<FirstRunViewModel>();
        s.AddSingleton<DashboardViewModel>();
        s.AddSingleton<PerformanceViewModel>();
        s.AddSingleton<OptimizeViewModel>();
        s.AddSingleton<TweaksViewModel>();
        s.AddSingleton<GamingViewModel>();
        s.AddSingleton<PowerViewModel>();
        s.AddSingleton<NetworkViewModel>();
        s.AddSingleton<ProcessesViewModel>();
        s.AddSingleton<StartupViewModel>();
        s.AddSingleton<CleanupViewModel>();
        s.AddSingleton<RestoreViewModel>();
        s.AddSingleton<SettingsViewModel>();

        s.AddSingleton<MainWindow>();
        return s.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var log = _services?.GetService<ILogService>();
        log?.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        try
        {
            _ = _services?.GetService<IDialogService>()?.ShowErrorAsync("Something went wrong",
                "An unexpected error occurred. FPS.LOL is still running and no change was reported as successful unless it was verified.",
                e.Exception.ToString());
        }
        catch
        {
            // Never throw from the last-chance handler.
        }
    }

    /// <summary>Closes FPS.LOL completely (as opposed to hiding it in the tray).</summary>
    public static void ExitApplication()
    {
        if (Current is App app) app.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateRegistration?.Unregister(null);
        _activateEvent?.Dispose();
        try
        {
            _services?.GetService<ILogService>()?.Info("FPS.LOL closed");
            _services?.GetService<IFpsMonitorService>()?.Stop();
            _services?.Dispose();
        }
        catch
        {
            // Shutting down — ignore disposal errors.
        }
        base.OnExit(e);
    }
}
