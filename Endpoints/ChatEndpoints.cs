using System.Text.Json;

using FoundryWebUI.Models;
using FoundryWebUI.Services;

namespace FoundryWebUI.Endpoints;

public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/chat", Chat);
    }

    private static async Task Chat(
        HttpContext context,
        ChatRequest request,
        FoundryLocalService provider,
        ILogger<Program> logger)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in provider.StreamChatAsync(request, context.RequestAborted))
            {
                var json = JsonSerializer.Serialize(chunk, JsonOptions);
                await WriteSSE(context, "message", json);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat error");
            await WriteSSE(context, "message", JsonSerializer.Serialize(new
            {
                content = $"\n\n⚠️ Error: {ex.Message}",
                done = true,
            }));
        }
    }

    private static async Task WriteSSE(HttpContext context, string eventType, string data)
    {
        await context.Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
        await context.Response.Body.FlushAsync();
    }
}
