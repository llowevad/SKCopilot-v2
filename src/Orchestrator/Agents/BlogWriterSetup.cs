using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SKCopilotV2.Orchestrator.Agents;

/// <summary>
/// Creates the BlogWriter ChatClientAgent — a single-purpose agent that
/// takes a topic and produces a well-structured blog post.
/// </summary>
public static class BlogWriterSetup
{
    public const string Name = "BlogWriter";

    public const string Instructions = """
        You are an expert blog writer and content strategist.

        When given a topic, produce a polished, publication-ready blog post following this structure:

        1. **Title** — Compelling, SEO-friendly headline. Use title case.
        2. **Introduction** (1-2 paragraphs) — Hook the reader, state the value proposition,
           preview what the post covers.
        3. **Body** (3-5 sections) — Each section has a descriptive H2 header and 1-3 paragraphs.
           Use concrete examples, data points, or analogies where appropriate.
           Vary paragraph length for readability.
        4. **Conclusion** (1 paragraph) — Summarize key takeaways and end with a call to action
           or thought-provoking question.

        Style guidelines:
        - Professional but approachable — write like an informed colleague, not a textbook.
        - Use active voice. Avoid jargon unless explaining it.
        - Target approximately 800 words (600-1000 acceptable range).
        - Use Markdown formatting for headers, bold, and lists.
        - Do NOT include meta-commentary about the writing process — just produce the post.
        """;

    /// <summary>
    /// Creates a ChatClientAgent configured for blog writing.
    /// </summary>
    public static ChatClientAgent Create(IChatClient chatClient)
    {
        return new ChatClientAgent(chatClient, instructions: Instructions, name: Name);
    }
}
