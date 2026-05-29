using System.Text.Json;

namespace FoundryWebUI.Services.Sse;

/// <summary>
/// Maps error JSON payloads from the chat completions SSE stream into user-facing messages.
/// </summary>
public static class ChatErrorMapper
{
    /// <summary>
    /// Extracts an error code and user-facing message from the "error" property of a chat response.
    /// </summary>
    public static (string? Code, string Message) Map(JsonElement errProp)
    {
        if (errProp.ValueKind == JsonValueKind.String)
        {
            return (null, errProp.GetString() ?? "Unknown error");
        }

        if (errProp.ValueKind == JsonValueKind.Object)
        {
            var code = errProp.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
            var errType = errProp.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            var message = errProp.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(message))
                message = code ?? errType ?? "Unknown error";

            return (code, message);
        }

        return (null, "Unknown error");
    }
}
