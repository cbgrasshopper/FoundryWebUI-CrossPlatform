using FoundryWebUI.Services.Platform;

namespace FoundryWebUI.UnitTests.Platform;

public class UserPathsTests
{
    [Test]
    public async Task ConfigDir_IsPlatformConventional()
    {
        var configDir = UserPaths.ConfigDir;

        await Assert.That(configDir).IsNotNull();
        await Assert.That(configDir).EndsWith("FoundryWebUI-X");

        if (OperatingSystem.IsMacOS())
        {
            await Assert.That(configDir).Contains("Library/Application Support");
        }
        else if (OperatingSystem.IsWindows())
        {
            // %LOCALAPPDATA% typically contains "AppData\\Local"
            await Assert.That(configDir).Contains("AppData");
        }
    }

    [Test]
    public async Task LogsDir_IsBelowConfigDir()
    {
        await Assert.That(UserPaths.LogsDir).StartsWith(UserPaths.ConfigDir);
        await Assert.That(UserPaths.LogsDir).EndsWith("logs");
    }

    [Test]
    public async Task SystemPromptsFile_IsBelowConfigDir()
    {
        await Assert.That(UserPaths.SystemPromptsFile).StartsWith(UserPaths.ConfigDir);
        await Assert.That(UserPaths.SystemPromptsFile).EndsWith("system-prompts.json");
    }

    [Test]
    public async Task FoundryHome_IsDotFoundryUnderUserProfile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await Assert.That(UserPaths.FoundryHome).IsEqualTo(Path.Combine(home, ".foundry"));
    }

    [Test]
    public async Task FoundryLogsDir_IsLogsUnderFoundryHome()
    {
        await Assert.That(UserPaths.FoundryLogsDir).IsEqualTo(Path.Combine(UserPaths.FoundryHome, "logs"));
    }

    [Test]
    public async Task FoundryConfigFile_IsConfigJsonUnderFoundryHome()
    {
        await Assert.That(UserPaths.FoundryConfigFile)
            .IsEqualTo(Path.Combine(UserPaths.FoundryHome, "foundry.config.json"));
    }

    [Test]
    public async Task EnsureConfigDirExists_CreatesBothDirs()
    {
        // Idempotent — calling twice should not throw.
        UserPaths.EnsureConfigDirExists();
        UserPaths.EnsureConfigDirExists();

        await Assert.That(Directory.Exists(UserPaths.ConfigDir)).IsTrue();
        await Assert.That(Directory.Exists(UserPaths.LogsDir)).IsTrue();
    }
}
