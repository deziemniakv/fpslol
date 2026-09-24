using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.SystemIntegration;

namespace FpsLol.Services;

public interface IPowerService
{
    IReadOnlyList<PowerScheme> List();
    DevicePowerInfo Device();
    ChangeRequest BuildActivate(PowerScheme target, PowerScheme? current);
    OperationResult AddUltimatePerformance();
    OperationResult DeleteCreated(PowerScheme scheme);
    bool IsCreatedByFpsLol(Guid scheme);
}

public sealed class PowerService(ILogService log, ISettingsService settings) : IPowerService
{
    public IReadOnlyList<PowerScheme> List() => PowerPlans.List();

    public DevicePowerInfo Device() => PowerPlans.GetDeviceInfo();

    public ChangeRequest BuildActivate(PowerScheme target, PowerScheme? current) => new()
    {
        SourceId = "power-plan",
        Title = "Power plan",
        Category = "Power",
        Before = current?.Name ?? "Unknown",
        After = target.Name,
        Actions = [new PowerSchemeAction(target.Id)],
    };

    public OperationResult AddUltimatePerformance()
    {
        const string title = "Ultimate Performance plan";
        if (PowerPlans.List().Any(s => s.Id == PowerPlans.UltimatePerformance || s.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Skip(title, "An Ultimate Performance plan already exists on this PC.");
        try
        {
            var id = PowerPlans.Duplicate(PowerPlans.UltimatePerformance);
            if (!PowerPlans.Exists(id))
                return OperationResult.Fail(title, "Windows did not create the plan. This device may only support the Balanced plan (Modern Standby).");
            settings.Current.CreatedPowerSchemes.Add(id);
            settings.Save();
            log.Success($"Ultimate Performance plan created ({id})");
            return OperationResult.Ok(title, "The plan was added. Select it below to activate it.");
        }
        catch (Exception ex)
        {
            log.Error("Could not create the Ultimate Performance plan.", ex);
            return OperationResult.Fail(title, "This edition of Windows or this device does not provide the Ultimate Performance template.", ex.Message);
        }
    }

    public bool IsCreatedByFpsLol(Guid scheme) => settings.Current.CreatedPowerSchemes.Contains(scheme);

    public OperationResult DeleteCreated(PowerScheme scheme)
    {
        var title = $"Remove {scheme.Name}";
        if (!IsCreatedByFpsLol(scheme.Id))
            return OperationResult.Skip(title, "FPS.LOL only removes plans it created itself.");
        if (scheme.IsActive)
            return OperationResult.Skip(title, "The active plan cannot be removed. Activate another plan first.");
        try
        {
            PowerPlans.Delete(scheme.Id);
            settings.Current.CreatedPowerSchemes.Remove(scheme.Id);
            settings.Save();
            log.Info($"Power plan removed: {scheme.Name}");
            return OperationResult.Ok(title, "The plan was removed.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(title, "The plan could not be removed.", ex.Message);
        }
    }
}
