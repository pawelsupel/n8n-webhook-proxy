using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookProxy.Options;
using WebhookProxy.Services;

namespace WebhookProxy.Tests;

public class ForwarderTests
{
    [Fact]
    public async Task MissingBaseUrl_FailsFast()
    {
        var forwarder = new Forwarder(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            Microsoft.Extensions.Options.Options.Create(new ForwardingOptions { BaseUrl = "" }),
            NullLogger<Forwarder>.Instance);

        var result = await forwarder.TryForwardAsync("orders", "{}", "application/json", new Dictionary<string, string>(), new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forwards_WhenSuccessResponse_ReturnsSuccessAndFiltersHeaders()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var forwarder = new Forwarder(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new ForwardingOptions { BaseUrl = "https://example.com" }),
            NullLogger<Forwarder>.Instance);

        var headers = new Dictionary<string, string>
        {
            ["X-Test"] = "123",
            ["Content-Length"] = "999",
            ["Host"] = "malicious",
            ["Connection"] = "close",
            ["Accept-Encoding"] = "gzip"
        };

        var query = new Dictionary<string, string> { ["utm"] = "abc", ["q"] = "1 2" };

        var result = await forwarder.TryForwardAsync("orders", """{"id":1}""", "application/json", headers, query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("utm=abc", captured!.RequestUri!.Query);
        Assert.Contains("q=1%202", captured!.RequestUri!.Query);
        Assert.DoesNotContain(captured!.Headers, h => h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(captured!.Headers, h => h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(captured!.Headers, h => h.Key.Equals("X-Test", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
