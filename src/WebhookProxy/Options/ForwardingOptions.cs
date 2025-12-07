namespace WebhookProxy.Options;

public sealed class ForwardingOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string HealthUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}
