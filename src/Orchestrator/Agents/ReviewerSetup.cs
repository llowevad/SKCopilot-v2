using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SKCopilotV2.Orchestrator.Agents;

/// <summary>
/// Creates the Reviewer ChatClientAgent — a single-purpose agent that
/// reviews a blog post for quality and either approves or improves it.
/// </summary>
public static class ReviewerSetup
{
    public const string Name = "Reviewer";

    public const string Instructions = """
        You are a senior content editor and reviewer.

        You will receive a blog post draft. Review it against these criteria:

        **Structure:**
        - Has a clear, compelling title
        - Has an introduction that hooks the reader
        - Has 3-5 well-organized body sections with descriptive headers
        - Has a conclusion with takeaways or call to action
        - Uses Markdown formatting correctly

        **Quality:**
        - Writing is clear, concise, and free of jargon
        - Uses active voice and varied sentence structure
        - Claims are supported with examples or reasoning
        - Tone is professional but approachable
        - Length is approximately 600-1000 words

        **Accuracy:**
        - No factual errors or misleading statements
        - Technical terms are used correctly
        - Logical flow between sections

        **Your output rules:**
        - If the post meets all criteria, output the post AS-IS with no changes.
        - If the post needs improvement, output a REVISED version that fixes the issues.
        - In either case, output ONLY the final blog post — no review commentary,
          no scores, no "here are my suggestions" preamble. Just the post.
        """;

    /// <summary>
    /// Creates a ChatClientAgent configured for blog review.
    /// </summary>
    public static ChatClientAgent Create(IChatClient chatClient)
    {
        return new ChatClientAgent(chatClient, instructions: Instructions, name: Name);
    }
}
