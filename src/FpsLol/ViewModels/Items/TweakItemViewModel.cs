using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.SystemIntegration;

namespace FpsLol.ViewModels.Items;

public sealed partial class TweakItemViewModel : ObservableObject
{
    private readonly ITweakActionService _actions;

    public TweakItemViewModel(TweakStatus status, ITweakActionService actions)
    {
        _actions = actions;
        Definition = status.Definition;
        Update(status);
    }

    public TweakDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string CategoryName => Definition.CategoryName;
    public RiskLevel Risk => Definition.Risk;
    public string RiskText => Definition.Risk.ToString().ToUpperInvariant();
    public string RestartText => CategoryNames.Of(Definition.Restart);
    public bool NeedsRestart => Definition.Restart != RestartRequirement.None;
    public bool RequiresAdmin => Definition.RequiresAdmin;
    public bool ShowAdminBadge => Definition.RequiresAdmin && !Elevation.IsElevated;

    [ObservableProperty] private string _current = string.Empty;
    [ObservableProperty] private string _recommended = string.Empty;
    [ObservableProperty] private bool _isOptimal;
    [ObservableProperty] private bool _isSupported;
    [ObservableProperty] private bool _stateUnknown;
    [ObservableProperty] private string? _unsupportedReason;
    [ObservableProperty] private bool _hasHistory;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RestoreCommand))]
    private bool _isBusy;

    public string StatusText => !IsSupported ? "Not supported" : StateUnknown ? "Unknown" : IsOptimal ? "Optimized" : "Available";
    public string StatusTone => !IsSupported ? "neutral" : StateUnknown ? "info" : IsOptimal ? "success" : "warning";

    public void Update(TweakStatus status)
    {
        Current = status.Detection.Current;
        Recommended = status.Detection.Recommended;
        IsOptimal = status.Detection.IsOptimal;
        IsSupported = status.Detection.Supported;
        StateUnknown = status.Detection.StateUnknown;
        UnsupportedReason = status.Detection.UnsupportedReason;
        HasHistory = status.HasHistory;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTone));
        ApplyCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() => !IsBusy && IsSupported && !IsOptimal;
    private bool CanRestore() => !IsBusy && IsSupported;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        try
        {
            if (await _actions.ApplyAsync(Definition) is { } s) Update(s);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        IsBusy = true;
        try
        {
            if (await _actions.RestoreAsync(Definition) is { } s) Update(s);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed record StatusItem(string Label, string Value, string Tone, string Icon);
