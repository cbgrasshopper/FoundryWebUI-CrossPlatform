using System.Diagnostics;

using FoundryWebUI.Services.Platform;

namespace FoundryWebUI.Services;

/// <summary>
/// Encapsulates the logic for finding and launching the Foundry Local process.
/// Extracted from StatusEndpoints to allow unit testing and keep endpoint handlers thin.
/// </summary>
public sealed class FoundryProcessLauncher(
    IConfiguration configuration,
    ILogger<FoundryProcessLauncher> logger)
{
    public sealed record LaunchResult(
        bool Started,
        int ExitCode = 0,
        string Stdout = "",
        string Stderr = "",
        string? Error = null,
        int StatusCode = 200);

    public bool TryFindExecutable(out string exePath)
    {
        return FoundryExecutable.TryFind(configuration, out exePath);
    }

    public async Task<LaunchResult> StartServiceAsync(string exePath, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                ArgumentList = { "service", "start" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return new LaunchResult(false, Error: "Failed to start 'foundry service start'.", StatusCode: 500);
            }

            using var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startTimeout.CancelAfter(TimeSpan.FromSeconds(60));
            await proc.WaitForExitAsync(startTimeout.Token);

            var stdout = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken);
            logger.LogInformation(
                "foundry service start exited {Code}. stdout: {Stdout} stderr: {Stderr}",
                proc.ExitCode, stdout.Trim(), stderr.Trim());

            return new LaunchResult(proc.ExitCode == 0, proc.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (OperationCanceledException)
        {
            return new LaunchResult(false, Error: "Timed out waiting for 'foundry service start'.", StatusCode: 504);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch Foundry Local via {Path}", exePath);
            return new LaunchResult(false, Error: ex.Message, StatusCode: 500);
        }
    }
}
