using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookProxy.Models;
using WebhookProxy.Options;
using WebhookProxy.Services;

namespace WebhookProxy.Tests;

public class ModeServiceTests
{
    [Fact]
    public void ForceQueue_SetsQueueModeAndReason()
    {
        var service = CreateService();

        service.ForceQueue("test", NullLogger.Instance);

        Assert.Equal(ProxyMode.Queue, service.CurrentMode);
        Assert.Equal("test", service.LastError);
    }

    [Fact]
    public void RecordHealthFailure_ReachesThreshold_SwitchesToQueue()
    {
        var service = CreateService(failureThreshold: 2);

        service.RecordHealthFailure("fail-1");
        Assert.Equal(ProxyMode.Normal, service.CurrentMode);

        service.RecordHealthFailure("fail-2", NullLogger.Instance);
        Assert.Equal(ProxyMode.Queue, service.CurrentMode);
        Assert.Equal("fail-2", service.LastError);
    }

    [Fact]
    public void MarkQueueDrainedIfStable_AfterHealthSuccesses_ReturnsToNormal()
    {
        var service = CreateService(successThreshold: 2);
        service.ForceQueue("force");

        service.RecordHealthSuccess();
        service.RecordHealthSuccess();
        service.MarkQueueDrainedIfStable();
        service.MarkQueueDrainedIfStable(NullLogger.Instance);

        Assert.Equal(ProxyMode.Normal, service.CurrentMode);
        Assert.Null(service.LastError);
    }

    private static ModeService CreateService(int failureThreshold = 3, int successThreshold = 3)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WorkerOptions
        {
            HealthFailureThreshold = failureThreshold,
            HealthSuccessThreshold = successThreshold
        });
        return new ModeService(options);
    }
}
