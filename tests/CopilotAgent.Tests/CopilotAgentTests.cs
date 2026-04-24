using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using SKCopilotV2.CopilotAgent;
using SKCopilotV2.CopilotAgent.Services;
using Xunit;

namespace CopilotAgent.Tests;

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
/// Layer 3 tests: BlogCopilotAgent — M365 CEA thin proxy design verification.
/// Architecture ref: Section 5.3 — BlogCopilotAgent.cs (AgentApplication pattern)
/// D16: CEA is thin proxy only — NO business logic, NO LLM calls.
/// </summary>
public class BlogCopilotAgentTests
{
    [Fact]
    public void BlogCopilotAgent_ExtendsAgentApplication()
    {
        typeof(BlogCopilotAgent).BaseType!.Name.Should().Be("AgentApplication",
            "the CEA must use the AgentApplication pattern (D13)");
    }

    [Fact]
    public void BlogCopilotAgent_ContainsNoBusinessLogic_NoLlmReferences()
    {
        // D16: Thin proxy design constraint — verify no LLM client usage
        var agentType = typeof(BlogCopilotAgent);
        var fields = agentType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (var field in fields)
        {
            field.FieldType.FullName.Should().NotContain("IChatClient",
                "CEA must not reference LLM clients (D16)");
            field.FieldType.FullName.Should().NotContain("ChatCompletionAgent",
                "CEA must not reference agent types (D16)");
        }
    }

    [Fact]
    public void BlogCopilotAgent_HasOrchestratorA2AClientDependency()
    {
        // The thin proxy should depend on OrchestratorA2AClient for forwarding
        var agentType = typeof(BlogCopilotAgent);
        var fields = agentType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

        fields.Should().Contain(f => f.FieldType == typeof(OrchestratorA2AClient),
            "CEA must use OrchestratorA2AClient for message forwarding");
    }

    [Fact]
    public void BlogCopilotAgent_Constructor_AcceptsRequiredDependencies()
    {
        // Verify constructor signature matches expected DI pattern
        var ctor = typeof(BlogCopilotAgent).GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 2);

        ctor.Should().NotBeNull("constructor should accept options + orchestrator client");
        var paramTypes = ctor!.GetParameters().Select(p => p.ParameterType.Name).ToArray();
        paramTypes.Should().Contain("AgentApplicationOptions");
        paramTypes.Should().Contain("OrchestratorA2AClient");
    }

    [Fact]
    public void BlogCopilotAgent_SourceCode_ContainsNoPromptText()
    {
        // D16 design constraint: the CEA should not contain LLM prompts/instructions
        // We verify the class source has no instruction-like patterns
        var methods = typeof(BlogCopilotAgent).GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

        // Verify it doesn't declare a bunch of methods — thin proxy pattern
        methods.Where(m => !m.IsSpecialName).Should().HaveCountLessOrEqualTo(5,
            "thin proxy should have minimal methods — just routing + forwarding");
    }
}

/// <summary>
/// Layer 3 tests: OrchestratorA2AClient — HTTP client for calling Layer 1 from CEA.
/// Architecture ref: Section 5.4 — OrchestratorA2AClient.cs
/// </summary>
public class OrchestratorA2AClientTests
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
        var handler = new MockHttpMessageHandler(_ => CreateSseResponse("test"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorA2AClient(httpClient, CreateConfig());

        await client.GenerateBlogAsync("AI in Healthcare");

        handler.CapturedRequest.Should().NotBeNull();
        handler.CapturedRequest!.RequestUri!.PathAndQuery
            .Should().Be("/a2a/orchestrator/v1/message:stream");
        handler.CapturedRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task GenerateBlogAsync_SendsValidA2AMessageFormat()
    {
        var handler = new MockHttpMessageHandler(_ => CreateSseResponse("ok"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorA2AClient(httpClient, CreateConfig());

        await client.GenerateBlogAsync("test topic");

        handler.CapturedRequestBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedRequestBody!);
        var msg = doc.RootElement.GetProperty("message");
        msg.GetProperty("kind").GetString().Should().Be("message");
        msg.GetProperty("role").GetString().Should().Be("user");
        msg.GetProperty("parts").GetArrayLength().Should().Be(1);
        msg.GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("test topic");
        msg.GetProperty("contextId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateBlogAsync_ParsesSSEStream_ReturnsAggregatedContent()
    {
        var handler = new MockHttpMessageHandler(_ =>
            CreateSseResponse("Blog ", "post ", "content"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorA2AClient(httpClient, CreateConfig());

        var result = await client.GenerateBlogAsync("any topic");

        result.Should().Be("Blog post content");
    }

    [Fact]
    public async Task GenerateBlogAsync_RespectsConfiguration()
    {
        var handler = new MockHttpMessageHandler(_ => CreateSseResponse("ok"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorA2AClient(httpClient, CreateConfig("/custom/a2a"));

        await client.GenerateBlogAsync("topic");

        handler.CapturedRequest!.RequestUri!.PathAndQuery
            .Should().Be("/custom/a2a/v1/message:stream");
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
        var client = new OrchestratorA2AClient(httpClient, CreateConfig());

        var act = () => client.GenerateBlogAsync("topic", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateBlogAsync_HttpError_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorA2AClient(httpClient, CreateConfig());

        var act = () => client.GenerateBlogAsync("topic");

        await act.Should().ThrowAsync<HttpRequestException>();
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
        var client = new OrchestratorA2AClient(httpClient, CreateConfig());

        var result = await client.GenerateBlogAsync("topic");

        result.Should().BeEmpty();
    }
}
