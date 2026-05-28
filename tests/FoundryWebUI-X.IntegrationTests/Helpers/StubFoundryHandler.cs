using System.Net;
using System.Text;

namespace FoundryWebUI.IntegrationTests.Helpers;

/// <summary>
/// HttpMessageHandler installed inside the test fixture's DI container so calls to
/// Foundry Local from <c>FoundryLocalService</c> are deterministically stubbed.
/// </summary>
public sealed class StubFoundryHandler : HttpMessageHandler
{
    private readonly List<Func<HttpRequestMessage, HttpResponseMessage?>> _handlers = [];
    public List<HttpRequestMessage> Requests { get; } = [];

    public StubFoundryHandler When(
        HttpMethod method,
        string substring,
        HttpStatusCode status,
        string body,
        string contentType = "application/json")
    {
        _handlers.Add(req =>
        {
            if (req.Method != method) return null;
            var uri = req.RequestUri?.ToString() ?? string.Empty;
            return uri.Contains(substring, StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) }
                : null;
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        foreach (var handler in _handlers)
        {
            var resp = handler(request);
            if (resp is not null) return Task.FromResult(resp);
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No stub for {request.Method} {request.RequestUri}"),
        });
    }
}
