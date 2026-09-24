using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.Services;
using FpsLol.SystemIntegration;

namespace FpsLol.ViewModels;

public sealed record PowerSchemeItem(PowerScheme Scheme, bool CreatedByFpsLol)
{
    public string Name => Scheme.Name;
    public bool IsActive => Scheme.IsActive;
    public string Personality => Scheme.Personality switch
    {
        PowerPersonality.HighPerformance => "Performance",
        PowerPersonality.Balanced => "Balanced",
        PowerPersonality.PowerSaver => "Power saver",
        _ => "Custom",
    };
    public string Id => Scheme.Id.ToString();
}

public sealed partial class PowerViewModel : PageViewModel
{
    private readonly IPowerService _power;
    private readonly IChangeService _changes;
    private readonly IHistoryService _history;
    private readonly ITweakActionService _tweaks;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly ILogService _log;

    public PowerViewModel(IPowerService power, IChangeService changes, IHistoryService history, ITweakActionService tweaks,
        IDialogService dialogs, IToastService toasts, ILogService log)
    {
        _power = power;
        _changes = changes;
        _history = history;
        _tweaks = tweaks;
        _dialogs = dialogs;
        _toasts = toasts;
        _log = log;
    }

    public override string Title => "Power";
    public override string Subtitle => "Power plans and gaming power profile";

    public ObservableCollection<PowerSchemeItem> Schemes { get; } = [];

    [ObservableProperty] private string _activeName = "N/A";
    [ObservableProperty] private string _activePersonality = string.Empty;
    [ObservableProperty] private bool _activeIsPerformance;
    [ObservableProperty] private string _deviceText = string.Empty;
    [ObservableProperty] private string? _notice;
    [ObservableProperty] private bool _hasPrevious;
    [ObservableProperty] private string _previousText = string.Empty;

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var (schemes, device) = await Task.Run(() => (_power.List(), _power.Device()));
            Schemes.Clear();
            foreach (var s in schemes) Schemes.Add(new PowerSchemeItem(s, _power.IsCreatedByFpsLol(s.Id)));

            var active = schemes.FirstOrDefault(s => s.IsActive);
            ActiveName = active?.Name ?? "N/A";
            ActivePersonality = active is null ? string.Empty : new PowerSchemeItem(active, false).Personality;
            ActiveIsPerformance = active?.IsPerformance == true;

            DeviceText = device.HasBattery
                ? $"Laptop · {(device.OnAcPower ? "plugged in" : "on battery")}{(device.BatteryPercent is { } p ? $" · {p}%" : string.Empty)}"
                : "Desktop · AC power";

            Notice = device.ModernStandby && schemes.Count <= 1
                ? "This device uses Modern Standby, where Windows exposes only the Balanced plan. Use Settings → System → Power → Power mode → Best performance for a similar effect."
                : device.HasBattery
                    ? "High performance plans increase power draw and heat. On battery, prefer Balanced."
                    : null;

            var records = _history.ActiveFor("power-plan");
            HasPrevious = records.Count > 0;
            PreviousText = HasPrevious ? $"Previous plan: {records[^1].Before} (changed {records[^1].Timestamp:g})" : string.Empty;
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            _log.Error("Could not read power plans.", ex);
            Notice = "Power plans could not be read on this system: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ActivateAsync(PowerSchemeItem? item)
    {
        if (item is null || item.IsActive) return;
        var current = Schemes.FirstOrDefault(s => s.IsActive)?.Scheme;
        var report = await _changes.ExecuteAsync([_power.BuildActivate(item.Scheme, current)], createRestorePoint: false, "Power plan");
        var result = report.Changes[0].Result;
        if (result.IsSuccess) _toasts.Success("Power plan", $"{item.Name} is now active.");
        else await _dialogs.ShowResultAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ApplyGamingProfileAsync()
    {
        var tweak = TweakCatalog.Find("power-plan")!;
        await _tweaks.ApplyAsync(tweak);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RestorePreviousAsync()
    {
        var records = _history.ActiveFor("power-plan");
        if (records.Count == 0) return;
        if (!await _dialogs.ConfirmAsync("Restore previous power plan?", $"FPS.LOL will re-activate \"{records[^1].Before}\".", "Restore"))
            return;
        var results = await _changes.RestoreAsync(records);
        var failed = results.FirstOrDefault(r => !r.Result.IsSuccess);
        if (failed.Record is null) _toasts.Success("Power plan", "Previous plan restored.");
        else await _dialogs.ShowResultAsync(failed.Result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AddUltimateAsync()
    {
        if (!await _dialogs.ConfirmAsync("Add the Ultimate Performance plan?",
                "Ultimate Performance removes micro power-management latencies. It is intended for desktops; it adds a new plan and does not change or remove existing ones.",
                "Add plan"))
            return;
        var result = await Task.Run(_power.AddUltimatePerformance);
        _toasts.Show(result);
        if (result.Status == OperationStatus.Failed) await _dialogs.ShowResultAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(PowerSchemeItem? item)
    {
        if (item is null) return;
        if (!await _dialogs.ConfirmAsync($"Remove \"{item.Name}\"?", "This plan was created by FPS.LOL. Removing it does not affect other plans.", "Remove", danger: true))
            return;
        var result = await Task.Run(() => _power.DeleteCreated(item.Scheme));
        _toasts.Show(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private static void OpenWindowsPowerSettings() => Utilities.ProcessRunner.OpenShell("ms-settings:powersleep");
}
