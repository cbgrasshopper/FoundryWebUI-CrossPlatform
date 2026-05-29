using FoundryWebUI.Services;

namespace FoundryWebUI.Endpoints;

public static class StatusEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/system-info", GetSystemInfo);
        app.MapGet("/api/status", GetStatus);
        app.MapPost("/api/reconnect", Reconnect);
        app.MapPost("/api/foundry/start", StartFoundry);
    }

    private static IResult GetSystemInfo()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        var totalRamBytes = gcInfo.TotalAvailableMemoryBytes;
        var totalRamMb = totalRamBytes / (1024.0 * 1024.0);
        var totalRamGb = totalRamMb / 1024.0;
        return Results.Ok(new
        {
            totalRamMb = Math.Round(totalRamMb, 0),
            totalRamGb = Math.Round(totalRamGb, 1),
        });
    }

    private static async Task<IResult> GetStatus(FoundryLocalService provider)
    {
        var status = await provider.GetStatusAsync();
        return Results.Ok(new[] { status });
    }

    private static async Task<IResult> Reconnect(
        FoundryLocalService provider)
    {
        var status = await provider.ReconnectAsync();
        return Results.Ok(status);
    }

    private static async Task<IResult> StartFoundry(
        FoundryProcessLauncher launcher,
        FoundryLocalService provider,
        CancellationToken cancellationToken)
    {
        if (!launcher.TryFindExecutable(out var exePath))
        {
            return Results.NotFound(new
            {
                error = "Foundry Local binary not found.",
                hint = OperatingSystem.IsMacOS()
                    ? "Install from https://github.com/microsoft/Foundry-Local and ensure 'foundry' is on PATH."
                    : "Install with: winget install Microsoft.FoundryLocal",
            });
        }

        var result = await launcher.StartServiceAsync(exePath, cancellationToken);
        if (!result.Started)
        {
            return Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
        }

        var status = await provider.ReconnectAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!status.IsAvailable && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            status = await provider.GetStatusAsync();
        }

        return Results.Ok(new
        {
            started = true,
            exitCode = result.ExitCode,
            stdout = result.Stdout,
            stderr = result.Stderr,
            status,
        });
    }
}
