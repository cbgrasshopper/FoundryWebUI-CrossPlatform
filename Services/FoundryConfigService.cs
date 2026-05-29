using System.Text.Json;

using FoundryWebUI.Services.Platform;

namespace FoundryWebUI.Services;

/// <summary>
/// Manages reading and writing the Foundry Local foundry.config.json file.
/// Extracted from SettingsEndpoints to allow unit testing of config mutation logic.
/// </summary>
public sealed class FoundryConfigService(IFileSystem fileSystem, ILogger<FoundryConfigService> logger)
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public sealed record SetCacheResult(bool Success, string? Path = null, string? Message = null, string? Error = null, int StatusCode = 200);

    public async Task<SetCacheResult> SetCacheDirectoryAsync(string newPath)
    {
        if (!System.IO.Path.IsPathFullyQualified(newPath))
        {
            return new SetCacheResult(false, Error: "Path must be an absolute path (for example, /Users/me/FoundryModelCache or D:\\FoundryModelCache).", StatusCode: 400);
        }

        try
        {
            if (!fileSystem.DirectoryExists(newPath))
            {
                fileSystem.CreateDirectory(newPath);
            }
        }
        catch (Exception ex)
        {
            return new SetCacheResult(false, Error: $"Cannot create directory: {ex.Message}", StatusCode: 400);
        }

        var configPath = UserPaths.FoundryConfigFile;
        if (!fileSystem.FileExists(configPath))
        {
            return new SetCacheResult(false, Error: "Cannot find foundry.config.json. Ensure Foundry Local has been started at least once.", StatusCode: 500);
        }

        try
        {
            logger.LogInformation("Updating Foundry config at: {Path}", configPath);

            var jsonText = await fileSystem.ReadAllTextAsync(configPath);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var configObj = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonText, CamelCaseOptions)
                            ?? [];

            var serviceSettings = new Dictionary<string, object>();
            if (root.TryGetProperty("serviceSettings", out var ss))
            {
                foreach (var prop in ss.EnumerateObject())
                {
                    serviceSettings[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString()!,
                        JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetRawText(),
                    };
                }
            }
            serviceSettings["cacheDirectoryPath"] = newPath;
            configObj["serviceSettings"] = serviceSettings;

            var updatedJson = JsonSerializer.Serialize(configObj, WriteOptions);
            await fileSystem.WriteAllTextAsync(configPath, updatedJson);

            logger.LogInformation("Cache directory changed to {Path} in {Config}", newPath, configPath);
            return new SetCacheResult(true, Path: newPath, Message: $"Cache directory updated to {newPath}. Restart the Foundry service for changes to take effect.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update foundry.config.json");
            return new SetCacheResult(false, Error: $"Failed to update Foundry config: {ex.Message}", StatusCode: 500);
        }
    }
}
