using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.SystemIntegration;

namespace FpsLol.ViewModels;

public sealed partial class StartupRow : ObservableObject
{
    public StartupRow(StartupEntry entry)
    {
        Entry = entry;
        _isEnabled = entry.Enabled;
    }

    public StartupEntry Entry { get; private set; }
    public string Name => Entry.Name;
    public string Publisher => Entry.Publisher;
    public string Command => Entry.Command;
    public string Location => Entry.Location;
    public string Path => Entry.ExecutablePath ?? Entry.Command;
    public bool ShowAdmin => Entry.RequiresAdmin && !Elevation.IsElevated;
    public string Impact => "N/A";

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ImageSource? _icon;

    public void Replace(StartupEntry entry)
    {
        Entry = entry;
        IsEnabled = entry.Enabled;
        OnPropertyChanged(nameof(IsEnabled));
    }
}

public sealed partial class StartupViewModel : PageViewModel
{
    private readonly IStartupService _startup;
    private readonly IChangeService _changes;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly ISystemScanService _scan;

    public StartupViewModel(IStartupService startup, IChangeService changes, IDialogService dialogs, IToastService toasts, ISystemScanService scan)
    {
        _startup = startup;
        _changes = changes;
        _dialogs = dialogs;
        _toasts = toasts;
        _scan = scan;
    }

    public override string Title => "Startup";
    public override string Subtitle => "Apps that start with Windows — disable, never delete";

    public ObservableCollection<StartupRow> Rows { get; } = [];

    [ObservableProperty] private string _summary = string.Empty;

    public bool IsEmpty => IsLoaded && Rows.Count == 0;

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await Task.Run(_startup.GetEntries);
            Rows.Clear();
            foreach (var e in entries)
            {
                var row = new StartupRow(e);
                Rows.Add(row);
                if (e.ExecutablePath is { } exe) _ = LoadIconAsync(row, exe);
            }
            UpdateSummary();
            IsLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateSummary() =>
        Summary = $"{Rows.Count(r => r.IsEnabled)} enabled · {Rows.Count(r => !r.IsEnabled)} disabled";

    private static async Task LoadIconAsync(StartupRow row, string exe)
    {
        if (!File.Exists(exe)) return;
        try
        {
            var source = await Task.Run(() =>
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon is null) return null;
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                bitmap.Freeze(); // frozen: safe to hand over to the UI thread; the icon handle is released by using
                return bitmap;
            });
            if (source is not null) row.Icon = source;
        }
        catch
        {
            // No icon — the view shows a generic glyph.
        }
    }

    [RelayCommand]
    private async Task ToggleAsync(StartupRow? row)
    {
        if (row is null || row.IsBusy) return;
        var enable = !row.Entry.Enabled;
        row.IsBusy = true;
        try
        {
            var report = await _changes.ExecuteAsync([_startup.BuildToggle(row.Entry, enable)], createRestorePoint: false, $"Startup: {row.Name}");
            var result = report.Changes[0].Result;
            if (result.IsSuccess)
            {
                row.Replace(row.Entry with { Enabled = enable });
                _toasts.Success(row.Name, enable ? "Will start with Windows." : "Will no longer start with Windows.");
                _ = _scan.ScanAsync();
            }
            else
            {
                row.Replace(row.Entry); // revert the switch
                await _dialogs.ShowResultAsync(result);
            }
            UpdateSummary();
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    [RelayCommand]
    private static void OpenTaskManager() => Utilities.ProcessRunner.OpenShell("taskmgr.exe", "/0 /startup");
}
