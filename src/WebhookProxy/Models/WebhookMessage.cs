using System.Text.Json.Serialization;

namespace WebhookProxy.Models;

public sealed record WebhookMessage(
    string Endpoint,
    string ContentType,
    string Payload,
    IDictionary<string, string> Headers,
    DateTimeOffset ReceivedAt)
{
    [JsonIgnore]
    public const int MaxPayloadBytes = 2 * 1024 * 1024; // 2 MB upper limit from requirements
}
