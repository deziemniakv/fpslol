using FpsLol.Models;
using FpsLol.Services;
using FpsLol.ViewModels.Items;

namespace FpsLol.SystemIntegration;

public static class GamingInfo
{
    private const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>True if the Xbox Game Bar package is registered for the current user.</summary>
    public static bool IsGameBarInstalled()
    {
        try
        {
            using var baseKey = RegistryUtil.OpenBase(RegRoot.CurrentUser);
            using var key = baseKey.OpenSubKey(PackagesKey);
            return key?.GetSubKeyNames().Any(n => n.StartsWith("Microsoft.XboxGamingOverlay_", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The five gaming status rows shown on Dashboard and Gaming, built from a real scan.</summary>
    public static IReadOnlyList<StatusItem> BuildStatus(IReadOnlyList<TweakStatus> tweaks)
    {
        TweakDetection? D(string id) => tweaks.FirstOrDefault(t => t.Definition.Id == id)?.Detection;

        StatusItem Row(string label, string icon, TweakDetection? d) => d is null || !d.Supported
            ? new StatusItem(label, "Not supported", "neutral", icon)
            : new StatusItem(label, d.Current, d.IsOptimal ? "success" : "warning", icon);

        var gameBar = IsGameBarInstalled();
        var controller = D("gamebar-controller");
        var gameBarRow = !gameBar
            ? new StatusItem("Xbox Game Bar", "Not installed", "success", "Icon.Layers")
            : new StatusItem("Xbox Game Bar", controller is { IsOptimal: false } ? "Installed · controller shortcut on" : "Installed", "info", "Icon.Layers");

        return
        [
            Row("Game Mode", "Icon.Gamepad", D("game-mode")),
            Row("Hardware-accelerated GPU scheduling", "Icon.Monitor", D("hags")),
            gameBarRow,
            Row("Background recording", "Icon.Activity", D("background-recording")),
            Row("Power plan", "Icon.Power", D("power-plan")),
        ];
    }
}
