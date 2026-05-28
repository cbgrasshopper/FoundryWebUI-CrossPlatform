using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using FoundryWebUI.Models;

namespace FoundryWebUI.Services;

public sealed class ModelDownloadService
{
    private readonly EndpointDiscoveryService _endpoints;
    private readonly ILogger<ModelDownloadService> _logger;
    private readonly ModelCatalogService _models;

    public ModelDownloadService(
        EndpointDiscoveryService endpoints,
        ILogger<ModelDownloadService> logger,
        ModelCatalogService models)
    {
        _endpoints = endpoints;
        _logger = logger;
        _models = models;
    }

    public async IAsyncEnumerable<DownloadProgress> DownloadModelAsync(
        string modelId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new DownloadProgress { ModelId = modelId, Status = "starting" };

        var endpoint = await _endpoints.GetEndpointAsync();

        var catalogEntry = _models.LookupCatalogEntry(modelId);
        if (catalogEntry == null)
        {
            await _models.GetAvailableModelsAsync();
            catalogEntry = _models.LookupCatalogEntry(modelId);
        }

        if (catalogEntry == null)
        {
            yield return new DownloadProgress { ModelId = modelId, Status = $"error: model '{modelId}' not found in catalog" };
            yield break;
        }

        var cat = catalogEntry.Value;
        var modelUri = cat.TryGetProperty("uri", out var u) ? u.GetString() ?? "" : "";
        var modelName = cat.TryGetProperty("name", out var mn) ? mn.GetString() ?? modelId : modelId;
        var providerType = "AzureFoundryLocal";
        var publisher = cat.TryGetProperty("publisher", out var pub) ? pub.GetString() ?? "" : "";

        var downloadBody = new
        {
            model = new
            {
                Uri = modelUri,
                Name = modelName,
                ProviderType = providerType,
                Publisher = publisher
            },
            ignorePipeReport = true
        };

        var jsonBody = JsonSerializer.Serialize(downloadBody);
        _logger.LogInformation("Starting REST download of {Model}: {Body}", modelId, jsonBody);
        yield return new DownloadProgress { ModelId = modelId, Status = "downloading", Percent = 0 };

        var channel = System.Threading.Channels.Channel.CreateUnbounded<DownloadProgress>();

        _ = Task.Run(async () =>
        {
            HttpResponseMessage? response = null;
            try
            {
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/openai/download")
                {
                    Content = content
                };
                response = await _endpoints.HttpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    await channel.Writer.WriteAsync(
                        new DownloadProgress { ModelId = modelId, Status = $"error: HTTP {response.StatusCode} — {errBody}" });
                    return;
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var reader = new StreamReader(stream);
                var buffer = new char[4096];
                var lineBuffer = new StringBuilder();
                double lastPercent = 0;
                var started = DateTime.UtcNow;

                while (!cancellationToken.IsCancellationRequested)
                {
                    int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0) break;

                    lineBuffer.Append(buffer, 0, read);
                    var text = lineBuffer.ToString();

                    var matches = Regex.Matches(text, @"Total\s+([\d.]+)%");
                    if (matches.Count > 0)
                    {
                        var latestMatch = matches[^1];
                        if (double.TryParse(latestMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
                        {
                            lastPercent = percent;
                            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
                            await channel.Writer.WriteAsync(new DownloadProgress
                            {
                                ModelId = modelId,
                                Status = $"downloading ({TimeSpan.FromSeconds(elapsed):mm\\:ss} elapsed)",
                                Percent = percent
                            });
                        }
                    }

                    if (text.Contains("\"success\"") || text.Contains("\"Success\""))
                    {
                        var jsonStart = text.IndexOf('{');
                        if (jsonStart >= 0)
                        {
                            var jsonStr = text[jsonStart..];
                            try
                            {
                                using var doc = JsonDocument.Parse(jsonStr);
                                var success = false;
                                if (doc.RootElement.TryGetProperty("success", out var s))
                                    success = s.GetBoolean();
                                else if (doc.RootElement.TryGetProperty("Success", out var s2))
                                    success = s2.GetBoolean();

                                if (success)
                                    await channel.Writer.WriteAsync(new DownloadProgress { ModelId = modelId, Status = "complete", Percent = 100 });
                                else
                                {
                                    var errMsg = doc.RootElement.TryGetProperty("errorMessage", out var e) ? e.GetString()
                                        : doc.RootElement.TryGetProperty("ErrorMessage", out var e2) ? e2.GetString()
                                        : "Unknown error";
                                    await channel.Writer.WriteAsync(new DownloadProgress { ModelId = modelId, Status = $"error: {errMsg}" });
                                }
                                return;
                            }
                            catch { }
                        }
                    }

                    var lastNewline = text.LastIndexOf('\n');
                    if (lastNewline >= 0)
                        lineBuffer = new StringBuilder(text[(lastNewline + 1)..]);
                }

                if (lastPercent >= 99)
                    await channel.Writer.WriteAsync(new DownloadProgress { ModelId = modelId, Status = "complete", Percent = 100 });
                else
                    await channel.Writer.WriteAsync(new DownloadProgress { ModelId = modelId, Status = $"error: download stream ended at {lastPercent:F1}%" });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Download stream error");
                await channel.Writer.WriteAsync(new DownloadProgress { ModelId = modelId, Status = $"error: {ex.Message}" });
            }
            finally
            {
                response?.Dispose();
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var progress in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return progress;
        }
    }
}
