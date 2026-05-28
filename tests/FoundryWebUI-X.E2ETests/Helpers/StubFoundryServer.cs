using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace FoundryWebUI.E2ETests.Helpers;

/// <summary>
/// In-process HTTP stub that impersonates Foundry Local for end-to-end tests.
/// Listens on a random loopback port so it never collides with a real instance.
/// </summary>
public sealed class StubFoundryServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }

    private StubFoundryServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<StubFoundryServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Listen(IPAddress.Loopback, 0);
        });
        builder.Environment.EnvironmentName = "Test";

        var app = builder.Build();

        app.MapGet("/openai/status", () => Results.Json(new { isAvailable = true }));
        app.MapGet("/openai/models", () => Results.Json(new[] { "phi-3.5-mini" }));
        app.MapGet("/openai/loadedmodels", () => Results.Json(Array.Empty<string>()));
        app.MapGet("/foundry/list", () => Results.Json(new[]
        {
            new
            {
                name = "phi-3.5-mini",
                displayName = "Phi-3.5 Mini",
                fileSizeMb = 1024,
                publisher = "Microsoft",
                runtime = new { deviceType = "cpu" },
            },
        }));

        await app.StartAsync();

        var addr = app.Urls.First();
        return new StubFoundryServer(app, addr);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
