using System.Net;
using System.Text;

namespace FoundryWebUI.UnitTests.Helpers;

/// <summary>
/// Tiny scriptable <see cref="HttpMessageHandler"/> used to mock Foundry Local's HTTP surface.
/// Each registered handler is a delegate that inspects the incoming request and returns a
/// response. Requests not matched by any handler return 404.
/// </summary>
public sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Func<HttpRequestMessage, HttpResponseMessage?>> _handlers = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public TestHttpMessageHandler When(
        HttpMethod method,
        string pathOrUrl,
        HttpStatusCode status = HttpStatusCode.OK,
        string body = "",
        string contentType = "application/json")
    {
        _handlers.Add(req =>
        {
            if (req.Method != method) return null;
            var uri = req.RequestUri?.ToString() ?? string.Empty;
            if (!uri.Contains(pathOrUrl, StringComparison.OrdinalIgnoreCase)) return null;
            var resp = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            };
            return resp;
        });
        return this;
    }

    public TestHttpMessageHandler When(
        HttpMethod method,
        string pathOrUrl,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _handlers.Add(req =>
        {
            if (req.Method != method) return null;
            var uri = req.RequestUri?.ToString() ?? string.Empty;
            return uri.Contains(pathOrUrl, StringComparison.OrdinalIgnoreCase)
                ? respond(req)
                : null;
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        foreach (var handler in _handlers)
        {
            var resp = handler(request);
            if (resp is not null)
            {
                return Task.FromResult(resp);
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No stubbed handler for {request.Method} {request.RequestUri}"),
        });
    }
}
