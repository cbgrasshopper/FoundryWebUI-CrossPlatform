namespace FoundryWebUI.Services;

/// <summary>
/// Thin facade over <see cref="InMemoryLogSink"/> consumed by the logs endpoint.
/// </summary>
public sealed class InMemoryLogReader
{
    private readonly InMemoryLogSink _sink;

    public InMemoryLogReader(InMemoryLogSink sink) => _sink = sink;

    public IReadOnlyList<LogEntry> GetRecent(int max) => _sink.Snapshot(max);

    public sealed record LogEntry(
        string Time,
        string Level,
        string? Category,
        string Message,
        string? Exception);
}
