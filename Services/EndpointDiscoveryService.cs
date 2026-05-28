using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

using FoundryWebUI.Models;
using FoundryWebUI.Services.Platform;

namespace FoundryWebUI.Services;

public sealed class EndpointDiscoveryService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EndpointDiscoveryService> _logger;
    private readonly IConfiguration _configuration;
    private string? _cachedEndpoint;

    private readonly SemaphoreSlim _endpointDiscoveryLock = new(1, 1);

    private static readonly Regex CliUrlRegex = new(
        @"https?://[^\s/]+(?=/openai/status)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ListeningOnRegex = new(
        @"Now listening on: http://127\.0\.0\.1:(\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public HttpClient HttpClient => _httpClient;

    public EndpointDiscoveryService(
        HttpClient httpClient,
        ILogger<EndpointDiscoveryService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<string> GetEndpointAsync()
    {
        var configEndpoint = _configuration["LlmProviders:Foundry:Endpoint"];
        if (!string.IsNullOrEmpty(configEndpoint))
            return configEndpoint.TrimEnd('/');

        if (_cachedEndpoint != null)
            return _cachedEndpoint;

        var cached = await TryReadCachedEndpointAsync();
        if (cached != null)
        {
            _cachedEndpoint = cached;
            _logger.LogInformation("Reusing cached Foundry Local endpoint {Endpoint}", _cachedEndpoint);
            return _cachedEndpoint;
        }

        var configPort = TryGetConfiguredPort();
        if (configPort.HasValue)
        {
            if (await ProbePortAsync(configPort.Value))
            {
                _cachedEndpoint = $"http://localhost:{configPort.Value}";
                await PersistEndpointAsync(_cachedEndpoint);
                _logger.LogInformation("Discovered Foundry Local at {Endpoint} via config port", _cachedEndpoint);
                return _cachedEndpoint;
            }
        }

        var logPort = await TryGetPortFromLogsAsync();
        if (logPort.HasValue && await ProbePortAsync(logPort.Value))
        {
            _cachedEndpoint = $"http://localhost:{logPort.Value}";
            await PersistEndpointAsync(_cachedEndpoint);
            _logger.LogInformation("Discovered Foundry Local at {Endpoint} via log file", _cachedEndpoint);
            return _cachedEndpoint;
        }

        await _endpointDiscoveryLock.WaitAsync();
        try
        {
            if (_cachedEndpoint != null)
                return _cachedEndpoint;

            var (cliEndpoint, serviceStopped) = await TryDiscoverViaCliAsync();
            if (!string.IsNullOrEmpty(cliEndpoint))
            {
                _cachedEndpoint = cliEndpoint;
                await PersistEndpointAsync(_cachedEndpoint);
                _logger.LogInformation("Discovered Foundry Local at {Endpoint} via 'foundry service status'", _cachedEndpoint);
                return _cachedEndpoint;
            }

            if (serviceStopped)
            {
                _logger.LogDebug("Foundry CLI reports service not running.");
            }
        }
        finally
        {
            _endpointDiscoveryLock.Release();
        }

        return "http://localhost:5272";
    }

    private async Task<bool> ProbePortAsync(int port)
    {
        try
        {
            using var cts = new CancellationTokenSource(5000);
            var resp = await _httpClient.GetAsync($"http://localhost:{port}/openai/status", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> TryReadCachedEndpointAsync()
    {
        try
        {
            var file = UserPaths.EndpointCacheFile;
            if (!File.Exists(file)) return null;

            var json = await File.ReadAllTextAsync(file);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("endpoint", out var ep)) return null;

            var url = ep.GetString();
            if (string.IsNullOrEmpty(url)) return null;

            using var cts = new CancellationTokenSource(3000);
            var resp = await _httpClient.GetAsync($"{url}/openai/status", cts.Token);
            return resp.IsSuccessStatusCode ? url : null;
        }
        catch
        {
            return null;
        }
    }

    private static void ClearEndpointCache()
    {
        try
        {
            var file = UserPaths.EndpointCacheFile;
            if (File.Exists(file)) File.Delete(file);
        }
        catch { }
    }

    private async Task PersistEndpointAsync(string endpoint)
    {
        try
        {
            UserPaths.EnsureConfigDirExists();
            var json = JsonSerializer.Serialize(new { endpoint });
            await File.WriteAllTextAsync(UserPaths.EndpointCacheFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist endpoint cache");
        }
    }

    private async Task<(string? Url, bool ServiceStopped)> TryDiscoverViaCliAsync()
    {
        if (!FoundryExecutable.TryFind(_configuration, out var exePath))
        {
            return (null, false);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                ArgumentList = { "service", "status" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return (null, false);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
            }

            var combined = (await stdoutTask) + "\n" + (await stderrTask);
            var match = CliUrlRegex.Match(combined);
            if (match.Success)
            {
                return (match.Value.TrimEnd('/'), false);
            }

            var stopped =
                combined.Contains("not running", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("is stopped", StringComparison.OrdinalIgnoreCase);
            return (null, stopped);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CLI-based Foundry discovery failed");
            return (null, false);
        }
    }

    private static async Task<int?> TryGetPortFromLogsAsync()
    {
        var logDir = UserPaths.FoundryLogsDir;
        if (!Directory.Exists(logDir))
            return null;

        var logFile = Directory.GetFiles(logDir, "foundry*.log")
            .MaxBy(f => f);
        if (logFile is null)
            return null;

        try
        {
            // Daily-rotated logs are typically < 100 KB, so reading the whole file is fine.
            // Use FileShare.ReadWrite since Foundry may still be appending to the file.
            await using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var text = await sr.ReadToEndAsync();
            var match = ListeningOnRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].ValueSpan, out var port))
            {
                return port;
            }
        }
        catch
        {
        }

        return null;
    }

    private int? TryGetConfiguredPort()
    {
        try
        {
            var configFile = UserPaths.FoundryConfigFile;
            if (!File.Exists(configFile))
                return null;

            var json = File.ReadAllText(configFile);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("serviceSettings", out var settings))
                return null;

            if (!settings.TryGetProperty("port", out var portProp) ||
                portProp.ValueKind != JsonValueKind.Number)
                return null;

            var port = portProp.GetInt32();
            return port > 0 ? port : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read port from foundry.config.json");
            return null;
        }
    }

    public async Task<string?> GetCacheDirectoryAsync()
    {
        try
        {
            var endpoint = await GetEndpointAsync();
            using var cts = new CancellationTokenSource(5000);
            var resp = await _httpClient.GetAsync($"{endpoint}/openai/status", cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("modelDirPath", out var mdp))
                    return mdp.GetString();
                if (doc.RootElement.TryGetProperty("ModelDirPath", out var mdp2))
                    return mdp2.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cache directory from Foundry status endpoint");
        }
        return null;
    }

    public async Task<ProviderStatus> GetStatusAsync()
    {
        try
        {
            var endpoint = await GetEndpointAsync();
            var response = await _httpClient.GetAsync($"{endpoint}/openai/status");
            return new ProviderStatus
            {
                Provider = "foundry",
                IsAvailable = response.IsSuccessStatusCode,
                Endpoint = endpoint
            };
        }
        catch (Exception ex)
        {
            return new ProviderStatus
            {
                Provider = "foundry",
                IsAvailable = false,
                Error = ex.Message
            };
        }
    }

    public async Task<ProviderStatus> ReconnectAsync()
    {
        _cachedEndpoint = null;
        ClearEndpointCache();
        _logger.LogInformation("Foundry Local endpoint cache cleared, re-discovering...");

        var configEndpoint = _configuration["LlmProviders:Foundry:Endpoint"];
        if (!string.IsNullOrEmpty(configEndpoint))
        {
            try
            {
                using var cts = new CancellationTokenSource(5000);
                var resp = await _httpClient.GetAsync($"{configEndpoint.TrimEnd('/')}/openai/status", cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    _cachedEndpoint = configEndpoint.TrimEnd('/');
                    return new ProviderStatus { Provider = "foundry", IsAvailable = true, Endpoint = _cachedEndpoint };
                }
            }
            catch { }

            return new ProviderStatus
            {
                Provider = "foundry",
                IsAvailable = false,
                Endpoint = configEndpoint,
                Error = $"Foundry Local not responding on {configEndpoint}. Run 'foundry service set --port {new Uri(configEndpoint).Port}' then 'foundry service start' to fix."
            };
        }

        return await GetStatusAsync();
    }

    public void ClearCache()
    {
        _cachedEndpoint = null;
        ClearEndpointCache();
    }

    public void Dispose()
    {
        _endpointDiscoveryLock.Dispose();
    }
}
