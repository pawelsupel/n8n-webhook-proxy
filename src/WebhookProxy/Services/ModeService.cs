using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookProxy.Models;
using WebhookProxy.Options;

namespace WebhookProxy.Services;

public sealed class ModeService
{
    private readonly WorkerOptions _options;
    private readonly object _gate = new();
    private ProxyMode _mode = ProxyMode.Normal;
    private int _healthFailures;
    private int _healthSuccesses;
    private int _queueEmptySuccesses;

    public ModeService(IOptions<WorkerOptions> options)
    {
        _options = options.Value;
    }

    public ProxyMode CurrentMode
    {
        get
        {
            lock (_gate)
            {
                return _mode;
            }
        }
    }

    public string? LastError { get; private set; }

    public void ForceQueue(string reason, ILogger? logger = null)
    {
        lock (_gate)
        {
            _mode = ProxyMode.Queue;
            LastError = reason;
            _healthFailures = 0;
            _healthSuccesses = 0;
            _queueEmptySuccesses = 0;
        }

        logger?.LogWarning("Switching to QUEUE mode: {Reason}", reason);
    }

    public void RecordHealthFailure(string reason, ILogger? logger = null)
    {
        lock (_gate)
        {
            _healthFailures++;
            _healthSuccesses = 0;
            _queueEmptySuccesses = 0;
            LastError = reason;

            if (_healthFailures >= _options.HealthFailureThreshold)
            {
                _mode = ProxyMode.Queue;
                logger?.LogWarning("Health failure threshold reached ({Failures}); switching to QUEUE mode", _healthFailures);
            }
        }
    }

    public void RecordHealthSuccess()
    {
        lock (_gate)
        {
            _healthSuccesses++;
            _healthFailures = 0;
        }
    }

    public void ResetQueueDrainCounter()
    {
        lock (_gate)
        {
            _queueEmptySuccesses = 0;
        }
    }

    public void MarkQueueDrainedIfStable(ILogger? logger = null)
    {
        lock (_gate)
        {
            if (_mode != ProxyMode.Queue)
            {
                return;
            }

            _queueEmptySuccesses++;

            if (_healthSuccesses >= _options.HealthSuccessThreshold &&
                _queueEmptySuccesses >= _options.HealthSuccessThreshold)
            {
                _mode = ProxyMode.Normal;
                LastError = null;
                logger?.LogInformation("Health stabilized and queue drained; switching to NORMAL mode");
            }
        }
    }
}
