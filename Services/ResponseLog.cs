using System.Text.Encodings.Web;
using System.Text.Json;
using ClaudeAzureGptProxy.Models;

namespace ClaudeAzureGptProxy.Services;

public sealed class ResponseLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ILogger<ResponseLog> _logger;

    public ResponseLog(ILogger<ResponseLog> logger)
    {
        _logger = logger;
    }

    private static string ToJson(object? value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string ToSingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return value.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    public void LogRequest(MessagesRequest request, bool isStream)
    {
        try
        {
            _logger.LogDebug(
                "event_type=anthropic_request route={Route} is_stream={IsStream} anthropic_model={AnthropicModel} azure_model={AzureModel} request_json={RequestJson}",
                "/v1/messages",
                isStream,
                request.OriginalModel ?? request.Model,
                request.ResolvedAzureModel,
                ToJson(request));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log request");
        }
    }

    public void LogAzureResponse(object azureResponse)
    {
        try
        {
            _logger.LogDebug(
                "event_type=azure_response route={Route} azure_response_json={AzureResponseJson}",
                "/v1/messages",
                ToJson(azureResponse));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log azure response");
        }
    }

    public void LogAzureStreamChunk(int chunkIndex, Dictionary<string, object?> chunk)
    {
        try
        {
            _logger.LogDebug(
                "event_type=azure_stream_chunk route={Route} chunk_index={ChunkIndex} azure_chunk_json={AzureChunkJson}",
                "/v1/messages",
                chunkIndex,
                ToJson(chunk));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log azure stream chunk chunk_index={ChunkIndex}", chunkIndex);
        }
    }

    public void LogAnthropicResponse(MessagesResponse response)
    {
        try
        {
            _logger.LogDebug(
                "event_type=anthropic_response route={Route} anthropic_response_json={AnthropicResponseJson}",
                "/v1/messages",
                ToJson(response));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log anthropic response");
        }
    }

    public void LogAnthropicSseEvent(int eventIndex, string sseEventRaw)
    {
        try
        {
            _logger.LogDebug(
                "event_type=anthropic_sse_event route={Route} sse_event_index={EventIndex} sse_event_raw_single_line={SseEventRawSingleLine}",
                "/v1/messages",
                eventIndex,
                ToSingleLine(sseEventRaw));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log sse event event_index={EventIndex}", eventIndex);
        }
    }

    public void LogAnthropicAggregatedResponse(MessagesResponse response)
    {
        try
        {
            _logger.LogDebug(
                "event_type=anthropic_response_aggregated route={Route} anthropic_response_aggregated_json={AnthropicResponseAggregatedJson}",
                "/v1/messages",
                ToJson(response));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log aggregated anthropic response");
        }
    }
}
