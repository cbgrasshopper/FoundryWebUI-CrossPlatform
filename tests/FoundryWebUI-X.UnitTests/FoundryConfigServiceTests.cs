using FoundryWebUI.Services;

using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryWebUI.UnitTests;

public class FoundryConfigServiceTests
{
    /// <summary>Returns an absolute path suitable for the current OS.</summary>
    private static string TestAbsolutePath(string unixStylePath)
    {
        var relative = unixStylePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(relative);
    }

    /// <summary>All-zero address that passes validation.</summary>
    private static readonly string ModelsDir = TestAbsolutePath("/opt/models");
    private static readonly string NewCacheDir = TestAbsolutePath("/new/cache/path");

    /// <summary>In-memory filesystem for testing config operations.</summary>
    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

        public void SeedFile(string path, string content) => _files[path] = content;
        public void SeedDirectory(string path) => _dirs.Add(path);

        public bool DirectoryExists(string path) => _dirs.Contains(path);
        public string[] GetDirectories(string path) => [];
        public void DeleteDirectory(string path, bool recursive) => _dirs.Remove(path);
        public void CreateDirectory(string path) => _dirs.Add(path);
        public bool FileExists(string path) => _files.ContainsKey(path);
        public Task<string> ReadAllTextAsync(string path) =>
            _files.TryGetValue(path, out var content) ? Task.FromResult(content) : throw new FileNotFoundException(path);
        public Task WriteAllTextAsync(string path, string contents) { _files[path] = contents; return Task.CompletedTask; }

        public string? GetWrittenContent(string path) => _files.GetValueOrDefault(path);
    }

    private static FoundryConfigService CreateService(FakeFileSystem fs) =>
        new(fs, NullLogger<FoundryConfigService>.Instance);

    [Test]
    public async Task SetCacheDirectory_RelativePath_ReturnsBadRequest()
    {
        var fs = new FakeFileSystem();
        var svc = CreateService(fs);

        var result = await svc.SetCacheDirectoryAsync("relative/path");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.StatusCode).IsEqualTo(400);
        await Assert.That(result.Error).Contains("absolute path");
    }

    [Test]
    public async Task SetCacheDirectory_ConfigFileNotFound_Returns500()
    {
        var fs = new FakeFileSystem();
        // Ensure the target directory "exists" so we pass that check
        fs.SeedDirectory(ModelsDir);
        var svc = CreateService(fs);

        var result = await svc.SetCacheDirectoryAsync(ModelsDir);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.StatusCode).IsEqualTo(500);
        await Assert.That(result.Error).Contains("foundry.config.json");
    }

    [Test]
    public async Task SetCacheDirectory_ValidPath_UpdatesConfig()
    {
        var fs = new FakeFileSystem();
        fs.SeedDirectory(ModelsDir);

        var configPath = Services.Platform.UserPaths.FoundryConfigFile;
        var existingConfig = """{"serviceSettings":{"someOther":"value"}}""";
        fs.SeedFile(configPath, existingConfig);

        var svc = CreateService(fs);

        var result = await svc.SetCacheDirectoryAsync(ModelsDir);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Path).IsEqualTo(ModelsDir);
        await Assert.That(result.Message).Contains("Restart the Foundry service");

        // Verify the config was written with the new path
        var written = fs.GetWrittenContent(configPath);
        await Assert.That(written).IsNotNull();
        var doc = System.Text.Json.JsonDocument.Parse(written!);
        var cacheDir = doc.RootElement
            .GetProperty("serviceSettings")
            .GetProperty("cacheDirectoryPath")
            .GetString();
        await Assert.That(cacheDir).IsEqualTo(ModelsDir);
    }

    [Test]
    public async Task SetCacheDirectory_CreatesDirectoryIfNotExists()
    {
        var fs = new FakeFileSystem();
        // Don't pre-seed the directory — it should be created

        var configPath = Services.Platform.UserPaths.FoundryConfigFile;
        fs.SeedFile(configPath, """{"serviceSettings":{}}""");

        var svc = CreateService(fs);

        var result = await svc.SetCacheDirectoryAsync(NewCacheDir);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(fs.DirectoryExists(NewCacheDir)).IsTrue();
    }
}
