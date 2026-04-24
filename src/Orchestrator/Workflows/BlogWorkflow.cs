using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SKCopilotV2.Orchestrator.Agents;

namespace SKCopilotV2.Orchestrator.Workflows;

/// <summary>
/// Builds the BlogWriter → Reviewer sequential workflow.
/// The entry agent (BlogWriter) receives the topic, drafts a post,
/// then the Reviewer agent evaluates and finalizes it.
/// </summary>
public static class BlogWorkflow
{
    /// <summary>
    /// Creates the workflow and returns the entry agent plus the built workflow.
    /// The entry agent is the BlogWriter — it receives the initial user message.
    /// </summary>
    public static (ChatClientAgent entryAgent, Workflow workflow) Build(IChatClient chatClient)
    {
        var blogWriter = BlogWriterSetup.Create(chatClient);
        var reviewer = ReviewerSetup.Create(chatClient);

        // Sequential pipeline: BlogWriter drafts → Reviewer finalizes
        var workflow = new WorkflowBuilder(blogWriter)
            .AddEdge(blogWriter, reviewer)
            .Build();

        return (blogWriter, workflow);
    }
}
