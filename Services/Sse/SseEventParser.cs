using System.Runtime.CompilerServices;

namespace FoundryWebUI.Services.Sse;

/// <summary>
/// Parses an SSE (Server-Sent Events) stream, yielding raw JSON payloads from "data:" lines.
/// Yields "[DONE]" as a sentinel value when the stream ends with that marker.
/// Lines that don't start with "data: " but begin with '{' are also treated as JSON payloads.
/// </summary>
public static class SseEventParser
{
    public static async IAsyncEnumerable<string> ParseAsync(
        Stream body,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(body);
        bool connectionClosed = false;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Connection closed during SSE stream");
                connectionClosed = true;
                break;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "HTTP error during SSE stream");
                connectionClosed = true;
                break;
            }

            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("data: "))
            {
                var payload = line["data: ".Length..];
                yield return payload;
            }
            else if (line.StartsWith("{"))
            {
                yield return line;
            }
        }

        if (connectionClosed)
            yield return "[CONNECTION_CLOSED]";
    }
}
