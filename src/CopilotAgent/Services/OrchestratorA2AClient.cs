using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SKCopilotV2.CopilotAgent.Services;

/// <summary>
/// A2A client that forwards messages to the Layer 1 orchestrator using JSON-RPC 2.0 protocol.
/// Tries streaming (message/stream) first, falls back to non-streaming (message/send).
/// v1: Full aggregation — no streaming to the Copilot UI. v2 will stream tokens through.
/// </summary>
public class OrchestratorA2AClient
{
    private readonly HttpClient _httpClient;
    private readonly string _a2aPath;
    private readonly ILogger<OrchestratorA2AClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OrchestratorA2AClient(HttpClient httpClient, IConfiguration config, ILogger<OrchestratorA2AClient> logger)
    {
        _httpClient = httpClient;
        _a2aPath = config["Orchestrator:A2APath"] ?? "/a2a/orchestrator";
        _logger = logger;
    }

    /// <summary>
    /// Sends the user's topic to the orchestrator via A2A JSON-RPC and returns the blog post.
    /// Tries streaming first, falls back to non-streaming if streaming fails.
    /// </summary>
    public async Task<string> GenerateBlogAsync(string topic, CancellationToken ct = default)
    {
        return await GenerateBlogAsync(topic, null, ct);
    }

    /// <summary>
    /// Sends the user's topic to the orchestrator via A2A JSON-RPC and returns the blog post.
    /// Tries streaming first, falls back to non-streaming if streaming fails.
    /// Calls the progress callback periodically to allow the caller to send typing indicators.
    /// </summary>
    public async Task<string> GenerateBlogAsync(string topic, Func<Task>? progressCallback, CancellationToken ct = default)
    {
        var contextId = Guid.NewGuid().ToString();

        try
        {
            return await SendStreamingAsync(topic, contextId, progressCallback, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Streaming request failed, falling back to non-streaming");
            return await SendNonStreamingAsync(topic, contextId, ct);
        }
    }

    private async Task<string> SendStreamingAsync(string topic, string contextId, Func<Task>? progressCallback, CancellationToken ct)
    {
        var envelope = BuildJsonRpcEnvelope("message/stream", topic, contextId);
        var request = CreateRequest(envelope);

        _logger.LogInformation("Sending streaming JSON-RPC request to {Path}", _a2aPath);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var result = new StringBuilder();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var lastProgressTime = DateTime.UtcNow;
        const int progressIntervalSeconds = 8;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("data: "))
            {
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                var textPart = ExtractTextFromJsonRpcEvent(data);
                if (textPart != null)
                    result.Append(textPart);

                // Send progress callback every N seconds to keep the connection alive
                if (progressCallback != null)
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressTime).TotalSeconds >= progressIntervalSeconds)
                    {
                        await progressCallback();
                        lastProgressTime = now;
                    }
                }
            }
        }

        return result.ToString();
    }

    private async Task<string> SendNonStreamingAsync(string topic, string contextId, CancellationToken ct)
    {
        var envelope = BuildJsonRpcEnvelope("message/send", topic, contextId);
        var request = CreateRequest(envelope);

        _logger.LogInformation("Sending non-streaming JSON-RPC request to {Path}", _a2aPath);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        return ExtractTextFromJsonRpcResponse(body);
    }

    private object BuildJsonRpcEnvelope(string method, string topic, string contextId)
    {
        return new
        {
            jsonrpc = "2.0",
            method,
            id = Guid.NewGuid().ToString(),
            @params = new
            {
                message = new
                {
                    kind = "message",
                    messageId = Guid.NewGuid().ToString(),
                    role = "user",
                    parts = new[] { new { kind = "text", text = topic } },
                    contextId
                }
            }
        };
    }

    private HttpRequestMessage CreateRequest(object envelope)
    {
        return new HttpRequestMessage(HttpMethod.Post, _a2aPath)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(envelope, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static string? ExtractTextFromJsonRpcEvent(string jsonData)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            if (root.TryGetProperty("result", out var result))
                return ExtractTextFromResult(result);
        }
        catch (JsonException)
        {
            return jsonData;
        }

        return null;
    }

    private static string ExtractTextFromJsonRpcResponse(string jsonBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var msg = error.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
                throw new InvalidOperationException($"Orchestrator error: {msg}");
            }

            if (root.TryGetProperty("result", out var result))
            {
                var text = ExtractTextFromResult(result);
                if (text != null) return text;
            }
        }
        catch (JsonException)
        {
            // Return raw if not JSON
        }

        return jsonBody;
    }

    private static string? ExtractTextFromResult(JsonElement result)
    {
        if (result.TryGetProperty("message", out var message) &&
            message.TryGetProperty("parts", out var msgParts))
            return ConcatTextParts(msgParts);

        if (result.TryGetProperty("parts", out var parts))
            return ConcatTextParts(parts);

        if (result.TryGetProperty("text", out var text))
            return text.GetString();

        return null;
    }

    private static string? ConcatTextParts(JsonElement parts)
    {
        if (parts.ValueKind != JsonValueKind.Array) return null;

        var sb = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
