using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Hardware;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.ViewModels;

public sealed partial class ScanStep : ObservableObject
{
    public required string Label { get; init; }
    public required string Icon { get; init; }
    [ObservableProperty] private string _value = "Waiting";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isDone;
}

public sealed partial class FirstRunViewModel(
    IHardwareService hardware,
    ISystemScanService scan,
    ISettingsService settings,
    INavigationService navigation,
    IServiceProvider services) : ViewModelBase
{
    public event EventHandler? Finished;

    /// <summary>0 = welcome, 1 = scanning, 2 = result.</summary>
    [ObservableProperty] private int _step;
    [ObservableProperty] private int _score;
    [ObservableProperty] private string _headline = string.Empty;
    [ObservableProperty] private int _availableCount;

    public ObservableCollection<ScanStep> Steps { get; } =
    [
        new() { Label = "Processor", Icon = "Icon.Cpu" },
        new() { Label = "Graphics", Icon = "Icon.Monitor" },
        new() { Label = "Memory", Icon = "Icon.Memory" },
        new() { Label = "Windows", Icon = "Icon.Layers" },
        new() { Label = "Power plan", Icon = "Icon.Power" },
        new() { Label = "Gaming settings", Icon = "Icon.Gamepad" },
    ];

    [RelayCommand]
    private async Task StartAsync()
    {
        Step = 1;
        foreach (var s in Steps.Take(4)) s.IsRunning = true;

        var hw = await hardware.GetAsync();
        await Complete(Steps[0], hw.CpuName);
        await Complete(Steps[1], hw.PrimaryGpu?.Name ?? Format.NotAvailable);
        await Complete(Steps[2], hw.RamTotalBytes > 0 ? Format.Bytes(hw.RamTotalBytes, 0) : Format.NotAvailable);
        await Complete(Steps[3], WindowsInfo.Current.Long);

        Steps[4].IsRunning = true;
        Steps[5].IsRunning = true;
        var result = await scan.ScanAsync();
        var power = result.Tweaks.FirstOrDefault(t => t.Definition.Id == "power-plan")?.Detection.Current ?? Format.NotAvailable;
        await Complete(Steps[4], power);
        var gaming = result.Tweaks.Where(t => t.Definition.Category == Models.TweakCategory.WindowsGaming && t.Detection.Supported).ToList();
        await Complete(Steps[5], $"{gaming.Count(t => t.Detection.IsOptimal)} of {gaming.Count} optimal");

        Score = result.Score.Score;
        Headline = result.Score.Headline;
        AvailableCount = result.Score.AvailableOptimizations;
        await Task.Delay(350);
        Step = 2;
    }

    private static async Task Complete(ScanStep step, string value)
    {
        step.Value = value;
        step.IsRunning = false;
        step.IsDone = true;
        await Task.Delay(Controls.Motion.Enabled ? 220 : 0);
    }

    [RelayCommand]
    private void ViewOptimizations() => Finish(startOptimize: false);

    [RelayCommand]
    private void OptimizeNow() => Finish(startOptimize: true);

    [RelayCommand]
    private void Skip() => Finish(startOptimize: false, goToOptimize: false);

    private void Finish(bool startOptimize, bool goToOptimize = true)
    {
        settings.Current.FirstRunCompleted = true;
        settings.Save();
        Finished?.Invoke(this, EventArgs.Empty);
        if (!goToOptimize) return;

        navigation.Navigate<OptimizeViewModel>();
        var optimize = (OptimizeViewModel)services.GetService(typeof(OptimizeViewModel))!;
        _ = optimize.StartScanAsync(applyDirectly: startOptimize);
    }
}
