using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Options;
using WebhookProxy.Models;
using WebhookProxy.Options;

namespace WebhookProxy.Services;

public sealed class QueueService
{
    private readonly QueueClient _queueClient;
    private readonly QueueOptions _options;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private readonly ILogger<QueueService> _logger;

    public QueueService(IOptions<QueueOptions> options, ILogger<QueueService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Queue connection string is not configured (Queue:ConnectionString)");
        }

        _queueClient = new QueueClient(
            _options.ConnectionString,
            _options.QueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

        _queueClient.CreateIfNotExists();
    }

    public async Task EnqueueAsync(WebhookMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(message, _serializerOptions);
        var size = Encoding.UTF8.GetByteCount(payload);
        if (size > 60_000)
        {
            throw new InvalidOperationException("Payload exceeds Azure Queue message size limit (~64KB)");
        }

        await _queueClient.SendMessageAsync(payload, cancellationToken: cancellationToken);
        _logger.LogInformation("Enqueued webhook for {Endpoint}", message.Endpoint);
    }

    public async Task<IReadOnlyList<DequeuedMessage>> ReceiveBatchAsync(
        int batchSize,
        int visibilityTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            Response<QueueMessage[]> response = await _queueClient.ReceiveMessagesAsync(
                batchSize,
                TimeSpan.FromSeconds(visibilityTimeoutSeconds),
                cancellationToken: cancellationToken);

            var items = new List<DequeuedMessage>(response.Value.Length);
            foreach (var queueMessage in response.Value)
            {
                var webhook = JsonSerializer.Deserialize<WebhookMessage>(queueMessage.Body.ToString(), _serializerOptions);
                if (webhook is null)
                {
                    continue;
                }

                items.Add(new DequeuedMessage(webhook, queueMessage.MessageId, queueMessage.PopReceipt));
            }

            return items;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to receive messages from queue {Queue}", _options.QueueName);
            return Array.Empty<DequeuedMessage>();
        }
    }

    public Task DeleteAsync(DequeuedMessage message, CancellationToken cancellationToken) =>
        _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);

    public async Task<int> GetApproximateLengthAsync(CancellationToken cancellationToken)
    {
        var props = await _queueClient.GetPropertiesAsync(cancellationToken);
        return props.Value.ApproximateMessagesCount;
    }
}

public sealed record DequeuedMessage(WebhookMessage Payload, string MessageId, string PopReceipt);
