using FoundryWebUI.Services;

using Serilog;

namespace FoundryWebUI.UnitTests;

public class InMemoryLogSinkTests
{
    private static ILogger BuildLogger(InMemoryLogSink sink) =>
        new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

    [Test]
    public async Task Emit_CapturesEventsInOrder()
    {
        var sink = new InMemoryLogSink();
        var logger = BuildLogger(sink);

        logger.Information("first");
        logger.Warning("second");
        logger.Error("third");

        var snapshot = sink.Snapshot(10);
        await Assert.That(snapshot.Count).IsEqualTo(3);
        await Assert.That(snapshot[0].Message).IsEqualTo("first");
        await Assert.That(snapshot[1].Level).IsEqualTo("warning");
        await Assert.That(snapshot[2].Level).IsEqualTo("error");
    }

    [Test]
    public async Task Snapshot_RespectsBoundedCapacity()
    {
        var sink = new InMemoryLogSink();
        var logger = BuildLogger(sink);

        for (var i = 0; i < InMemoryLogSink.Capacity + 50; i++)
        {
            logger.Information("event {N}", i);
        }

        var snapshot = sink.Snapshot(InMemoryLogSink.Capacity + 100);
        await Assert.That(snapshot.Count).IsEqualTo(InMemoryLogSink.Capacity);
        // The oldest 50 should have been dropped.
        await Assert.That(snapshot[0].Message).Contains("50");
    }

    [Test]
    public async Task Snapshot_LimitsToRequestedMax()
    {
        var sink = new InMemoryLogSink();
        var logger = BuildLogger(sink);

        for (var i = 0; i < 100; i++) logger.Information("event {N}", i);

        var snapshot = sink.Snapshot(10);
        await Assert.That(snapshot.Count).IsEqualTo(10);
        // We get the last 10.
        await Assert.That(snapshot[^1].Message).Contains("99");
    }

    [Test]
    public async Task Snapshot_IncludesExceptionText()
    {
        var sink = new InMemoryLogSink();
        var logger = BuildLogger(sink);

        try
        {
            throw new InvalidOperationException("kaboom");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "something failed");
        }

        var snapshot = sink.Snapshot(5);
        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(snapshot[0].Exception).IsNotNull();
        await Assert.That(snapshot[0].Exception!).Contains("kaboom");
    }

    [Test]
    public async Task Snapshot_ZeroMaxReturnsEmpty()
    {
        var sink = new InMemoryLogSink();
        var logger = BuildLogger(sink);
        logger.Information("hi");

        var snapshot = sink.Snapshot(0);
        await Assert.That(snapshot.Count).IsEqualTo(0);
    }
}
