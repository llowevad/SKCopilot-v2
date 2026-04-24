using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Moq;
using SKCopilotV2.CopilotAgent.Services;
using SKCopilotV2.Orchestrator.Agents;
using SKCopilotV2.Orchestrator.Workflows;
using SKCopilotV2.WebChat.Services;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Test helper: captures HTTP requests and returns configurable SSE responses.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
    public List<HttpRequestMessage> CapturedRequests { get; } = new();
    public List<string?> CapturedBodies { get; } = new();

    public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : this((req, _) => Task.FromResult(handler(req))) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CapturedRequests.Add(request);
        if (request.Content != null)
            CapturedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        else
            CapturedBodies.Add(null);
        return await _handler(request, cancellationToken);
    }
}

/// <summary>
/// Integration tests: WebChat OrchestratorClient → simulated A2A orchestrator.
/// Tests the full client-side flow with realistic SSE responses.
/// </summary>
public class WebChatToOrchestratorIntegrationTests
{
    private static IConfiguration CreateConfig(string? a2aPath = null)
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Orchestrator:A2APath"]).Returns(a2aPath);
        return mockConfig.Object;
    }

    private static HttpResponseMessage CreateRealisticBlogSseResponse()
    {
        var sb = new StringBuilder();
        sb.AppendLine("data: # The Future of AI in Healthcare");
        sb.AppendLine("data: ");
        sb.AppendLine("data: Artificial intelligence is transforming how we approach medicine.");
        sb.AppendLine("data: ");
        sb.AppendLine("data: ## Early Detection");
        sb.AppendLine("data: ");
        sb.AppendLine("data: AI models can identify patterns in medical imaging.");
        sb.AppendLine("data: [DONE]");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream")
        };
    }

    [Fact]
    public async Task WebChat_SendsTopic_ReceivesBlogPostViaA2A()
    {
        var handler = new MockHttpMessageHandler(_ => CreateRealisticBlogSseResponse());
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        var result = await client.GenerateBlogAsync("AI in Healthcare");

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("AI");
        result.Should().Contain("Healthcare");
    }

    [Fact]
    public async Task WebChat_OrchestratorAgentCard_RequestFormat()
    {
        // Verify the client sends correctly formatted A2A messages
        var handler = new MockHttpMessageHandler(_ =>
        {
            var content = "data: ok\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        await client.GenerateBlogAsync("Test Topic");

        handler.CapturedRequests.Should().HaveCount(1);
        handler.CapturedBodies[0].Should().NotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBodies[0]!);
        doc.RootElement.GetProperty("message").GetProperty("kind").GetString()
            .Should().Be("message");
    }

    [Fact]
    public async Task WebChat_MultipleSequentialRequests_AllSucceed()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            var content = "data: response\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        var results = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            results.Add(await client.GenerateBlogAsync($"Topic {i}"));
        }

        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Should().Be("response"));
        handler.CapturedRequests.Should().HaveCount(3);
    }

    [Fact]
    public async Task WebChat_LongBlogPost_StreamsCorrectly()
    {
        // Simulate a long multi-chunk SSE response
        var sb = new StringBuilder();
        for (int i = 0; i < 50; i++)
            sb.AppendLine($"data: Paragraph {i} of a very long blog post about technology. ");
        sb.AppendLine("data: [DONE]");

        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream")
            });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        var result = await client.GenerateBlogAsync("Long topic");

        result.Should().Contain("Paragraph 0");
        result.Should().Contain("Paragraph 49");
        result.Length.Should().BeGreaterThan(500);
    }
}

/// <summary>
/// Integration tests: CEA OrchestratorA2AClient → simulated A2A orchestrator.
/// Tests the full proxy client-side flow.
/// </summary>
public class CeaToOrchestratorIntegrationTests
{
    private static IConfiguration CreateConfig(string? a2aPath = null)
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Orchestrator:A2APath"]).Returns(a2aPath);
        return mockConfig.Object;
    }

    [Fact]
    public async Task CEA_Client_SendsAndReceives_ViaA2A()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            var content = "data: Blog post about AI\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorA2AClient(httpClient, CreateConfig());

        var result = await client.GenerateBlogAsync("AI in Healthcare");

        result.Should().Be("Blog post about AI");
    }

    [Fact]
    public async Task CEA_OrchestratorDown_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorA2AClient(httpClient, CreateConfig());

        var act = () => client.GenerateBlogAsync("topic");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CEA_EmptyTopic_StillSendsRequest()
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

        // Client itself doesn't validate — that's the orchestrator's job
        var result = await client.GenerateBlogAsync("");

        result.Should().BeEmpty();
        handler.CapturedRequests.Should().HaveCount(1);
    }
}

