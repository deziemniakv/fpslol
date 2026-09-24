using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;
using FpsLol.Networking;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;
using FpsLol.ViewModels.Items;

namespace FpsLol.ViewModels;

public sealed partial class NetworkViewModel : PageViewModel
{
    private static readonly string[] NetworkTweaks = ["tcp-autotuning", "adapter-power-saving", "delivery-optimization"];

    private readonly INetworkService _network;
    private readonly IChangeService _changes;
    private readonly ISystemScanService _scan;
    private readonly ITweakActionService _actions;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;

    public NetworkViewModel(INetworkService network, IChangeService changes, ISystemScanService scan, ITweakActionService actions,
        IDialogService dialogs, IToastService toasts)
    {
        _network = network;
        _changes = changes;
        _scan = scan;
        _actions = actions;
        _dialogs = dialogs;
        _toasts = toasts;
        scan.Scanned += (_, r) => Ui.Post(() => ApplyScan(r));
    }

    public override string Title => "Network";
    public override string Subtitle => "Adapter status, latency and justified network settings";

    public ObservableCollection<AdapterInfo> Adapters { get; } = [];
    public ObservableCollection<LatencyResult> Latency { get; } = [];
    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];
    public IReadOnlyList<DnsPreset> DnsPresets => _network.DnsPresets;

    [ObservableProperty] private AdapterInfo? _selectedAdapter;
    [ObservableProperty] private DnsPreset? _selectedPreset;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _hasLatency;

    public bool ShowAdminHint => !Elevation.IsElevated;

    public override async Task OnNavigatedToAsync()
    {
        await RefreshAsync();
        ApplyScan(await _scan.EnsureAsync());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var id = SelectedAdapter?.Id;
        var adapters = await Task.Run(_network.GetAdapters);
        Adapters.Clear();
        foreach (var a in adapters) Adapters.Add(a);
        SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == id) ?? Adapters.FirstOrDefault();
        IsLoaded = true;
    }

    partial void OnSelectedAdapterChanged(AdapterInfo? value)
    {
        if (value is null) return;
        SelectedPreset = value.DnsAutomatic
            ? DnsPresets[0]
            : DnsPresets.FirstOrDefault(p => p.IPv4.Length > 0 && value.DnsServers.Contains(p.IPv4[0]));
    }

    private void ApplyScan(ScanResult r)
    {
        foreach (var status in r.Tweaks.Where(t => NetworkTweaks.Contains(t.Definition.Id)))
        {
            var existing = Tweaks.FirstOrDefault(t => t.Id == status.Definition.Id);
            if (existing is null) Tweaks.Add(new TweakItemViewModel(status, _actions));
            else if (!existing.IsBusy) existing.Update(status);
        }
    }

    [RelayCommand]
    private async Task RunLatencyTestAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        try
        {
            var targets = new List<(string Label, string Host)>();
            if (SelectedAdapter?.Gateways.FirstOrDefault(g => !g.Contains(':')) is { } gw) targets.Add(("Router (gateway)", gw));
            targets.Add(("Cloudflare", "1.1.1.1"));
            targets.Add(("Google", "8.8.8.8"));

            var results = await Task.WhenAll(targets.Select(t => _network.MeasureAsync(t.Label, t.Host, 20)));
            Latency.Clear();
            foreach (var r in results) Latency.Add(r);
            HasLatency = true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Latency test failed", "The latency test could not be completed.", ex.Message);
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task ApplyDnsAsync()
    {
        if (SelectedAdapter is not { } adapter || SelectedPreset is not { } preset) return;
        if (preset.IsAutomatic && adapter.DnsAutomatic)
        {
            _toasts.Info("DNS", $"{adapter.Name} already uses automatic DNS.");
            return;
        }

        var lines = new List<string>
        {
            $"Adapter: {adapter.Name}",
            $"DNS: {(adapter.DnsAutomatic ? "Automatic" : adapter.DnsText)} → {(preset.IsAutomatic ? "Automatic" : preset.ServersText)}",
            "The previous configuration is saved in the Restore Center",
        };
        if (!Elevation.IsElevated) lines.Add("Windows will ask for administrator permission");
        if (!await _dialogs.ConfirmAsync($"Use {preset.Name} DNS?", preset.Description + " A different DNS resolver changes how fast domain names are looked up; it does not change in-game ping.", "Apply DNS", items: lines))
            return;

        IsBusy = true;
        try
        {
            var report = await _changes.ExecuteAsync([_network.BuildDnsChange(adapter, preset)], createRestorePoint: false, "DNS configuration");
            var result = report.Changes[0].Result;
            if (result.IsSuccess)
            {
                _network.FlushDns();
                _toasts.Success("DNS updated", $"{adapter.Name} now uses {preset.Name} DNS.");
            }
            else
            {
                await _dialogs.ShowResultAsync(result);
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private void FlushDns() => _toasts.Show(_network.FlushDns());
}
