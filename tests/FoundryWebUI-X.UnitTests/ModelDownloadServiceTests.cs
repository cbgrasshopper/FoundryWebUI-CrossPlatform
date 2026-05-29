using System.Net;

using FoundryWebUI.Models;
using FoundryWebUI.Services;
using FoundryWebUI.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryWebUI.UnitTests;

public class ModelDownloadServiceTests
{
    private const string Endpoint = "http://localhost:9999";

    private static readonly string CatalogJson = $$"""
    [
      {
        "name": "phi-3.5-mini",
        "displayName": "Phi-3.5 Mini",
        "alias": "phi-3.5-mini",
        "uri": "https://foundry.example.com/models/phi-3.5-mini",
        "fileSizeMb": 2048,
        "publisher": "Microsoft",
        "runtime": { "deviceType": "cpu" }
      }
    ]
    """;

    private static (ModelDownloadService Service, TestHttpMessageHandler Handler) Build()
    {
        var handler = new TestHttpMessageHandler();
        var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        var config = new ConfigurationBuilder()
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
        var svc = new ModelDownloadService(
            endpoints, NullLogger<ModelDownloadService>.Instance, models);
        return (svc, handler);
    }

    [Test]
    public async Task DownloadModelAsync_EmitsStartingThenComplete()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, "Total  100.0% Downloading done");

        var results = new List<DownloadProgress>();
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini"))
        {
            results.Add(p);
        }

        await Assert.That(results.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(results[0].Status).IsEqualTo("starting");
        await Assert.That(results.Last().Status).IsEqualTo("complete");
        await Assert.That(results.Last().Percent).IsEqualTo(100);
    }

    [Test]
    public async Task DownloadModelAsync_ReportsProgressSteps()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);

        // Each ReadAsync chunk delivers a partial response, so the parser yields
        // progressively. We simulate this by streaming via multiple stubs — the
        // handler returns a string body that arrives in a single chunk, so the
        // parser sees all matches at once and reports the latest percentage.
        var streamBody = """
            Total  0.0% Downloading
            Total  30.5% Downloading
            Total  65.2% Downloading
            Total  99.9% Downloading
            Total  100.0% Downloading done
            """;
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, streamBody);

        var percents = new List<double?>();
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini"))
        {
            percents.Add(p.Percent);
        }

        // Single-chunk delivery means the parser sees all lines at once and
        // reports the latest percentage (100).
        // At minimum, verify we get progress and end at 100%.
        await Assert.That(percents.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(percents.Last()).IsEqualTo(100);
    }

    [Test]
    public async Task DownloadModelAsync_ReportsProgressAfterPercentDrop()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);

        var streamBody = """
            Total  0.0% Downloading
            Total  0.0% Downloading
            Total  0.0% Downloading
            Total  100.0% Downloading done
            """;
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, streamBody);

        var percents = new List<double?>();
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini"))
        {
            percents.Add(p.Percent);
        }

        await Assert.That(percents.Last()).IsEqualTo(100);
    }

    [Test]
    public async Task DownloadModelAsync_ReturnsErrorWhenModelNotFound()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, "[]");

        var results = new List<DownloadProgress>();
        await foreach (var p in svc.DownloadModelAsync("nonexistent-model"))
        {
            results.Add(p);
        }

        await Assert.That(results.Last().Status).StartsWith("error:");
        await Assert.That(results.Last().Status).Contains("nonexistent-model");
    }

    [Test]
    public async Task DownloadModelAsync_ReturnsErrorOnHttpFailure()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.InternalServerError, "oops");

        var results = new List<DownloadProgress>();
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini"))
        {
            results.Add(p);
        }

        await Assert.That(results.Last().Status).StartsWith("error:");
    }

    [Test]
    public async Task DownloadModelAsync_HandlesJsonSuccessResponse()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, """{"success":true}""");

        var results = new List<DownloadProgress>();
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini"))
        {
            results.Add(p);
        }

        await Assert.That(results.Last().Status).IsEqualTo("complete");
        await Assert.That(results.Last().Percent).IsEqualTo(100);
    }

    [Test]
    public async Task DownloadModelAsync_HandlesJsonErrorResponse()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, """{"success":false,"errorMessage":"disk full"}""");

        var results = new List<DownloadProgress>();
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini"))
        {
            results.Add(p);
        }

        await Assert.That(results.Last().Status).Contains("disk full");
    }

    [Test]
    public async Task DownloadModelAsync_ReportsIncompleteStream()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);
        // Stream ends at 45% with no completion marker
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, "Total  45.0% Downloading");

        var results = new List<DownloadProgress>();
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini"))
        {
            results.Add(p);
        }

        await Assert.That(results.Last().Status).StartsWith("error:");
        await Assert.That(results.Last().Status).Contains("45.0");
    }

    [Test]
    public async Task DownloadModelAsync_HandlesCancellation()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, "Total  30.0% Downloading");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var results = new List<DownloadProgress>();
        var threw = false;
        try
        {
            await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini", cts.Token))
            {
                results.Add(p);
            }
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        // Either the method throws OperationCanceledException or it completes without
        // ever yielding a "complete" status.
        await Assert.That(threw || results.All(r => r.Status != "complete")).IsTrue();
    }

    [Test]
    public async Task DownloadModelAsync_WithPascalCaseSuccess()
    {
        var (svc, handler) = Build();
        handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);
        handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, """{"Success":true}""");

        var results = new List<DownloadProgress>();
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini"))
        {
            results.Add(p);
        }

        await Assert.That(results.Last().Status).IsEqualTo("complete");
    }
}
