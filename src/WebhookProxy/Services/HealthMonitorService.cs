using Microsoft.Extensions.Options;
using WebhookProxy.Options;

namespace WebhookProxy.Services;

public sealed class HealthMonitorService : BackgroundService
{
    private readonly HealthCheckClient _healthCheckClient;
    private readonly ModeService _modeService;
    private readonly WorkerOptions _options;
    private readonly ILogger<HealthMonitorService> _logger;

    public HealthMonitorService(
        HealthCheckClient healthCheckClient,
        ModeService modeService,
        IOptions<WorkerOptions> options,
        ILogger<HealthMonitorService> logger)
    {
        _healthCheckClient = healthCheckClient;
        _modeService = modeService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.HealthCheckIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            var healthy = await _healthCheckClient.IsHealthyAsync(stoppingToken);
            if (healthy)
            {
                _modeService.RecordHealthSuccess();
                continue;
            }

            _modeService.RecordHealthFailure("Remote health-check failed", _logger);
        }
    }
}
