using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Logging;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    private const int MaxLogLines = 400;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILogService _log;
    private readonly IToastService _toasts;
    private readonly IDialogService _dialogs;
    private bool _loading;

    public SettingsViewModel(ISettingsService settings, IThemeService theme, ILogService log, IToastService toasts, IDialogService dialogs)
    {
        _settings = settings;
        _theme = theme;
        _log = log;
        _toasts = toasts;
        _dialogs = dialogs;

        foreach (var e in log.Recent().TakeLast(MaxLogLines)) Logs.Add(e);
        log.EntryAdded += (_, e) => Ui.Post(() =>
        {
            Logs.Add(e);
            while (Logs.Count > MaxLogLines) Logs.RemoveAt(0);
        });
        Load();
    }

    public override string Title => "Settings";
    public override string Subtitle => "Preferences, appearance and diagnostics";

    public ObservableCollection<LogEntry> Logs { get; } = [];

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _notifications;
    [ObservableProperty] private double _glassIntensity;
    [ObservableProperty] private bool _blurEnabled;
    [ObservableProperty] private double _animationIntensity;
    [ObservableProperty] private bool _reduceAnimations;
    [ObservableProperty] private bool _askBeforeApplying;
    [ObservableProperty] private bool _createRestorePoint;
    [ObservableProperty] private bool _loggingEnabled;
    [ObservableProperty] private bool _debugMode;

    public bool BlurSupported => ThemeService.BackdropSupported;
    public string BlurNote => BlurSupported ? "Uses the Windows acrylic backdrop to blur what is behind the window." : "Requires Windows 11 (build 22523 or newer).";
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    public string RuntimeText => $".NET {Environment.Version} · {(Environment.Is64BitProcess ? "x64" : "x86")} · {(Elevation.IsElevated ? "Administrator" : "Standard user")}";
    public string DataFolder => AppPaths.Root;
    public string WindowsText => WindowsInfo.Current.Long;

    private void Load()
    {
        _loading = true;
        var s = _settings.Current;
        StartWithWindows = s.StartWithWindows;
        MinimizeToTray = s.MinimizeToTray;
        Notifications = s.Notifications;
        GlassIntensity = s.GlassIntensity;
        BlurEnabled = s.BlurEnabled;
        AnimationIntensity = s.AnimationIntensity;
        ReduceAnimations = s.ReduceAnimations;
        AskBeforeApplying = s.AskBeforeApplying;
        CreateRestorePoint = s.CreateRestorePoint;
        LoggingEnabled = s.LoggingEnabled;
        DebugMode = s.DebugMode;
        _loading = false;
    }

    public override Task OnNavigatedToAsync()
    {
        Load();
        return Task.CompletedTask;
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        try
        {
            _settings.SetStartWithWindows(value);
            _log.Info($"Start with Windows: {(value ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            _toasts.Error("Start with Windows", "The setting could not be changed.", ex.Message);
            _loading = true;
            StartWithWindows = !value;
            _loading = false;
        }
    }

    partial void OnMinimizeToTrayChanged(bool value) => Save(s => s.MinimizeToTray = value);
    partial void OnNotificationsChanged(bool value) => Save(s => s.Notifications = value);
    partial void OnGlassIntensityChanged(double value) => Save(s => s.GlassIntensity = Math.Round(value), applyTheme: true);
    partial void OnBlurEnabledChanged(bool value) => Save(s => s.BlurEnabled = value, applyTheme: true);
    partial void OnAnimationIntensityChanged(double value) => Save(s => s.AnimationIntensity = Math.Round(value), applyTheme: true);
    partial void OnReduceAnimationsChanged(bool value) => Save(s => s.ReduceAnimations = value, applyTheme: true);
    partial void OnAskBeforeApplyingChanged(bool value) => Save(s => s.AskBeforeApplying = value);
    partial void OnCreateRestorePointChanged(bool value) => Save(s => s.CreateRestorePoint = value);

    partial void OnLoggingEnabledChanged(bool value)
    {
        Save(s => s.LoggingEnabled = value);
        _log.FileLoggingEnabled = value;
    }

    partial void OnDebugModeChanged(bool value)
    {
        Save(s => s.DebugMode = value);
        _log.DebugEnabled = value;
        if (!_loading) _log.Info($"Debug mode {(value ? "enabled" : "disabled")}");
    }

    private void Save(Action<AppSettings> apply, bool applyTheme = false)
    {
        if (_loading) return;
        apply(_settings.Current);
        _settings.Save();
        if (applyTheme) _theme.Apply(_settings.Current);
    }

    [RelayCommand]
    private static void OpenLogsFolder() => ProcessRunner.OpenShell("explorer.exe", $"\"{AppPaths.Logs}\"");

    [RelayCommand]
    private static void OpenDataFolder() => ProcessRunner.OpenShell("explorer.exe", $"\"{AppPaths.Root}\"");

    [RelayCommand]
    private void CopyLogs()
    {
        try
        {
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, Logs.Select(l => l.ToString())));
            _toasts.Success("Logs copied", $"{Logs.Count} lines copied to the clipboard.");
        }
        catch (Exception ex)
        {
            _toasts.Error("Copy failed", "The clipboard is in use by another application.", ex.Message);
        }
    }

    [RelayCommand]
    private void ClearLogView() => Logs.Clear();

    [RelayCommand]
    private async Task ResetFirstRunAsync()
    {
        if (!await _dialogs.ConfirmAsync("Show the welcome screen again?", "The welcome screen and initial system scan will appear the next time FPS.LOL starts.", "Confirm"))
            return;
        _settings.Current.FirstRunCompleted = false;
        _settings.Save();
        _toasts.Info("Welcome screen", "It will be shown on next start.");
    }
}
