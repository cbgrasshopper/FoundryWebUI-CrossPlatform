using System.CommandLine;

using FoundryWebUI.Endpoints;
using FoundryWebUI.Services;
using FoundryWebUI.Services.Platform;

using Serilog;
using Serilog.Events;

namespace FoundryWebUI;

public sealed class Program
{
    private Program() { }

    public static async Task<int> Main(string[] args)
    {
        var hostOption = new Option<string>("--host")
        {
            Description = "Hostname or IP to bind. Defaults to 127.0.0.1 (loopback only).",
            DefaultValueFactory = _ => "127.0.0.1",
        };

        var portOption = new Option<int>("--port")
        {
            Description = "TCP port to listen on. Defaults to 5207.",
            DefaultValueFactory = _ => 5207,
        };

        var noBrowserOption = new Option<bool>("--no-browser")
        {
            Description =
                "Do not automatically open the system browser after startup. " +
                "Equivalent to setting FOUNDRYWEBUI_NO_BROWSER=1.",
        };

        var configOption = new Option<FileInfo?>("--config")
        {
            Description = "Path to an alternate appsettings.json file.",
        };

        var root = new RootCommand("FoundryWebUI-X — cross-platform web UI for Microsoft Foundry Local.")
        {
            hostOption,
            portOption,
            noBrowserOption,
            configOption,
        };

        root.SetAction((parseResult, cancellationToken) =>
        {
            var host = parseResult.GetValue(hostOption) ?? "127.0.0.1";
            var port = parseResult.GetValue(portOption);
            var noBrowser = parseResult.GetValue(noBrowserOption);
            var configFile = parseResult.GetValue(configOption);

            return RunAsync(args, host, port, noBrowser, configFile?.FullName, cancellationToken);
        });

        var parseResult = root.Parse(args);
        return await parseResult.InvokeAsync();
    }

    public static async Task<int> RunAsync(
        string[] args,
        string host,
        int port,
        bool noBrowser,
        string? configFilePath,
        CancellationToken cancellationToken = default)
    {
        UserPaths.EnsureConfigDirExists();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var app = BuildApp(args, builder =>
            {
                if (!string.IsNullOrWhiteSpace(configFilePath))
                {
                    builder.Configuration.AddJsonFile(configFilePath, optional: false, reloadOnChange: true);
                }

                builder.WebHost.UseUrls($"http://{host}:{port}");
                builder.Services.AddSingleton(new BrowserLauncher.Options { Disabled = noBrowser });
            });

            await app.RunAsync(cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "FoundryWebUI-X terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Builds the fully-configured <see cref="WebApplication"/>.
    /// Used by <see cref="RunAsync"/> in production and by the integration test fixture.
    /// </summary>
    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder);
        configure?.Invoke(builder);

        // Make BrowserLauncher.Options optional — integration tests can opt-out by not registering it.
        if (!builder.Services.Any(d => d.ServiceType == typeof(BrowserLauncher.Options)))
        {
            builder.Services.AddSingleton(new BrowserLauncher.Options { Disabled = true });
        }

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseSerilogRequestLogging();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapEndpoints();

        return app;
    }

    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        // Register the in-memory sink as a singleton so the Logs page reader and Serilog share it.
        var inMemorySink = new InMemoryLogSink();
        builder.Services.AddSingleton(inMemorySink);

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var rollingPath = Path.Combine(UserPaths.LogsDir, "app-.log");

            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Information)
                .WriteTo.Console()
                .WriteTo.File(
                    path: rollingPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(inMemorySink);
        });

        builder.Services.AddRazorPages();
        builder.Services.AddControllers();

        builder.Services.AddHttpClient<EndpointDiscoveryService>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromHours(2));
        builder.Services.AddSingleton<ModelCatalogService>();
        builder.Services.AddSingleton<ChatStreamingService>();
        builder.Services.AddSingleton<ModelDownloadService>();
        builder.Services.AddSingleton<ModelDeletionService>();
        builder.Services.AddSingleton<FoundryLocalService>();

        builder.Services.AddSingleton<ApplicationVersion>();
        builder.Services.AddSingleton<ContextWindowLookup>();
        builder.Services.AddSingleton<SystemPromptStore>();
        builder.Services.AddSingleton<InMemoryLogReader>();
        builder.Services.AddHostedService<BrowserLauncher>();
    }
}
