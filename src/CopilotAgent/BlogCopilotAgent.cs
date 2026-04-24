using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;
using SKCopilotV2.CopilotAgent.Services;

namespace SKCopilotV2.CopilotAgent;

/// <summary>
/// M365 Custom Engine Agent — THIN PROXY ONLY.
/// Receives Activity messages from Copilot/Teams, forwards to the orchestrator via A2A,
/// and returns the complete response. Contains NO business logic, NO LLM calls.
/// </summary>
public class BlogCopilotAgent : AgentApplication
{
    private readonly OrchestratorA2AClient _orchestrator;

    public BlogCopilotAgent(
        AgentApplicationOptions options,
        OrchestratorA2AClient orchestrator) : base(options)
    {
        _orchestrator = orchestrator;

        OnConversationUpdate(ConversationUpdateEvents.MembersAdded, WelcomeAsync);
        OnActivity(ActivityTypes.Message, OnMessageAsync, rank: RouteRank.Last);
    }

    private async Task WelcomeAsync(
        ITurnContext turnContext, ITurnState turnState, CancellationToken ct)
    {
        await turnContext.SendActivityAsync(
            MessageFactory.Text("Hello! Give me a topic and I'll write a blog post."), ct);
    }

    private async Task OnMessageAsync(
        ITurnContext turnContext, ITurnState turnState, CancellationToken ct)
    {
        var topic = turnContext.Activity.Text;

        // Send acknowledgment immediately
        await turnContext.SendActivityAsync(
            MessageFactory.Text($"✍️ Working on your blog post about \"{topic}\"..."), ct);

        // Send typing indicator initially
        await turnContext.SendActivityAsync(
            new Activity { Type = ActivityTypes.Typing }, ct);

        // Forward to orchestrator with progress callbacks to keep connection alive
        var response = await _orchestrator.GenerateBlogAsync(topic, async () =>
        {
            // Send typing indicator every ~8 seconds to prevent timeout
            await turnContext.SendActivityAsync(
                new Activity { Type = ActivityTypes.Typing }, ct);
        }, ct);

        // v1: Return complete response as single message (v2: stream tokens through)
        await turnContext.SendActivityAsync(
            MessageFactory.Text(response), ct);
    }
}
