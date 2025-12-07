using Microsoft.Extensions.Options;
using WebhookProxy.Options;

namespace WebhookProxy.Services;

public sealed class HealthCheckClient
{
    private readonly HttpClient _httpClient;
    private readonly ForwardingOptions _options;
    private readonly ILogger<HealthCheckClient> _logger;

    public HealthCheckClient(HttpClient httpClient, IOptions<ForwardingOptions> options, ILogger<HealthCheckClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.HealthUrl))
        {
            return true;
        }

        try
        {
            var response = await _httpClient.GetAsync(_options.HealthUrl, cancellationToken);
            var ok = response.IsSuccessStatusCode;
            if (!ok)
            {
                _logger.LogWarning("Health-check returned status {StatusCode}", (int)response.StatusCode);
            }

            return ok;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Health-check request failed");
            return false;
        }
    }
}
