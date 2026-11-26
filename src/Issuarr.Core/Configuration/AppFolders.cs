namespace Issuarr.Core.Configuration;

/// <summary>
/// Where Issuarr keeps its config.xml and database. Like Sonarr/Radarr this
/// is a single "AppData" folder: /config in Docker, ~/.config/Issuarr otherwise.
/// Override with --data=/path or the ISSUARR_DATA environment variable.
/// </summary>
public class AppFolders
{
    public AppFolders(string appDataFolder)
    {
        AppDataFolder = Path.GetFullPath(appDataFolder);
        Directory.CreateDirectory(AppDataFolder);
    }

    public string AppDataFolder { get; }
    public string ConfigFile => Path.Combine(AppDataFolder, "config.xml");
    public string DatabaseFile => Path.Combine(AppDataFolder, "issuarr.db");

    public static AppFolders Resolve(string[] args)
    {
        var fromArgs = args.FirstOrDefault(a => a.StartsWith("--data=", StringComparison.OrdinalIgnoreCase));
        if (fromArgs != null)
        {
            return new AppFolders(fromArgs["--data=".Length..]);
        }

        var fromEnv = Environment.GetEnvironmentVariable("ISSUARR_DATA");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return new AppFolders(fromEnv);
        }

        if (Directory.Exists("/config") && OperatingSystem.IsLinux() && File.Exists("/.dockerenv"))
        {
            return new AppFolders("/config");
        }

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AppFolders(Path.Combine(baseDir, "Issuarr"));
    }
}
