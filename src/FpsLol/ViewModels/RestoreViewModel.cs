using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.ViewModels;

public sealed partial class ChangeRow(ChangeRecord record) : ObservableObject
{
    public ChangeRecord Record { get; } = record;
    public string DateGroup => Record.Timestamp.Date == DateTime.Today ? $"Today · {Record.Timestamp:yyyy-MM-dd}"
        : Record.Timestamp.Date == DateTime.Today.AddDays(-1) ? $"Yesterday · {Record.Timestamp:yyyy-MM-dd}"
        : Record.Timestamp.ToString("yyyy-MM-dd");
    public string Time => Record.Timestamp.ToString("HH:mm");
    public string Title => Record.Title;
    public string Category => Record.Category;
    public string Change => $"{Record.Before} → {Record.After}";
    public bool Restored => Record.Restored;
    public string RestoredText => Record.RestoredAt is { } t ? $"Restored {t:g}" : "Restored";
    public bool ShowAdmin => Record.RequiresAdmin && !Elevation.IsElevated;

    [ObservableProperty] private bool _isSelected;
}

public sealed partial class RestoreViewModel : PageViewModel
{
    private readonly IHistoryService _history;
    private readonly IChangeService _changes;
    private readonly IBackupService _backups;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly ISystemScanService _scan;

    public RestoreViewModel(IHistoryService history, IChangeService changes, IBackupService backups, IDialogService dialogs,
        IToastService toasts, ISystemScanService scan)
    {
        _history = history;
        _changes = changes;
        _backups = backups;
        _dialogs = dialogs;
        _toasts = toasts;
        _scan = scan;

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ChangeRow.DateGroup)));
        RowsView.Filter = o => o is ChangeRow r && (ShowRestored || !r.Restored);
        history.Changed += (_, _) => Ui.Post(Load);
    }

    public override string Title => "Restore Center";
    public override string Subtitle => "Every change FPS.LOL made — restore any of them at any time";

    public ObservableCollection<ChangeRow> Rows { get; } = [];
    public ICollectionView RowsView { get; }
    public ObservableCollection<BackupFileInfo> Backups { get; } = [];

    [ObservableProperty] private bool _showRestored;
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _hasBackups;

    public bool IsEmpty => Rows.Count(r => ShowRestored || !r.Restored) == 0;

    public string EmptyTitle => Rows.Count == 0 ? "No changes yet" : "No active changes";

    public string EmptyText => Rows.Count == 0
        ? "Every setting FPS.LOL changes will appear here with its previous value, ready to restore."
        : "All changes made by FPS.LOL have been restored. Turn on “Show restored” to see the history.";

    partial void OnShowRestoredChanged(bool value)
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(IsEmpty));
    }

    public override Task OnNavigatedToAsync()
    {
        Load();
        return Task.CompletedTask;
    }

    private void Load()
    {
        Rows.Clear();
        foreach (var r in _history.Records)
        {
            var row = new ChangeRow(r);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChangeRow.IsSelected)) UpdateSelection();
            };
            Rows.Add(row);
        }
        ActiveCount = Rows.Count(r => !r.Restored);
        Backups.Clear();
        foreach (var b in _backups.List().Take(20)) Backups.Add(b);
        HasBackups = Backups.Count > 0;
        UpdateSelection();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyText));
        IsLoaded = true;
    }

    private void UpdateSelection()
    {
        SelectedCount = Rows.Count(r => r.IsSelected && !r.Restored);
        RestoreSelectedCommand.NotifyCanExecuteChanged();
        RestoreAllCommand.NotifyCanExecuteChanged();
    }

    private bool CanRestoreSelected() => SelectedCount > 0 && !IsBusy;
    private bool CanRestoreAll() => ActiveCount > 0 && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRestoreSelected))]
    private Task RestoreSelectedAsync() => RestoreAsync(Rows.Where(r => r.IsSelected && !r.Restored).Select(r => r.Record).ToList(), "Restore selected changes?");

    [RelayCommand(CanExecute = nameof(CanRestoreAll))]
    private Task RestoreAllAsync() => RestoreAsync(Rows.Where(r => !r.Restored).Select(r => r.Record).ToList(), "Restore all changes?");

    [RelayCommand]
    private Task RestoreOneAsync(ChangeRow? row) =>
        row is null || row.Restored ? Task.CompletedTask : RestoreAsync([row.Record], $"Restore \"{row.Title}\"?");

    private async Task RestoreAsync(IReadOnlyList<ChangeRecord> records, string title)
    {
        if (records.Count == 0) return;
        var lines = records.OrderByDescending(r => r.Timestamp).Select(r => $"{r.Title}: {r.After} → {r.Before}").Take(12).ToList();
        if (records.Count > 12) lines.Add($"…and {records.Count - 12} more");
        if (!Elevation.IsElevated && records.Any(r => r.RequiresAdmin)) lines.Add("Windows will ask once for administrator permission");
        if (!await _dialogs.ConfirmAsync(title, "Each setting is returned to the exact value it had before FPS.LOL changed it, then verified.", "Restore", items: lines))
            return;

        IsBusy = true;
        try
        {
            var results = await _changes.RestoreAsync(records);
            var failed = results.Where(r => !r.Result.IsSuccess).ToList();
            if (failed.Count == 0)
                _toasts.Success("Restore complete", $"{results.Count} change(s) restored and verified.");
            else
                await _dialogs.ShowErrorAsync("Some changes could not be restored",
                    $"{results.Count - failed.Count} restored, {failed.Count} failed.",
                    string.Join(Environment.NewLine, failed.Select(f => $"{f.Record.Title}: {f.Result.Message}{(f.Result.Details is null ? string.Empty : " — " + f.Result.Details)}")));
            _ = _scan.ScanAsync();
        }
        finally
        {
            IsBusy = false;
            Load();
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupFileInfo? info)
    {
        if (info is null) return;
        var backup = _backups.Load(info.Path);
        if (backup is null)
        {
            await _dialogs.ShowErrorAsync("Backup unreadable", "This backup file could not be read.", info.Path);
            return;
        }
        if (!await _dialogs.ConfirmAsync($"Restore backup from {info.Timestamp:g}?",
                $"All {backup.Items.Count} setting(s) captured in \"{info.Label}\" will be returned to the values they had at that time.",
                "Restore backup", items: backup.Items.Select(i => $"{i.Title}: {i.State}").Take(12).ToList()))
            return;

        IsBusy = true;
        try
        {
            var result = await _changes.RestoreBackupAsync(backup);
            if (result.IsSuccess) _toasts.Success("Backup restored", result.Message);
            else await _dialogs.ShowResultAsync(result);
            _ = _scan.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearRestoredAsync()
    {
        if (!await _dialogs.ConfirmAsync("Clear restored entries?", "Entries that were already restored are removed from the history. Active changes stay restorable.", "Clear"))
            return;
        _history.ClearRestored();
    }

    [RelayCommand]
    private static void OpenSystemRestore() => ProcessRunner.OpenShell(ProcessRunner.SystemTool("rstrui.exe"));

    [RelayCommand]
    private static void OpenBackupsFolder() => ProcessRunner.OpenShell("explorer.exe", $"\"{AppPaths.Backups}\"");
}
