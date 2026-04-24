using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using SKCopilotV2.WebChat.Services;
using Xunit;

namespace WebChat.Tests;

/// <summary>
/// Test helper: captures HTTP requests and returns configurable SSE responses.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
    public HttpRequestMessage? CapturedRequest { get; private set; }
    public string? CapturedRequestBody { get; private set; }

    public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : this((req, _) => Task.FromResult(handler(req))) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CapturedRequest = request;
        if (request.Content != null)
            CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return await _handler(request, cancellationToken);
    }
}

/// <summary>
/// Layer 2 tests: OrchestratorClient — A2A HTTP client for calling Layer 1.
/// Architecture ref: Section 4.4 — OrchestratorClient.cs
/// </summary>
public class OrchestratorClientTests
{
    private static IConfiguration CreateConfig(string? a2aPath = null)
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Orchestrator:A2APath"]).Returns(a2aPath);
        return mockConfig.Object;
    }

    private static HttpResponseMessage CreateSseResponse(params string[] dataLines)
    {
        var sb = new StringBuilder();
        foreach (var line in dataLines)
            sb.AppendLine($"data: {line}");
        sb.AppendLine("data: [DONE]");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream")
        };
    }

    [Fact]
    public async Task GenerateBlogAsync_CallsCorrectA2AEndpoint()
    {
        var handler = new MockHttpMessageHandler(_ =>
            CreateSseResponse("test content"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        await client.GenerateBlogAsync("AI in Healthcare");

        handler.CapturedRequest.Should().NotBeNull();
        handler.CapturedRequest!.RequestUri!.PathAndQuery
            .Should().Be("/a2a/orchestrator/v1/message:stream");
        handler.CapturedRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task GenerateBlogAsync_SendsValidA2AMessageFormat()
    {
        var handler = new MockHttpMessageHandler(_ =>
            CreateSseResponse("ok"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        await client.GenerateBlogAsync("test topic");

        handler.CapturedRequestBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedRequestBody!);
        var msg = doc.RootElement.GetProperty("message");
        msg.GetProperty("kind").GetString().Should().Be("message");
        msg.GetProperty("role").GetString().Should().Be("user");
        msg.GetProperty("parts").GetArrayLength().Should().Be(1);
        msg.GetProperty("parts")[0].GetProperty("kind").GetString().Should().Be("text");
        msg.GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("test topic");
        msg.GetProperty("contextId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateBlogAsync_ParsesSSEStream_ReturnsAggregatedContent()
    {
        var handler = new MockHttpMessageHandler(_ =>
            CreateSseResponse("Hello ", "World", "!"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        var result = await client.GenerateBlogAsync("any topic");

        result.Should().Be("Hello World!");
    }

    [Fact]
    public async Task GenerateBlogAsync_HttpError_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        var act = () => client.GenerateBlogAsync("any topic");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GenerateBlogAsync_RespectsConfiguredA2APath()
    {
        var handler = new MockHttpMessageHandler(_ =>
            CreateSseResponse("ok"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig("/custom/path"));

        await client.GenerateBlogAsync("topic");

        handler.CapturedRequest!.RequestUri!.PathAndQuery
            .Should().Be("/custom/path/v1/message:stream");
    }

    [Fact]
    public async Task GenerateBlogAsync_DefaultA2APath_IsOrchestratorPath()
    {
        var handler = new MockHttpMessageHandler(_ =>
            CreateSseResponse("ok"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig(null));

        await client.GenerateBlogAsync("topic");

        handler.CapturedRequest!.RequestUri!.PathAndQuery
            .Should().Contain("/a2a/orchestrator/");
    }

    [Fact]
    public async Task GenerateBlogAsync_SupportsCancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new MockHttpMessageHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CreateSseResponse("should not reach"));
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        var act = () => client.GenerateBlogAsync("topic", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateBlogAsync_EmptySSEStream_ReturnsEmptyString()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            var content = "data: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        var result = await client.GenerateBlogAsync("topic");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateBlogAsync_SSEWithBlankLines_IgnoresNonDataLines()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            var content = "\ndata: hello\n\ndata: world\n\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        var result = await client.GenerateBlogAsync("topic");

        result.Should().Be("helloworld");
    }

    [Fact]
    public async Task GenerateBlogAsync_RequestContentType_IsApplicationJson()
    {
        var handler = new MockHttpMessageHandler(_ =>
            CreateSseResponse("ok"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        await client.GenerateBlogAsync("topic");

        handler.CapturedRequest!.Content!.Headers.ContentType!.MediaType
            .Should().Be("application/json");
    }
}
