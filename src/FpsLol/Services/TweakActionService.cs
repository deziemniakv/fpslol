using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.SystemIntegration;

namespace FpsLol.Services;

public interface ITweakActionService
{
    /// <summary>Applies one tweak (with confirmation when enabled in settings). Returns the refreshed status.</summary>
    Task<TweakStatus?> ApplyAsync(TweakDefinition tweak);

    /// <summary>Restores the value FPS.LOL replaced, or — when there is no history — offers the Windows default.</summary>
    Task<TweakStatus?> RestoreAsync(TweakDefinition tweak);
}

public sealed class TweakActionService(
    ILogService log,
    ITweakEngine engine,
    IChangeService changes,
    IHistoryService history,
    ISettingsService settings,
    IDialogService dialogs,
    IToastService toasts,
    ISystemScanService scan) : ITweakActionService
{
    public async Task<TweakStatus?> ApplyAsync(TweakDefinition tweak)
    {
        var ctx = await engine.GetContextAsync(refresh: true);
        var status = await Task.Run(() => engine.Detect(tweak, ctx));

        if (!status.Detection.Supported)
        {
            toasts.Info(tweak.Name, status.Detection.UnsupportedReason ?? "Not supported on this PC.");
            return status;
        }
        if (status.Detection.IsOptimal)
        {
            toasts.Info(tweak.Name, "Already in the recommended state.");
            return status;
        }

        if (settings.Current.AskBeforeApplying)
        {
            var lines = new List<string>
            {
                $"Change: {status.Detection.Current} → {status.Detection.Recommended}",
                $"Risk level: {tweak.Risk}",
                CategoryNames.Of(tweak.Restart),
            };
            if (tweak.RequiresAdmin && !Elevation.IsElevated) lines.Add("Administrator privileges required (Windows will ask for permission)");
            lines.Add("The previous value is saved and can be restored at any time");
            if (!await dialogs.ConfirmAsync($"Apply \"{tweak.Name}\"?", tweak.Description, "Apply", items: lines)) return status;
        }

        IReadOnlyList<ChangeRequest> request;
        try
        {
            request = [engine.BuildApply(tweak, ctx, status.Detection)];
        }
        catch (Exception ex)
        {
            await dialogs.ShowErrorAsync(tweak.Name, "The change could not be prepared.", ex.Message);
            return status;
        }

        var report = await changes.ExecuteAsync(request, createRestorePoint: false, tweak.Name);
        await ReportAsync(report.Changes[0].Result, report.RestartRequired);
        return await RefreshAsync(tweak);
    }

    public async Task<TweakStatus?> RestoreAsync(TweakDefinition tweak)
    {
        var records = history.ActiveFor(tweak.Id);
        if (records.Count > 0)
        {
            var original = records[^1];
            if (!await dialogs.ConfirmAsync($"Restore \"{tweak.Name}\"?",
                    $"FPS.LOL will restore the value it replaced on {original.Timestamp:g}.", "Restore",
                    items: [$"Change: {records[0].After} → {original.Before}"]))
                return null;

            var results = await changes.RestoreAsync(records);
            var failed = results.FirstOrDefault(r => !r.Result.IsSuccess);
            await ReportAsync(failed.Record is null ? OperationResult.Ok(tweak.Name, "Previous value restored and verified.") : failed.Result, tweak.Restart == RestartRequirement.Restart);
            return await RefreshAsync(tweak);
        }

        // No FPS.LOL history: offer the Windows default instead.
        var ctx = await engine.GetContextAsync(refresh: true);
        var status = await Task.Run(() => engine.Detect(tweak, ctx));
        if (!status.Detection.Supported) return status;
        if (!await dialogs.ConfirmAsync($"Reset \"{tweak.Name}\" to Windows default?",
                "FPS.LOL has not changed this setting, so there is no previous value to restore. You can reset it to the Windows default instead. The current value will be saved in the Restore Center.",
                "Reset to default"))
            return status;

        var report = await changes.ExecuteAsync([engine.BuildDefaults(tweak, ctx, status.Detection)], createRestorePoint: false, tweak.Name + " (default)");
        await ReportAsync(report.Changes[0].Result, report.RestartRequired);
        return await RefreshAsync(tweak);
    }

    private async Task ReportAsync(OperationResult result, bool restart)
    {
        switch (result.Status)
        {
            case OperationStatus.Success:
                toasts.Success(result.Title, restart ? "Applied. Restart Windows for the change to take effect." : result.Message);
                break;
            case OperationStatus.Failed:
                await dialogs.ShowErrorAsync(result.Title, result.Message, result.Details);
                break;
            default:
                toasts.Show(result);
                break;
        }
    }

    private async Task<TweakStatus?> RefreshAsync(TweakDefinition tweak)
    {
        try
        {
            var ctx = await engine.GetContextAsync(refresh: true);
            var status = await Task.Run(() => engine.Detect(tweak, ctx));
            engine.NotifyStateChanged();
            _ = scan.ScanAsync(); // keep score and other pages in sync
            return status;
        }
        catch (Exception ex)
        {
            log.Warn("Could not refresh tweak state.", ex);
            return null;
        }
    }
}
