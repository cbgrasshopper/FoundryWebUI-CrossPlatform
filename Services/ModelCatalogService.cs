using System.Text.Json;

using FoundryWebUI.Models;

namespace FoundryWebUI.Services;

public sealed class ModelCatalogService
{
    private readonly EndpointDiscoveryService _endpoints;
    private readonly ILogger<ModelCatalogService> _logger;
    private readonly ContextWindowLookup _contextWindows;
    private List<JsonElement>? _catalogCache;

    public ModelCatalogService(
        EndpointDiscoveryService endpoints,
        ILogger<ModelCatalogService> logger,
        ContextWindowLookup contextWindows)
    {
        _endpoints = endpoints;
        _logger = logger;
        _contextWindows = contextWindows;
    }

    public async Task<List<ModelInfo>> GetAvailableModelsAsync()
    {
        var models = new List<ModelInfo>();
        try
        {
            var endpoint = await _endpoints.GetEndpointAsync();
            _logger.LogInformation("Fetching catalog from {Endpoint}/foundry/list", endpoint);
            var response = await _endpoints.HttpClient.GetAsync($"{endpoint}/foundry/list");
            _logger.LogInformation("Catalog response: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Catalog request failed: {Status} — {Body}", response.StatusCode, errBody);
                return models;
            }

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Catalog JSON length: {Length}", json.Length);
            using var doc = JsonDocument.Parse(json);

            JsonElement modelsArray;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                modelsArray = doc.RootElement;
            }
            else if (doc.RootElement.TryGetProperty("models", out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                modelsArray = nested;
            }
            else
            {
                _logger.LogWarning("Catalog response has unexpected format. Root kind: {Kind}", doc.RootElement.ValueKind);
                return models;
            }
            {
                var catalog = new List<JsonElement>();
                _logger.LogInformation("Catalog contains {Count} models", modelsArray.GetArrayLength());
                foreach (var model in modelsArray.EnumerateArray())
                {
                    catalog.Add(model.Clone());

                    var name = model.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var displayName = model.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? name : name;
                    var alias = model.TryGetProperty("alias", out var a) ? a.GetString() : null;

                    long? sizeBytes = null;
                    double? fileSizeMb = null;
                    if (model.TryGetProperty("fileSizeMb", out var fsz) && fsz.ValueKind == JsonValueKind.Number)
                    {
                        fileSizeMb = fsz.GetDouble();
                        sizeBytes = (long)(fileSizeMb.Value * 1024 * 1024);
                    }

                    double? estimatedRamMb = fileSizeMb.HasValue ? Math.Round(fileSizeMb.Value * 1.2, 0) : null;

                    var publisher = model.TryGetProperty("publisher", out var pub) ? pub.GetString() : null;
                    var deviceType = "";
                    if (model.TryGetProperty("runtime", out var rt) && rt.TryGetProperty("deviceType", out var dt))
                        deviceType = dt.GetString() ?? "";

                    int? maxOutputTokens = null;
                    if (model.TryGetProperty("maxOutputTokens", out var mot) && mot.ValueKind == JsonValueKind.Number)
                        maxOutputTokens = mot.GetInt32();

                    var taskStr = model.TryGetProperty("task", out var taskProp) ? taskProp.GetString() : null;
                    var supportsTools = model.TryGetProperty("supportsToolCalling", out var stc)
                        && stc.ValueKind == JsonValueKind.True;

                    var caps = alias != null ? _contextWindows.GetCapabilities(alias) : null;
                    if (caps == null)
                    {
                        caps = [];
                        if (string.Equals(taskStr, "vision-language-chat", StringComparison.OrdinalIgnoreCase))
                            caps.Add("vision");
                        if (string.Equals(taskStr, "automatic-speech-recognition", StringComparison.OrdinalIgnoreCase))
                            caps.Add("speech");
                        if (supportsTools)
                            caps.Add("tools");
                        if (displayName.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                            || displayName.Contains("r1-distill", StringComparison.OrdinalIgnoreCase))
                            caps.Add("reasoning");
                        if (displayName.Contains("coder", StringComparison.OrdinalIgnoreCase))
                            caps.Add("code");
                    }

                    models.Add(new ModelInfo
                    {
                        Id = name,
                        Name = displayName,
                        Description = publisher != null ? $"by {publisher} ({deviceType})" : deviceType,
                        Size = sizeBytes,
                        EstimatedRamMb = estimatedRamMb,
                        MaxOutputTokens = maxOutputTokens,
                        ContextWindow = alias != null ? _contextWindows.GetContextWindow(alias) : null,
                        Status = "available",
                        Provider = "foundry",
                        Family = taskStr,
                        ParameterSize = deviceType,
                        Capabilities = caps,
                    });
                }

                if (catalog.Count > 0)
                {
                    _catalogCache = catalog;
                }
                else
                {
                    _catalogCache = null;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Foundry Local unreachable while fetching catalog: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available models from Foundry Local");
        }
        return models;
    }

    public async Task<List<ModelInfo>> GetLoadedModelsAsync()
    {
        var models = new List<ModelInfo>();
        try
        {
            var endpoint = await _endpoints.GetEndpointAsync();

            var response = await _endpoints.HttpClient.GetAsync($"{endpoint}/openai/models");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var loadedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var loadedResp = await _endpoints.HttpClient.GetAsync($"{endpoint}/openai/loadedmodels");
                        if (loadedResp.IsSuccessStatusCode)
                        {
                            var loadedJson = await loadedResp.Content.ReadAsStringAsync();
                            using var loadedDoc = JsonDocument.Parse(loadedJson);
                            if (loadedDoc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in loadedDoc.RootElement.EnumerateArray())
                                {
                                    var val = item.GetString();
                                    if (val != null) loadedSet.Add(val);
                                }
                            }
                        }
                    }
                    catch { }

                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var modelName = item.ValueKind == JsonValueKind.String
                            ? item.GetString() ?? ""
                            : item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";

                        if (string.IsNullOrEmpty(modelName)) continue;

                        var isLoaded = loadedSet.Contains(modelName);

                        models.Add(new ModelInfo
                        {
                            Id = modelName,
                            Name = modelName,
                            Status = isLoaded ? "loaded" : "downloaded",
                            Provider = "foundry"
                        });
                    }
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Foundry Local unreachable while listing models: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get models from Foundry Local");
        }
        return models;
    }

    public void ClearCache()
    {
        _catalogCache = null;
    }

    public JsonElement? LookupCatalogEntry(string modelId)
    {
        if (_catalogCache == null) return null;

        foreach (var entry in _catalogCache)
        {
            var alias = entry.TryGetProperty("alias", out var a) ? a.GetString() : null;
            var name = entry.TryGetProperty("name", out var n) ? n.GetString() : null;
            var displayName = entry.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            if (string.Equals(alias, modelId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, modelId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(displayName, modelId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }
}
