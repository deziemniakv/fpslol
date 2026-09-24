using System.Diagnostics;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.Optimizations;
using FpsLol.SystemIntegration;

namespace FpsLol.Services;

public sealed record StartupEntry(
    string Name,
    string Command,
    string? ExecutablePath,
    string Publisher,
    string Location,
    bool Enabled,
    RegRoot ApprovedRoot,
    string ApprovedPath,
    string ApprovedName)
{
    public bool RequiresAdmin => ApprovedRoot != RegRoot.CurrentUser;
    public string Id => $"{ApprovedRoot}|{ApprovedPath}|{ApprovedName}";
}

public interface IStartupService
{
    IReadOnlyList<StartupEntry> GetEntries();
    int CountEnabled();
    ChangeRequest BuildToggle(StartupEntry entry, bool enable);
}

/// <summary>
/// Reads startup programs from the Run keys and Startup folders and toggles them exactly like
/// Task Manager does — through the StartupApproved flags. Entries are never deleted.
/// </summary>
public sealed class StartupService(ILogService log) : IStartupService
{
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const string ApprovedBaseLm = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public IReadOnlyList<StartupEntry> GetEntries()
    {
        var list = new List<StartupEntry>();
        try
        {
            ReadRunKey(list, RegRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Registry (current user)", RegRoot.CurrentUser, ApprovedBase + "Run");
            ReadRunKey(list, RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Registry (all users)", RegRoot.LocalMachine, ApprovedBaseLm + "Run");
            ReadRunKey(list, RegRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Registry (all users, 32-bit)", RegRoot.LocalMachine, ApprovedBaseLm + "Run32");
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup folder (current user)", RegRoot.CurrentUser, ApprovedBase + "StartupFolder");
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Startup folder (all users)", RegRoot.LocalMachine, ApprovedBaseLm + "StartupFolder");
        }
        catch (Exception ex)
        {
            log.Error("Could not read startup entries.", ex);
        }
        return list.OrderByDescending(e => e.Enabled).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public int CountEnabled() => GetEntries().Count(e => e.Enabled);

    public ChangeRequest BuildToggle(StartupEntry entry, bool enable)
    {
        var data = new byte[12];
        data[0] = enable ? (byte)0x02 : (byte)0x03;
        if (!enable) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);

        return new ChangeRequest
        {
            SourceId = "startup:" + entry.Id,
            Title = $"Startup: {entry.Name}",
            Category = "Startup",
            Before = entry.Enabled ? "Enabled" : "Disabled",
            After = enable ? "Enabled" : "Disabled",
            Actions = [new RegistryValueAction(entry.ApprovedRoot, entry.ApprovedPath, entry.ApprovedName, RegValue.Binary(data))],
        };
    }

    private static void ReadRunKey(List<StartupEntry> list, RegRoot root, string path, string location, RegRoot approvedRoot, string approvedPath)
    {
        using var baseKey = RegistryUtil.OpenBase(root);
        using var key = baseKey.OpenSubKey(path);
        if (key is null) return;
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            var command = key.GetValue(name)?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(command)) continue;
            var exe = ResolveExecutable(command);
            list.Add(new StartupEntry(DisplayName(name, exe), command, exe, Publisher(exe), location,
                IsEnabled(approvedRoot, approvedPath, name), approvedRoot, approvedPath, name));
        }
    }

    private static void ReadFolder(List<StartupEntry> list, string folder, string location, RegRoot approvedRoot, string approvedPath)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) : file;
            list.Add(new StartupEntry(DisplayName(Path.GetFileNameWithoutExtension(file), target), target ?? file, target,
                Publisher(target), location, IsEnabled(approvedRoot, approvedPath, fileName), approvedRoot, approvedPath, fileName));
        }
    }

    private static bool IsEnabled(RegRoot root, string path, string name)
    {
        var value = RegistryUtil.Read(root, path, name);
        if (value is null) return true; // no StartupApproved entry = enabled
        var bytes = value.Bytes;
        return bytes.Length == 0 || (bytes[0] & 0x1) == 0;
    }

    private static string DisplayName(string fallback, string? exe)
    {
        if (exe is not null && File.Exists(exe))
        {
            try
            {
                var desc = FileVersionInfo.GetVersionInfo(exe).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc) && desc.Length <= 60) return desc.Trim();
            }
            catch
            {
                // ignored
            }
        }
        return fallback;
    }

    private static string Publisher(string? exe)
    {
        if (exe is null || !File.Exists(exe)) return "N/A";
        try
        {
            var company = FileVersionInfo.GetVersionInfo(exe).CompanyName;
            return string.IsNullOrWhiteSpace(company) ? "N/A" : company.Trim();
        }
        catch
        {
            return "N/A";
        }
    }

    public static string? ResolveExecutable(string command)
    {
        var cmd = Environment.ExpandEnvironmentVariables(command.Trim());
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            return end > 1 ? cmd[1..end] : null;
        }
        var idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) return cmd[..(idx + 4)];
        var space = cmd.IndexOf(' ');
        return space > 0 ? cmd[..space] : cmd;
    }

    private static string? ResolveShortcut(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnk);
                string target = shortcut.TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            return null;
        }
    }
}
