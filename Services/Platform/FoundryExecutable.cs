namespace FoundryWebUI.Services.Platform;

/// <summary>
/// Cross-platform resolution of the Foundry Local CLI binary path.
/// Resolution order:
///   1. <c>Foundry:ExecutablePath</c> in configuration (explicit override)
///   2. <c>PATH</c> search
///   3. Platform-conventional install locations
///   4. Bare binary name (let the OS resolve via PATH at exec time)
/// </summary>
public static class FoundryExecutable
{
    public static string Resolve(IConfiguration configuration)
    {
        // 1. Configuration override.
        var configured = configuration["Foundry:ExecutablePath"];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var exeName = OperatingSystem.IsWindows() ? "foundry.exe" : "foundry";

        // 2. PATH search.
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
            }
        }

        // 3. Platform conventional install locations.
        foreach (var candidate in PlatformCandidates(exeName))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 4. Fallback — let the OS resolve at exec time.
        return exeName;
    }

    public static bool TryFind(IConfiguration configuration, out string path)
    {
        path = Resolve(configuration);
        return File.Exists(path);
    }

    private static IEnumerable<string> PlatformCandidates(string exeName)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
        {
            yield return "/usr/local/bin/foundry";
            yield return "/opt/homebrew/bin/foundry";
            yield return Path.Combine(home, ".foundry", "bin", "foundry");
            yield return "/Applications/Foundry Local.app/Contents/MacOS/foundry";
        }
        else if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            yield return Path.Combine(programFiles, "FoundryLocal", exeName);
            yield return Path.Combine(localAppData, "Programs", "FoundryLocal", exeName);

            // WinGet MSIX install — search known WindowsApps subdirectories as a last resort.
            string? winApps = null;
            try
            {
                winApps = Path.Combine(programFiles, "WindowsApps");
                if (Directory.Exists(winApps))
                {
                    foreach (var dir in Directory.GetDirectories(winApps, "Microsoft.FoundryLocal_*")
                                                  .OrderByDescending(d => d))
                    {
                        var exe = Path.Combine(dir, exeName);
                        if (File.Exists(exe))
                        {
                            yield return exe;
                        }
                    }
                }
            }
            finally
            {
                // WindowsApps may be access-restricted; failure here is silently swallowed
                // by virtue of the try block never having anything thrown out of it.
                _ = winApps;
            }
        }
    }
}
