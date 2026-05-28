using System.Net;

using FoundryWebUI.Services.Platform;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoundryWebUI.E2ETests.Helpers;

/// <summary>
/// Boots a long-lived FoundryWebUI-X instance on a random loopback port,
/// with the Foundry Local endpoint redirected at <see cref="StubFoundryServer"/>.
/// Shared by all Playwright tests in the assembly.
/// </summary>
public sealed class AppHostFixture : IAsyncDisposable
{
    public StubFoundryServer Foundry { get; }
    public WebApplication App { get; }
    public string BaseUrl { get; }

    private AppHostFixture(StubFoundryServer foundry, WebApplication app, string baseUrl)
    {
        Foundry = foundry;
        App = app;
        BaseUrl = baseUrl;
    }

    public static async Task<AppHostFixture> StartAsync()
    {
        Environment.SetEnvironmentVariable("FOUNDRYWEBUI_NO_BROWSER", "1");

        var foundry = await StubFoundryServer.StartAsync();
        var contentRoot = LocateAppContentRoot();

        var app = Program.BuildApp(
            args: [],
            configure: b =>
            {
                b.Environment.EnvironmentName = "Test";
                b.Environment.ContentRootPath = contentRoot;
                b.Environment.WebRootPath = Path.Combine(contentRoot, "wwwroot");

                // Point app at our stub Foundry server.
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LlmProviders:Foundry:Endpoint"] = foundry.BaseUrl,
                });

                // Random loopback port.
                b.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));

                // Disable browser auto-launch unconditionally.
                b.Services.AddSingleton(new BrowserLauncher.Options { Disabled = true });
            });

        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var baseUrl = server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new AppHostFixture(foundry, app, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        await Foundry.DisposeAsync();
    }

    private static string LocateAppContentRoot()
    {
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
