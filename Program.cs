using ClaudeCodeAzureGptProxy.Infrastructure;
using ClaudeCodeAzureGptProxy.Models;
using ClaudeCodeAzureGptProxy.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddAzureOpenAiConfig(builder.Configuration);
builder.Services.AddSingleton<AzureOpenAiClientFactory>();
builder.Services.AddSingleton<AzureOpenAiProxy>();

var logLevelSection = builder.Configuration.GetSection("Logging:LogLevel");
var defaultLevel = MapLogLevel(logLevelSection["Default"]) ?? LogEventLevel.Information;
var microsoftLevel = MapLogLevel(logLevelSection["Microsoft"]) ?? LogEventLevel.Warning;
var aspNetCoreLevel = MapLogLevel(logLevelSection["Microsoft.AspNetCore"]) ?? microsoftLevel;
var systemLevel = MapLogLevel(logLevelSection["System"]) ?? LogEventLevel.Warning;

var logFilePath = Path.Combine(AppContext.BaseDirectory, "logs", "proxy-.log");
var logDirectory = Path.GetDirectoryName(logFilePath);
if (!string.IsNullOrWhiteSpace(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(defaultLevel)
    .MinimumLevel.Override("Microsoft", microsoftLevel)
    .MinimumLevel.Override("Microsoft.AspNetCore", aspNetCoreLevel)
    .MinimumLevel.Override("System", systemLevel)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        logFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        shared: true)
    .CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var urls = app.Urls.Count > 0 ? string.Join(", ", app.Urls) : builder.Configuration["ASPNETCORE_URLS"];
    if (string.IsNullOrWhiteSpace(urls))
    {
        urls = "unknown";
    }
    logger.LogInformation("Listening on: {Urls}", urls);
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/v1/messages", StringComparison.OrdinalIgnoreCase))
    {
        var azureOptions = context.RequestServices.GetRequiredService<NormalizedAzureOpenAiOptions>();
        if (!string.IsNullOrWhiteSpace(azureOptions.AuthToken))
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(authHeader))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = "Missing Authorization header." });
                return;
            }

            if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = "Invalid Authorization scheme." });
                return;
            }

            var token = authHeader["Bearer ".Length..].Trim();
            if (!string.Equals(token, azureOptions.AuthToken, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden", message = "Invalid auth token." });
                return;
            }
        }
    }
    await next();
});

app.MapGet("/", () => Results.Json(new { message = "Anthropic Proxy for Azure OpenAI" }));

app.MapPost("/v1/messages", async (
    MessagesRequest request,
    AzureOpenAiProxy proxy,
    NormalizedAzureOpenAiOptions azureOptions,
    ILogger<Program> logger,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    request.OriginalModel ??= request.Model;
    request.ResolvedAzureModel = AnthropicConversion.ResolveAzureModel(request, azureOptions);

    if (request.Stream)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";

        try
        {
            var stream = proxy.StreamAsync(request, cancellationToken);
            await foreach (var sse in SseStreaming.HandleStreaming(stream, request, logger)
                               .WithCancellation(cancellationToken))
            {
                await response.WriteAsync(sse, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Streaming canceled by client.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Streaming error");
            if (!response.HasStarted)
            {
                response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }

        return Results.Empty;
    }

    try
    {
        var azureResponse = await proxy.SendAsync(request, cancellationToken);
        var anthropicResponse = AnthropicConversion.ConvertAzureToAnthropic(azureResponse, request, logger);
        return Results.Json(anthropicResponse);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Request failed");
        return Results.Problem(
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Proxy request failed");
    }
});

app.MapPost("/v1/messages/count_tokens", (
    TokenCountRequest request,
    NormalizedAzureOpenAiOptions azureOptions,
    HttpRequest httpRequest,
    ILogger<Program> logger) =>
{
    request.OriginalModel ??= request.Model;
    request.ResolvedAzureModel = AnthropicConversion.ResolveAzureModel(request, azureOptions);

    logger.LogWarning("Token counting not supported; returning fixed value.");
    return Results.Json(new TokenCountResponse { InputTokens = 1000 });
});

app.Run();

static LogEventLevel? MapLogLevel(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (value.Equals("Trace", StringComparison.OrdinalIgnoreCase))
    {
        return LogEventLevel.Verbose;
    }

    if (value.Equals("Critical", StringComparison.OrdinalIgnoreCase))
    {
        return LogEventLevel.Fatal;
    }

    return Enum.TryParse(value, true, out LogEventLevel level) ? level : null;
}
