using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.Services;

public sealed class AppSettings
{
    // General
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool Notifications { get; set; } = true;

    // Appearance
    public double GlassIntensity { get; set; } = 60;
    public bool BlurEnabled { get; set; } = true;
    public double AnimationIntensity { get; set; } = 100;
    public bool ReduceAnimations { get; set; }

    // Optimization
    public bool AskBeforeApplying { get; set; } = true;
    public bool CreateRestorePoint { get; set; } = true;

    // Advanced
    public bool LoggingEnabled { get; set; } = true;
    public bool DebugMode { get; set; }

    // State
    public bool FirstRunCompleted { get; set; }
    public int? LastScore { get; set; }
    public List<Guid> CreatedPowerSchemes { get; set; } = [];
}

public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler? Changed;
    void Save();
    void SetStartWithWindows(bool enabled);
}

public sealed class SettingsService : ISettingsService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "FPS.LOL";

    public SettingsService()
    {
        Current = JsonStore.Load(AppPaths.SettingsFile, () => new AppSettings());
        // Reflect the real autostart state (the user may have removed it in Task Manager).
        Current.StartWithWindows = RegistryUtil.ReadString(RegRoot.CurrentUser, RunKey, RunValue) is not null;
    }

    public AppSettings Current { get; }

    public event EventHandler? Changed;

    public void Save()
    {
        JsonStore.Save(AppPaths.SettingsFile, Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetStartWithWindows(bool enabled)
    {
        if (enabled)
            RegistryUtil.Write(RegRoot.CurrentUser, RunKey, RunValue, RegValue.String($"\"{Environment.ProcessPath}\" --minimized"));
        else
            RegistryUtil.DeleteValue(RegRoot.CurrentUser, RunKey, RunValue);
        Current.StartWithWindows = enabled;
        Save();
    }
}
