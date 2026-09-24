using System.Text;
using FpsLol.Models;

namespace FpsLol.Optimizations;

public sealed class ChangeOutcome
{
    public OperationStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public List<ActionSnapshot> Snapshots { get; set; } = [];

    public static ChangeOutcome Failed(string message, string? details = null) =>
        new() { Status = OperationStatus.Failed, Message = message, Details = details };
}

/// <summary>
/// Applies a group of actions as one unit: validate → snapshot everything → execute → verify.
/// If anything fails, every action already executed is rolled back in reverse order.
/// </summary>
public static class TransactionRunner
{
    public static ChangeOutcome Apply(IReadOnlyList<SystemAction> actions)
    {
        if (actions.Count == 0)
            return new ChangeOutcome { Status = OperationStatus.Skipped, Message = "Nothing to change." };

        foreach (var action in actions)
        {
            if (action.Validate() is { } error)
                return ChangeOutcome.Failed("Blocked by the FPS.LOL safety policy.", error);
        }

        var snapshots = new List<ActionSnapshot>(actions.Count);
        try
        {
            foreach (var action in actions) snapshots.Add(action.Capture());
        }
        catch (Exception ex)
        {
            return ChangeOutcome.Failed("Could not read the current setting, so nothing was changed.", Describe(ex));
        }

        int executed = 0;
        try
        {
            for (; executed < actions.Count; executed++)
            {
                var action = actions[executed];
                action.Execute();
                if (!action.Verify())
                {
                    executed++;
                    throw new InvalidOperationException($"Verification failed: \"{action.Describe()}\" did not take effect.");
                }
            }
        }
        catch (Exception ex)
        {
            var details = new StringBuilder(Describe(ex));
            var rollbackOk = true;
            for (int i = executed - 1; i >= 0; i--)
            {
                try
                {
                    snapshots[i].Restore();
                }
                catch (Exception rollbackEx)
                {
                    rollbackOk = false;
                    details.AppendLine().Append("Rollback failed for ").Append(snapshots[i].Describe()).Append(": ").Append(rollbackEx.Message);
                }
            }
            return ChangeOutcome.Failed(
                rollbackOk ? "The change could not be applied. The previous state was restored." : "The change failed and could not be fully rolled back. See details.",
                details.ToString());
        }

        return new ChangeOutcome { Status = OperationStatus.Success, Message = "Applied and verified.", Snapshots = snapshots };
    }

    public static ChangeOutcome Restore(IReadOnlyList<ActionSnapshot> snapshots)
    {
        foreach (var s in snapshots)
        {
            if (s.Validate() is { } error)
                return ChangeOutcome.Failed("Blocked by the FPS.LOL safety policy.", error);
        }

        var failures = new StringBuilder();
        // Restore in reverse order of capture.
        for (int i = snapshots.Count - 1; i >= 0; i--)
        {
            var s = snapshots[i];
            try
            {
                s.Restore();
                if (!s.VerifyRestored()) failures.AppendLine($"Verification failed: {s.Describe()}");
            }
            catch (Exception ex)
            {
                failures.AppendLine($"{s.Describe()}: {Describe(ex)}");
            }
        }

        return failures.Length == 0
            ? new ChangeOutcome { Status = OperationStatus.Success, Message = "Previous state restored and verified." }
            : ChangeOutcome.Failed("The previous state could not be fully restored.", failures.ToString().Trim());
    }

    public static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException or System.Security.SecurityException => "Access denied. Administrator privileges are required.",
        System.ComponentModel.Win32Exception w when w.NativeErrorCode == 5 => "Access denied. Administrator privileges are required.",
        _ => ex.Message,
    };
}
