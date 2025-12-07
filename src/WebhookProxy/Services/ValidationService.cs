using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using NJsonSchema;
using WebhookProxy.Options;

namespace WebhookProxy.Services;

public sealed class ValidationService
{
    private readonly ValidationOptions _options;
    private readonly ILogger<ValidationService> _logger;
    private readonly ConcurrentDictionary<string, (JsonSchema Schema, DateTime LastWrite)> _cache = new();

    public ValidationService(IOptions<ValidationOptions> options, ILogger<ValidationService> logger)
    {
        _options = options.Value;
        _logger = logger;

        Directory.CreateDirectory(GetValidationDirectory());
    }

    public async Task<ValidationResult> ValidateAsync(string endpoint, JsonNode payload, CancellationToken cancellationToken)
    {
        var schema = await LoadSchemaAsync(endpoint, cancellationToken);

        if (schema is null)
        {
            if (string.Equals(_options.Mode, "strict", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Failure("Validation schema not found for endpoint");
            }

            return ValidationResult.Success();
        }

        var errors = schema.Validate(payload.ToJsonString());
        if (errors.Count == 0)
        {
            return ValidationResult.Success();
        }

        var reason = string.Join("; ", errors.Select(e => e.ToString()));
        return ValidationResult.Failure(reason);
    }

    public async Task SaveSchemaAsync(string endpoint, string rawSchema, CancellationToken cancellationToken)
    {
        var directory = GetValidationDirectory();
        Directory.CreateDirectory(directory);

        var targetPath = Path.Combine(directory, $"{endpoint}.json");
        await File.WriteAllTextAsync(targetPath, rawSchema, cancellationToken);
        _cache.TryRemove(targetPath, out _);

        _logger.LogInformation("Validation schema updated for endpoint {Endpoint}", endpoint);
    }

    private async Task<JsonSchema?> LoadSchemaAsync(string endpoint, CancellationToken cancellationToken)
    {
        var directory = GetValidationDirectory();
        var endpointPath = Path.Combine(directory, $"{endpoint}.json");
        var defaultPath = Path.Combine(directory, "default.json");

        string? targetPath = File.Exists(endpointPath)
            ? endpointPath
            : File.Exists(defaultPath) ? defaultPath : null;

        if (targetPath is null)
        {
            return null;
        }

        var lastWrite = File.GetLastWriteTimeUtc(targetPath);
        if (_cache.TryGetValue(targetPath, out var cached) && cached.LastWrite >= lastWrite)
        {
            return cached.Schema;
        }

        var rawSchema = await File.ReadAllTextAsync(targetPath, cancellationToken);
        var schema = await JsonSchema.FromJsonAsync(rawSchema, cancellationToken: cancellationToken);
        _cache[targetPath] = (schema, lastWrite);
        return schema;
    }

    private string GetValidationDirectory()
    {
        return Path.IsPathRooted(_options.BasePath)
            ? _options.BasePath
            : Path.Combine(AppContext.BaseDirectory, _options.BasePath);
    }
}

public sealed record ValidationResult(bool IsValid, string? Error)
{
    public static ValidationResult Success() => new(true, null);
    public static ValidationResult Failure(string error) => new(false, error);
}
