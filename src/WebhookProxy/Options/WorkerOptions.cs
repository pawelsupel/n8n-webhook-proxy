namespace WebhookProxy.Options;

public sealed class WorkerOptions
{
    public int PollIntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 16;
    public int HealthCheckIntervalSeconds { get; set; } = 60;
    public int HealthFailureThreshold { get; set; } = 3;
    public int HealthSuccessThreshold { get; set; } = 3;
}
