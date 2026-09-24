using System.ServiceProcess;
using FpsLol.Models;
using FpsLol.Networking;
using FpsLol.SystemIntegration;

namespace FpsLol.Optimizations;

/// <summary>
/// The complete list of tweaks FPS.LOL offers. Every entry is a real, documented Windows setting with a
/// measurable effect on gaming (frame pacing, input, background load or network). Nothing here disables
/// Windows Defender, Windows Update or any other security feature.
/// </summary>
public static class TweakCatalog
{
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameConfigStoreKey = @"System\GameConfigStore";
    private const string DirectXKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string DirectXGlobal = "DirectXUserGlobalSettings";
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string BackgroundAppsKey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string DeliveryOptimizationKey = @"S-1-5-20\Software\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Settings";

    public static IReadOnlyList<TweakDefinition> All { get; } = Build();

    public static TweakDefinition? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private static string OnOff(bool on) => on ? "On" : "Off";

    private static IReadOnlyList<TweakDefinition> Build() =>
    [
        // ------------------------------------------------------------------ Windows Gaming
        new TweakDefinition
        {
            Id = "game-mode",
            Name = "Game Mode",
            Category = TweakCategory.WindowsGaming,
            Description = "Prioritises the game you are playing: Windows Update will not install drivers or show restart notifications, and the game receives preferred CPU scheduling. Improves frame-time consistency on busy systems.",
            Detect = _ =>
            {
                var on = RegistryUtil.ReadNumber(RegRoot.CurrentUser, GameBarKey, "AutoGameModeEnabled") != 0; // missing = Windows default (On)
                return TweakDetection.Of(OnOff(on), "On", on);
            },
            Apply = _ => [new RegistryValueAction(RegRoot.CurrentUser, GameBarKey, "AutoGameModeEnabled", RegValue.Dword(1))],
            Defaults = _ => [new RegistryValueAction(RegRoot.CurrentUser, GameBarKey, "AutoGameModeEnabled", RegValue.Dword(1))],
        },
        new TweakDefinition
        {
            Id = "background-recording",
            Name = "Background recording",
            Category = TweakCategory.WindowsGaming,
            Description = "\"Record what happened\" continuously encodes the last minutes of gameplay in the background. Turning it off frees GPU encoder time and disk writes. Manual recording (Win+Alt+R) keeps working.",
            Detect = _ =>
            {
                var on = RegistryUtil.ReadNumber(RegRoot.CurrentUser, GameDvrKey, "HistoricalCaptureEnabled") == 1;
                return TweakDetection.Of(OnOff(on), "Off", !on);
            },
            Apply = _ => [new RegistryValueAction(RegRoot.CurrentUser, GameDvrKey, "HistoricalCaptureEnabled", RegValue.Dword(0))],
            Defaults = _ => [new RegistryValueAction(RegRoot.CurrentUser, GameDvrKey, "HistoricalCaptureEnabled", RegValue.Dword(0))],
            Restart = RestartRequirement.RestartGame,
        },
        new TweakDefinition
        {
            Id = "game-dvr",
            Name = "Game DVR capture",
            Category = TweakCategory.WindowsGaming,
            Risk = RiskLevel.Moderate,
            Description = "Disables the Game DVR capture hooks that Windows attaches to games. Removes a small per-frame overhead, but Xbox Game Bar screenshots and clip recording will stop working until restored.",
            Preselect = _ => false,
            Detect = _ =>
            {
                var enabled = RegistryUtil.ReadNumber(RegRoot.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled") != 0;
                var capture = RegistryUtil.ReadNumber(RegRoot.CurrentUser, GameDvrKey, "AppCaptureEnabled") != 0;
                var on = enabled || capture;
                return TweakDetection.Of(OnOff(on), "Off", !on);
            },
            Apply = _ =>
            [
                new RegistryValueAction(RegRoot.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled", RegValue.Dword(0)),
                new RegistryValueAction(RegRoot.CurrentUser, GameDvrKey, "AppCaptureEnabled", RegValue.Dword(0)),
            ],
            Defaults = _ =>
            [
                new RegistryValueAction(RegRoot.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled", RegValue.Dword(1)),
                new RegistryValueAction(RegRoot.CurrentUser, GameDvrKey, "AppCaptureEnabled", null),
            ],
            Restart = RestartRequirement.RestartGame,
        },
        new TweakDefinition
        {
            Id = "gamebar-controller",
            Name = "Xbox Game Bar controller shortcut",
            Category = TweakCategory.WindowsGaming,
            Description = "Stops the Xbox button on a controller from opening the Game Bar overlay, which steals focus from fullscreen games. Game Bar itself stays available via Win+G.",
            Preselect = _ => false,
            Detect = _ =>
            {
                var on = RegistryUtil.ReadNumber(RegRoot.CurrentUser, GameBarKey, "UseNexusForGameBarEnabled") != 0;
                return TweakDetection.Of(OnOff(on), "Off", !on);
            },
            Apply = _ => [new RegistryValueAction(RegRoot.CurrentUser, GameBarKey, "UseNexusForGameBarEnabled", RegValue.Dword(0))],
            Defaults = _ => [new RegistryValueAction(RegRoot.CurrentUser, GameBarKey, "UseNexusForGameBarEnabled", RegValue.Dword(1))],
        },

        // ------------------------------------------------------------------ Input
        new TweakDefinition
        {
            Id = "mouse-acceleration",
            Name = "Enhance pointer precision",
            Category = TweakCategory.Input,
            Description = "Windows mouse acceleration changes cursor distance based on movement speed, which makes aim inconsistent. Turning it off gives 1:1 movement. Games using raw input are unaffected either way.",
            Preselect = _ => false,
            Detect = _ =>
            {
                var v = SystemParametersUtil.Get(SpiSetting.MouseAcceleration);
                var on = v[2] != 0;
                return TweakDetection.Of(OnOff(on), "Off", !on);
            },
            Apply = _ => [new SpiAction(SpiSetting.MouseAcceleration, [0, 0, 0])],
            Defaults = _ => [new SpiAction(SpiSetting.MouseAcceleration, [6, 10, 1])],
        },
        new TweakDefinition
        {
            Id = "accessibility-shortcuts",
            Name = "Sticky/Filter/Toggle Keys shortcuts",
            Category = TweakCategory.Input,
            Description = "Pressing Shift five times or holding Shift opens accessibility prompts that minimise fullscreen games. This only disables the keyboard shortcuts — the accessibility features themselves stay available in Settings.",
            Preselect = _ => false,
            Detect = _ =>
            {
                var any = new[] { SpiSetting.StickyKeys, SpiSetting.FilterKeys, SpiSetting.ToggleKeys }
                    .Any(s => (SystemParametersUtil.Get(s)[0] & SystemParametersUtil.HotkeyActiveFlag) != 0);
                return TweakDetection.Of(any ? "Shortcuts on" : "Shortcuts off", "Shortcuts off", !any);
            },
            Apply = _ => AccessibilityShortcuts(enabled: false),
            Defaults = _ => AccessibilityShortcuts(enabled: true),
        },

        // ------------------------------------------------------------------ GPU
        new TweakDefinition
        {
            Id = "hags",
            Name = "Hardware-accelerated GPU scheduling",
            Category = TweakCategory.Gpu,
            Risk = RiskLevel.Moderate,
            RequiresAdmin = true,
            Restart = RestartRequirement.Restart,
            MinBuild = 19041,
            Description = "Lets the GPU manage its own memory scheduling, reducing CPU overhead and latency. Required for DLSS Frame Generation. A few older games may behave differently, so it is not pre-selected.",
            Preselect = _ => false,
            Detect = ctx =>
            {
                if (!ctx.GpuSchedulingSupported)
                    return TweakDetection.NotSupported("Your GPU or driver does not support hardware-accelerated GPU scheduling (requires a WDDM 2.7+ driver and a supported GPU).");
                var reg = RegistryUtil.ReadNumber(RegRoot.LocalMachine, GraphicsDriversKey, "HwSchMode");
                var configured = reg is null ? ctx.GpuSchedulingEnabledNow : reg == 2;
                var current = OnOff(ctx.GpuSchedulingEnabledNow);
                if (configured != ctx.GpuSchedulingEnabledNow) current += $" (→ {OnOff(configured)} after restart)";
                return TweakDetection.Of(current, "On", configured);
            },
            Apply = _ => [new RegistryValueAction(RegRoot.LocalMachine, GraphicsDriversKey, "HwSchMode", RegValue.Dword(2))],
            Defaults = _ => [new RegistryValueAction(RegRoot.LocalMachine, GraphicsDriversKey, "HwSchMode", null)],
        },
        new TweakDefinition
        {
            Id = "windowed-optimizations",
            Name = "Optimizations for windowed games",
            Category = TweakCategory.Gpu,
            MinBuild = 22621,
            Restart = RestartRequirement.RestartGame,
            Description = "Upgrades DirectX 10/11 games running in windowed or borderless mode to the flip presentation model. Lowers latency and enables Auto HDR / VRR in borderless games.",
            Detect = _ =>
            {
                var raw = RegistryUtil.ReadString(RegRoot.CurrentUser, DirectXKey, DirectXGlobal);
                var value = RegistryStringMapAction.Get(raw, "SwapEffectUpgradeEnable");
                var on = value == "1";
                return TweakDetection.Of(value is null ? "Windows default" : OnOff(on), "On", on);
            },
            Apply = _ => [new RegistryStringMapAction(RegRoot.CurrentUser, DirectXKey, DirectXGlobal, "SwapEffectUpgradeEnable", "1")],
            Defaults = _ => [new RegistryStringMapAction(RegRoot.CurrentUser, DirectXKey, DirectXGlobal, "SwapEffectUpgradeEnable", null)],
        },

        // ------------------------------------------------------------------ Power
        new TweakDefinition
        {
            Id = "power-plan",
            Name = "High performance power plan",
            Category = TweakCategory.Power,
            Description = "Keeps the CPU at full performance states instead of parking cores and lowering clocks between frames, which reduces frame-time spikes. Uses more power; on laptops it drains the battery faster.",
            Preselect = ctx => !ctx.HasBattery,
            Detect = _ =>
            {
                var active = PowerPlans.GetActive();
                var name = PowerPlans.ReadName(active);
                var perf = PowerPlans.GetPersonality(active) == PowerPersonality.HighPerformance;
                return TweakDetection.Of(name, "High performance", perf);
            },
            Apply = _ => [HighPerformanceAction()],
            Defaults = _ => [new PowerSchemeAction(PowerPlans.Balanced)],
        },

        // ------------------------------------------------------------------ Background processes
        new TweakDefinition
        {
            Id = "background-apps",
            Name = "Background apps",
            Category = TweakCategory.BackgroundProcesses,
            Risk = RiskLevel.Moderate,
            Description = "Prevents Microsoft Store apps from running in the background when you are not using them. Reduces background CPU wake-ups and memory use. Store apps (e.g. Mail) may stop showing live notifications.",
            Preselect = _ => false,
            Detect = _ =>
            {
                var disabled = RegistryUtil.ReadNumber(RegRoot.CurrentUser, BackgroundAppsKey, "GlobalUserDisabled") == 1;
                return TweakDetection.Of(disabled ? "Restricted" : "Allowed", "Restricted", disabled);
            },
            Apply = _ =>
            [
                new RegistryValueAction(RegRoot.CurrentUser, BackgroundAppsKey, "GlobalUserDisabled", RegValue.Dword(1)),
                new RegistryValueAction(RegRoot.CurrentUser, SearchKey, "BackgroundAppGlobalToggle", RegValue.Dword(0)),
            ],
            Defaults = _ =>
            [
                new RegistryValueAction(RegRoot.CurrentUser, BackgroundAppsKey, "GlobalUserDisabled", null),
                new RegistryValueAction(RegRoot.CurrentUser, SearchKey, "BackgroundAppGlobalToggle", null),
            ],
        },

        // ------------------------------------------------------------------ Privacy & background services
        new TweakDefinition
        {
            Id = "delivery-optimization",
            Name = "Delivery Optimization peer sharing",
            Category = TweakCategory.PrivacyServices,
            RequiresAdmin = true,
            Description = "Stops Windows from uploading update data to other PCs. Frees upload bandwidth that can otherwise add latency during online play. Windows Update keeps working normally.",
            Preselect = _ => false,
            Detect = ctx =>
            {
                if (!RegistryUtil.CanRead(RegRoot.Users, DeliveryOptimizationKey))
                    return TweakDetection.Unknown("Off", "Administrator privileges are required to read this setting.");
                var mode = RegistryUtil.ReadNumber(RegRoot.Users, DeliveryOptimizationKey, "DownloadMode");
                var label = mode switch
                {
                    0 => "Off",
                    1 => "Local network",
                    3 => "Local network and Internet",
                    null => "Windows default (local network)",
                    _ => $"Mode {mode}",
                };
                return TweakDetection.Of(label, "Off", mode == 0);
            },
            Apply = _ => [new RegistryValueAction(RegRoot.Users, DeliveryOptimizationKey, "DownloadMode", RegValue.Dword(0))],
            Defaults = _ => [new RegistryValueAction(RegRoot.Users, DeliveryOptimizationKey, "DownloadMode", RegValue.Dword(1))],
        },
        new TweakDefinition
        {
            Id = "diagtrack",
            Name = "Connected User Experiences and Telemetry",
            Category = TweakCategory.PrivacyServices,
            Risk = RiskLevel.Advanced,
            RequiresAdmin = true,
            Description = "The DiagTrack service periodically collects and uploads diagnostic data, causing short bursts of disk and network activity. Disabling it has a small effect on performance and does not affect security or Windows Update.",
            Preselect = _ => false,
            Detect = _ =>
            {
                if (!ServiceConfig.Exists("DiagTrack")) return TweakDetection.NotSupported("The DiagTrack service is not present on this system.");
                var mode = ServiceConfig.GetStartMode("DiagTrack");
                return TweakDetection.Of(mode.ToString(), "Disabled", mode == ServiceStartMode.Disabled);
            },
            Apply = _ => [new ServiceStartAction("DiagTrack", ServiceStartMode.Disabled, stopNow: true)],
            Defaults = _ => [new ServiceStartAction("DiagTrack", ServiceStartMode.Automatic, stopNow: false)],
        },

        // ------------------------------------------------------------------ Visual effects
        new TweakDefinition
        {
            Id = "window-animations",
            Name = "Window animations",
            Category = TweakCategory.VisualEffects,
            Description = "Turns off minimise/maximise and in-window control animations. Makes alt-tabbing out of games feel faster on low-end systems; it does not change in-game FPS.",
            Preselect = _ => false,
            Detect = _ =>
            {
                var on = SystemParametersUtil.Get(SpiSetting.WindowAnimation)[0] != 0
                         || SystemParametersUtil.Get(SpiSetting.ClientAreaAnimation)[0] != 0;
                return TweakDetection.Of(OnOff(on), "Off", !on);
            },
            Apply = _ =>
            [
                new SpiAction(SpiSetting.WindowAnimation, [0]),
                new SpiAction(SpiSetting.ClientAreaAnimation, [0]),
            ],
            Defaults = _ =>
            [
                new SpiAction(SpiSetting.WindowAnimation, [1]),
                new SpiAction(SpiSetting.ClientAreaAnimation, [1]),
            ],
        },

        // ------------------------------------------------------------------ Network
        new TweakDefinition
        {
            Id = "tcp-autotuning",
            Name = "TCP receive window auto-tuning",
            Category = TweakCategory.Network,
            RequiresAdmin = true,
            Description = "Windows' default \"normal\" auto-tuning lets TCP scale its receive window to your connection. Old \"gaming tweak\" scripts often disable it, which throttles downloads and game updates. This restores the correct setting.",
            Detect = _ =>
            {
                var level = TcpSettings.ReadAutoTuningLevel();
                return level is null
                    ? TweakDetection.NotSupported("The TCP settings could not be read on this system.")
                    : TweakDetection.Of(Capitalize(TcpSettings.Name(level.Value)), "Normal", level == TcpSettings.Normal);
            },
            Apply = _ => [new TcpAutoTuningAction { Level = TcpSettings.Normal }],
            Defaults = _ => [new TcpAutoTuningAction { Level = TcpSettings.Normal }],
        },
        new TweakDefinition
        {
            Id = "adapter-power-saving",
            Name = "Network adapter power saving",
            Category = TweakCategory.Network,
            RequiresAdmin = true,
            Description = "Prevents Windows from powering down your active network adapter to save energy, which can cause brief disconnects and latency spikes after idle periods.",
            Preselect = ctx => !ctx.HasBattery,
            Detect = ctx =>
            {
                if (ctx.PrimaryAdapterPnpId is null)
                    return TweakDetection.NotSupported("No active physical network adapter was found.");
                var allowed = AdapterPowerUtil.Read(ctx.PrimaryAdapterPnpId);
                if (allowed is null)
                    return TweakDetection.NotSupported($"{ctx.PrimaryAdapterName} does not expose power management settings.");
                return TweakDetection.Of(allowed.Value ? "Allowed to power off" : "Always on", "Always on", !allowed.Value);
            },
            Apply = ctx => [new AdapterPowerAction { PnpDeviceId = ctx.PrimaryAdapterPnpId ?? string.Empty, AdapterName = ctx.PrimaryAdapterName ?? "adapter", AllowPowerOff = false }],
            Defaults = ctx => [new AdapterPowerAction { PnpDeviceId = ctx.PrimaryAdapterPnpId ?? string.Empty, AdapterName = ctx.PrimaryAdapterName ?? "adapter", AllowPowerOff = true }],
        },
    ];

    /// <summary>Activates an existing High/Ultimate performance plan, or creates a copy of the built-in High performance template.</summary>
    public static PowerSchemeAction HighPerformanceAction()
    {
        var schemes = PowerPlans.List();
        var existing = schemes.FirstOrDefault(s => s.Id == PowerPlans.HighPerformance)
                       ?? schemes.FirstOrDefault(s => s.Id == PowerPlans.UltimatePerformance)
                       ?? schemes.FirstOrDefault(s => s.IsPerformance);
        return existing is not null
            ? new PowerSchemeAction(existing.Id)
            : new PowerSchemeAction(PowerPlans.HighPerformance, PowerPlans.HighPerformance);
    }

    private static IReadOnlyList<SystemAction> AccessibilityShortcuts(bool enabled)
    {
        var list = new List<SystemAction>();
        foreach (var setting in new[] { SpiSetting.StickyKeys, SpiSetting.FilterKeys, SpiSetting.ToggleKeys })
        {
            var flags = SystemParametersUtil.Get(setting)[0];
            flags = enabled ? flags | SystemParametersUtil.HotkeyActiveFlag : flags & ~SystemParametersUtil.HotkeyActiveFlag;
            list.Add(new SpiAction(setting, [flags]));
        }
        return list;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
