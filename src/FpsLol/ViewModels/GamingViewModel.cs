using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;
using FpsLol.ViewModels.Items;
using Microsoft.Win32;

namespace FpsLol.ViewModels;

public sealed partial class GameProfileItem : ObservableObject
{
    public GameProfileItem(GameProfile profile) => Profile = profile;

    public GameProfile Profile { get; }
    public string Name => Profile.Name;
    public string Source => Profile.Source;
    public string ExecutableName => Profile.ExecutableName;
    public string ExecutablePath => Profile.ExecutablePath;
    public string Initials => string.Concat(Profile.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    [ObservableProperty] private PriorityOption _priority;
    [ObservableProperty] private bool _highPerformanceGpu;
    [ObservableProperty] private bool _disableFullscreenOptimizations;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private bool _hasPendingChanges;
    [ObservableProperty] private bool _hasAppliedSettings;
    [ObservableProperty] private bool _exeExists;

    public void Load(ProfileState state, bool hasRecords)
    {
        Priority = Profile.Priority;
        HighPerformanceGpu = Profile.HighPerformanceGpu;
        DisableFullscreenOptimizations = Profile.DisableFullscreenOptimizations;
        Notes = Profile.Notes;
        ExeExists = File.Exists(Profile.ExecutablePath);
        HasAppliedSettings = hasRecords;
        Refresh(state);
    }

    public void Refresh(ProfileState state)
    {
        HasPendingChanges = state.Priority != Priority || state.HighPerformanceGpu != HighPerformanceGpu
                            || state.FullscreenOptimizationsDisabled != DisableFullscreenOptimizations;
        StateText = HasPendingChanges ? "Changes not applied" : "Applied";
    }
}

public sealed partial class GamingViewModel : PageViewModel
{
    private readonly ISystemScanService _scan;
    private readonly IGameLibraryService _library;
    private readonly IGameProfileService _profiles;
    private readonly IChangeService _changes;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly INavigationService _navigation;
    private bool _loadingProfile;

    public GamingViewModel(ISystemScanService scan, IGameLibraryService library, IGameProfileService profiles, IChangeService changes,
        IDialogService dialogs, IToastService toasts, INavigationService navigation)
    {
        _scan = scan;
        _library = library;
        _profiles = profiles;
        _changes = changes;
        _dialogs = dialogs;
        _toasts = toasts;
        _navigation = navigation;
        scan.Scanned += (_, r) => Ui.Post(() => ApplyScan(r));
    }

    public override string Title => "Gaming";
    public override string Subtitle => "Gaming status and per-game profiles";

    public ObservableCollection<StatusItem> Status { get; } = [];
    public ObservableCollection<GameProfileItem> Profiles { get; } = [];
    public ObservableCollection<DetectedGame> Suggestions { get; } = [];

    public IReadOnlyList<PriorityOption> PriorityOptions { get; } = Enum.GetValues<PriorityOption>();

    [ObservableProperty] private GameProfileItem? _selected;
    [ObservableProperty] private bool _isDetecting;
    [ObservableProperty] private bool _detectionDone;

    public bool HasProfiles => Profiles.Count > 0;
    public bool HasSuggestions => Suggestions.Count > 0;

    public override async Task OnNavigatedToAsync()
    {
        LoadProfiles();
        ApplyScan(await _scan.EnsureAsync());
        if (!DetectionDone) await DetectGamesAsync();
    }

    private void ApplyScan(ScanResult r)
    {
        Status.Clear();
        foreach (var s in GamingInfo.BuildStatus(r.Tweaks)) Status.Add(s);
    }

