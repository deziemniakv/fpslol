using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.Storage;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.ViewModels;

public enum OptimizeState
{
    Idle,
    Scanning,
    Review,
    Applying,
    Results,
}

public enum OptimizationKind
{
    Tweak,
    Cleanup,
    StartupReview,
}

public sealed partial class OptimizationItem : ObservableObject
{
    public required OptimizationKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string CategoryName { get; init; }
    public string Current { get; init; } = string.Empty;
    public string Recommended { get; init; } = string.Empty;
    public RiskLevel Risk { get; init; }
    public bool RequiresAdmin { get; init; }
    public RestartRequirement Restart { get; init; }
    public TweakStatus? Tweak { get; init; }
    public IReadOnlyList<CleanupCategory> CleanupCategories { get; init; } = [];

    [ObservableProperty] private bool _isSelected;

    public bool IsSelectable => Kind != OptimizationKind.StartupReview;
    public bool IsStartupReview => Kind == OptimizationKind.StartupReview;
    public string RiskText => Risk.ToString().ToUpperInvariant();
    public bool ShowChange => !string.IsNullOrEmpty(Current);
    public bool ShowAdmin => RequiresAdmin && !Elevation.IsElevated;
    public bool NeedsRestart => Restart != RestartRequirement.None;
    public string RestartText => CategoryNames.Of(Restart);
}

public sealed partial class ResultItem(OperationResult result) : ObservableObject
{
    public OperationResult Result { get; } = result;
    public string Title => Result.Title;
    public string Message => Result.Message;
    public OperationStatus Status => Result.Status;
    public string StatusText => Result.StatusText;
    public bool HasDetails => !string.IsNullOrWhiteSpace(Result.Details);
}

public sealed partial class OptimizeViewModel : PageViewModel
{
    private static readonly string[] DefaultCleanup = ["user-temp", "windows-temp", "error-reports"];
    private const long CleanupThreshold = 50L * 1024 * 1024;

    private readonly ISystemScanService _scan;
    private readonly ITweakEngine _engine;
    private readonly IChangeService _changes;
    private readonly ICleanupService _cleanup;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly INavigationService _navigation;
    private readonly ILogService _log;

