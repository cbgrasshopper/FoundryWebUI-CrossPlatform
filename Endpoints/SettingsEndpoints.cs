using FoundryWebUI.Services;
using FoundryWebUI.Services.Platform;

namespace FoundryWebUI.Endpoints;

public static class SettingsEndpoints
{
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
        FoundryConfigService configService)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return Results.BadRequest(new { error = "Path is required" });
        }

        var result = await configService.SetCacheDirectoryAsync(request.Path.Trim());

        if (!result.Success)
        {
            return result.StatusCode == 400
                ? Results.BadRequest(new { error = result.Error })
                : Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
        }

        return Results.Ok(new { path = result.Path, message = result.Message });
    }
}

public sealed class CacheDirectoryRequest
{
    public string Path { get; set; } = string.Empty;
}