    private void LoadProfiles()
    {
        var selectedId = Selected?.Profile.Id;
        Profiles.Clear();
        foreach (var p in _profiles.Profiles)
        {
            var item = new GameProfileItem(p);
            item.Load(_profiles.ReadState(p), _profiles.ActiveRecords(p).Count > 0);
            item.PropertyChanged += OnProfileEdited;
            Profiles.Add(item);
        }
        Selected = Profiles.FirstOrDefault(p => p.Profile.Id == selectedId) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasProfiles));
    }

    private void OnProfileEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loadingProfile || sender is not GameProfileItem item) return;
        if (e.PropertyName is not (nameof(GameProfileItem.Priority) or nameof(GameProfileItem.HighPerformanceGpu)
            or nameof(GameProfileItem.DisableFullscreenOptimizations) or nameof(GameProfileItem.Notes))) return;

        item.Profile.Priority = item.Priority;
        item.Profile.HighPerformanceGpu = item.HighPerformanceGpu;
        item.Profile.DisableFullscreenOptimizations = item.DisableFullscreenOptimizations;
        item.Profile.Notes = item.Notes;
        _profiles.Update(item.Profile);
        item.Refresh(_profiles.ReadState(item.Profile));
    }

    [RelayCommand]
    private async Task DetectGamesAsync()
    {
        IsDetecting = true;
        try
        {
            var games = await _library.DetectAsync();
            Suggestions.Clear();
            var known = _profiles.Profiles.Select(p => p.ExecutablePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var g in games.Where(g => g.ExecutablePath is not null && !known.Contains(g.ExecutablePath))) Suggestions.Add(g);
            DetectionDone = true;
        }
        finally
        {
            IsDetecting = false;
            OnPropertyChanged(nameof(HasSuggestions));
        }
    }

    [RelayCommand]
    private void AddDetected(DetectedGame? game)
    {
        if (game?.ExecutablePath is null) return;
        var profile = _profiles.Add(game.Name, game.ExecutablePath, game.Source, game.InstallDir);
        Suggestions.Remove(game);
        OnPropertyChanged(nameof(HasSuggestions));
        LoadProfiles();
        Selected = Profiles.FirstOrDefault(p => p.Profile.Id == profile.Id);
        _toasts.Success("Game added", $"{game.Name} was added to your profiles.");
    }

    [RelayCommand]
    private void AddManual()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the game executable",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        var exe = dialog.FileName;
        string name;
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName.Trim() : Path.GetFileNameWithoutExtension(exe);
        }
        catch
        {
            name = Path.GetFileNameWithoutExtension(exe);
        }
        var profile = _profiles.Add(name, exe, "Manual", Path.GetDirectoryName(exe) ?? string.Empty);
        LoadProfiles();
        Selected = Profiles.FirstOrDefault(p => p.Profile.Id == profile.Id);
    }

    [RelayCommand]
    private async Task ApplyProfileAsync()
    {
        if (Selected is not { } item) return;
        if (!item.ExeExists)
        {
            await _dialogs.ShowErrorAsync(item.Name, "The game executable no longer exists at the saved path.", item.ExecutablePath);
            return;
        }

        var requests = _profiles.BuildApply(item.Profile);
        if (requests.Count == 0)
        {
            _toasts.Info(item.Name, "The profile is already applied.");
            return;
        }

        var lines = requests.Select(r => $"{r.Title.Replace(item.Name + ": ", string.Empty)}: {r.Before} → {r.After}").ToList();
        if (requests.Any(r => r.RequiresAdmin) && !Elevation.IsElevated) lines.Add("Windows will ask for administrator permission (CPU priority)");
        lines.Add("Takes effect the next time the game starts");
        if (!await _dialogs.ConfirmAsync($"Apply profile for {item.Name}?", "These per-game settings will be applied and recorded in the Restore Center.", "Apply profile", items: lines))
            return;

        IsBusy = true;
        try
        {
            var report = await _changes.ExecuteAsync(requests, createRestorePoint: false, $"Game profile: {item.Name}");
            item.Profile.LastApplied = DateTime.Now;
            _profiles.Update(item.Profile);
            var failed = report.Changes.Where(c => c.Result.Status == OperationStatus.Failed).ToList();
            if (failed.Count == 0) _toasts.Success(item.Name, "Profile applied. Restart the game for it to take effect.");
            else await _dialogs.ShowErrorAsync(item.Name, $"{failed.Count} setting(s) could not be applied.", string.Join(Environment.NewLine, failed.Select(f => $"{f.Result.Title}: {f.Result.Message} {f.Result.Details}")));
        }
        finally
        {
            IsBusy = false;
            LoadProfiles();
        }
    }

    [RelayCommand]
    private async Task RevertProfileAsync()
    {
        if (Selected is not { } item) return;
        var records = _profiles.ActiveRecords(item.Profile);
        if (records.Count == 0)
        {
            _toasts.Info(item.Name, "FPS.LOL has not changed any settings for this game.");
            return;
        }
        if (!await _dialogs.ConfirmAsync($"Revert {item.Name}?", "The per-game settings FPS.LOL changed will be restored to their previous values.", "Revert", items: records.Select(r => $"{r.Title}: {r.After} → {r.Before}").ToList()))
            return;

        IsBusy = true;
        try
        {
            var results = await _changes.RestoreAsync(records);
            var failed = results.Where(r => !r.Result.IsSuccess).ToList();
            if (failed.Count == 0)
            {
                var state = _profiles.ReadState(item.Profile);
                item.Profile.Priority = state.Priority;
                item.Profile.HighPerformanceGpu = state.HighPerformanceGpu;
                item.Profile.DisableFullscreenOptimizations = state.FullscreenOptimizationsDisabled;
                _profiles.Update(item.Profile);
                _toasts.Success(item.Name, "Per-game settings restored.");
            }
            else
            {
                await _dialogs.ShowErrorAsync(item.Name, "Some settings could not be restored.", string.Join(Environment.NewLine, failed.Select(f => $"{f.Record.Title}: {f.Result.Message}")));
            }
        }
        finally
        {
            IsBusy = false;
            LoadProfiles();
        }
    }

    [RelayCommand]
    private async Task RemoveProfileAsync()
    {
        if (Selected is not { } item) return;
        var records = _profiles.ActiveRecords(item.Profile);
        var message = records.Count > 0
            ? "The profile will be removed. Its applied settings stay active — revert them first if you want Windows defaults back (they also remain in the Restore Center)."
            : "The profile will be removed from FPS.LOL. No system settings are changed.";
        if (!await _dialogs.ConfirmAsync($"Remove {item.Name}?", message, "Remove", danger: true)) return;
        _profiles.Remove(item.Profile);
        LoadProfiles();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Selected is { } item && Path.GetDirectoryName(item.ExecutablePath) is { } dir && Directory.Exists(dir))
            ProcessRunner.OpenShell("explorer.exe", $"\"{dir}\"");
    }

    [RelayCommand]
    private void OpenTweaks() => _navigation.Navigate<TweaksViewModel>();

    partial void OnSelectedChanged(GameProfileItem? value)
    {
        if (value is null) return;
        _loadingProfile = true;
        value.Load(_profiles.ReadState(value.Profile), _profiles.ActiveRecords(value.Profile).Count > 0);
        _loadingProfile = false;
    }
}
