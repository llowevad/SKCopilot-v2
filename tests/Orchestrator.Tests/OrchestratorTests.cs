using System.Reflection;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Moq;
using SKCopilotV2.Orchestrator.Agents;
using SKCopilotV2.Orchestrator.Workflows;
using Xunit;

namespace Orchestrator.Tests;

/// <summary>
/// Layer 1 tests: BlogWriterSetup — agent creation and configuration.
/// Architecture ref: Section 3.3 — BlogWriter agent
/// </summary>
public class BlogWriterSetupTests
{
    private readonly Mock<IChatClient> _mockChatClient = new();

    [Fact]
    public void Create_ReturnsNonNullAgent()
    {
        var agent = BlogWriterSetup.Create(_mockChatClient.Object);
        agent.Should().NotBeNull();
    }

    [Fact]
    public void Create_AgentHasCorrectName()
    {
        var agent = BlogWriterSetup.Create(_mockChatClient.Object);
        agent.Name.Should().Be("BlogWriter");
    }

    [Fact]
    public void Name_Constant_IsBlogWriter()
    {
        BlogWriterSetup.Name.Should().Be("BlogWriter");
    }

    [Fact]
    public void Instructions_ContainBlogStructureGuidance()
    {
        BlogWriterSetup.Instructions.Should().Contain("blog");
        BlogWriterSetup.Instructions.Should().Contain("Title");
        BlogWriterSetup.Instructions.Should().Contain("Introduction");
        BlogWriterSetup.Instructions.Should().Contain("Conclusion");
    }

    [Fact]
    public void Instructions_AreNonEmpty()
    {
        BlogWriterSetup.Instructions.Should().NotBeNullOrWhiteSpace();
        BlogWriterSetup.Instructions.Length.Should().BeGreaterThan(100,
            "instructions should be detailed enough to guide the LLM");
    }
}

/// <summary>
/// Layer 1 tests: ReviewerSetup — agent creation and configuration.
/// Architecture ref: Section 3.4 — Reviewer agent
/// </summary>
public class ReviewerSetupTests
{
    private readonly Mock<IChatClient> _mockChatClient = new();

    [Fact]
    public void Create_ReturnsNonNullAgent()
    {
        var agent = ReviewerSetup.Create(_mockChatClient.Object);
        agent.Should().NotBeNull();
    }

    [Fact]
    public void Create_AgentHasCorrectName()
    {
        var agent = ReviewerSetup.Create(_mockChatClient.Object);
        agent.Name.Should().Be("Reviewer");
    }

    [Fact]
    public void Name_Constant_IsReviewer()
    {
        ReviewerSetup.Name.Should().Be("Reviewer");
    }

    [Fact]
    public void Instructions_ContainReviewCriteria()
    {
        ReviewerSetup.Instructions.Should().Contain("review");
        ReviewerSetup.Instructions.Should().Contain("Structure");
        ReviewerSetup.Instructions.Should().Contain("Quality");
    }

    [Fact]
    public void Instructions_AreNonEmpty()
    {
        ReviewerSetup.Instructions.Should().NotBeNullOrWhiteSpace();
        ReviewerSetup.Instructions.Length.Should().BeGreaterThan(100);
    }
}

/// <summary>
/// Layer 1 tests: BlogWorkflow — workflow pipeline construction.
/// Architecture ref: Section 3.5 — BlogWriter → Reviewer pipeline
/// </summary>
public class BlogWorkflowTests
{
    private readonly Mock<IChatClient> _mockChatClient = new();

    [Fact]
    public void Build_ReturnsNonNullTuple()
    {
        var (entryAgent, workflow) = BlogWorkflow.Build(_mockChatClient.Object);
        entryAgent.Should().NotBeNull();
        workflow.Should().NotBeNull();
    }

    [Fact]
    public void Build_EntryAgentIsBlogWriter()
    {
        var (entryAgent, _) = BlogWorkflow.Build(_mockChatClient.Object);
        entryAgent.Name.Should().Be("BlogWriter",
            "the entry agent should be BlogWriter — it receives the initial topic");
    }

    [Fact]
    public void Build_WorkflowIsNotNull()
    {
        var (_, workflow) = BlogWorkflow.Build(_mockChatClient.Object);
        workflow.Should().NotBeNull("the workflow pipeline must be constructed");
    }

    [Fact]
    public void Build_ReturnsDifferentInstancesPerCall()
    {
        var (agent1, workflow1) = BlogWorkflow.Build(_mockChatClient.Object);
        var (agent2, workflow2) = BlogWorkflow.Build(_mockChatClient.Object);

        agent1.Should().NotBeSameAs(agent2, "each call should create fresh agents");
        workflow1.Should().NotBeSameAs(workflow2, "each call should create a fresh workflow");
    }
}

/// <summary>
/// Layer 1 tests: Orchestrator program configuration verification.
/// Architecture ref: Section 3 — Orchestrator A2A setup
/// </summary>
public class OrchestratorConfigTests
{
    [Fact]
    public void BlogWriter_And_Reviewer_HaveDifferentNames()
    {
        BlogWriterSetup.Name.Should().NotBe(ReviewerSetup.Name,
            "the two agents must have distinct names for workflow routing");
    }

    [Fact]
    public void BlogWriter_And_Reviewer_HaveDifferentInstructions()
    {
        BlogWriterSetup.Instructions.Should().NotBe(ReviewerSetup.Instructions,
            "each agent needs distinct system prompts for their specialized roles");
    }

    [Fact]
    public void AgentNames_AreValidIdentifiers()
    {
        BlogWriterSetup.Name.Should().MatchRegex(@"^[A-Za-z][A-Za-z0-9]*$",
            "agent names should be clean identifiers");
        ReviewerSetup.Name.Should().MatchRegex(@"^[A-Za-z][A-Za-z0-9]*$");
    }
}
