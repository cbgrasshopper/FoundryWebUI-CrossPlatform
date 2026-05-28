using System.Text.Json;

namespace FoundryWebUI.Services;

public sealed class ModelDeletionService
{
    private readonly EndpointDiscoveryService _endpoints;
    private readonly ILogger<ModelDeletionService> _logger;

    public ModelDeletionService(
        EndpointDiscoveryService endpoints,
        ILogger<ModelDeletionService> logger)
    {
        _endpoints = endpoints;
        _logger = logger;
    }

    public async Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting model {ModelId} via REST + file deletion", modelId);
        var endpoint = await _endpoints.GetEndpointAsync();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(10000);
            var unloadResp = await _endpoints.HttpClient.GetAsync(
                $"{endpoint}/openai/unload/{Uri.EscapeDataString(modelId)}?force=true", cts.Token);
            _logger.LogInformation("Unload {ModelId}: {Status}", modelId, unloadResp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unload request failed for {ModelId} (may not be loaded)", modelId);
        }

        string? modelDirPath = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(5000);
            var statusResp = await _endpoints.HttpClient.GetAsync($"{endpoint}/openai/status", cts.Token);
            if (statusResp.IsSuccessStatusCode)
            {
                var statusJson = await statusResp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(statusJson);
                if (doc.RootElement.TryGetProperty("modelDirPath", out var mdp))
                    modelDirPath = mdp.GetString();
                else if (doc.RootElement.TryGetProperty("ModelDirPath", out var mdp2))
                    modelDirPath = mdp2.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get model directory path from status endpoint");
        }

        if (string.IsNullOrEmpty(modelDirPath) || !Directory.Exists(modelDirPath))
        {
            _logger.LogError("Cannot determine model cache directory (modelDirPath={Path}, exists={Exists})",
                modelDirPath, modelDirPath != null && Directory.Exists(modelDirPath));
            return false;
        }

        _logger.LogInformation("Model cache directory: {Path}", modelDirPath);

        var dirName = modelId.Replace(':', '-');
        bool deleted = false;

        try
        {
            foreach (var pubDir in Directory.GetDirectories(modelDirPath))
            {
                _logger.LogInformation("  Publisher dir: {Dir}", pubDir);
                foreach (var modelDir2 in Directory.GetDirectories(pubDir))
                {
                    _logger.LogInformation("    Model dir: {Dir}", Path.GetFileName(modelDir2));
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission denied accessing cache directory {Path}. The FoundryWebUI-X process must have read/write access to the Foundry Local model cache.", modelDirPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot enumerate cache directory (permission issue?)");
        }

        _logger.LogInformation("Looking for directory named '{DirName}' in publisher subdirectories", dirName);

        foreach (var publisherDir in Directory.GetDirectories(modelDirPath))
        {
            var modelDir = Path.Combine(publisherDir, dirName);
            _logger.LogInformation("Checking: {Path} (exists={Exists})", modelDir, Directory.Exists(modelDir));
            if (Directory.Exists(modelDir))
            {
                try
                {
                    Directory.Delete(modelDir, recursive: true);
                    _logger.LogInformation("Deleted model directory: {Path}", modelDir);
                    deleted = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete model directory {Path}", modelDir);
                    return false;
                }
            }
        }

        if (!deleted)
        {
            foreach (var publisherDir in Directory.GetDirectories(modelDirPath))
            {
                var nameWithoutVersion = modelId.Contains(':') ? modelId[..modelId.LastIndexOf(':')] : modelId;
                foreach (var dir in Directory.GetDirectories(publisherDir))
                {
                    var folderName = Path.GetFileName(dir);
                    if (folderName.StartsWith(nameWithoutVersion.Replace(':', '-'), StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Directory.Delete(dir, recursive: true);
                            _logger.LogInformation("Deleted model directory (partial match): {Path}", dir);
                            deleted = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to delete model directory {Path}", dir);
                            return false;
                        }
                    }
                }
                if (deleted) break;
            }
        }

        if (!deleted)
        {
            _logger.LogWarning("Could not find model directory for {ModelId} in {CachePath}", modelId, modelDirPath);
            return false;
        }

        return true;
    }
}
