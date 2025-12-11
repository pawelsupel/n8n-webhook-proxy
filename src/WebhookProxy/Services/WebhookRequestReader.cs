using System.Text;
using Microsoft.AspNetCore.Http;
using WebhookProxy.Models;

namespace WebhookProxy.Services;

public static class WebhookRequestReader
{
    public static async Task<(string Body, string ContentType, IDictionary<string, string> Headers, IDictionary<string, string> Query)> ReadAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Request.EnableBuffering();

        if (context.Request.ContentLength.HasValue &&
            context.Request.ContentLength.Value > WebhookMessage.MaxPayloadBytes)
        {
            throw new InvalidOperationException("Payload exceeds 2MB limit");
        }

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (Encoding.UTF8.GetByteCount(body) > WebhookMessage.MaxPayloadBytes)
        {
            throw new InvalidOperationException("Payload exceeds 2MB limit");
        }

        var contentType = string.IsNullOrWhiteSpace(context.Request.ContentType)
            ? "application/json"
            : context.Request.ContentType!;

        var headers = context.Request.Headers
            .Where(h => !string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(h.Key, "Host", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToString());

        var query = context.Request.Query.ToDictionary(
            q => q.Key,
            q => q.Value.ToString());

        return (body, contentType, headers, query);
    }
}
