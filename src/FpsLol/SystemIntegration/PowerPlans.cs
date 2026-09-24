using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using static FpsLol.SystemIntegration.NativeMethods;

namespace FpsLol.SystemIntegration;

/// <summary>Values of GUID_POWERSCHEME_PERSONALITY (verified against the built-in schemes).</summary>
public enum PowerPersonality
{
    PowerSaver = 0,
    HighPerformance = 1,
    Balanced = 2,
    Unknown = -1,
}

public sealed record PowerScheme(Guid Id, string Name, PowerPersonality Personality, bool IsActive)
{
    public bool IsPerformance => Personality == PowerPersonality.HighPerformance;
}

public sealed record DevicePowerInfo(bool HasBattery, bool OnAcPower, bool ModernStandby, int? BatteryPercent);

/// <summary>Power scheme management through the documented powrprof API (no locale-dependent powercfg parsing).</summary>
public static class PowerPlans
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private static readonly Guid NoSubgroup = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid PersonalitySetting = new("245d8541-3943-4422-b025-13a784f679b7");

    public static Guid GetActive()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var ptr);
        if (result != 0) throw new Win32Exception((int)result);
        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    public static void SetActive(Guid scheme)
    {
        var result = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (result != 0) throw new Win32Exception((int)result);
    }

    public static IReadOnlyList<PowerScheme> List()
    {
        var active = SafeGetActive();
        var list = new List<PowerScheme>();
        for (uint i = 0; ; i++)
        {
            uint size = 16;
            var buffer = new byte[16];
            var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, buffer, ref size);
            if (result != 0) break; // ERROR_NO_MORE_ITEMS
            var id = new Guid(buffer);
            list.Add(new PowerScheme(id, ReadName(id), GetPersonality(id), id == active));
        }
        return list;
    }

    public static bool Exists(Guid scheme) => List().Any(s => s.Id == scheme);

    public static string ReadName(Guid scheme)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0) return scheme.ToString();
        var buffer = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0)
            return scheme.ToString();
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    public static PowerPersonality GetPersonality(Guid scheme)
    {
        var sub = NoSubgroup;
        var setting = PersonalitySetting;
        if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var value) == 0)
            return (PowerPersonality)(int)value;

        // OEM/chipset plans (e.g. "AMD Ryzen High Performance") often have no personality. Classify them by
        // what they actually do: a minimum processor state of 90%+ on AC keeps the CPU at full performance.
        var processor = ProcessorSubgroup;
        var minState = ProcessorMinimumState;
        if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref processor, ref minState, out var min) == 0)
            return min >= 90 ? PowerPersonality.HighPerformance : PowerPersonality.Balanced;
        return PowerPersonality.Unknown;
    }

    private static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcessorMinimumState = new("893dee8e-2bef-41e0-89c6-b55d0929964c");

    /// <summary>Creates a copy of a built-in scheme template (e.g. Ultimate Performance). Never deletes anything.</summary>
    public static Guid Duplicate(Guid template)
    {
        IntPtr dest = IntPtr.Zero;
        var result = PowerDuplicateScheme(IntPtr.Zero, ref template, ref dest);
        if (result != 0) throw new Win32Exception((int)result);
        try
        {
            return Marshal.PtrToStructure<Guid>(dest);
        }
        finally
        {
            LocalFree(dest);
        }
    }

    public static void Delete(Guid scheme)
    {
        var result = PowerDeleteScheme(IntPtr.Zero, ref scheme);
        if (result != 0) throw new Win32Exception((int)result);
    }

    public static DevicePowerInfo GetDeviceInfo()
    {
        bool hasBattery = false, onAc = true;
        int? percent = null;
        if (GetSystemPowerStatus(out var status))
        {
            hasBattery = status.BatteryFlag != 128 && status.BatteryFlag != 255;
            onAc = status.ACLineStatus != 0;
            if (hasBattery && status.BatteryLifePercent <= 100) percent = status.BatteryLifePercent;
        }

        bool modernStandby = false;
        // SYSTEM_POWER_CAPABILITIES: AoAc (Modern Standby) is the BOOLEAN at offset 20.
        var caps = Marshal.AllocHGlobal(128);
        try
        {
            for (int i = 0; i < 128; i++) Marshal.WriteByte(caps, i, 0);
            if (GetPwrCapabilities(caps)) modernStandby = Marshal.ReadByte(caps, 20) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(caps);
        }

        return new DevicePowerInfo(hasBattery, onAc, modernStandby, percent);
    }

    private static Guid SafeGetActive()
    {
        try { return GetActive(); } catch { return Guid.Empty; }
    }
}
