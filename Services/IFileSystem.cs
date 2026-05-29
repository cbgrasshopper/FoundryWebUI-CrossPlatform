namespace FoundryWebUI.Services;

/// <summary>
/// Minimal filesystem abstraction for directory operations, enabling unit testing
/// of code that scans the filesystem without touching real disk.
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);
    string[] GetDirectories(string path);
    void DeleteDirectory(string path, bool recursive);
}

/// <summary>Production implementation that delegates to <see cref="System.IO.Directory"/>.</summary>
public sealed class RealFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
}
