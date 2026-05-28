using System.Net;
using System.Text.Json;

using FoundryWebUI.Models;
using FoundryWebUI.Services;
using FoundryWebUI.TestInfrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryWebUI.UnitTests;

public class FoundryLocalServiceTests
{
    private const string Endpoint = "http://localhost:5273";

    private static (FoundryLocalService Service, TestHttpMessageHandler Handler) Build(
        IConfiguration? configuration = null)
    {
        var handler = new TestHttpMessageHandler();
        var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        var config = configuration ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LlmProviders:Foundry:Endpoint"] = Endpoint,
            })
            .Build();

        var env = new TestWebHostEnvironment();
        var ctxLookup = new ContextWindowLookup(env, NullLogger<ContextWindowLookup>.Instance);

        var endpoints = new EndpointDiscoveryService(
            http, NullLogger<EndpointDiscoveryService>.Instance, config);
        var models = new ModelCatalogService(
            endpoints, NullLogger<ModelCatalogService>.Instance, ctxLookup);
        var chat = new ChatStreamingService(
            endpoints, NullLogger<ChatStreamingService>.Instance);
        var download = new ModelDownloadService(
            endpoints, NullLogger<ModelDownloadService>.Instance, models);
        var deletion = new ModelDeletionService(
            endpoints, NullLogger<ModelDeletionService>.Instance);

        var svc = new FoundryLocalService(endpoints, models, chat, download, deletion);
        return (svc, handler);
    }

    [Test]
    public async Task GetStatusAsync_ReturnsAvailableWhenEndpointResponds()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/status", HttpStatusCode.OK, "{}");

        var status = await svc.GetStatusAsync();

        await Assert.That(status.Provider).IsEqualTo("foundry");
        await Assert.That(status.IsAvailable).IsTrue();
        await Assert.That(status.Endpoint).IsEqualTo(Endpoint);
    }

    [Test]
    public async Task GetStatusAsync_ReturnsUnavailableWhenEndpointFails()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/status", HttpStatusCode.InternalServerError, "boom");

        var status = await svc.GetStatusAsync();

        await Assert.That(status.IsAvailable).IsFalse();
    }

    [Test]
    public async Task GetCacheDirectoryAsync_ParsesModelDirPath()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/status", HttpStatusCode.OK, """{"modelDirPath":"/var/cache/foundry"}""");

        var dir = await svc.GetCacheDirectoryAsync();

        await Assert.That(dir).IsEqualTo("/var/cache/foundry");
    }

    [Test]
    public async Task GetCacheDirectoryAsync_FallsBackToPascalCase()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/status", HttpStatusCode.OK, """{"ModelDirPath":"/cache/pascal"}""");

        var dir = await svc.GetCacheDirectoryAsync();

        await Assert.That(dir).IsEqualTo("/cache/pascal");
    }

    [Test]
    public async Task GetCacheDirectoryAsync_ReturnsNullWhenMissing()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/status", HttpStatusCode.OK, "{}");

        var dir = await svc.GetCacheDirectoryAsync();
        await Assert.That(dir).IsNull();
    }

    [Test]
    public async Task GetAvailableModelsAsync_ParsesCatalogArray()
    {
        var catalog = """
        [
          {
            "name": "phi-3.5-mini",
            "displayName": "Phi-3.5 Mini",
            "alias": "phi-3.5-mini",
            "fileSizeMb": 2048,
            "publisher": "Microsoft",
            "runtime": { "deviceType": "cpu" },
            "maxOutputTokens": 4096,
            "task": "text-generation"
          }
        ]
        """;
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, catalog);

        var models = await svc.GetAvailableModelsAsync();

        await Assert.That(models.Count).IsEqualTo(1);
        var m = models[0];
        await Assert.That(m.Id).IsEqualTo("phi-3.5-mini");
        await Assert.That(m.Name).IsEqualTo("Phi-3.5 Mini");
        await Assert.That(m.Size).IsEqualTo(2048L * 1024 * 1024);
        await Assert.That(m.EstimatedRamMb).IsEqualTo(Math.Round(2048 * 1.2, 0));
        await Assert.That(m.MaxOutputTokens).IsEqualTo(4096);
        await Assert.That(m.ParameterSize).IsEqualTo("cpu");
        await Assert.That(m.Family).IsEqualTo("text-generation");
        await Assert.That(m.Description).IsEqualTo("by Microsoft (cpu)");
    }

    [Test]
    public async Task GetAvailableModelsAsync_ParsesObjectWrappedCatalog()
    {
        var catalog = """{ "models": [ { "name": "x", "displayName": "X", "fileSizeMb": 100 } ] }""";
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, catalog);

        var models = await svc.GetAvailableModelsAsync();
        await Assert.That(models.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetAvailableModelsAsync_ReturnsEmptyOnHttpError()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.InternalServerError, "");

        var models = await svc.GetAvailableModelsAsync();
        await Assert.That(models.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetLoadedModelsAsync_TagsLoadedAndDownloaded()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/models", HttpStatusCode.OK, """["m1","m2"]""");
        handler.When(HttpMethod.Get, "/openai/loadedmodels", HttpStatusCode.OK, """["m1"]""");

        var models = await svc.GetLoadedModelsAsync();

        await Assert.That(models.Count).IsEqualTo(2);
        var m1 = models.First(m => m.Id == "m1");
        var m2 = models.First(m => m.Id == "m2");
        await Assert.That(m1.Status).IsEqualTo("loaded");
        await Assert.That(m2.Status).IsEqualTo("downloaded");
    }

    [Test]
    public async Task ReconnectAsync_ReportsErrorWhenConfiguredEndpointDown()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/status", HttpStatusCode.ServiceUnavailable, "down");

        var status = await svc.ReconnectAsync();

        await Assert.That(status.IsAvailable).IsFalse();
        await Assert.That(status.Endpoint).IsEqualTo(Endpoint);
        await Assert.That(status.Error).IsNotNull();
    }

    [Test]
    public async Task StreamChatAsync_EmitsContentDeltasUntilDone()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/load/phi", HttpStatusCode.OK, """{"success":true}""");

        var chunks = new[]
        {
            """{"choices":[{"delta":{"content":"Hello"}}]}""",
            """{"choices":[{"delta":{"content":" world"}}]}""",
            "[DONE]",
        };
        var body = string.Concat(chunks.Select(c => $"data: {c}\n"));

        handler.When(HttpMethod.Post, "/v1/chat/completions", HttpStatusCode.OK, body);

        var request = new ChatRequest
        {
            Model = "phi",
            Messages = [new ChatMessage { Role = "user", Content = "hi" }],
        };

        var content = new List<string>();
        var done = false;
        await foreach (var chunk in svc.StreamChatAsync(request))
        {
            if (!string.IsNullOrEmpty(chunk.Content)) content.Add(chunk.Content);
            if (chunk.Done) done = true;
        }

        await Assert.That(done).IsTrue();
        await Assert.That(string.Concat(content)).IsEqualTo("Hello world");
    }

    [Test]
    public async Task StreamChatAsync_SurfacesErrorObject()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/openai/load/x", HttpStatusCode.OK, "{}");
        handler.When(HttpMethod.Post, "/v1/chat/completions", HttpStatusCode.OK,
            """data: {"error":{"code":"out_of_memory","message":"oom"}}""" + "\n");

        var request = new ChatRequest { Model = "x", Messages = [new ChatMessage { Content = "hi" }] };

        var responses = new List<ChatResponse>();
        await foreach (var chunk in svc.StreamChatAsync(request))
        {
            responses.Add(chunk);
        }

        await Assert.That(responses.Any(r => r.Error == "out_of_memory")).IsTrue();
    }

    [Test]
    public async Task ProviderName_IsFoundry()
    {
        var (svc, _) = Build();
        await Assert.That(svc.ProviderName).IsEqualTo("foundry");
    }
}
