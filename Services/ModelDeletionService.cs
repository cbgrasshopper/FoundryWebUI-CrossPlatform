using System.Text.Json;

namespace FoundryWebUI.Services;

public sealed class ModelDeletionService
{
    private readonly EndpointDiscoveryService _endpoints;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ModelDeletionService> _logger;

    public ModelDeletionService(
        EndpointDiscoveryService endpoints,
        IFileSystem fileSystem,
        ILogger<ModelDeletionService> logger)
    {
        _endpoints = endpoints;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting model {ModelId} via REST + file deletion", modelId);
        var endpoint = await _endpoints.GetEndpointAsync();

        await TryUnloadModelAsync(endpoint, modelId, cancellationToken);

        var modelDirPath = await GetCacheDirectoryAsync(endpoint, cancellationToken);
        if (string.IsNullOrEmpty(modelDirPath) || !_fileSystem.DirectoryExists(modelDirPath))
        {
            _logger.LogError("Cannot determine model cache directory (modelDirPath={Path}, exists={Exists})",
                modelDirPath, modelDirPath != null && _fileSystem.DirectoryExists(modelDirPath));
            return false;
        }

        _logger.LogInformation("Model cache directory: {Path}", modelDirPath);

        var matchedDir = ModelDirectoryMatcher.FindModelDir(modelDirPath, modelId, _fileSystem);
        if (matchedDir == null)
        {
            _logger.LogWarning("Could not find model directory for {ModelId} in {CachePath}", modelId, modelDirPath);
            return false;
        }

        try
        {
            _fileSystem.DeleteDirectory(matchedDir, recursive: true);
            _logger.LogInformation("Deleted model directory: {Path}", matchedDir);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete model directory {Path}", matchedDir);
            return false;
        }
    }

    private async Task TryUnloadModelAsync(string endpoint, string modelId, CancellationToken cancellationToken)
    {
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
    }

    private async Task<string?> GetCacheDirectoryAsync(string endpoint, CancellationToken cancellationToken)
    {
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
                    return mdp.GetString();
                if (doc.RootElement.TryGetProperty("ModelDirPath", out var mdp2))
                    return mdp2.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get model directory path from status endpoint");
        }

        return null;
    }
}

