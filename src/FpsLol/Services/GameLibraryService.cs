using System.Text.Json;
using System.Text.RegularExpressions;
using FpsLol.Logging;
using FpsLol.Models;
using FpsLol.SystemIntegration;

namespace FpsLol.Services;

public interface IGameLibraryService
{
    Task<IReadOnlyList<DetectedGame>> DetectAsync();
    string? GuessExecutable(string installDir);
}

/// <summary>
/// Finds installed games by reading launcher metadata (Steam library manifests, Epic manifests, Riot product settings).
/// Paths are always discovered, never assumed.
/// </summary>
public sealed partial class GameLibraryService(ILogService log) : IGameLibraryService
{
    private static readonly Dictionary<string, string> KnownSteamExecutables = new()
    {
        ["730"] = @"game\bin\win64\cs2.exe",
        ["570"] = @"game\bin\win64\dota2.exe",
        ["578080"] = @"TslGame\Binaries\Win64\TslGame.exe",
        ["1172470"] = "r5apex.exe",
        ["271590"] = "GTA5.exe",
        ["252490"] = "RustClient.exe",
        ["440"] = "tf_win64.exe",
        ["359550"] = "RainbowSix.exe",
        ["1085660"] = "destiny2.exe",
        ["2357570"] = @"Overwatch.exe",
    };

    private static readonly HashSet<string> IgnoredSteamApps = ["228980", "1070560", "1391110", "1628350", "250820"]; // redistributables, Steam Linux runtimes, SteamVR

    private static readonly string[] ExcludedExeFragments =
    [
        "unins", "crash", "redist", "setup", "install", "update", "helper", "report", "vc_", "dxsetup", "directx",
        "easyanticheat", "beservice", "battleye", "launcherpatcher", "prereq", "dotnet", "vcredist", "ue4prereq",
        "cefprocess", "webhelper", "overlay", "benchmark", "config", "editor", "server", "handler",
    ];

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPath();

    [GeneratedRegex("\"(appid|name|installdir)\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AcfField();

    [GeneratedRegex("product_install_full_path:\\s*\"?([^\"\\r\\n]+)\"?", RegexOptions.IgnoreCase)]
    private static partial Regex RiotInstallPath();

    public Task<IReadOnlyList<DetectedGame>> DetectAsync() => Task.Run<IReadOnlyList<DetectedGame>>(() =>
    {
        var games = new List<DetectedGame>();
        Safe("Steam", () => games.AddRange(DetectSteam()));
        Safe("Epic Games", () => games.AddRange(DetectEpic()));
        Safe("Riot Games", () => games.AddRange(DetectRiot()));
        log.Info($"Game detection finished: {games.Count} game(s) found");
        return games.GroupBy(g => g.InstallDir, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    });

    private void Safe(string source, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            log.Warn($"{source} library could not be read.", ex);
        }
    }

    private IEnumerable<DetectedGame> DetectSteam()
    {
        var steamPath = RegistryUtil.ReadString(RegRoot.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                        ?? RegistryUtil.ReadString(RegRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (string.IsNullOrWhiteSpace(steamPath)) yield break;
        steamPath = steamPath.Replace('/', '\\');

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string? appId = null, name = null, installDir = null;
                foreach (Match m in AcfField().Matches(File.ReadAllText(manifest)))
                {
                    switch (m.Groups[1].Value.ToLowerInvariant())
                    {
                        case "appid": appId ??= m.Groups[2].Value; break;
                        case "name": name ??= m.Groups[2].Value; break;
                        case "installdir": installDir ??= m.Groups[2].Value; break;
                    }
                }
                if (appId is null || name is null || installDir is null || IgnoredSteamApps.Contains(appId)) continue;
                if (name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)) continue;

                var dir = Path.Combine(apps, "common", installDir);
                if (!Directory.Exists(dir)) continue;
                string? exe = null;
                if (KnownSteamExecutables.TryGetValue(appId, out var rel) && File.Exists(Path.Combine(dir, rel))) exe = Path.Combine(dir, rel);
                exe ??= GuessExecutable(dir);
                yield return new DetectedGame(name, "Steam", dir, exe);
            }
        }
    }

    private IEnumerable<DetectedGame> DetectEpic()
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
        if (!Directory.Exists(manifests)) yield break;
        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            DetectedGame? game = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                var dir = root.TryGetProperty("InstallLocation", out var d) ? d.GetString() : null;
                var launch = root.TryGetProperty("LaunchExecutable", out var l) ? l.GetString() : null;
                if (name is null || dir is null || !Directory.Exists(dir)) continue;

                string? exe = null;
                if (name.Contains("Fortnite", StringComparison.OrdinalIgnoreCase))
                {
                    var fn = Path.Combine(dir, @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe");
                    if (File.Exists(fn)) exe = fn;
                }
                if (exe is null && launch is not null && File.Exists(Path.Combine(dir, launch))) exe = Path.Combine(dir, launch);
                game = new DetectedGame(name, "Epic Games", dir, exe ?? GuessExecutable(dir));
            }
            catch (JsonException)
            {
            }
            if (game is not null) yield return game;
        }
    }

    private IEnumerable<DetectedGame> DetectRiot()
    {
        var metadata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\Metadata");
        if (!Directory.Exists(metadata)) yield break;
        foreach (var file in Directory.EnumerateFiles(metadata, "*.product_settings.yaml", SearchOption.AllDirectories))
        {
            var match = RiotInstallPath().Match(File.ReadAllText(file));
            if (!match.Success) continue;
            var dir = match.Groups[1].Value.Trim().Replace('/', '\\');
            if (!Directory.Exists(dir)) continue;

            var product = Path.GetFileName(Path.GetDirectoryName(file)) ?? string.Empty;
            string name;
            string? exe;
            if (product.StartsWith("valorant", StringComparison.OrdinalIgnoreCase))
            {
                name = "VALORANT";
                exe = Path.Combine(dir, @"ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe");
            }
            else if (product.StartsWith("league_of_legends", StringComparison.OrdinalIgnoreCase))
            {
                name = "League of Legends";
                exe = Path.Combine(dir, @"Game\League of Legends.exe");
            }
            else
            {
                continue;
            }
            yield return new DetectedGame(name, "Riot Games", dir, File.Exists(exe) ? exe : GuessExecutable(dir));
        }
    }

    /// <summary>Best-effort: the most likely game executable inside an install folder (user can change it).</summary>
    public string? GuessExecutable(string installDir)
    {
        try
        {
            var folderKey = Normalize(Path.GetFileName(installDir.TrimEnd('\\')));
            var candidates = new List<(string Path, double Score)>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var exe in Directory.EnumerateFiles(installDir, "*.exe", options).Take(3000))
            {
                var file = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                if (ExcludedExeFragments.Any(file.Contains)) continue;
                var key = Normalize(file);
                double score = 0;
                if (key.Length > 0 && folderKey.Length > 0 && (folderKey.Contains(key) || key.Contains(folderKey))) score += 50;
                if (exe.Contains("win64", StringComparison.OrdinalIgnoreCase) || exe.Contains("x64", StringComparison.OrdinalIgnoreCase)) score += 15;
                if (file.Contains("shipping")) score += 25;
                if (file.Contains("launcher")) score -= 20;
                try { score += Math.Min(new FileInfo(exe).Length / (1024.0 * 1024.0) / 5, 40); } catch { }
                candidates.Add((exe, score));
            }
            return candidates.OrderByDescending(c => c.Score).Select(c => c.Path).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
