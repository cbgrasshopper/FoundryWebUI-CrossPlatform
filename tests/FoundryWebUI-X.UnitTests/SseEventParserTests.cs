using System.Text;

using FoundryWebUI.Services.Sse;

using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryWebUI.UnitTests;

public class SseEventParserTests
{
    private static Stream MakeStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Test]
    public async Task ParseAsync_DataPrefixedLines_YieldsPayloads()
    {
        var stream = MakeStream("data: {\"choices\":[]}\ndata: {\"choices\":[{\"delta\":{}}]}\n");
        var results = new List<string>();

        await foreach (var item in SseEventParser.ParseAsync(stream, NullLogger.Instance))
        {
            results.Add(item);
        }

        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results[0]).IsEqualTo("{\"choices\":[]}");
    }

    [Test]
    public async Task ParseAsync_BareJson_YieldsPayload()
    {
        var stream = MakeStream("{\"error\":\"something\"}\n");
        var results = new List<string>();

        await foreach (var item in SseEventParser.ParseAsync(stream, NullLogger.Instance))
        {
            results.Add(item);
        }

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results[0]).StartsWith("{");
    }

    [Test]
    public async Task ParseAsync_DoneMarker_IsYieldedAsPayload()
    {
        // The parser yields [DONE] as a regular payload — the consumer decides to stop.
        var stream = MakeStream("data: {\"choices\":[]}\ndata: [DONE]\ndata: should-also-appear\n");
        var results = new List<string>();

        await foreach (var item in SseEventParser.ParseAsync(stream, NullLogger.Instance))
        {
            results.Add(item);
        }

        await Assert.That(results.Count).IsEqualTo(3);
        await Assert.That(results[1]).IsEqualTo("[DONE]");
    }

    [Test]
    public async Task ParseAsync_EmptyLines_AreSkipped()
    {
        var stream = MakeStream("\n\ndata: hello\n\n");
        var results = new List<string>();

        await foreach (var item in SseEventParser.ParseAsync(stream, NullLogger.Instance))
        {
            results.Add(item);
        }

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results[0]).IsEqualTo("hello");
    }

    [Test]
    public async Task ParseAsync_NonDataNonJsonLines_AreSkipped()
    {
        var stream = MakeStream("event: message\nid: 123\ndata: payload\n");
        var results = new List<string>();

        await foreach (var item in SseEventParser.ParseAsync(stream, NullLogger.Instance))
        {
            results.Add(item);
        }

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results[0]).IsEqualTo("payload");
    }
}
