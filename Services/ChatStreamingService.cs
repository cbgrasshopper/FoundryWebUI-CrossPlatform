using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using FoundryWebUI.Models;
using FoundryWebUI.Services.Sse;

namespace FoundryWebUI.Services;

public sealed class ChatStreamingService
{
    private readonly EndpointDiscoveryService _endpoints;
    private readonly ILogger<ChatStreamingService> _logger;

    public ChatStreamingService(
        EndpointDiscoveryService endpoints,
        ILogger<ChatStreamingService> logger)
    {
        _endpoints = endpoints;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatResponse> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var endpoint = await _endpoints.GetEndpointAsync();

        try
        {
            _logger.LogInformation("Loading model {Model} at {Endpoint}", request.Model, endpoint);
            var loadResp = await _endpoints.HttpClient.GetAsync(
                $"{endpoint}/openai/load/{Uri.EscapeDataString(request.Model)}", cancellationToken);
            var loadBody = await loadResp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Model load response: {Status} — {Body}", loadResp.StatusCode, loadBody);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model load request failed for {Model}", request.Model);
        }

        var payloadDict = new Dictionary<string, object>
        {
            ["model"] = request.Model,
            ["messages"] = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            ["stream"] = true,
            ["temperature"] = request.Temperature
        };
        if (request.MaxTokens.HasValue && request.MaxTokens.Value > 0)
            payloadDict["max_tokens"] = request.MaxTokens.Value;

        var jsonStr = JsonSerializer.Serialize(payloadDict);
        _logger.LogInformation("Chat request to {Endpoint}/v1/chat/completions", endpoint);
        var jsonContent = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/v1/chat/completions")
        {
            Content = jsonContent
        };

        using var response = await _endpoints.HttpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        _logger.LogInformation(
            "Chat response status: {Status}, Content-Type: {CT}",
            response.StatusCode, response.Content.Headers.ContentType);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Chat completions returned {Status}: {Body}", response.StatusCode, errorBody);

            string userMessage;
            if (errorBody.Contains("No OpenAIService provider found", StringComparison.OrdinalIgnoreCase))
            {
                userMessage = "This model is not available for chat. It may need to be downloaded first — visit the Models page to download it.";
            }
            else
            {
                userMessage = $"The model returned an error ({response.StatusCode}). Check the Logs page for details.";
            }

            yield return new ChatResponse { Content = "", Done = true, Error = userMessage };
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        bool receivedAnyContent = false;
        bool connectionClosed = false;

        await foreach (var jsonData in SseEventParser.ParseAsync(stream, _logger, cancellationToken))
        {
            if (jsonData == "[CONNECTION_CLOSED]")
            {
                connectionClosed = true;
                break;
            }

            if (jsonData == "[DONE]")
            {
                if (!receivedAnyContent)
                    yield return new ChatResponse { Content = "(Model returned [DONE] with no content)", Done = true };
                else
                    yield return new ChatResponse { Done = true };
                yield break;
            }

            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(jsonData); }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse: {Data} — {Error}", jsonData, ex.Message);
                continue;
            }

            using (doc)
            {
                if (doc.RootElement.TryGetProperty("error", out var errProp))
                {
                    var (code, message) = ChatErrorMapper.Map(errProp);
                    yield return new ChatResponse { Content = "", Done = true, Error = code ?? message };
                    yield break;
                }

                if (doc.RootElement.TryGetProperty("choices", out var choices))
                {
                    foreach (var choice in choices.EnumerateArray())
                    {
                        if (choice.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("content", out var content))
                        {
                            var text = content.GetString() ?? "";
                            if (text.Length > 0) receivedAnyContent = true;
                            yield return new ChatResponse { Content = text };
                        }
                        else if (choice.TryGetProperty("message", out var message) &&
                                 message.TryGetProperty("content", out var msgContent))
                        {
                            var text = msgContent.GetString() ?? "";
                            if (text.Length > 0) receivedAnyContent = true;
                            yield return new ChatResponse { Content = text, Done = true };
                        }
                    }
                }
            }
        }

        if (connectionClosed)
        {
            yield return new ChatResponse { Content = "", Done = true, Error = "connection_closed" };
        }
        else if (!receivedAnyContent)
        {
            _logger.LogWarning("Chat stream ended with no content for model {Model}", request.Model);
            yield return new ChatResponse { Content = "⚠️ No response from model. Check the Application logs (Logs page) for details.", Done = true };
        }
    }
}
