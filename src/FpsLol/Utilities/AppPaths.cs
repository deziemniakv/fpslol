namespace FpsLol.Utilities;

/// <summary>Well-known storage locations. Everything FPS.LOL writes lives under %LOCALAPPDATA%\FPS.LOL.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FPS.LOL");

    public static string Logs => Ensure(Path.Combine(Root, "logs"));
    public static string Backups => Ensure(Path.Combine(Root, "backups"));
    public static string SettingsFile => Path.Combine(Ensure(Root), "settings.json");
    public static string HistoryFile => Path.Combine(Ensure(Root), "history.json");
    public static string ProfilesFile => Path.Combine(Ensure(Root), "profiles.json");
    public static string PowerFile => Path.Combine(Ensure(Root), "power.json");

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
