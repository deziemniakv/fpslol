using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using Xunit;

namespace FpsLol.Tests;

/// <summary>Pure logic tests — no system state is modified.</summary>
public class SafetyAndTransactionTests
{
    [Theory]
    [InlineData(RegRoot.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", true)]
    [InlineData(RegRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", true)]
    [InlineData(RegRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", true)]
    [InlineData(RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe\PerfOptions", "CpuPriorityClass", true)]
    [InlineData(RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe", "Debugger", false)]
    [InlineData(RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe\PerfOptions", "IoPriority", false)]
    [InlineData(RegRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", false)]
    [InlineData(RegRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", false)]
    [InlineData(RegRoot.CurrentUser, @"Software\Microsoft\GameBar\..\..\Windows", "x", false)]
    public void Registry_allow_list_is_enforced(RegRoot root, string path, string name, bool allowed)
    {
        Assert.Equal(allowed, SafetyPolicy.CheckRegistry(root, path, name) is null);
    }

    [Theory]
    [InlineData("DiagTrack", true)]
    [InlineData("WinDefend", false)]
    [InlineData("wuauserv", false)]
    [InlineData("SysMain", false)]
    public void Service_allow_list_is_enforced(string service, bool allowed)
    {
        Assert.Equal(allowed, SafetyPolicy.CheckService(service) is null);
    }

    [Fact]
    public void Blocked_action_is_never_executed()
    {
        var action = new RegistryValueAction(RegRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", RegValue.Dword(1));
        var outcome = TransactionRunner.Apply([action]);
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.Contains("safety policy", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(outcome.Snapshots);
    }

    [Fact]
    public void Failed_verification_rolls_back_previous_actions()
    {
        var state = new Dictionary<string, int> { ["a"] = 1, ["b"] = 1 };
        var first = new FakeAction(state, "a", 2, verifies: true);
        var second = new FakeAction(state, "b", 2, verifies: false);

        var outcome = TransactionRunner.Apply([first, second]);

        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.Equal(1, state["a"]); // rolled back
        Assert.Equal(1, state["b"]); // rolled back
        Assert.Contains("previous state was restored", outcome.Message);
    }

    [Fact]
    public void Successful_transaction_returns_snapshots_for_restore()
    {
        var state = new Dictionary<string, int> { ["a"] = 1 };
        var outcome = TransactionRunner.Apply([new FakeAction(state, "a", 5, verifies: true)]);
        Assert.Equal(OperationStatus.Success, outcome.Status);
        Assert.Equal(5, state["a"]);

        var restore = TransactionRunner.Restore(outcome.Snapshots);
        Assert.Equal(OperationStatus.Success, restore.Status);
        Assert.Equal(1, state["a"]);
    }

    [Fact]
    public void String_map_parsing_preserves_other_entries()
    {
        var map = RegistryStringMapAction.Parse("SwapEffectUpgradeEnable=0;VRROptimizeEnable=1;");
        Assert.Equal(2, map.Count);
        Assert.Equal("1", RegistryStringMapAction.Get("SwapEffectUpgradeEnable=0;VRROptimizeEnable=1;", "VRROptimizeEnable"));
        Assert.Null(RegistryStringMapAction.Get(null, "GpuPreference"));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\tool.exe -silent", "C:\\Tools\\tool.exe")]
    [InlineData("C:\\Tools\\tool.EXE", "C:\\Tools\\tool.EXE")]
    public void Startup_command_resolves_executable(string command, string expected)
    {
        Assert.Equal(expected, StartupService.ResolveExecutable(command));
    }

    [Fact]
    public void Score_is_deterministic_and_excludes_unsupported_settings()
    {
        var game = TweakCatalog.Find("game-mode")!;
        var hags = TweakCatalog.Find("hags")!;
        var tweaks = new List<TweakStatus>
        {
            new(game, TweakDetection.Of("On", "On", true), false),
            new(hags, TweakDetection.NotSupported("no driver support"), false),
        };
        var service = new ScoreService();
        var a = service.Compute(tweaks, null, enabledStartupApps: 3);
        var b = service.Compute(tweaks, null, enabledStartupApps: 3);

        Assert.Equal(a.Score, b.Score);
        Assert.DoesNotContain(a.Items, i => i.Title == hags.Name);
        Assert.Equal(0, a.AvailableOptimizations);
    }

    private sealed class FakeAction(Dictionary<string, int> state, string key, int value, bool verifies) : SystemAction
    {
        public override bool RequiresAdmin => false;
        public override string Describe() => $"{key}={value}";
        public override ActionSnapshot Capture() => new FakeSnapshot(state, key, state[key]);
        public override void Execute() => state[key] = value;
        public override bool Verify() => verifies && state[key] == value;
    }

    private sealed class FakeSnapshot(Dictionary<string, int> state, string key, int previous) : ActionSnapshot
    {
        public override bool RequiresAdmin => false;
        public override string Describe() => $"{key}={previous}";
        public override void Restore() => state[key] = previous;
        public override bool VerifyRestored() => state[key] == previous;
    }
}
