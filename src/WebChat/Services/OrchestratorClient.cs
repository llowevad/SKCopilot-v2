using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SKCopilotV2.WebChat.Services;

/// <summary>
/// HTTP client that calls the orchestrator's A2A endpoint using JSON-RPC protocol.
/// Supports both streaming (message/stream) and non-streaming (message/send) methods.
/// </summary>
public class OrchestratorClient
{
    private readonly HttpClient _httpClient;
    private readonly string _a2aPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OrchestratorClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _a2aPath = config["Orchestrator:A2APath"] ?? "/a2a/orchestrator";
    }

    /// <summary>
    /// Sends a topic to the orchestrator via A2A JSON-RPC and returns the blog post.
    /// Tries streaming first, falls back to non-streaming if streaming fails.
    /// </summary>
    public async Task<string> GenerateBlogAsync(string topic, CancellationToken ct = default)
    {
        var contextId = Guid.NewGuid().ToString();

        // Try streaming first
        try
        {
            return await SendStreamingAsync(topic, contextId, ct);
        }
        catch (HttpRequestException)
        {
            // Fall back to non-streaming
            return await SendNonStreamingAsync(topic, contextId, ct);
        }
    }

    private async Task<string> SendStreamingAsync(string topic, string contextId, CancellationToken ct)
    {
        var envelope = BuildJsonRpcEnvelope("message/stream", topic, contextId);
        var request = CreateRequest(envelope);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Parse SSE stream containing JSON-RPC result events
        var result = new StringBuilder();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("data: "))
            {
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                // Each SSE data line is a JSON-RPC response object
                var textPart = ExtractTextFromJsonRpcEvent(data);
                if (textPart != null)
                    result.Append(textPart);
            }
        }

        return result.ToString();
    }

    private async Task<string> SendNonStreamingAsync(string topic, string contextId, CancellationToken ct)
    {
        var envelope = BuildJsonRpcEnvelope("message/send", topic, contextId);
        var request = CreateRequest(envelope);

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

    /// <summary>
    /// Extracts text content from a JSON-RPC SSE event data line.
    /// Events may contain result objects with message parts.
    /// </summary>
    private static string? ExtractTextFromJsonRpcEvent(string jsonData)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            // Check for result.parts[].text or result.message.parts[].text
            if (root.TryGetProperty("result", out var result))
            {
                return ExtractTextFromResult(result);
            }
        }
        catch (JsonException)
        {
            // Not JSON — might be raw text chunk
            return jsonData;
        }

        return null;
    }

    /// <summary>
    /// Extracts text from a complete JSON-RPC response (non-streaming).
    /// </summary>
    private static string ExtractTextFromJsonRpcResponse(string jsonBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;

            // Check for error
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
        // Try result.message.parts[].text (A2A task result format)
        if (result.TryGetProperty("message", out var message) &&
            message.TryGetProperty("parts", out var msgParts))
        {
            return ConcatTextParts(msgParts);
        }

        // Try result.parts[].text directly
        if (result.TryGetProperty("parts", out var parts))
        {
            return ConcatTextParts(parts);
        }

        // Try result.text directly
        if (result.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

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
