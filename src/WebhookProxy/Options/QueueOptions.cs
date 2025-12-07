namespace WebhookProxy.Options;

public sealed class QueueOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string QueueName { get; set; } = "webhooks";
    public int VisibilityTimeoutSeconds { get; set; } = 30;
}
