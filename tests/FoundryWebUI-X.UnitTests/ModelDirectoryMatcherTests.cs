using FoundryWebUI.Services;

namespace FoundryWebUI.UnitTests;

public class ModelDirectoryMatcherTests
{
    /// <summary>Simple in-memory filesystem for testing directory matching logic.</summary>
    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _dirs;

        public FakeFileSystem(IEnumerable<string> dirs)
        {
            _dirs = new HashSet<string>(dirs, StringComparer.OrdinalIgnoreCase);
        }

        public bool DirectoryExists(string path) => _dirs.Contains(Normalize(path));

        public string[] GetDirectories(string path)
        {
            var norm = Normalize(path);
            var prefix = norm.EndsWith('/') ? norm : norm + '/';
            return _dirs
                .Where(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && !d[prefix.Length..].Contains('/'))
                .ToArray();
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            _dirs.RemoveWhere(d => d.StartsWith(Normalize(path), StringComparison.OrdinalIgnoreCase));
        }

        public void CreateDirectory(string path) => _dirs.Add(Normalize(path));
        public bool FileExists(string path) => false;
        public Task<string> ReadAllTextAsync(string path) => Task.FromResult("");
        public Task WriteAllTextAsync(string path, string contents) => Task.CompletedTask;

        private static string Normalize(string p) => p.Replace('\\', '/').TrimEnd('/');
    }

    [Test]
    public async Task FindModelDir_ExactMatch_ReturnsPath()
    {
        var fs = new FakeFileSystem([
            "/cache",
            "/cache/Microsoft",
            "/cache/Microsoft/phi-3.5-mini-cpu-int4",
        ]);

        var result = ModelDirectoryMatcher.FindModelDir("/cache", "phi-3.5-mini-cpu-int4", fs);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!).Contains("phi-3.5-mini-cpu-int4");
    }

    [Test]
    public async Task FindModelDir_ColonToDash_ReturnsPath()
    {
        var fs = new FakeFileSystem([
            "/cache",
            "/cache/Microsoft",
            "/cache/Microsoft/phi-3.5-mini-cpu-int4",
        ]);

        var result = ModelDirectoryMatcher.FindModelDir("/cache", "phi-3.5-mini:cpu-int4", fs);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!).Contains("phi-3.5-mini-cpu-int4");
    }

    [Test]
    public async Task FindModelDir_PartialFallback_ReturnsPath()
    {
        var fs = new FakeFileSystem([
            "/cache",
            "/cache/Microsoft",
            "/cache/Microsoft/phi-3.5-mini-cpu-int4-v2",
        ]);

        // Model ID with version suffix — the prefix "phi-3.5-mini" should match "phi-3.5-mini-cpu-int4-v2"
        var result = ModelDirectoryMatcher.FindModelDir("/cache", "phi-3.5-mini:cpu-int4", fs);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!).Contains("phi-3.5-mini-cpu-int4-v2");
    }

    [Test]
    public async Task FindModelDir_NoMatch_ReturnsNull()
    {
        var fs = new FakeFileSystem([
            "/cache",
            "/cache/Microsoft",
            "/cache/Microsoft/llama-3-8b",
        ]);

        var result = ModelDirectoryMatcher.FindModelDir("/cache", "phi-3.5-mini:cpu-int4", fs);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FindModelDir_MultiplePublishers_ReturnsFirstMatch()
    {
        var fs = new FakeFileSystem([
            "/cache",
            "/cache/Meta",
            "/cache/Meta/llama-3-8b",
            "/cache/Microsoft",
            "/cache/Microsoft/phi-3.5-mini-cpu-int4",
        ]);

        var result = ModelDirectoryMatcher.FindModelDir("/cache", "phi-3.5-mini-cpu-int4", fs);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!).Contains("Microsoft");
    }

    [Test]
    public async Task FindModelDir_CacheDirMissing_ReturnsNull()
    {
        var fs = new FakeFileSystem([]);

        var result = ModelDirectoryMatcher.FindModelDir("/nonexistent", "phi-3.5-mini", fs);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FindModelDir_NoColonInId_ExactMatchWorks()
    {
        var fs = new FakeFileSystem([
            "/cache",
            "/cache/Microsoft",
            "/cache/Microsoft/phi-3.5-mini",
        ]);

        var result = ModelDirectoryMatcher.FindModelDir("/cache", "phi-3.5-mini", fs);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!).Contains("phi-3.5-mini");
    }
}
