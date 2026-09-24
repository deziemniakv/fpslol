using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Services;
using FpsLol.SystemIntegration;

namespace FpsLol.ViewModels;

public sealed record NavItem(string Title, string Icon, Type PageType);

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private bool _syncing;

    public MainViewModel(INavigationService navigation, IDialogService dialogs, IToastService toasts, FirstRunViewModel firstRun)
    {
        _navigation = navigation;
        _dialogs = dialogs;
        Toasts = toasts.Toasts;
        FirstRun = firstRun;

        NavItems =
        [
            new("Dashboard", "Icon.Dashboard", typeof(DashboardViewModel)),
            new("Performance", "Icon.Activity", typeof(PerformanceViewModel)),
            new("Optimize", "Icon.Zap", typeof(OptimizeViewModel)),
            new("Tweaks", "Icon.Sliders", typeof(TweaksViewModel)),
            new("Gaming", "Icon.Gamepad", typeof(GamingViewModel)),
            new("Power", "Icon.Power", typeof(PowerViewModel)),
            new("Network", "Icon.Wifi", typeof(NetworkViewModel)),
            new("Processes", "Icon.List", typeof(ProcessesViewModel)),
            new("Startup", "Icon.Rocket", typeof(StartupViewModel)),
            new("Cleanup", "Icon.Trash", typeof(CleanupViewModel)),
            new("Restore", "Icon.History", typeof(RestoreViewModel)),
            new("Settings", "Icon.Settings", typeof(SettingsViewModel)),
        ];

        navigation.Navigated += (_, page) =>
        {
            CurrentPage = page;
            _syncing = true;
            SelectedNav = NavItems.FirstOrDefault(n => n.PageType == page.GetType());
            _syncing = false;
        };
        dialogs.CurrentChanged += (_, _) => Dialog = dialogs.Current;
        firstRun.Finished += (_, _) => IsFirstRunVisible = false;
    }

    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<Toast> Toasts { get; }
    public FirstRunViewModel FirstRun { get; }

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private PageViewModel? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShellEnabled))]
    private DialogViewModel? _dialog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShellEnabled))]
    private bool _isFirstRunVisible;

    /// <summary>The page and sidebar are disabled (keyboard, mouse and automation) while a dialog or the welcome screen is open.</summary>
    public bool IsShellEnabled => Dialog is null && !IsFirstRunVisible;

    public string WindowsVersion => $"{WindowsInfo.Current.Short} · {WindowsInfo.Current.FullBuild}";
    public bool IsElevated => Elevation.IsElevated;
    public string PrivilegeText => Elevation.IsElevated ? "Administrator" : "Standard user";
    public string AppVersion => "FPS.LOL " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (!_syncing && value is not null) _navigation.Navigate(value.PageType);
    }

    [RelayCommand]
    private async Task RestartAsAdminAsync()
    {
        if (!await _dialogs.ConfirmAsync("Restart as administrator?",
                "FPS.LOL normally runs as a standard user and asks for permission only for individual operations. Running elevated enables features such as the frame-rate monitor and reading admin-only settings.",
                "Restart"))
            return;

        if (Elevation.TryRestartElevated()) App.ExitApplication();
        else await _dialogs.ShowInfoAsync("Not restarted", "Administrator permission was declined. FPS.LOL keeps running as a standard user.");
    }
}
