using System.Text.Json;

namespace FoundryWebUI.Endpoints;

/// <summary>
/// Shared JSON serializer options and SSE writing utilities for endpoint handlers.
/// </summary>
public static class EndpointHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task WriteSseAsync(HttpContext context, string eventType, string data)
    {
        await context.Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
        await context.Response.Body.FlushAsync();
    }
}
