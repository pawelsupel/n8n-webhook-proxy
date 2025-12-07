using Microsoft.Extensions.Options;
using WebhookProxy.Models;
using WebhookProxy.Options;

namespace WebhookProxy.Services;

public sealed class QueueWorkerService : BackgroundService
{
    private readonly QueueService _queueService;
    private readonly Forwarder _forwarder;
    private readonly HealthCheckClient _healthCheckClient;
    private readonly ModeService _modeService;
    private readonly WorkerOptions _workerOptions;
    private readonly QueueOptions _queueOptions;
    private readonly ILogger<QueueWorkerService> _logger;

    public QueueWorkerService(
        QueueService queueService,
        Forwarder forwarder,
        HealthCheckClient healthCheckClient,
        ModeService modeService,
        IOptions<WorkerOptions> workerOptions,
        IOptions<QueueOptions> queueOptions,
        ILogger<QueueWorkerService> logger)
    {
        _queueService = queueService;
        _forwarder = forwarder;
        _healthCheckClient = healthCheckClient;
        _modeService = modeService;
        _workerOptions = workerOptions.Value;
        _queueOptions = queueOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_workerOptions.PollIntervalSeconds);
        using var timer = new PeriodicTimer(pollInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_modeService.CurrentMode == ProxyMode.Normal)
            {
                continue;
            }

            var healthy = await _healthCheckClient.IsHealthyAsync(stoppingToken);
            if (!healthy)
            {
                _modeService.RecordHealthFailure("Health-check failed while draining queue", _logger);
                continue;
            }

            _modeService.RecordHealthSuccess();

            var messages = await _queueService.ReceiveBatchAsync(
                _workerOptions.BatchSize,
                _queueOptions.VisibilityTimeoutSeconds,
                stoppingToken);

            if (messages.Count == 0)
            {
                _modeService.MarkQueueDrainedIfStable(_logger);
                continue;
            }

            _modeService.ResetQueueDrainCounter();

            foreach (var message in messages)
            {
                var forwardResult = await _forwarder.TryForwardAsync(
                    message.Payload.Endpoint,
                    message.Payload.Payload,
                    message.Payload.ContentType,
                    message.Payload.Headers,
                    stoppingToken);

                if (forwardResult.Success)
                {
                    await _queueService.DeleteAsync(message, stoppingToken);
                    _logger.LogInformation("Forwarded queued webhook for {Endpoint}", message.Payload.Endpoint);
                    continue;
                }

                _modeService.ForceQueue(forwardResult.Error ?? "Forwarding failed during queue drain", _logger);
            }
        }
    }
}
