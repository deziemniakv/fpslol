using FpsLol.SystemIntegration;

namespace FpsLol.Storage;

public sealed record CleanupCategory(
    string Id,
    string Name,
    string Description,
    bool RequiresAdmin,
    bool SelectedByDefault,
    TimeSpan MinimumAge,
    string? Warning,
    Func<IEnumerable<string>> Directories,
    Func<IEnumerable<string>>? SingleFiles = null);

public sealed record CleanupScan(string CategoryId, long Bytes, int Files, bool AccessDenied);

public sealed record CleanupRun(string CategoryId, long BytesFreed, int FilesDeleted, int FilesSkipped);

/// <summary>
/// Removes only regenerable data from a fixed set of well-known cache/temp folders.
/// Never follows junctions/symlinks, never deletes the folders themselves, and never touches user documents.
/// </summary>
public static class CleanupEngine
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static IReadOnlyList<CleanupCategory> Categories { get; } =
    [
        new("user-temp", "User temporary files",
            "Files in your %TEMP% folder older than 24 hours. Files still in use are skipped automatically.",
            RequiresAdmin: false, SelectedByDefault: true, TimeSpan.FromHours(24), null,
            () => [Path.GetTempPath()]),

        new("windows-temp", "Windows temporary files",
            "Files in C:\\Windows\\Temp older than 24 hours, left behind by installers and system components.",
            RequiresAdmin: true, SelectedByDefault: true, TimeSpan.FromHours(24), null,
            () => [Path.Combine(Windows, "Temp")]),

        new("error-reports", "Windows error reports",
            "Queued and archived Windows Error Reporting data for your account.",
            RequiresAdmin: false, SelectedByDefault: true, TimeSpan.Zero, null,
            () =>
            [
                Path.Combine(Local, @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(Local, @"Microsoft\Windows\WER\ReportQueue"),
            ]),

        new("directx-shader-cache", "DirectX shader cache",
            "Compiled shaders stored by Windows. Useful after a GPU driver update if games stutter or crash on load.",
            RequiresAdmin: false, SelectedByDefault: false, TimeSpan.Zero,
            "Games recompile shaders on next launch, which can cause temporary stutter or longer loading.",
            () => [Path.Combine(Local, "D3DSCache")]),

        new("gpu-vendor-shader-cache", "GPU driver shader cache",
            "NVIDIA, AMD and Intel driver shader caches (DirectX, Vulkan and OpenGL).",
            RequiresAdmin: false, SelectedByDefault: false, TimeSpan.Zero,
            "Games recompile shaders on next launch, which can cause temporary stutter or longer loading.",
            () =>
            [
                Path.Combine(Local, @"NVIDIA\DXCache"),
                Path.Combine(Local, @"NVIDIA\GLCache"),
                Path.Combine(Local, @"NVIDIA Corporation\NV_Cache"),
                Path.Combine(Local, @"AMD\DxCache"),
                Path.Combine(Local, @"AMD\DxcCache"),
                Path.Combine(Local, @"AMD\VkCache"),
                Path.Combine(Local, @"AMD\GLCache"),
                Path.Combine(Local, @"Intel\ShaderCache"),
            ]),

        new("user-crash-dumps", "Application crash dumps",
            "Memory dumps written when applications crash. Only needed if you are sending crash reports to a developer.",
            RequiresAdmin: false, SelectedByDefault: false, TimeSpan.Zero, null,
            () => [Path.Combine(Local, "CrashDumps")]),

        new("system-crash-dumps", "System crash dumps",
            "Minidumps and MEMORY.DMP written after blue screens. Keep them if you are troubleshooting system crashes.",
            RequiresAdmin: true, SelectedByDefault: false, TimeSpan.Zero,
            "Removes information needed to diagnose past blue screens.",
            () =>
            [
                Path.Combine(Windows, "Minidump"),
                Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"),
            ],
            () => [Path.Combine(Windows, "MEMORY.DMP")]),
    ];

    public static CleanupCategory? Find(string id) => Categories.FirstOrDefault(c => c.Id == id);

    public static CleanupScan Scan(CleanupCategory category, CancellationToken ct = default)
    {
        long bytes = 0;
        int files = 0;
        bool denied = false;
        var cutoff = DateTime.UtcNow - category.MinimumAge;

        foreach (var file in EnumerateCandidates(category, ct, ref denied))
        {
            if (file.LastWriteTimeUtc > cutoff) continue;
            bytes += file.Length;
            files++;
        }
        return new CleanupScan(category.Id, bytes, files, denied);
    }

    public static CleanupRun Clean(CleanupCategory category, CancellationToken ct = default)
    {
        if (category.RequiresAdmin && !Elevation.IsElevated)
            throw new UnauthorizedAccessException("Administrator privileges are required for this category.");

        long freed = 0;
        int deleted = 0, skipped = 0;
        bool denied = false;
        var cutoff = DateTime.UtcNow - category.MinimumAge;

        foreach (var file in EnumerateCandidates(category, ct, ref denied).ToList())
        {
            if (file.LastWriteTimeUtc > cutoff) continue;
            try
            {
                var length = file.Length;
                if ((file.Attributes & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                    file.Attributes = FileAttributes.Normal;
                file.Delete();
                freed += length;
                deleted++;
            }
            catch
            {
                skipped++; // in use or protected — leave it alone
            }
        }

        foreach (var root in ExistingRoots(category)) RemoveEmptySubdirectories(new DirectoryInfo(root), isRoot: true);
        return new CleanupRun(category.Id, freed, deleted, skipped);
    }

    private static IEnumerable<string> ExistingRoots(CleanupCategory category) =>
        category.Directories().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<FileInfo> EnumerateCandidates(CleanupCategory category, CancellationToken ct, ref bool denied)
    {
        var result = new List<FileInfo>();
        foreach (var root in ExistingRoots(category))
        {
            var dir = new DirectoryInfo(root);
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) continue; // never follow a redirected root
            Walk(dir, result, ct, ref denied);
        }

        if (category.SingleFiles is not null)
        {
            foreach (var path in category.SingleFiles())
            {
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists && (fi.Attributes & FileAttributes.ReparsePoint) == 0) result.Add(fi);
                }
                catch
                {
                    denied = true;
                }
            }
        }
        return result;
    }

    private static void Walk(DirectoryInfo dir, List<FileInfo> sink, CancellationToken ct, ref bool denied)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(dir);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            try
            {
                foreach (var entry in current.EnumerateFileSystemInfos())
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue; // junctions / symlinks
                    if (entry is DirectoryInfo sub) stack.Push(sub);
                    else if (entry is FileInfo fi) sink.Add(fi);
                }
            }
            catch (UnauthorizedAccessException)
            {
                denied = true;
            }
            catch (IOException)
            {
                // Folder vanished or is locked — skip.
            }
        }
    }

    private static bool RemoveEmptySubdirectories(DirectoryInfo dir, bool isRoot)
    {
        bool empty = true;
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo sub && (sub.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    if (!RemoveEmptySubdirectories(sub, isRoot: false)) empty = false;
                }
                else
                {
                    empty = false;
                }
            }
            if (empty && !isRoot)
            {
                dir.Delete(recursive: false);
                return true;
            }
        }
        catch
        {
            return false;
        }
        return empty && !isRoot;
    }
}
