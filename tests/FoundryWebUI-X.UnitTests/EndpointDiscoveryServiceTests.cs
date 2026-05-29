using FoundryWebUI.Services;
using FoundryWebUI.TestInfrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryWebUI.UnitTests;

/// <summary>
/// Tests the easily-reachable parts of EndpointDiscoveryService's discovery cascade.
/// Full testing of CLI and log-based discovery requires injecting IUserPaths + IFoundryCli (future refactor).
/// </summary>
public class EndpointDiscoveryServiceTests
{
    private static EndpointDiscoveryService Build(
        TestHttpMessageHandler handler,
        Dictionary<string, string?>? config = null)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? [])
            .Build();
        return new EndpointDiscoveryService(
            http, NullLogger<EndpointDiscoveryService>.Instance, configuration);
    }

    [Test]
    public async Task GetEndpointAsync_ConfigSet_ReturnsConfigEndpointVerbatim()
    {
        var handler = new TestHttpMessageHandler();
        var svc = Build(handler, new Dictionary<string, string?>
        {
            ["LlmProviders:Foundry:Endpoint"] = "http://127.0.0.1:65274/"
        });

        var endpoint = await svc.GetEndpointAsync();
        await Assert.That(endpoint).IsEqualTo("http://127.0.0.1:65274");
    }

    [Test]
    public async Task GetEndpointAsync_ConfigSet_TrimsTrailingSlash()
    {
        var handler = new TestHttpMessageHandler();
        var svc = Build(handler, new Dictionary<string, string?>
        {
            ["LlmProviders:Foundry:Endpoint"] = "http://localhost:5272/"
        });

        var endpoint = await svc.GetEndpointAsync();
        await Assert.That(endpoint).DoesNotEndWith("/");
    }

    [Test]
    public async Task GetEndpointAsync_CachedOnSecondCall()
    {
        var handler = new TestHttpMessageHandler();
        var svc = Build(handler, new Dictionary<string, string?>
        {
            ["LlmProviders:Foundry:Endpoint"] = "http://localhost:1234"
        });

        var first = await svc.GetEndpointAsync();
        var second = await svc.GetEndpointAsync();
        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task GetEndpointAsync_NoConfig_ReturnsValidEndpoint()
    {
        // Without explicit config, the service runs its discovery cascade.
        // On a machine with Foundry installed, it may find a real endpoint.
        // On a clean machine, it falls back to http://localhost:5272.
        // Either way, the result should be a valid http URL.
        var handler = new TestHttpMessageHandler();
        var svc = Build(handler);

        var endpoint = await svc.GetEndpointAsync();
        await Assert.That(endpoint).StartsWith("http");
        await Assert.That(endpoint).DoesNotEndWith("/");
    }

    // TODO: Test the full discovery cascade (config port probing, log-based discovery, CLI discovery)
    // requires injecting IUserPaths + IFoundryCli seams into EndpointDiscoveryService.
}
