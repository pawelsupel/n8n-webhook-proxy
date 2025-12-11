using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using WebhookProxy.Options;

namespace WebhookProxy.Services;

public sealed class Forwarder
{
    private static readonly HashSet<string> RestrictedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Length",
        "Host",
        "Connection",
        "Accept-Encoding"
    };

    private readonly HttpClient _httpClient;
    private readonly ForwardingOptions _options;
    private readonly ILogger<Forwarder> _logger;

    public Forwarder(HttpClient httpClient, IOptions<ForwardingOptions> options, ILogger<Forwarder> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds <= 0 ? 10 : _options.TimeoutSeconds);
    }

    public async Task<ForwardResult> TryForwardAsync(
        string endpoint,
        string payload,
        string contentType,
        IDictionary<string, string> headers,
        IDictionary<string, string> query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return ForwardResult.FromFailure("Forwarding base URL is not configured");
        }

        var prefix = _options.PathPrefix?.Trim('/') ?? string.Empty;
        var basePath = _options.BaseUrl.TrimEnd('/');
        var targetUrl = string.IsNullOrEmpty(prefix)
            ? $"{basePath}/{endpoint}"
            : $"{basePath}/{prefix}/{endpoint}";

        if (query.Count > 0)
        {
            var queryString = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            targetUrl = $"{targetUrl}?{queryString}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, contentType)
        };

        foreach (var header in headers)
        {
            if (RestrictedHeaders.Contains(header.Key))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ForwardResult.FromSuccess(response.StatusCode);
            }

            var reason = $"Forwarding failed with status {(int)response.StatusCode}";
            _logger.LogWarning("{Reason} for endpoint {Endpoint}", reason, endpoint);
            return ForwardResult.FromFailure(reason, response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var reason = $"Forwarding exception: {ex.Message}";
            _logger.LogWarning(ex, "Forwarding exception for endpoint {Endpoint}", endpoint);
            return ForwardResult.FromFailure(reason);
        }
    }
}

public sealed record ForwardResult(bool Success, string? Error, HttpStatusCode? StatusCode)
{
    public static ForwardResult FromSuccess(HttpStatusCode statusCode) => new(true, null, statusCode);
    public static ForwardResult FromFailure(string error, HttpStatusCode? statusCode = null) => new(false, error, statusCode);
}
