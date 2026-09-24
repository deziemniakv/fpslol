using System.Diagnostics;
using static FpsLol.SystemIntegration.NativeMethods;

namespace FpsLol.SystemIntegration;

public enum ProcessProtection
{
    None,
    /// <summary>Runs in session 0 or is part of Windows — ending it can destabilise the system.</summary>
    System,
    /// <summary>Critical process — ending it crashes Windows (or it is FPS.LOL itself).</summary>
    Protected,
}

/// <summary>Decides whether a process may be ended. Errs on the side of refusing.</summary>
public static class ProcessGuard
{
    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Secure System", "Memory Compression", "smss", "csrss", "wininit",
        "winlogon", "services", "lsass", "LsaIso", "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon",
        "explorer", "spoolsv", "MsMpEng", "NisSrv", "MpDefenderCoreService", "SecurityHealthService",
        "SecurityHealthSystray", "smartscreen", "audiodg", "WmiPrvSE", "WUDFHost", "taskhostw",
        "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "TextInputHost",
        "conhost", "dllhost", "LogonUI", "userinit", "vmmem", "vmcompute", "Interrupts",
    };

    public static ProcessProtection Classify(int pid, string name, int sessionId)
    {
        if (pid <= 4 || pid == Environment.ProcessId) return ProcessProtection.Protected;
        if (CriticalNames.Contains(name) || IsCriticalFlagSet(pid)) return ProcessProtection.Protected;
        if (sessionId == 0) return ProcessProtection.System;
        return ProcessProtection.None;
    }

    /// <summary>Reads the kernel "BreakOnTermination" flag, which Windows sets on processes whose termination bugchecks.</summary>
    private static bool IsCriticalFlagSet(int pid)
    {
        try
        {
            using var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle.IsInvalid) return false;
            return NtQueryInformationProcess(handle, ProcessBreakOnTermination, out var critical, sizeof(int), out _) == 0 && critical != 0;
        }
        catch
        {
            return false;
        }
    }

    public static string? GetImagePath(int pid)
    {
        try
        {
            using var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle.IsInvalid) return null;
            var buffer = new char[1024];
            int size = buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Ends a process after re-checking identity and protection. Throws on failure.</summary>
    public static void Kill(int pid, string expectedName)
    {
        using var process = Process.GetProcessById(pid);
        if (!string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The process has already exited (its ID now belongs to a different process).");

        var protection = Classify(pid, process.ProcessName, process.SessionId);
        if (protection != ProcessProtection.None)
            throw new InvalidOperationException($"{process.ProcessName} is a {protection.ToString().ToLowerInvariant()} process and cannot be ended by FPS.LOL.");

        process.Kill();
        if (!process.WaitForExit(5000))
            throw new TimeoutException("The process did not exit within 5 seconds.");
    }
}
