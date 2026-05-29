using System.Text.Json;

namespace FoundryWebUI.Services;

public sealed record ModelCardEntry(int? ContextWindow, List<string>? Capabilities);

public sealed class ContextWindowLookup
{
    private readonly Dictionary<string, ModelCardEntry> _entries;
    private readonly ILogger<ContextWindowLookup> _logger;

    public ContextWindowLookup(IWebHostEnvironment env, ILogger<ContextWindowLookup> logger)
    {
        _logger = logger;
        _entries = new Dictionary<string, ModelCardEntry>(StringComparer.OrdinalIgnoreCase);

        var path = Path.Combine(env.WebRootPath, "data", "model-cards.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning("Context window lookup not found at {Path}", path);
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith('_')) continue;

                int? contextWindow = null;
                if (prop.Value.TryGetProperty("contextWindow", out var cw) && cw.ValueKind == JsonValueKind.Number)
                {
                    contextWindow = cw.GetInt32();
                }

                List<string>? capabilities = null;
                if (prop.Value.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                {
                    capabilities = [.. caps.EnumerateArray()
                        .Select(c => c.GetString()!)
                        .Where(s => s is not null)];
                }

                _entries[prop.Name] = new ModelCardEntry(contextWindow, capabilities);
            }

            _logger.LogInformation("Loaded model card data for {Count} model families", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model card data from {Path}", path);
        }
    }

    public ModelCardEntry? GetEntry(string alias)
    {
        if (string.IsNullOrEmpty(alias)) return null;
        return _entries.TryGetValue(alias, out var entry) ? entry : null;
    }

    public int? GetContextWindow(string alias)
    {
        return GetEntry(alias)?.ContextWindow;
    }

    public List<string>? GetCapabilities(string alias)
    {
        return GetEntry(alias)?.Capabilities;
    }
}
