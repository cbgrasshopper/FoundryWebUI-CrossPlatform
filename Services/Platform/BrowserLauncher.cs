using System.Diagnostics;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace FoundryWebUI.Services.Platform;

/// <summary>
/// Opens the user's default browser at the server's first listening URL once Kestrel reports it.
/// Suppressed when:
///   * <c>--no-browser</c> CLI flag set (handled by caller via the <see cref="Options"/> instance)
///   * <c>FOUNDRYWEBUI_NO_BROWSER=1</c> env var is set
///   * <c>ASPNETCORE_ENVIRONMENT</c> equals <c>Test</c> or stdout is redirected
/// </summary>
public sealed class BrowserLauncher : IHostedService
{
    private readonly IServer _server;
    private readonly IHostEnvironment _env;
    private readonly ILogger<BrowserLauncher> _logger;
    private readonly Options _options;
    private readonly IHostApplicationLifetime _appLifetime;

    public BrowserLauncher(
        IServer server,
        IHostEnvironment env,
        ILogger<BrowserLauncher> logger,
        Options options,
        IHostApplicationLifetime appLifetime)
    {
        _server = server;
        _env = env;
        _logger = logger;
        _options = options;
        _appLifetime = appLifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ShouldLaunch())
        {
            _logger.LogInformation("Browser auto-launch suppressed.");
            return Task.CompletedTask;
        }

        _ = _appLifetime.ApplicationStarted.Register(OnApplicationStarted);
        return Task.CompletedTask;
    }

    private void OnApplicationStarted()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var url = addresses?.FirstOrDefault();
        if (string.IsNullOrEmpty(url))
        {
            _logger.LogWarning("No server addresses reported; cannot auto-launch browser.");
            return;
        }

        TryOpen(url);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool ShouldLaunch()
    {
        if (_options.Disabled)
        {
            return false;
        }

        if (string.Equals(
                Environment.GetEnvironmentVariable("FOUNDRYWEBUI_NO_BROWSER"),
                "1",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (_env.IsEnvironment("Test"))
        {
            return false;
        }

        // CI / detached process detection — best-effort.
        if (Console.IsOutputRedirected && Console.IsInputRedirected)
        {
            return false;
        }

        return true;
    }

    private void TryOpen(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else
            {
                _logger.LogInformation("Browser auto-launch is not supported on this OS.");
                return;
            }

            _logger.LogInformation("Opened browser at {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-launch browser at {Url}", url);
        }
    }

    public sealed class Options
    {
        public bool Disabled { get; set; }
    }
}
