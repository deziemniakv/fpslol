using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.Services;

public interface IGameProfileService
{
    IReadOnlyList<GameProfile> Profiles { get; }
    event EventHandler? Changed;
    GameProfile Add(string name, string exePath, string source, string installDir);
    void Update(GameProfile profile);
    void Remove(GameProfile profile);
    ProfileState ReadState(GameProfile profile);
    IReadOnlyList<ChangeRequest> BuildApply(GameProfile profile);
    IReadOnlyList<ChangeRecord> ActiveRecords(GameProfile profile);
}

/// <summary>
/// Per-game settings using documented Windows mechanisms:
/// CPU priority (Image File Execution Options\PerfOptions), GPU preference (Graphics settings) and
/// the "Disable fullscreen optimizations" compatibility flag.
/// </summary>
public sealed class GameProfileService(IHistoryService history) : IGameProfileService
{
    private const string IfeoBase = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";
    private const string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string FsoFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    private readonly List<GameProfile> _profiles = JsonStore.Load(AppPaths.ProfilesFile, () => new List<GameProfile>());

    public IReadOnlyList<GameProfile> Profiles => _profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public event EventHandler? Changed;

    public GameProfile Add(string name, string exePath, string source, string installDir)
    {
        var existing = _profiles.FirstOrDefault(p => string.Equals(p.ExecutablePath, exePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var profile = new GameProfile { Name = name, ExecutablePath = exePath, Source = source, InstallDir = installDir };
        var state = ReadState(profile);
        profile.Priority = state.Priority;
        profile.HighPerformanceGpu = state.HighPerformanceGpu;
        profile.DisableFullscreenOptimizations = state.FullscreenOptimizationsDisabled;
        _profiles.Add(profile);
        Save();
        return profile;
    }

    public void Update(GameProfile profile) => Save();

    public void Remove(GameProfile profile)
    {
        _profiles.Remove(profile);
        Save();
    }

    public ProfileState ReadState(GameProfile profile)
    {
        var exeName = Path.GetFileName(profile.ExecutablePath);
        var priority = RegistryUtil.ReadNumber(RegRoot.LocalMachine, IfeoBase + exeName + @"\PerfOptions", "CpuPriorityClass") switch
        {
            3 => PriorityOption.High,
            6 => PriorityOption.AboveNormal,
            _ => PriorityOption.Default,
        };
        var gpu = RegistryStringMapAction.Get(RegistryUtil.ReadString(RegRoot.CurrentUser, GpuPrefKey, profile.ExecutablePath), "GpuPreference") == "2";
        var fso = AppCompatLayerAction.HasFlag(profile.ExecutablePath, FsoFlag);
        return new ProfileState(priority, gpu, fso);
    }

    public IReadOnlyList<ChangeRequest> BuildApply(GameProfile profile)
    {
        var state = ReadState(profile);
        var requests = new List<ChangeRequest>();
        var exeName = Path.GetFileName(profile.ExecutablePath);

        if (state.Priority != profile.Priority)
        {
            RegValue? value = profile.Priority switch
            {
                PriorityOption.High => RegValue.Dword(3),
                PriorityOption.AboveNormal => RegValue.Dword(6),
                _ => null,
            };
            requests.Add(new ChangeRequest
            {
                SourceId = $"profile:{profile.Id}:priority",
                Title = $"{profile.Name}: CPU priority",
                Category = "Game Profiles",
                Before = Label(state.Priority),
                After = Label(profile.Priority),
                Restart = RestartRequirement.RestartGame,
                Actions = [new RegistryValueAction(RegRoot.LocalMachine, IfeoBase + exeName + @"\PerfOptions", "CpuPriorityClass", value)],
            });
        }

        if (state.HighPerformanceGpu != profile.HighPerformanceGpu)
        {
            requests.Add(new ChangeRequest
            {
                SourceId = $"profile:{profile.Id}:gpu",
                Title = $"{profile.Name}: GPU preference",
                Category = "Game Profiles",
                Before = state.HighPerformanceGpu ? "High performance GPU" : "Let Windows decide",
                After = profile.HighPerformanceGpu ? "High performance GPU" : "Let Windows decide",
                Restart = RestartRequirement.RestartGame,
                Actions = [new RegistryStringMapAction(RegRoot.CurrentUser, GpuPrefKey, profile.ExecutablePath, "GpuPreference", profile.HighPerformanceGpu ? "2" : null)],
            });
        }

        if (state.FullscreenOptimizationsDisabled != profile.DisableFullscreenOptimizations)
        {
            requests.Add(new ChangeRequest
            {
                SourceId = $"profile:{profile.Id}:fso",
                Title = $"{profile.Name}: Fullscreen optimizations",
                Category = "Game Profiles",
                Before = state.FullscreenOptimizationsDisabled ? "Disabled" : "Enabled",
                After = profile.DisableFullscreenOptimizations ? "Disabled" : "Enabled",
                Restart = RestartRequirement.RestartGame,
                Actions = [new AppCompatLayerAction(profile.ExecutablePath, FsoFlag, profile.DisableFullscreenOptimizations)],
            });
        }
        return requests;
    }

    public IReadOnlyList<ChangeRecord> ActiveRecords(GameProfile profile) =>
        history.Records.Where(r => !r.Restored && r.SourceId.StartsWith($"profile:{profile.Id}:", StringComparison.Ordinal)).ToList();

    private static string Label(PriorityOption p) => p switch
    {
        PriorityOption.High => "High",
        PriorityOption.AboveNormal => "Above normal",
        _ => "Windows default",
    };

    private void Save()
    {
        JsonStore.Save(AppPaths.ProfilesFile, _profiles);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