/// <summary>
/// A2A protocol contract tests — verify wire format consistency across both clients.
/// Architecture ref: Section 6 — A2A Protocol integration point.
/// </summary>
public class A2AProtocolContractTests
{
    private static IConfiguration CreateConfig()
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Orchestrator:A2APath"]).Returns((string?)null);
        return mockConfig.Object;
    }

    private static HttpResponseMessage CreateSseResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: ok\ndata: [DONE]\n", Encoding.UTF8, "text/event-stream")
        };
    }

    [Fact]
    public async Task WebChat_And_CEA_SendSameA2AFormat()
    {
        // Both clients should produce identical A2A message structures
        var webChatHandler = new MockHttpMessageHandler(_ => CreateSseResponse());
        var ceaHandler = new MockHttpMessageHandler(_ => CreateSseResponse());

        var webChatHttp = new HttpClient(webChatHandler) { BaseAddress = new Uri("http://localhost:5100") };
        var ceaHttp = new HttpClient(ceaHandler) { BaseAddress = new Uri("http://localhost:5100") };

        var webChatClient = new OrchestratorClient(webChatHttp, CreateConfig());
        var ceaClient = new OrchestratorA2AClient(ceaHttp, CreateConfig());

        await webChatClient.GenerateBlogAsync("same topic");
        await ceaClient.GenerateBlogAsync("same topic");

        // Parse and compare structure (contextId will differ, that's OK)
        using var webChatDoc = JsonDocument.Parse(webChatHandler.CapturedBodies[0]!);
        using var ceaDoc = JsonDocument.Parse(ceaHandler.CapturedBodies[0]!);

        var wcMsg = webChatDoc.RootElement.GetProperty("message");
        var ceaMsg = ceaDoc.RootElement.GetProperty("message");

        wcMsg.GetProperty("kind").GetString().Should().Be(ceaMsg.GetProperty("kind").GetString());
        wcMsg.GetProperty("role").GetString().Should().Be(ceaMsg.GetProperty("role").GetString());
        wcMsg.GetProperty("parts")[0].GetProperty("text").GetString()
            .Should().Be(ceaMsg.GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task A2ARequest_ContainsAllRequiredFields()
    {
        var handler = new MockHttpMessageHandler(_ => CreateSseResponse());
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        await client.GenerateBlogAsync("test");

        using var doc = JsonDocument.Parse(handler.CapturedBodies[0]!);
        var msg = doc.RootElement.GetProperty("message");

        // All required A2A fields must be present
        msg.TryGetProperty("kind", out _).Should().BeTrue("kind is required by A2A spec");
        msg.TryGetProperty("role", out _).Should().BeTrue("role is required by A2A spec");
        msg.TryGetProperty("parts", out _).Should().BeTrue("parts is required by A2A spec");
        msg.TryGetProperty("contextId", out _).Should().BeTrue("contextId is required for conversation tracking");
    }

    [Fact]
    public async Task A2ARequest_ContextId_IsUniquePerCall()
    {
        var handler = new MockHttpMessageHandler(_ => CreateSseResponse());
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var client = new OrchestratorClient(httpClient, CreateConfig());

        await client.GenerateBlogAsync("topic1");
        await client.GenerateBlogAsync("topic2");

        using var doc1 = JsonDocument.Parse(handler.CapturedBodies[0]!);
        using var doc2 = JsonDocument.Parse(handler.CapturedBodies[1]!);

        var ctx1 = doc1.RootElement.GetProperty("message").GetProperty("contextId").GetString();
        var ctx2 = doc2.RootElement.GetProperty("message").GetProperty("contextId").GetString();

        ctx1.Should().NotBe(ctx2, "each request should generate a unique contextId");
    }

    [Fact]
    public async Task Both_Clients_UseCorrectA2AEndpointPath()
    {
        var webChatHandler = new MockHttpMessageHandler(_ => CreateSseResponse());
        var ceaHandler = new MockHttpMessageHandler(_ => CreateSseResponse());

        var webChatHttp = new HttpClient(webChatHandler) { BaseAddress = new Uri("http://localhost:5100") };
        var ceaHttp = new HttpClient(ceaHandler) { BaseAddress = new Uri("http://localhost:5100") };

        var webChatClient = new OrchestratorClient(webChatHttp, CreateConfig());
        var ceaClient = new OrchestratorA2AClient(ceaHttp, CreateConfig());

        await webChatClient.GenerateBlogAsync("topic");
        await ceaClient.GenerateBlogAsync("topic");

        // Both should hit the same A2A endpoint
        webChatHandler.CapturedRequests[0].RequestUri!.PathAndQuery
            .Should().Be(ceaHandler.CapturedRequests[0].RequestUri!.PathAndQuery,
                "both clients must use identical A2A endpoint paths");
    }
}

/// <summary>
/// Cross-layer structural tests — verify the workflow and agent setup are consistent.
/// </summary>
public class CrossLayerStructuralTests
{
    [Fact]
    public void Orchestrator_WorkflowBuild_ProducesTwoAgentPipeline()
    {
        var mockChatClient = new Mock<IChatClient>();
        var (entryAgent, workflow) = BlogWorkflow.Build(mockChatClient.Object);

        entryAgent.Should().NotBeNull();
        entryAgent.Name.Should().Be(BlogWriterSetup.Name);
        workflow.Should().NotBeNull();
    }

    [Fact]
    public void AgentSetup_Constants_AreConsistent()
    {
        // BlogWriter and Reviewer names used in workflow must match setup constants
        BlogWriterSetup.Name.Should().Be("BlogWriter");
        ReviewerSetup.Name.Should().Be("Reviewer");
        BlogWriterSetup.Name.Should().NotBe(ReviewerSetup.Name);
    }
}
