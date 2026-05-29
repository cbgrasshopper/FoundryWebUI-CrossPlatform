namespace FoundryWebUI.Services;

/// <summary>
/// Locates the on-disk directory for a Foundry model within the cache directory.
/// The cache layout is: {cacheDir}/{publisher}/{modelDirName}/
/// </summary>
public static class ModelDirectoryMatcher
{
    /// <summary>
    /// Find the model directory within <paramref name="cacheDir"/> that matches <paramref name="modelId"/>.
    /// Returns the full path of the matching directory, or null if no match is found.
    /// </summary>
    /// <remarks>
    /// Matching strategy (in order):
    /// 1. Exact: replace ':' with '-' in modelId, look for that folder name under each publisher dir.
    /// 2. Partial: strip the version suffix (everything after last ':') and look for directories
    ///    whose name starts with the version-stripped prefix (also with ':' → '-').
    /// </remarks>
    public static string? FindModelDir(string cacheDir, string modelId, IFileSystem fs)
    {
        if (!fs.DirectoryExists(cacheDir))
            return null;

        var dirName = modelId.Replace(':', '-');

        // Pass 1: Exact match
        foreach (var publisherDir in fs.GetDirectories(cacheDir))
        {
            var candidate = Path.Combine(publisherDir, dirName);
            if (fs.DirectoryExists(candidate))
                return candidate;
        }

        // Pass 2: Partial (version-stripped prefix) match
        var nameWithoutVersion = modelId.Contains(':')
            ? modelId[..modelId.LastIndexOf(':')]
            : modelId;
        var prefix = nameWithoutVersion.Replace(':', '-');

        foreach (var publisherDir in fs.GetDirectories(cacheDir))
        {
            foreach (var dir in fs.GetDirectories(publisherDir))
            {
                var folderName = Path.GetFileName(dir);
                if (folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return dir;
            }
        }

        return null;
    }
}
