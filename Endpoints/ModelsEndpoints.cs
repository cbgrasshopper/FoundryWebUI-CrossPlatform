using System.Text.Json;

using FoundryWebUI.Models;
using FoundryWebUI.Services;

namespace FoundryWebUI.Endpoints;

public static class ModelsEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/models", GetModels);
        app.MapGet("/api/models/loaded", GetLoadedModels);
        app.MapPost("/api/models/download", DownloadModel);
        app.MapDelete("/api/models/{*modelId}", DeleteModel);
    }

    private static async Task<IResult> GetModels(
        FoundryLocalService provider,
        ILogger<Program> logger)
    {
        var allModels = new List<ModelInfo>();

        try
        {
            var loaded = await provider.GetLoadedModelsAsync();
            var available = await provider.GetAvailableModelsAsync();

            var catalogById = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in available)
            {
                catalogById.TryAdd(m.Id, m);
            }

            var loadedIds = new HashSet<string>(loaded.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var m in loaded)
            {
                if (!catalogById.TryGetValue(m.Id, out var catModel))
                {
                    var idWithoutVersion = m.Id.Contains(':') ? m.Id[..m.Id.LastIndexOf(':')] : m.Id;
                    catModel = available.FirstOrDefault(a =>
                        string.Equals(a.Name, idWithoutVersion, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.Name, m.Id, StringComparison.OrdinalIgnoreCase));
                }
                if (catModel is not null)
                {
                    m.Size ??= catModel.Size;
                    m.EstimatedRamMb ??= catModel.EstimatedRamMb;
                    m.Description ??= catModel.Description;
                    m.Family ??= catModel.Family;
                    m.ParameterSize ??= catModel.ParameterSize;
                    m.Capabilities ??= catModel.Capabilities;
                    m.ContextWindow ??= catModel.ContextWindow;
                    if (string.IsNullOrEmpty(m.Name) || m.Name == m.Id)
                    {
                        m.Name = catModel.Name;
                    }
                }
            }
            allModels.AddRange(loaded);
            allModels.AddRange(available.Where(m => !loadedIds.Contains(m.Id)));
            logger.LogInformation(
                "Models: {Loaded} loaded/downloaded, {Available} in catalog, {Total} total after merge",
                loaded.Count,
                available.Count,
                allModels.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get models from provider");
        }

        return Results.Ok(allModels);
    }

    private static async Task<IResult> GetLoadedModels(
        FoundryLocalService provider,
        ILogger<Program> logger)
    {
        try
        {
            var models = await provider.GetLoadedModelsAsync();
            return Results.Ok(models);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get loaded models");
            return Results.Ok(new List<ModelInfo>());
        }
    }

    private static async Task DownloadModel(
        HttpContext context,
        DownloadRequest request,
        FoundryLocalService provider,
        ILogger<Program> logger)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var progress in provider.DownloadModelAsync(request.ModelId, context.RequestAborted))
            {
                var json = JsonSerializer.Serialize(progress, JsonOptions);
                await WriteSSE(context, "progress", json);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Download error");
            await WriteSSE(context, "error", JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    private static async Task<IResult> DeleteModel(
        string modelId,
        FoundryLocalService provider,
        ILogger<Program> logger)
    {
        modelId = Uri.UnescapeDataString(modelId);
        logger.LogInformation("Delete request for model: {ModelId}", modelId);

        try
        {
            var success = await provider.DeleteModelAsync(modelId, default);
            return success
                ? Results.Ok(new { message = $"Model '{modelId}' removed successfully" })
                : Results.Json(
                    new
                    {
                        error =
                            $"Failed to remove model '{modelId}'. The FoundryWebUI-X process may lack write access to the Foundry Local cache directory. Check server logs for details.",
                    },
                    statusCode: 500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delete failed for {ModelId}", modelId);
            return Results.Json(new { error = $"Failed to remove model '{modelId}': {ex.Message}" }, statusCode: 500);
        }
    }

    private static async Task WriteSSE(HttpContext context, string eventType, string data)
    {
        await context.Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
        await context.Response.Body.FlushAsync();
    }
}
