namespace FoundryWebUI.Services.Platform;

/// <summary>
/// Cross-platform location of user-scoped data for FoundryWebUI-X.
///
/// macOS:   ~/Library/Application Support/FoundryWebUI-X/
/// Windows: %LOCALAPPDATA%\FoundryWebUI-X\
/// </summary>
public static class UserPaths
{
    public const string AppFolderName = "FoundryWebUI-X";

    public static string ConfigDir
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, AppFolderName);
            }

            // macOS (and any other Unix fallback).
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }
    }

    public static string LogsDir => Path.Combine(ConfigDir, "logs");

    public static string SystemPromptsFile => Path.Combine(ConfigDir, "system-prompts.json");

    /// <summary>The user's `.foundry` directory — same convention on macOS and Windows.</summary>
    public static string FoundryHome => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".foundry");

    public static string FoundryLogsDir => Path.Combine(FoundryHome, "logs");

    public static string FoundryConfigFile => Path.Combine(FoundryHome, "foundry.config.json");

    /// <summary>File that caches the last-discovered Foundry Local endpoint URL.</summary>
    public static string EndpointCacheFile => Path.Combine(ConfigDir, "last-endpoint.json");

    public static void EnsureConfigDirExists()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogsDir);
    }
}
