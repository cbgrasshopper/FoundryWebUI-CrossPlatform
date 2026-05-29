using FoundryWebUI.Services;
using FoundryWebUI.TestInfrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace FoundryWebUI.IntegrationTests.Helpers;

/// <summary>
/// Spins up the FoundryWebUI-X host with the Foundry Local <see cref="HttpClient"/> handler
/// replaced by <see cref="TestHttpMessageHandler"/>, served via the in-memory TestServer.
/// </summary>
public sealed class FoundryWebUIFactory : WebApplicationFactory<Program>
{
    public TestHttpMessageHandler FoundryStub { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var contentRoot = LocateAppContentRoot();

        var app = Program.BuildApp(
            args: [],
            configure: b =>
            {
                b.Environment.EnvironmentName = "Test";

                // Point the host at the actual app project so wwwroot and views are reachable.
                b.Environment.ContentRootPath = contentRoot;
                b.Environment.WebRootPath = Path.Combine(contentRoot, "wwwroot");

                // In-memory test server pipe (replaces Kestrel for tests).
                b.WebHost.UseTestServer();

                // Re-register the typed HttpClient with our stub as the primary handler.
                b.Services.AddHttpClient<EndpointDiscoveryService>()
                    .ConfigurePrimaryHttpMessageHandler(_ => FoundryStub);

                b.Services.AddSingleton(FoundryStub);
            });

        app.Start();
        return app;
    }

    private static string LocateAppContentRoot()
    {
        // Walk up from the test binary until we find FoundryWebUI-X.csproj.
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var csproj = Path.Combine(d.FullName, "FoundryWebUI-X.csproj");
            if (File.Exists(csproj))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate FoundryWebUI-X.csproj walking up from '{dir}'.");
    }
}
