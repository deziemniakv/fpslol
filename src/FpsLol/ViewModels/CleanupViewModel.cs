using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.Storage;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.ViewModels;

public sealed partial class CleanupItem : ObservableObject
{
    public CleanupItem(CleanupCategory category)
    {
        Category = category;
        _isSelected = category.SelectedByDefault;
    }

    public CleanupCategory Category { get; }
    public string Name => Category.Name;
    public string Description => Category.Description;
    public string? Warning => Category.Warning;
    public bool HasWarning => Category.Warning is not null;
    public bool ShowAdmin => Category.RequiresAdmin && !Elevation.IsElevated;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private long _bytes;
    [ObservableProperty] private string _sizeText = "—";
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private bool _scanned;
}

public sealed partial class CleanupViewModel : PageViewModel
{
    private readonly ICleanupService _cleanup;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;

    public CleanupViewModel(ICleanupService cleanup, IDialogService dialogs, IToastService toasts)
    {
        _cleanup = cleanup;
        _dialogs = dialogs;
        _toasts = toasts;
        foreach (var c in CleanupEngine.Categories)
        {
            var item = new CleanupItem(c);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CleanupItem.IsSelected)) UpdateTotal();
            };
            Items.Add(item);
        }
    }

    public override string Title => "Cleanup";
    public override string Subtitle => "Safe removal of temporary files and caches";

    public ObservableCollection<CleanupItem> Items { get; } = [];
    public ObservableCollection<OperationResult> Results { get; } = [];

    [ObservableProperty] private bool _hasScanned;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private string _totalText = "Scan to see how much space can be freed";
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _hasResults;

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        HasResults = false;
        try
        {
            var scans = await _cleanup.ScanAsync(Items.Select(i => i.Category));
            foreach (var s in scans)
            {
                var item = Items.First(i => i.Category.Id == s.CategoryId);
                item.Bytes = s.Bytes;
                item.SizeText = Format.Bytes(s.Bytes);
                item.DetailText = s.Files == 0
                    ? (s.AccessDenied ? "Some folders require administrator access to measure" : "Nothing to clean")
                    : $"{s.Files:N0} files{(s.AccessDenied ? " · some folders need administrator access" : string.Empty)}";
                item.Scanned = true;
            }
            HasScanned = true;
            UpdateTotal();
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Scan failed", "The cleanup scan could not be completed.", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void UpdateTotal()
    {
        if (!HasScanned) return;
        var total = Items.Where(i => i.IsSelected).Sum(i => i.Bytes);
        TotalText = total > 0 ? $"{Format.Bytes(total)} can be cleaned" : "Nothing selected to clean";
        CleanCommand.NotifyCanExecuteChanged();
    }

    private bool CanClean() => HasScanned && !IsCleaning && Items.Any(i => i.IsSelected && i.Bytes > 0);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selected = Items.Where(i => i.IsSelected && i.Bytes > 0).ToList();
        var lines = selected.Select(i => $"{i.Name}: {i.SizeText}").ToList();
        lines.AddRange(selected.Where(i => i.HasWarning).Select(i => $"Note: {i.Warning}"));
        if (selected.Any(i => i.ShowAdmin)) lines.Add("Windows will ask for administrator permission for system folders");
        if (!await _dialogs.ConfirmAsync("Clean selected items?", "Only temporary and regenerable data in the listed locations is removed. Files that are in use are skipped. Deleted files cannot be restored.", "Clean", items: lines))
            return;

        IsCleaning = true;
        CleanCommand.NotifyCanExecuteChanged();
        try
        {
            var summary = await _cleanup.CleanAsync(selected.Select(i => i.Category));
            Results.Clear();
            foreach (var r in summary.Results) Results.Add(r);
            HasResults = true;
            ResultText = $"Freed {Format.Bytes(summary.BytesFreed)} · {summary.FilesDeleted:N0} files removed · {summary.FilesSkipped:N0} in use skipped";
            if (summary.Results.All(r => r.IsSuccess)) _toasts.Success("Cleanup complete", $"Freed {Format.Bytes(summary.BytesFreed)}.");
            else _toasts.Warning("Cleanup finished with errors", "Some categories could not be cleaned. See the results.");
        }
        finally
        {
            IsCleaning = false;
            await ScanAsync();
        }
    }

    [RelayCommand]
    private static void OpenDiskCleanup() => ProcessRunner.OpenShell(ProcessRunner.SystemTool("cleanmgr.exe"));

    [RelayCommand]
    private static void OpenStorageSettings() => ProcessRunner.OpenShell("ms-settings:storagesense");
}
