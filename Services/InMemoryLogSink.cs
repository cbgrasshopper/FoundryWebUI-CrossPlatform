using System.Collections.Concurrent;
using System.Globalization;

using Serilog.Core;
using Serilog.Events;

namespace FoundryWebUI.Services;

/// <summary>
/// Bounded in-memory Serilog sink that backs the Logs page's "Application" tab.
/// Holds the most recent <see cref="Capacity"/> events; older ones are dropped on overflow.
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    public const int Capacity = 2000;

    private readonly ConcurrentQueue<LogEvent> _events = new();
    private int _count;

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        _events.Enqueue(logEvent);
        if (Interlocked.Increment(ref _count) > Capacity)
        {
            if (_events.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _count);
            }
        }
    }

    public IReadOnlyList<InMemoryLogReader.LogEntry> Snapshot(int max)
    {
        if (max < 1)
        {
            return [];
        }

        var snapshot = _events.ToArray();
        var slice = snapshot.Length > max
            ? snapshot[^max..]
            : snapshot;

        var result = new List<InMemoryLogReader.LogEntry>(slice.Length);
        foreach (var e in slice)
        {
            string? category = null;
            if (e.Properties.TryGetValue("SourceContext", out var ctx))
            {
                category = ctx.ToString().Trim('"');
            }

            result.Add(new InMemoryLogReader.LogEntry(
                e.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                e.Level.ToString().ToLowerInvariant(),
                category,
                e.RenderMessage(CultureInfo.InvariantCulture),
                e.Exception?.ToString()));
        }

        return result;
    }
}
