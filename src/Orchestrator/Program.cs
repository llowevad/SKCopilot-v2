using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SKCopilotV2.Orchestrator.Workflows;

var builder = WebApplication.CreateBuilder(args);

// --- Foundry configuration (placeholder values in appsettings.json) ---
string endpoint = builder.Configuration["Foundry:Endpoint"]
    ?? throw new InvalidOperationException("Foundry:Endpoint is required. Set it in appsettings.json or user secrets.");
string model = builder.Configuration["Foundry:Model"] ?? "gpt-4o";

// Create the Foundry project client (DefaultAzureCredential — no API keys)
var aiProjectClient = new AIProjectClient(
    new Uri(endpoint), new DefaultAzureCredential());

// Create the IChatClient via Foundry Responses pattern
IChatClient chatClient = aiProjectClient
    .GetProjectOpenAIClient()
    .GetProjectResponsesClient()
    .AsIChatClient(model);

// Build the multi-agent workflow: BlogWriter → Reviewer
var (entryAgent, workflow) = BlogWorkflow.Build(chatClient);

// Register the workflow and entry agent for DI so A2A hosting can resolve them
builder.Services.AddSingleton(workflow);
builder.Services.AddSingleton(entryAgent);

// Register the blog orchestrator agent with the Agent Framework hosting.
// The A2A endpoint delegates to the workflow pipeline internally.
builder.Services.AddAIAgent(
    name: "BlogOrchestrator",
    instructions: """
    You are a blog post orchestrator. When given a topic, route it through the
    BlogWriter → Reviewer workflow pipeline to produce a polished blog post.
    """,
    chatClient: chatClient);

var app = builder.Build();

// Health check endpoint for Azure App Service warmup probe
app.MapGet("/", () => Results.Ok("SKCopilot-v2 Orchestrator is running."));

// Expose the agent via A2A protocol — framework handles card + message endpoints
app.MapA2A(
    agentName: "BlogOrchestrator",
    path: "/a2a/orchestrator",
    agentCard: new()
    {
        Name = "BlogOrchestrator",
        Description = "Orchestrates AI-powered blog post generation with specialist agents (BlogWriter → Reviewer pipeline).",
        Version = "2.0"
    });

app.Run();
