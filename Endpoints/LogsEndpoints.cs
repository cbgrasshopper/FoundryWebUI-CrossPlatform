using FoundryWebUI.Services;
using FoundryWebUI.Services.Platform;

namespace FoundryWebUI.Endpoints;

public static class LogsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/logs/{source}", GetLogs);
    }

    private static IResult GetLogs(
        string source,
        InMemoryLogReader logReader,
        ILogger<Program> logger,
        int lines = 500)
    {
        lines = Math.Clamp(lines, 10, 5000);
        try
        {
            return source.ToLowerInvariant() switch
            {
                "app" => Results.Ok(GetAppLogs(logReader, lines)),
                "stdout" => Results.Ok(ReadFileLogs(UserPaths.LogsDir, ["app-*.log"], "stdout", lines)),
                "foundry" => Results.Ok(ReadFileLogs(UserPaths.FoundryLogsDir, ["*.log", "*.txt"], "foundry", lines)),
                _ => Results.BadRequest(new { error = $"Unknown log source: {source}" }),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read logs for source {Source}", source);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    private static object GetAppLogs(InMemoryLogReader logReader, int lines)
    {
        var entries = logReader.GetRecent(lines);
        return new { source = "app", entries };
    }

    private static object ReadFileLogs(string logDir, string[] patterns, string source, int lines)
    {
        var logLines = new List<object>();

        if (Directory.Exists(logDir))
        {
            IEnumerable<string> fileQuery = [];
            foreach (var pattern in patterns)
            {
                fileQuery = fileQuery.Concat(Directory.GetFiles(logDir, pattern));
            }

            var logFiles = fileQuery
                .OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f))
                .Take(3);

            foreach (var file in logFiles)
            {
                try
                {
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    var content = reader.ReadToEnd();
                    var fileLines = content.Split('\n');
                    var fileName = Path.GetFileName(file);
                    foreach (var line in fileLines.TakeLast(lines))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            logLines.Add(new { file = fileName, message = line.TrimEnd('\r') });
                        }
                    }
                }
                catch (Exception ex)
                {
                    logLines.Add(new
                    {
                        file = Path.GetFileName(file),
                        message = $"[Error reading: {ex.Message}]",
                    });
                }
            }
        }

        return new
        {
            source,
            entries = logLines.TakeLast(lines),
            logDir = source == "foundry" && !Directory.Exists(logDir) ? "(not found)" : logDir,
        };
    }
}
