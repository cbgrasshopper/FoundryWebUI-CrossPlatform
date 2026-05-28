using FoundryWebUI.Models;

namespace FoundryWebUI.Services;

public class FoundryLocalService : ILlmProvider
{
    private readonly EndpointDiscoveryService _endpoints;
    private readonly ModelCatalogService _models;
    private readonly ChatStreamingService _chat;
    private readonly ModelDownloadService _download;
    private readonly ModelDeletionService _deletion;

    public string ProviderName => "foundry";

    public FoundryLocalService(
        EndpointDiscoveryService endpoints,
        ModelCatalogService models,
        ChatStreamingService chat,
        ModelDownloadService download,
        ModelDeletionService deletion)
    {
        _endpoints = endpoints;
        _models = models;
        _chat = chat;
        _download = download;
        _deletion = deletion;
    }

    public Task<ProviderStatus> GetStatusAsync() =>
        _endpoints.GetStatusAsync();

    public Task<ProviderStatus> ReconnectAsync()
    {
        _models.ClearCache();
        return _endpoints.ReconnectAsync();
    }

    public Task<string?> GetCacheDirectoryAsync() =>
        _endpoints.GetCacheDirectoryAsync();

    public Task<List<ModelInfo>> GetAvailableModelsAsync() =>
        _models.GetAvailableModelsAsync();

    public Task<List<ModelInfo>> GetLoadedModelsAsync() =>
        _models.GetLoadedModelsAsync();

    public IAsyncEnumerable<ChatResponse> StreamChatAsync(
        ChatRequest request, CancellationToken cancellationToken = default) =>
        _chat.StreamChatAsync(request, cancellationToken);

    public IAsyncEnumerable<DownloadProgress> DownloadModelAsync(
        string modelId, CancellationToken cancellationToken = default) =>
        _download.DownloadModelAsync(modelId, cancellationToken);

    public Task<bool> DeleteModelAsync(
        string modelId, CancellationToken cancellationToken = default) =>
        _deletion.DeleteModelAsync(modelId, cancellationToken);
}
