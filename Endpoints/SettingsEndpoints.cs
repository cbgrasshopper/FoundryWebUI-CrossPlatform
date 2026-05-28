using System.Text.Json;

using FoundryWebUI.Services;
using FoundryWebUI.Services.Platform;

namespace FoundryWebUI.Endpoints;

public static class SettingsEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/settings/cache-directory", GetCacheDirectory);
        app.MapPut("/api/settings/cache-directory", SetCacheDirectory);
        app.MapGet("/api/settings/foundry-info", GetFoundryInfo);
    }

    private static async Task<IResult> GetCacheDirectory(FoundryLocalService provider)
    {
        var path = await provider.GetCacheDirectoryAsync();
        return Results.Ok(new { path = path ?? "", detected = path is not null });
    }

    private static IResult GetFoundryInfo(IConfiguration configuration)
    {
        var resolvedPath = FoundryExecutable.Resolve(configuration);
        var found = System.IO.File.Exists(resolvedPath);
        return Results.Ok(new { executablePath = resolvedPath, found });
    }

    private static async Task<IResult> SetCacheDirectory(
        CacheDirectoryRequest request,
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return Results.BadRequest(new { error = "Path is required" });
        }

        var newPath = request.Path.Trim();
        if (!Path.IsPathFullyQualified(newPath))
        {
            return Results.BadRequest(new
            {
                error =
                    "Path must be an absolute path (for example, /Users/me/FoundryModelCache or D:\\FoundryModelCache).",
            });
        }

        try
        {
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Cannot create directory: {ex.Message}" });
        }

        try
        {
            var configPath = UserPaths.FoundryConfigFile;
            if (!System.IO.File.Exists(configPath))
            {
                return Results.Json(
                    new
                    {
                        error =
                            "Cannot find foundry.config.json. Ensure Foundry Local has been started at least once.",
                    },
                    statusCode: 500);
            }

            logger.LogInformation("Updating Foundry config at: {Path}", configPath);

            var jsonText = await System.IO.File.ReadAllTextAsync(configPath);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var configObj = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonText, JsonOptions)
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

            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(configObj, options);
            await System.IO.File.WriteAllTextAsync(configPath, updatedJson);

            logger.LogInformation("Cache directory changed to {Path} in {Config}", newPath, configPath);
            return Results.Ok(new
            {
                path = newPath,
                message = $"Cache directory updated to {newPath}. Restart the Foundry service for changes to take effect.",
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update foundry.config.json");
            return Results.Json(new { error = $"Failed to update Foundry config: {ex.Message}" }, statusCode: 500);
        }
    }
}

public sealed class CacheDirectoryRequest
{
    public string Path { get; set; } = string.Empty;
}
