using FoundryWebUI.Services.Platform;

using Microsoft.Extensions.Configuration;

namespace FoundryWebUI.UnitTests.Platform;

public class FoundryExecutableTests
{
    private static IConfiguration Config(IDictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

    [Test]
    public async Task Resolve_PrefersConfigOverrideWhenFileExists()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["Foundry:ExecutablePath"] = tempFile,
            });

            var resolved = FoundryExecutable.Resolve(config);

            await Assert.That(resolved).IsEqualTo(tempFile);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Resolve_IgnoresConfigOverrideWhenFileMissing()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N"));
        var config = Config(new Dictionary<string, string?>
        {
            ["Foundry:ExecutablePath"] = bogus,
        });

        var resolved = FoundryExecutable.Resolve(config);

        await Assert.That(resolved).IsNotEqualTo(bogus);
    }

    [Test]
    public async Task Resolve_FallsBackToPlatformExeName()
    {
        var resolved = FoundryExecutable.Resolve(Config());
        var expectedName = OperatingSystem.IsWindows() ? "foundry.exe" : "foundry";

        // Result should either be the bare name (fallback) or a path ending in that name.
        await Assert.That(resolved.EndsWith(expectedName, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Resolve_FindsBinaryOnPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fwx-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exeName = OperatingSystem.IsWindows() ? "foundry.exe" : "foundry";
        var candidate = Path.Combine(dir, exeName);
        File.WriteAllText(candidate, "");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + (originalPath ?? ""));
            var resolved = FoundryExecutable.Resolve(Config());

            await Assert.That(resolved).IsEqualTo(candidate);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Test]
    public async Task TryFind_ReturnsTrueWhenConfiguredPathExists()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var config = Config(new Dictionary<string, string?>
            {
                ["Foundry:ExecutablePath"] = tempFile,
            });

            var found = FoundryExecutable.TryFind(config, out var path);

            await Assert.That(found).IsTrue();
            await Assert.That(path).IsEqualTo(tempFile);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
