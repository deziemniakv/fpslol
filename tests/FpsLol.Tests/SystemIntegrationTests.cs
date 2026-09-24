using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.SystemIntegration;
using Xunit;

namespace FpsLol.Tests;

/// <summary>
/// Real-system tests. Each test applies a reversible, per-user change through the same code path the app
/// uses, verifies it, restores the original value and verifies the restore. Nothing requires elevation.
/// Exclude with: dotnet test --filter "Category!=System"
/// </summary>
[Trait("Category", "System")]
[Collection("System")]
public class SystemIntegrationTests
{
    [Fact]
    public void Registry_value_apply_and_restore_round_trip()
    {
        const string path = @"Software\Microsoft\GameBar";
        const string name = "FpsLolIntegrationTest";
        RegistryUtil.DeleteValue(RegRoot.CurrentUser, path, name);

        var outcome = TransactionRunner.Apply([new RegistryValueAction(RegRoot.CurrentUser, path, name, RegValue.Dword(0xFFFFFFFF))]);
        Assert.Equal(OperationStatus.Success, outcome.Status);
        Assert.Equal(0xFFFFFFFFL, RegistryUtil.ReadNumber(RegRoot.CurrentUser, path, name));

        var restore = TransactionRunner.Restore(outcome.Snapshots);
        Assert.Equal(OperationStatus.Success, restore.Status);
        Assert.Null(RegistryUtil.Read(RegRoot.CurrentUser, path, name)); // value did not exist before → removed again
    }

    [Fact]
    public void Window_animation_setting_apply_and_restore()
    {
        var before = SystemParametersUtil.Get(SpiSetting.WindowAnimation);
        var target = before[0] == 0 ? 1 : 0;

        var outcome = TransactionRunner.Apply([new SpiAction(SpiSetting.WindowAnimation, [target])]);
        Assert.Equal(OperationStatus.Success, outcome.Status);
        Assert.Equal(target, SystemParametersUtil.Get(SpiSetting.WindowAnimation)[0]);

        Assert.Equal(OperationStatus.Success, TransactionRunner.Restore(outcome.Snapshots).Status);
        Assert.Equal(before, SystemParametersUtil.Get(SpiSetting.WindowAnimation));
    }

    [Fact]
    public void Power_plan_switch_and_restore()
    {
        var original = PowerPlans.GetActive();
        var other = PowerPlans.List().First(s => s.Id != original);

        var outcome = TransactionRunner.Apply([new PowerSchemeAction(other.Id)]);
        Assert.Equal(OperationStatus.Success, outcome.Status);
        Assert.Equal(other.Id, PowerPlans.GetActive());

        Assert.Equal(OperationStatus.Success, TransactionRunner.Restore(outcome.Snapshots).Status);
        Assert.Equal(original, PowerPlans.GetActive());
    }

    [Fact]
    public void Snapshots_survive_json_round_trip()
    {
        var snapshot = RegistryValueSnapshot.Take(RegRoot.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
        var record = new ChangeRecord { Title = "Game Mode", Snapshots = [snapshot, new PowerSchemeSnapshot { Previous = PowerPlans.GetActive() }] };
        var json = System.Text.Json.JsonSerializer.Serialize(record, Utilities.JsonStore.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<ChangeRecord>(json, Utilities.JsonStore.Options)!;

        Assert.IsType<RegistryValueSnapshot>(back.Snapshots[0]);
        Assert.IsType<PowerSchemeSnapshot>(back.Snapshots[1]);
        Assert.True(back.Snapshots[0].VerifyRestored()); // still matches the live value
    }

    [Fact]
    public void Every_tweak_detects_without_throwing()
    {
        var ctx = new TweakContext(WindowsInfo.Current.Build, Elevation.IsElevated, false, false, false, false, null, null);
        foreach (var tweak in TweakCatalog.All)
        {
            var d = tweak.Detect(ctx);
            Assert.False(string.IsNullOrWhiteSpace(d.Current), tweak.Id);
        }
    }
}
