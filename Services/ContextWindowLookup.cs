using System.Text.Json;

namespace FoundryWebUI.Services;

public sealed class ContextWindowLookup
{
    private readonly Dictionary<string, int> _windows;
    private readonly ILogger<ContextWindowLookup> _logger;

    public ContextWindowLookup(IWebHostEnvironment env, ILogger<ContextWindowLookup> logger)
    {
        _logger = logger;
        _windows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var path = Path.Combine(env.WebRootPath, "data", "context-windows.json");
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
                if (prop.Value.TryGetProperty("contextWindow", out var cw) && cw.ValueKind == JsonValueKind.Number)
                {
                    _windows[prop.Name] = cw.GetInt32();
                }
            }

            _logger.LogInformation("Loaded context windows for {Count} model families", _windows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load context window lookup from {Path}", path);
        }
    }

    public int? GetContextWindow(string alias)
    {
        if (string.IsNullOrEmpty(alias)) return null;
        return _windows.TryGetValue(alias, out var value) ? value : null;
    }
}