    public OptimizeViewModel(ISystemScanService scan, ITweakEngine engine, IChangeService changes, ICleanupService cleanup,
        ISettingsService settings, IDialogService dialogs, IToastService toasts, INavigationService navigation, ILogService log)
    {
        _scan = scan;
        _engine = engine;
        _changes = changes;
        _cleanup = cleanup;
        _settings = settings;
        _dialogs = dialogs;
        _toasts = toasts;
        _navigation = navigation;
        _log = log;

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OptimizationItem.CategoryName)));
        _createRestorePoint = settings.Current.CreateRestorePoint;
        scan.Scanned += (_, r) => Ui.Post(() =>
        {
            if (State == OptimizeState.Idle) LastScanText = DescribeScan(r);
        });
    }

    private static string DescribeScan(ScanResult r) =>
        $"Last scan at {r.Timestamp:HH:mm}: {r.Score.AvailableText.ToLowerInvariant()}. Score {r.Score.Score}/100.";

    public override string Title => "Optimize";
    public override string Subtitle => "One-click optimization with full backup and restore";

    public ObservableCollection<OptimizationItem> Items { get; } = [];
    public ICollectionView ItemsView { get; }
    public ObservableCollection<ResultItem> Results { get; } = [];

    [ObservableProperty] private OptimizeState _state = OptimizeState.Idle;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _createRestorePoint;
    [ObservableProperty] private string _scoreBefore = string.Empty;
    [ObservableProperty] private string _scoreAfter = string.Empty;
    [ObservableProperty] private bool _restartRequired;
    [ObservableProperty] private bool _signOutRequired;
    [ObservableProperty] private string _restorePointText = string.Empty;
    [ObservableProperty] private string _resultSummary = string.Empty;
    [ObservableProperty] private int _successCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private string _lastScanText = "Scan your system to find available optimizations.";

    public int ActionableCount => Items.Count(i => i.IsSelectable);
    public bool HasItems => Items.Count > 0;

    public override Task OnNavigatedToAsync()
    {
        CreateRestorePoint = _settings.Current.CreateRestorePoint;
        if (_scan.Last is { } last && State == OptimizeState.Idle) LastScanText = DescribeScan(last);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ScanAsync() => StartScanAsync();

    /// <summary>Scans and shows the review list. With <paramref name="applyDirectly"/> it continues straight to the
    /// apply confirmation for the pre-selected items ("Optimize now").</summary>
    public async Task StartScanAsync(bool applyDirectly = false)
    {
        if (State is OptimizeState.Scanning or OptimizeState.Applying) return;
        State = OptimizeState.Scanning;
        Items.Clear();
        try
        {
            ProgressText = "Reading Windows gaming, GPU, power and network settings…";
            var result = await _scan.ScanAsync();
            ScoreBefore = result.Score.Score.ToString();
            var ctx = await _engine.GetContextAsync();

            foreach (var status in result.Tweaks.Where(t => t.Detection.Supported && !t.Detection.IsOptimal && !t.Detection.StateUnknown))
            {
                var d = status.Definition;
                var item = new OptimizationItem
                {
                    Kind = OptimizationKind.Tweak,
                    Title = d.Name,
                    Description = d.Description,
                    CategoryName = d.CategoryName,
                    Current = status.Detection.Current,
                    Recommended = status.Detection.Recommended,
                    Risk = d.Risk,
                    RequiresAdmin = d.RequiresAdmin,
                    Restart = d.Restart,
                    Tweak = status,
                    IsSelected = d.Preselect(ctx),
                };
                Add(item);
            }

            ProgressText = "Measuring temporary files…";
            var categories = DefaultCleanup.Select(CleanupEngine.Find).OfType<CleanupCategory>().ToList();
            var scans = await _cleanup.ScanAsync(categories);
            var bytes = scans.Sum(s => s.Bytes);
            if (bytes >= CleanupThreshold)
            {
                Add(new OptimizationItem
                {
                    Kind = OptimizationKind.Cleanup,
                    Title = "Temporary files",
                    Description = "Removes old temporary files and error reports. Files in use are skipped. This cannot be undone, but only regenerable data is removed.",
                    CategoryName = "Cleanup",
                    Current = Format.Bytes(bytes),
                    Recommended = "Removed",
                    RequiresAdmin = categories.Any(c => c.RequiresAdmin),
                    CleanupCategories = categories,
                    IsSelected = true,
                });
            }

            if (result.StartupEnabled > 8)
            {
                Add(new OptimizationItem
                {
                    Kind = OptimizationKind.StartupReview,
                    Title = $"{result.StartupEnabled} apps start with Windows",
                    Description = "Startup apps keep running in the background while you play. Review them and disable the ones you do not need — FPS.LOL never disables startup apps automatically.",
                    CategoryName = "Startup",
                });
            }

            UpdateSelection();
            OnPropertyChanged(nameof(ActionableCount));
            OnPropertyChanged(nameof(HasItems));
            State = OptimizeState.Review;
            LastScanText = $"Last scan at {DateTime.Now:HH:mm}.";

            if (applyDirectly && SelectedCount > 0) await ApplyAsync();
        }
        catch (Exception ex)
        {
            _log.Error("System scan failed.", ex);
            State = OptimizeState.Idle;
            await _dialogs.ShowErrorAsync("Scan failed", "The system scan could not be completed.", ex.Message);
        }
    }

    private void Add(OptimizationItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptimizationItem.IsSelected)) UpdateSelection();
        };
        Items.Add(item);
    }

    private void UpdateSelection()
    {
        SelectedCount = Items.Count(i => i.IsSelectable && i.IsSelected);
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var i in Items.Where(i => i.IsSelectable)) i.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var i in Items) i.IsSelected = false;
    }

    [RelayCommand]
    private async Task SelectRecommendedAsync()
    {
        var ctx = await _engine.GetContextAsync();
        foreach (var i in Items.Where(i => i.IsSelectable))
            i.IsSelected = i.Kind == OptimizationKind.Cleanup || (i.Tweak?.Definition.Preselect(ctx) ?? false);
    }

    [RelayCommand]
    private void OpenStartup() => _navigation.Navigate<StartupViewModel>();

    private bool CanApply() => SelectedCount > 0 && State == OptimizeState.Review;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var selected = Items.Where(i => i.IsSelectable && i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var needsAdmin = !Elevation.IsElevated && (selected.Any(i => i.RequiresAdmin) || CreateRestorePoint);
        if (_settings.Current.AskBeforeApplying)
        {
            var lines = selected.Select(i => i.ShowChange ? $"{i.Title}: {i.Current} → {i.Recommended}" : i.Title).ToList();
            lines.Add(CreateRestorePoint ? "A Windows restore point and an FPS.LOL backup will be created first" : "An FPS.LOL backup will be created first");
            if (needsAdmin) lines.Add("Windows will ask once for administrator permission");
            if (!await _dialogs.ConfirmAsync($"Apply {selected.Count} optimization{(selected.Count == 1 ? string.Empty : "s")}?",
                    "Review the changes below. Every setting change is verified after it is applied and can be restored from the Restore Center.",
                    "Apply changes", items: lines))
                return;
        }

        State = OptimizeState.Applying;
        Results.Clear();
        var allResults = new List<OperationResult>();
        try
        {
            ProgressText = "Creating backup and applying changes…";
            var ctx = await _engine.GetContextAsync(refresh: true);
            var requests = selected.Where(i => i.Kind == OptimizationKind.Tweak && i.Tweak is not null)
                .Select(i => _engine.BuildApply(i.Tweak!.Definition, ctx, i.Tweak.Detection)).ToList();

            var report = await _changes.ExecuteAsync(requests, CreateRestorePoint, "One-click optimization");
            if (report.RestorePoint is { } rp)
            {
                RestorePointText = rp.Message;
                allResults.Add(rp);
            }
            else
            {
                RestorePointText = CreateRestorePoint ? string.Empty : "Restore point creation is turned off in Settings. FPS.LOL's own backup was created.";
            }
            allResults.AddRange(report.Changes.Select(c => c.Result));
            RestartRequired = report.RestartRequired;
            SignOutRequired = report.SignOutRequired;

            foreach (var item in selected.Where(i => i.Kind == OptimizationKind.Cleanup))
            {
                ProgressText = "Cleaning temporary files…";
                var summary = await _cleanup.CleanAsync(item.CleanupCategories);
                var failed = summary.Results.FirstOrDefault(r => !r.IsSuccess);
                allResults.Add(failed is null
                    ? OperationResult.Ok("Temporary files", $"Freed {Format.Bytes(summary.BytesFreed)} ({summary.FilesDeleted} files, {summary.FilesSkipped} in use skipped).")
                    : new OperationResult("Temporary files", failed.Status, failed.Message, failed.Details));
            }

            ProgressText = "Verifying results…";
            var after = await _scan.ScanAsync();
            ScoreAfter = after.Score.Score.ToString();
        }
        catch (Exception ex)
        {
            _log.Error("Optimization failed.", ex);
            allResults.Add(OperationResult.Fail("Optimization", "An unexpected error stopped the optimization. Changes completed before the error were recorded in the Restore Center.", ex.ToString()));
        }

        foreach (var r in allResults) Results.Add(new ResultItem(r));
        SuccessCount = allResults.Count(r => r.Status == OperationStatus.Success);
        FailedCount = allResults.Count(r => r.Status == OperationStatus.Failed);
        SkippedCount = allResults.Count(r => r.Status is OperationStatus.Skipped or OperationStatus.NotSupported);
        ResultSummary = FailedCount == 0
            ? SuccessCount > 0 ? "Optimization completed" : "No changes were made"
            : SuccessCount > 0 ? "Optimization completed with errors" : "Optimization failed";

        if (FailedCount == 0 && SuccessCount > 0) _toasts.Success("System optimized", $"{SuccessCount} change(s) applied and verified.");
        else if (FailedCount > 0) _toasts.Error("Some changes failed", $"{FailedCount} change(s) could not be applied. See the results for details.");
        State = OptimizeState.Results;
    }

    [RelayCommand]
    private async Task ShowDetailsAsync(ResultItem? item)
    {
        if (item is null) return;
        await _dialogs.ShowAsync(new ConfirmDialogViewModel
        {
            Title = item.Title,
            Message = item.Message,
            Details = item.Result.Details,
            ConfirmText = "Close",
            CancelText = null,
            Icon = item.Status == OperationStatus.Success ? "Icon.CircleCheck" : "Icon.CircleX",
            Tone = item.Status == OperationStatus.Success ? "success" : "danger",
        });
    }

    [RelayCommand]
    private void Done()
    {
        State = OptimizeState.Idle;
        Items.Clear();
        if (_scan.Last is { } last) LastScanText = DescribeScan(last);
    }

    [RelayCommand]
    private void OpenRestoreCenter() => _navigation.Navigate<RestoreViewModel>();

    [RelayCommand]
    private async Task RestartNowAsync()
    {
        if (!await _dialogs.ConfirmAsync("Restart Windows now?", "Save your work in other applications first. Windows will restart in 10 seconds.", "Restart now", danger: true))
            return;
        try
        {
            ProcessRunner.Run(ProcessRunner.SystemTool("shutdown.exe"), "/r /t 10 /c \"FPS.LOL: restarting to apply changes\"");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Restart failed", "Windows could not be restarted automatically. Please restart manually.", ex.Message);
        }
    }

    partial void OnStateChanged(OptimizeState value) => ApplyCommand.NotifyCanExecuteChanged();

    partial void OnCreateRestorePointChanged(bool value)
    {
        _settings.Current.CreateRestorePoint = value;
        _settings.Save();
    }
}
