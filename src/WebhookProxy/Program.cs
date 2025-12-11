using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using WebhookProxy.Models;
using WebhookProxy.Options;
using WebhookProxy.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardingOptions>(builder.Configuration.GetSection("Forwarding"));
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection("Queue"));
builder.Services.Configure<ValidationOptions>(builder.Configuration.GetSection("Validation"));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection("Cors"));

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length == 0 || corsOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(corsOrigins);
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddSingleton<ModeService>();
builder.Services.AddSingleton<ValidationService>();
builder.Services.AddSingleton<QueueService>();
builder.Services.AddHttpClient<Forwarder>();
builder.Services.AddHttpClient<HealthCheckClient>();

builder.Services.AddHostedService<HealthMonitorService>();
builder.Services.AddHostedService<QueueWorkerService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapPost("/webhook/{**endpoint}", async (
    string endpoint,
    HttpContext httpContext,
    ValidationService validationService,
    ModeService modeService,
    QueueService queueService,
    Forwarder forwarder,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(endpoint))
    {
        return Results.BadRequest(new { error = "Endpoint is required" });
    }

    (string Body, string ContentType, IDictionary<string, string> Headers, IDictionary<string, string> Query) request;
    try
    {
        request = await WebhookRequestReader.ReadAsync(httpContext, cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    JsonNode? jsonPayload = null;
    var loweredContentType = request.ContentType.ToLowerInvariant();

    if (loweredContentType.Contains("json"))
    {
        try
        {
            jsonPayload = JsonNode.Parse(request.Body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid JSON payload for endpoint {Endpoint}", endpoint);
            return Results.Problem("Invalid JSON payload", statusCode: StatusCodes.Status400BadRequest);
        }

    var validationResult = await validationService.ValidateAsync(endpoint, jsonPayload!, cancellationToken);
        if (!validationResult.IsValid)
        {
            var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["validation"] = new[] { validationResult.Error ?? "Validation failed" }
            })
            {
                Status = StatusCodes.Status422UnprocessableEntity
            };

            return Results.ValidationProblem(problem.Errors, statusCode: problem.Status);
        }
    }
    else if (loweredContentType.Contains("xml"))
    {
        try
        {
            _ = XDocument.Parse(request.Body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid XML payload for endpoint {Endpoint}", endpoint);
            return Results.Problem("Invalid XML payload", statusCode: StatusCodes.Status400BadRequest);
        }
    }

    var message = new WebhookMessage(endpoint, request.ContentType, request.Body, request.Headers, request.Query, DateTimeOffset.UtcNow);

    if (modeService.CurrentMode == ProxyMode.Queue)
    {
        try
        {
            await queueService.EnqueueAsync(message, cancellationToken);
            return Results.Accepted($"/queue/{endpoint}", new { status = "queued", mode = "QUEUE" });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Payload too large to enqueue for endpoint {Endpoint}", endpoint);
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue webhook for endpoint {Endpoint}", endpoint);
            return Results.Problem("Failed to enqueue payload", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    var forwardResult = await forwarder.TryForwardAsync(endpoint, request.Body, request.ContentType, request.Headers, request.Query, cancellationToken);
    if (forwardResult.Success)
    {
        return Results.Ok(new { status = "forwarded", mode = "NORMAL" });
    }

    modeService.ForceQueue(forwardResult.Error ?? "Forwarding failed", logger);
    try
    {
        await queueService.EnqueueAsync(message, cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        logger.LogWarning(ex, "Payload too large to enqueue after forward error for endpoint {Endpoint}", endpoint);
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to enqueue webhook after forward error for endpoint {Endpoint}", endpoint);
        return Results.Problem("Failed to enqueue payload", statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Accepted($"/queue/{endpoint}", new
    {
        status = "queued",
        mode = "QUEUE",
        reason = forwardResult.Error ?? "forwarding_failed"
    });
})
.Accepts<JsonObject>("application/json", "application/xml")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status413PayloadTooLarge)
.Produces(StatusCodes.Status422UnprocessableEntity)
.Produces(StatusCodes.Status500InternalServerError)
.WithOpenApi(o =>
{
    o.Summary = "Receive webhook, validate, forward or enqueue";
    o.Description = "Accepts arbitrary JSON/XML payloads. In NORMAL mode forwards to n8n; on failure or QUEUE mode enqueues message.";
    return o;
});

app.MapPut("/validations/{endpoint}", async (
    string endpoint,
    HttpContext httpContext,
    ValidationService validationService,
    CancellationToken cancellationToken) =>
{
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(body))
    {
        return Results.BadRequest(new { error = "Schema body is required" });
    }

    await validationService.SaveSchemaAsync(endpoint, body, cancellationToken);
    return Results.Ok(new { status = "validation_updated", endpoint });
});

app.MapGet("/status", async (
    QueueService queueService,
    ModeService modeService,
    HealthCheckClient healthCheckClient,
    CancellationToken cancellationToken) =>
{
    var queueLength = await queueService.GetApproximateLengthAsync(cancellationToken);
    var healthOk = await healthCheckClient.IsHealthyAsync(cancellationToken);

    return Results.Ok(new
    {
        mode = modeService.CurrentMode.ToString().ToUpperInvariant(),
        queue_length = queueLength,
        last_error = modeService.LastError,
        health = healthOk ? "ok" : "error"
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
