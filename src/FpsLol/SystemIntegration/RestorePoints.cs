using System.Management;
using FpsLol.Models;

namespace FpsLol.SystemIntegration;

/// <summary>Creates Windows System Restore points via WMI (requires administrator).</summary>
public static class RestorePoints
{
    private const uint ModifySettings = 12;
    private const uint BeginSystemChange = 100;

    public static OperationResult Create(string description)
    {
        const string title = "System Restore point";
        if (!Elevation.IsElevated)
            return OperationResult.Skip(title, "Administrator privileges are required to create a restore point.");

        try
        {
            var before = LatestSequence();

            using var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            using var input = cls.GetMethodParameters("CreateRestorePoint");
            input["Description"] = description;
            input["RestorePointType"] = ModifySettings;
            input["EventType"] = BeginSystemChange;
            using var output = cls.InvokeMethod("CreateRestorePoint", input, null);
            var code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u);

            if (code == 1058)
                return OperationResult.Skip(title, "System Protection is turned off for the system drive, so Windows cannot create restore points. FPS.LOL's own backup was still created.");
            if (code != 0)
                return OperationResult.Fail(title, $"Windows could not create a restore point (error {code}). FPS.LOL's own backup was still created.");

            var after = LatestSequence();
            if (after is not null && after != before)
                return OperationResult.Ok(title, "Restore point created.");

            return OperationResult.Skip(title,
                "Windows did not create a new restore point because one was already created in the last 24 hours (Windows limit). The existing restore point and FPS.LOL's own backup remain available.");
        }
        catch (ManagementException ex)
        {
            return OperationResult.Fail(title, "System Restore is not available on this system.", ex.Message);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(title, "Could not create a restore point.", ex.Message);
        }
    }

    private static uint? LatestSequence()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber FROM SystemRestore");
            uint? max = null;
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var seq = Convert.ToUInt32(mo["SequenceNumber"]);
                    if (max is null || seq > max) max = seq;
                }
            }
            return max;
        }
        catch
        {
            return null;
        }
    }
}
