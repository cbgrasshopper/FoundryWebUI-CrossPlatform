namespace FoundryWebUI.Services;

/// <summary>
/// Minimal filesystem abstraction for directory and file operations, enabling unit testing
/// of code that interacts with disk without touching real filesystem.
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);
    string[] GetDirectories(string path);
    void DeleteDirectory(string path, bool recursive);
    void CreateDirectory(string path);
    bool FileExists(string path);
    Task<string> ReadAllTextAsync(string path);
    Task WriteAllTextAsync(string path, string contents);
}

/// <summary>Production implementation that delegates to <see cref="System.IO.Directory"/> and <see cref="System.IO.File"/>.</summary>
public sealed class RealFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool FileExists(string path) => File.Exists(path);
    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);
    public Task WriteAllTextAsync(string path, string contents) => File.WriteAllTextAsync(path, contents);
}
