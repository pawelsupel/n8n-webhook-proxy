using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookProxy.Options;
using WebhookProxy.Services;

namespace WebhookProxy.Tests;

public class ValidationServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ValidationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"validations-{Guid.NewGuid()}");
    }

    [Fact]
    public async Task PermissiveMode_AllowsMissingSchema()
    {
        var service = CreateService(mode: "permissive");

        var result = await service.ValidateAsync("orders", JsonNode.Parse("""{"id":1}""")!, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task StrictMode_WithoutSchema_Fails()
    {
        var service = CreateService(mode: "strict");

        var result = await service.ValidateAsync("orders", JsonNode.Parse("""{"id":1}""")!, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_UsesSchema_SuccessAndFailure()
    {
        var service = CreateService(mode: "strict");
        var schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "id": { "type": "integer" }
          },
          "required": ["id"]
        }
        """;

        await service.SaveSchemaAsync("orders", schema, CancellationToken.None);

        var ok = await service.ValidateAsync("orders", JsonNode.Parse("""{"id":123}""")!, CancellationToken.None);
        var bad = await service.ValidateAsync("orders", JsonNode.Parse("""{"id":"abc"}""")!, CancellationToken.None);

        Assert.True(ok.IsValid);
        Assert.False(bad.IsValid);
    }

    private ValidationService CreateService(string mode)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ValidationOptions
        {
            BasePath = _tempDir,
            Mode = mode
        });
        return new ValidationService(options, NullLogger<ValidationService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
