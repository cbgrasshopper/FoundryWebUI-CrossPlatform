using System.Text.Json;

using FoundryWebUI.Services.Sse;

namespace FoundryWebUI.UnitTests;

public class ChatErrorMapperTests
{
    [Test]
    public async Task Map_StringError_ReturnsMessageOnly()
    {
        using var doc = JsonDocument.Parse("""{"error":"something went wrong"}""");
        var errProp = doc.RootElement.GetProperty("error");

        var (code, message) = ChatErrorMapper.Map(errProp);

        await Assert.That(code).IsNull();
        await Assert.That(message).IsEqualTo("something went wrong");
    }

    [Test]
    public async Task Map_ObjectError_WithCodeAndMessage()
    {
        using var doc = JsonDocument.Parse("""{"error":{"code":"model_not_found","type":"invalid_request","message":"Model does not exist"}}""");
        var errProp = doc.RootElement.GetProperty("error");

        var (code, message) = ChatErrorMapper.Map(errProp);

        await Assert.That(code).IsEqualTo("model_not_found");
        await Assert.That(message).IsEqualTo("Model does not exist");
    }

    [Test]
    public async Task Map_ObjectError_MissingMessage_FallsBackToCode()
    {
        using var doc = JsonDocument.Parse("""{"error":{"code":"rate_limited","type":"throttle"}}""");
        var errProp = doc.RootElement.GetProperty("error");

        var (code, message) = ChatErrorMapper.Map(errProp);

        await Assert.That(code).IsEqualTo("rate_limited");
        await Assert.That(message).IsEqualTo("rate_limited");
    }

    [Test]
    public async Task Map_ObjectError_MissingAll_ReturnsUnknown()
    {
        using var doc = JsonDocument.Parse("""{"error":{}}""");
        var errProp = doc.RootElement.GetProperty("error");

        var (code, message) = ChatErrorMapper.Map(errProp);

        await Assert.That(code).IsNull();
        await Assert.That(message).IsEqualTo("Unknown error");
    }

    [Test]
    public async Task Map_NumberError_ReturnsUnknown()
    {
        using var doc = JsonDocument.Parse("""{"error":42}""");
        var errProp = doc.RootElement.GetProperty("error");

        var (code, message) = ChatErrorMapper.Map(errProp);

        await Assert.That(code).IsNull();
        await Assert.That(message).IsEqualTo("Unknown error");
    }
}
