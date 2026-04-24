# SKCopilot-v2 — Architecture Proposal

> **Author:** Venkman (Lead)  
> **Date:** 2026-04-22  
> **Revised:** 2026-04-28  
> **Status:** Proposed — pending team review  
> **Version:** v1.3

---

## 1. Overview

Three-layer system for AI-powered blog post generation. The **Orchestrator is the core product** — independently deployable, usable without any M365 dependency. Two client layers consume the orchestrator's A2A API: a web chat application for direct interaction, and an M365 Custom Engine Agent for Copilot/Teams users.

- **Layer 1 (Orchestrator + Child Agents):** Built on the **Microsoft Agent Framework** — the successor to both Semantic Kernel and AutoGen. Uses Agent Framework Workflows for multi-agent orchestration. Exposes an A2A endpoint that **any client** can call. This is the core — everything else is a consumer.
- **Layer 2 (Web Chat Application):** A **Blazor Web App** providing a chat interface for interacting with the orchestrator directly — no M365 required. Calls the orchestrator's A2A endpoint over HTTP.
- **Layer 3 (M365 Copilot Agent):** Built on the **Microsoft 365 Agents SDK** — a separate framework for building Custom Engine Agents (CEAs) that surface in Teams, M365 Copilot, and other channels. Acts as a **thin proxy** — receives messages from Copilot, forwards to the orchestrator, returns the response. Contains **no business logic, no agent orchestration, no LLM calls**.

> ⚠️ **Key principle:** The orchestrator is standalone. Layer 2 and Layer 3 are both clients of Layer 1. Neither client contains AI logic — all intelligence lives in the orchestrator.

> ⚠️ **Different frameworks:** The Agent Framework owns AI agent logic and orchestration (Layer 1). The M365 Agents SDK owns channel connectivity and the Activity protocol (Layer 3). Blazor is a standard ASP.NET Core web UI (Layer 2).

```
                                ┌─────────────────────────┐
                                │   Microsoft 365 Copilot  │
                                │  (Teams / M365 Chat)     │
                                └───────────┬─────────────┘
                                            │ Activity protocol
                                            ▼
┌────────────────────────┐     ┌──────────────────────────────────────────┐
│  LAYER 2: Web Chat App  │     │  LAYER 3: M365 Copilot Agent (CEA)      │
│  ───────────────────── │     │  ─────────────────────────────────       │
│  Blazor Web App         │     │  Microsoft 365 Agents SDK                │
│  (Interactive Server)   │     │  (AgentApplication pattern)              │
│                         │     │                                          │
│  • Browser-based chat   │     │  • Receives activities from Copilot      │
│  • Calls A2A endpoint   │     │  • Forwards to orchestrator via A2A      │
│  • No M365 dependency   │     │  • Returns response — NO business logic  │
│  • No LLM calls         │     │  • No LLM calls, no orchestration       │
│                         │     │  • Endpoint: POST /api/messages          │
│  Port: 5200             │     │  Port: 5300                              │
└───────────┬─────────────┘     └──────────────────┬───────────────────────┘
            │                                      │
            │         A2A Protocol (HTTP)           │
            │         ┌────────────────┐            │
            └────────►│                │◄───────────┘
                      └───────┬────────┘
                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│  LAYER 1: Orchestrator  (Agent Framework)  ★ CORE PRODUCT ★         │
│  ────────────────────────────────────────                            │
│  Microsoft Agent Framework (AIAgent + Workflows)                     │
│  Independently deployable — no M365 dependency                       │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────┐         │
│  │  Workflow (WorkflowBuilder → sequential pipeline)        │         │
│  │                                                          │         │
│  │  BlogWriter Agent (ChatClientAgent)                      │         │
│  │  • Instructions tuned for blog generation                │         │
│  │  • Produces structured blog post from topic              │         │
│  │              ↓  (Edge)                                   │         │
│  │  Reviewer Agent (ChatClientAgent)                        │         │
│  │  • Reviews blog post for quality and completeness        │         │
│  │  • Yields final output via workflow context              │         │
│  └─────────────────────────────────────────────────────────┘         │
│                                                                      │
│  A2A Server: GET  /a2a/orchestrator/v1/card                          │
│              POST /a2a/orchestrator/v1/message:stream                 │
│  Port: 5100                                                          │
│                                                                      │
│  NuGet: Microsoft.Agents.AI.Foundry                                  │
│         Microsoft.Agents.AI.Workflows                                │
│         Microsoft.Agents.AI.Hosting.A2A.AspNetCore                   │
│         Azure.AI.Projects                                            │
│         Azure.Identity                                               │
└──────────────────────────────────────────────────────────────────────┘
                       │
                       ▼
           Microsoft AI Foundry Project
              (GPT-4o deployment)
```

---

## 2. Solution Structure

```
SKCopilot-v2/
├── SKCopilot-v2.sln
│
├── src/
│   ├── Orchestrator/                       # LAYER 1 — Agent Framework (core product)
│   │   ├── Orchestrator.csproj
│   │   ├── Program.cs                      # ASP.NET Core host + A2A server via MapA2A()
│   │   ├── appsettings.json                # Foundry Project config
│   │   ├── Workflows/
│   │   │   └── BlogWorkflow.cs             # WorkflowBuilder pipeline (BlogWriter → Reviewer)
│   │   └── Agents/
│   │       ├── BlogWriterSetup.cs          # Agent creation + instructions
│   │       └── ReviewerSetup.cs            # Agent creation + instructions
│   │
│   ├── WebChat/                            # LAYER 2 — Blazor Web App (chat UI)
│   │   ├── WebChat.csproj
│   │   ├── Program.cs                       # Blazor Web App host (interactive server)
│   │   ├── appsettings.json                 # Orchestrator URL config
│   │   ├── Components/
│   │   │   ├── App.razor                    # Root component
│   │   │   ├── Routes.razor                 # Routing
│   │   │   └── Pages/
│   │   │       └── Chat.razor               # Chat page — main UI
│   │   └── Services/
│   │       └── OrchestratorClient.cs        # A2A client → Layer 1 (HTTP)
│   │
│   └── CopilotAgent/                       # LAYER 3 — M365 Agents SDK (CEA proxy)
│       ├── CopilotAgent.csproj
│       ├── Program.cs                       # M365 Agents SDK host + /api/messages
│       ├── appsettings.json                 # Bot registration, auth, orchestrator URL
│       ├── BlogCopilotAgent.cs              # AgentApplication subclass (thin proxy)
│       └── Services/
│           └── OrchestratorA2AClient.cs     # A2A client → Layer 1 (HTTP)
│
├── tests/
│   ├── Orchestrator.Tests/
│   │   └── Orchestrator.Tests.csproj
│   ├── WebChat.Tests/
│   │   └── WebChat.Tests.csproj
│   └── CopilotAgent.Tests/
│       └── CopilotAgent.Tests.csproj
│
├── ARCHITECTURE.md                          # This file
```

---

## 3. Layer 1: Orchestrator (Microsoft Agent Framework) — ★ Core Product ★

> **The orchestrator is independently deployable.** It has zero dependency on M365, Teams, or any specific client. It exposes an A2A endpoint that any HTTP client can call — the web chat UI, the M365 CEA, a CLI tool, a mobile app, or a third-party agent.

### 3.1 Technology

| Component | Choice | Rationale |
|---|---|---|
| Runtime | .NET 8+ (ASP.NET Core) | Current LTS, team expertise |
| Agent Framework | **Microsoft Agent Framework** (`Microsoft.Agents.AI.Foundry`) | Successor to Semantic Kernel + AutoGen; `AIAgent` / `ChatClientAgent` base types |
| Multi-Agent Orchestration | **Agent Framework Workflows** (`WorkflowBuilder`, `Executor`, `Edge`) | Graph-based, type-safe, streaming, superstep execution model |
| A2A Protocol | `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` | Framework-provided `MapA2A()` for A2A server endpoints |
| LLM | GPT-4o via Microsoft AI Foundry Project | Enterprise-grade, standalone project deployment |
| LLM Client | `AIProjectClient` from `Azure.AI.Projects` | Creates `ChatClientAgent` via `.AsAIAgent()` extension |
| Streaming | `RunStreamingAsync` on agents / `InProcessExecution.RunStreamingAsync` on workflows | End-to-end streaming from LLM → workflow → A2A → Copilot |

### 3.2 Agent Card (A2A Discovery)

The Agent Framework's `MapA2A()` automatically serves the Agent Card. With path `/a2a/orchestrator`, the card is at:

```
GET http://localhost:5100/a2a/orchestrator/v1/card
```

The card is configured in code:

```csharp
app.MapA2A(orchestratorAgent, path: "/a2a/orchestrator", agentCard: new()
{
    Name = "BlogOrchestrator",
    Description = "Orchestrates AI-powered blog post generation with specialist agents.",
    Version = "1.0"
});
```

### 3.3 Agent Design

**BlogWriter Agent** — instructions:
```
You are an expert blog writer. Given a topic, produce a well-structured
blog post with: title, introduction, 3-5 key sections with headers,
and a conclusion. Use a professional but approachable tone.
Target ~800 words.
```

**Reviewer Agent** — instructions:
```
You are a content reviewer. Review the blog post for quality, clarity,
structure, and completeness. If it meets standards, output it as-is.
If not, provide a concise improved version.
```

### 3.4 Orchestration via Workflow

The Agent Framework uses Workflows (not AgentGroupChat) for multi-agent orchestration. Workflows are directed graphs of Executors connected by Edges, executed in supersteps:

```csharp
// BlogWorkflow.cs — simplified
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

public static class BlogWorkflow
{
    public static (AIAgent entryAgent, Workflow workflow) Build(
        AIProjectClient aiProjectClient, string model)
    {
        // Create agents using the Foundry Responses pattern
        // (code-first — no server-managed agent resource created)
        IChatClient chatClient = aiProjectClient
            .GetProjectOpenAIClient()
            .GetProjectResponsesClient()
            .AsIChatClient(model);

        var blogWriter = new ChatClientAgent(chatClient,
            instructions: "You are an expert blog writer...",
            name: "BlogWriter");

        var reviewer = new ChatClientAgent(chatClient,
            instructions: "You are a content reviewer...",
            name: "Reviewer");

        // Build a sequential workflow: BlogWriter → Reviewer
        var workflow = new WorkflowBuilder(blogWriter)
            .AddEdge(blogWriter, reviewer)
            .Build();

        return (blogWriter, workflow);
    }
}
```

### 3.5 Workflow Streaming Execution

```csharp
// Executing the workflow with streaming
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
    workflow, new ChatMessage(ChatRole.User, $"Write a blog post about: {topic}"));

// TurnToken triggers agent processing after message caching
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is AgentResponseUpdateEvent update)
    {
        // Stream each chunk to caller (A2A handles this automatically)
        Console.Write(update.Data);
    }
}
```

### 3.6 Program.cs (Orchestrator — Layer 1)

```csharp
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

string endpoint = builder.Configuration["Foundry:Endpoint"]
    ?? throw new InvalidOperationException("Foundry:Endpoint is not set.");
string model = builder.Configuration["Foundry:Model"] ?? "gpt-4o";

// Create the Foundry project client (uses DefaultAzureCredential, not API keys)
var aiProjectClient = new AIProjectClient(
    new Uri(endpoint), new DefaultAzureCredential());

// Register the IChatClient for DI
IChatClient chatClient = aiProjectClient
    .GetProjectOpenAIClient()
    .GetProjectResponsesClient()
    .AsIChatClient(model);
builder.Services.AddSingleton(chatClient);

// Register the orchestrator agent exposed via A2A
var orchestratorAgent = builder.AddAIAgent("BlogOrchestrator",
    instructions: """
    You are a blog post orchestrator. When given a topic, produce a
    well-structured blog post with title, introduction, 3-5 key sections,
    and a conclusion. Professional but approachable tone. ~800 words.
    """);

var app = builder.Build();

// Expose via A2A protocol — framework handles card + message endpoints
app.MapA2A(orchestratorAgent, path: "/a2a/orchestrator", agentCard: new()
{
    Name = "BlogOrchestrator",
    Description = "Generates AI-powered blog posts on any topic.",
    Version = "1.0"
});

app.Run();
```

> **Note on v1 simplicity:** The Program.cs above shows a single-agent A2A endpoint, which is the simplest working pattern from the official docs. The Workflow-based multi-agent pipeline (Section 3.4) is the target architecture but may be wired in Phase 2 once the single-agent A2A skeleton is validated. Either way, `MapA2A()` is the correct entry point — the internal orchestration is an implementation detail hidden behind the A2A boundary.

---

## 4. Layer 2: Web Chat Application (Blazor)

> A simple web-based chat interface for interacting with the orchestrator directly — no M365 account or Teams client required. This is the easiest way to use the orchestrator.

### 4.1 Technology

| Component | Choice | Rationale |
|---|---|---|
| Runtime | .NET 8+ (ASP.NET Core) | Same stack as Layer 1 |
| UI Framework | **Blazor Web App** (Interactive Server render mode) | Native .NET — no JavaScript build toolchain, same language as orchestrator |
| Rendering | Server-side interactive (SignalR) | Simplest Blazor model, no WASM download, fast startup |
| A2A Client | HTTP client calling Layer 1's A2A endpoint | Same pattern as Layer 3 — standard HTTP |
| Styling | Minimal CSS (or Bootstrap) | Keep it simple — it's a chat interface, not a full app |
| Port | `http://localhost:5200` | Separate from Layer 1 (5100) and Layer 3 (5300) |

### 4.2 Key NuGet Packages

```xml
<!-- Blazor Web App — included in the default template, no extra packages needed -->
<!-- HTTP client for calling orchestrator A2A endpoint -->
<PackageReference Include="Microsoft.Extensions.Http" Version="8.*" />
```

> The Blazor Web App template provides everything needed. The only addition is `HttpClient` for calling the orchestrator.

### 4.3 Chat Page (Razor Component)

```razor
@* Chat.razor — simplified *@
@page "/"
@rendermode InteractiveServer
@inject OrchestratorClient Orchestrator

<h3>Blog Post Generator</h3>

<div class="chat-history">
    @foreach (var msg in _messages)
    {
        <div class="message @msg.Role">@msg.Text</div>
    }
</div>

<form @onsubmit="SendAsync">
    <input @bind="_input" placeholder="Enter a blog topic..." />
    <button type="submit" disabled="@_loading">Generate</button>
</form>

@code {
    private List<ChatMessage> _messages = new();
    private string _input = "";
    private bool _loading;

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(_input)) return;

        _messages.Add(new("user", _input));
        _loading = true;
        var topic = _input;
        _input = "";

        var response = await Orchestrator.GenerateBlogAsync(topic);
        _messages.Add(new("agent", response));
        _loading = false;
    }

    private record ChatMessage(string Role, string Text);
}
```

### 4.4 Orchestrator Client Service

```csharp
// OrchestratorClient.cs — calls Layer 1's A2A endpoint
public class OrchestratorClient
{
    private readonly HttpClient _httpClient;
    private readonly string _a2aPath;

    public OrchestratorClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _a2aPath = config["Orchestrator:A2APath"] ?? "/a2a/orchestrator";
    }

    public async Task<string> GenerateBlogAsync(
        string topic, CancellationToken ct = default)
    {
        var payload = new
        {
            message = new
            {
                kind = "message",
                role = "user",
                parts = new[] { new { kind = "text", text = topic } },
                contextId = Guid.NewGuid().ToString()
            }
        };

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_a2aPath}/v1/message:stream")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Read SSE stream and aggregate (v1: non-streaming to UI)
        var result = new StringBuilder();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line?.StartsWith("data: ") == true)
            {
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;
                result.Append(data);
            }
        }

        return result.ToString();
    }
}
```

### 4.5 Program.cs (Web Chat — Layer 2)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register A2A client for calling the orchestrator
builder.Services.AddHttpClient<OrchestratorClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5100");
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

### 4.6 Configuration — `appsettings.json`

```json
{
  "Orchestrator": {
    "BaseUrl": "http://localhost:5100",
    "A2APath": "/a2a/orchestrator"
  }
}
```

> No bot registration, no M365 tenant, no Azure AD — just point it at the orchestrator and go.

---

## 5. Layer 3: M365 Copilot Agent (M365 Agents SDK) — Proxy Only

> **This layer is a thin proxy.** Its sole purpose is to bridge Copilot/Teams users to the orchestrator. It contains NO business logic, NO agent orchestration, and makes NO LLM calls. It receives an Activity from Copilot, extracts the user's message, forwards it to the orchestrator via A2A, and returns the response.

### 5.1 Technology

| Component | Choice | Rationale |
|---|---|---|
| Runtime | .NET 8+ (ASP.NET Core) | Same stack as Layer 1 |
| SDK | **Microsoft 365 Agents SDK** (`Microsoft.Agents.Hosting.AspNetCore`) | Purpose-built for Custom Engine Agents (CEAs) in M365 |
| Agent Pattern | **`AgentApplication`** (from `Microsoft.Agents.Builder.App`) | Modern M365 SDK pattern — NOT the old Bot Framework `ActivityHandler` |
| A2A Client | HTTP client calling Layer 1's A2A endpoint | Standard HTTP + SSE consumption |
| Channel | Teams / M365 Copilot | Primary user-facing surface |
| Endpoint | `POST /api/messages` | Standard M365 Agents SDK endpoint |

### 5.2 Key NuGet Packages

```xml
<!-- M365 Agents SDK — single package provides hosting, builder, state, models -->
<PackageReference Include="Microsoft.Agents.Hosting.AspNetCore" Version="1.*" />
```

> The M365 Agents SDK is model- and orchestrator-agnostic. It handles channel connectivity, the Activity protocol, turns, and state. All AI logic lives in Layer 1.

### 5.3 Agent Application (Thin Proxy)

The M365 Agents SDK uses the `AgentApplication` pattern. Note: this class contains **zero AI logic** — it simply forwards messages to the orchestrator and returns the response:

```csharp
// BlogCopilotAgent.cs — thin proxy, no business logic
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

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

        // Forward to orchestrator — NO local LLM calls, NO business logic
        var response = await _orchestrator.GenerateBlogAsync(topic, ct);

        // v1: Return complete response as single message
        await turnContext.SendActivityAsync(
            MessageFactory.Text(response), ct);
    }
}
```

### 5.4 A2A Client (Calling the Orchestrator)

```csharp
// OrchestratorA2AClient.cs — simplified
// Calls the Layer 1 A2A endpoint using standard HTTP + SSE
public class OrchestratorA2AClient
{
    private readonly HttpClient _httpClient;
    private readonly string _a2aPath;

    public OrchestratorA2AClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _a2aPath = config["Orchestrator:A2APath"] ?? "/a2a/orchestrator";
    }

    public async Task<string> GenerateBlogAsync(
        string topic, CancellationToken ct = default)
    {
        // A2A message format per spec
        var payload = new
        {
            message = new
            {
                kind = "message",
                role = "user",
                parts = new[] { new { kind = "text", text = topic } },
                contextId = Guid.NewGuid().ToString()
            }
        };

        // Call the A2A streaming endpoint
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_a2aPath}/v1/message:stream")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Read SSE stream and aggregate response
        var result = new StringBuilder();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line?.StartsWith("data: ") == true)
            {
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;
                result.Append(data);
            }
        }

        return result.ToString();
    }
}
```

### 5.5 Program.cs (Copilot Agent — Layer 3)

```csharp
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;

var builder = WebApplication.CreateBuilder(args);

// M365 Agents SDK registration
builder.Services.AddHttpClient();
builder.AddAgentApplicationOptions();
builder.AddAgent<BlogCopilotAgent>();
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// Register A2A client for calling Layer 1
builder.Services.AddHttpClient<OrchestratorA2AClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5100");
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// M365 Agents SDK endpoint — receives activities from Copilot/Teams channels
app.MapPost("/api/messages", async (
    HttpRequest request,
    HttpResponse response,
    IAgentHttpAdapter adapter,
    IAgent agent,
    CancellationToken ct) =>
{
    await adapter.ProcessAsync(request, response, agent, ct);
});

app.Run();
```

### 5.6 Configuration — `appsettings.json`

```json
{
  "Orchestrator": {
    "BaseUrl": "http://localhost:5100",
    "A2APath": "/a2a/orchestrator"
  },
  "Connections": {
    "BotServiceConnection": {
      "Assembly": "Microsoft.Agents.Authentication.Msal",
      "Type": "MsalAuth",
      "Settings": {
        "ClientId": "<bot-app-id>",
        "ClientSecret": "<from-user-secrets>",
        "AuthorityEndpoint": "https://login.microsoftonline.com",
        "TenantId": "<tenant-id>"
      }
    }
  }
}
```

> ⚠️ **Secrets go in User Secrets or Azure Key Vault. Never in source.**

---

## 6. Integration Point: A2A Protocol

### Decision: Agent-to-Agent (A2A) Protocol (unchanged from v1.1)

| Option | Pros | Cons |
|---|---|---|
| **A2A Protocol** ✅ | Industry standard, framework-provided `MapA2A()`, built-in discovery + streaming | More protocol surface than raw HTTP |
| Custom HTTP API | Simple | No standard discovery, no streaming, no interop |
| In-process reference | Zero latency | Tight coupling, can't deploy separately |
| Message queue | Fully async, resilient | Over-engineered for v1 |

### A2A Communication Flow

Both Layer 2 (Web Chat) and Layer 3 (M365 CEA) use the same A2A protocol to talk to the orchestrator:

```
Any A2A Client                                 Orchestrator (A2A Server via MapA2A)
(Web Chat, M365 CEA, CLI, etc.)                        │
        │                                                │
        │  GET /a2a/orchestrator/v1/card                 │
        │ ──────────────────────────────────────────────► │
        │  ◄── { name, description, version }            │
        │                                                │
        │  POST /a2a/orchestrator/v1/message:stream      │
        │  { message: {                                  │
        │      kind: "message",                          │
        │      role: "user",                             │
        │      parts: [{ kind: "text",                   │
        │                text: "blog about X" }],        │
        │      contextId: "abc-123"                      │
        │  }}                                            │
        │ ──────────────────────────────────────────────► │
        │                                                │
        │  ◄── SSE stream (or JSON response)             │
        │  { kind: "message", role: "agent",             │
        │    parts: [{ kind: "text",                     │
        │              text: "# My Blog Post..." }],     │
        │    contextId: "abc-123" }                      │
        │                                                │
```

> **Key principle:** The orchestrator doesn't know or care which client is calling. Web Chat, M365 CEA, `curl`, a Python script — all use the same A2A endpoint. This is what makes the orchestrator standalone.

**For v1, all three projects run locally.** Layer 2 and Layer 3 both call Layer 1 at `http://localhost:5100/a2a/orchestrator/v1/message:stream`. In production, Layer 1 deploys as its own Container App with a stable endpoint.

---

## 7. Key Dependencies

### Layer 1: Orchestrator (Agent Framework)

```xml
<!-- Microsoft Agent Framework + Foundry integration -->
<PackageReference Include="Microsoft.Agents.AI.Foundry" Version="*-*" />

<!-- Multi-agent orchestration via Workflows -->
<PackageReference Include="Microsoft.Agents.AI.Workflows" Version="*-*" />

<!-- A2A protocol server (provides MapA2A) -->
<PackageReference Include="Microsoft.Agents.AI.Hosting.A2A.AspNetCore" Version="*-*" />

<!-- Foundry project client + auth -->
<PackageReference Include="Azure.AI.Projects" Version="*-*" />
<PackageReference Include="Azure.Identity" Version="1.*" />
```

### Layer 2: Web Chat (Blazor)

```xml
<!-- Default Blazor Web App template — no extra packages beyond: -->
<PackageReference Include="Microsoft.Extensions.Http" Version="8.*" />
```

> Blazor Web App is the lightest client. No AI packages, no M365 packages — just HTTP for calling the orchestrator.

### Layer 3: Copilot Agent (M365 Agents SDK)

```xml
<!-- M365 Agents SDK — all-in-one hosting package -->
<PackageReference Include="Microsoft.Agents.Hosting.AspNetCore" Version="1.*" />
```

> The M365 Agents SDK handles channel connectivity. All AI logic lives in Layer 1.

### Prerequisites

- .NET 8 SDK or later
- **Microsoft AI Foundry Project** with GPT-4o model deployed
  - Endpoint format: `https://ai-foundry-<resource>.services.ai.azure.com/api/projects/ai-project-<project>`
- `az login` authenticated (for `DefaultAzureCredential` — no API keys needed for local dev)
- Azure Bot registration (for Layer 3 / M365 channel connectivity only)
- M365 developer tenant (for Copilot testing — Layer 3 only)
- Microsoft 365 Agents Playground (`npm install -g @microsoft/teams-app-test-tool`) for Layer 3 local testing

> **Note:** Layers 1 and 2 require NO M365 infrastructure. You can run the orchestrator + web chat with only a Foundry Project and `az login`.

---

## 8. Configuration

### Layer 1 — `appsettings.json`

```json
{
  "Foundry": {
    "Endpoint": "https://ai-foundry-<resource>.services.ai.azure.com/api/projects/ai-project-<project>",
    "Model": "gpt-4o"
  }
}
```

### Layer 2 — `appsettings.json`

```json
{
  "Orchestrator": {
    "BaseUrl": "http://localhost:5100",
    "A2APath": "/a2a/orchestrator"
  }
}
```

### Layer 3 — `appsettings.json`

```json
{
  "Orchestrator": {
    "BaseUrl": "http://localhost:5100",
    "A2APath": "/a2a/orchestrator"
  },
  "Connections": {
    "BotServiceConnection": {
      "Assembly": "Microsoft.Agents.Authentication.Msal",
      "Type": "MsalAuth",
      "Settings": {
        "ClientId": "<bot-app-id>",
        "ClientSecret": "<from-user-secrets>",
        "AuthorityEndpoint": "https://login.microsoftonline.com",
        "TenantId": "<tenant-id>"
      }
    }
  }
}
```

> ⚠️ **Secrets go in User Secrets or Azure Key Vault. Never in source.**

---

## 9. Version Scope: v1 vs v2

### v1 — Ship This (Current Build Target)

**What works in v1:**
- ✅ **Orchestrator is standalone** — runs independently, no M365 dependency
- ✅ **Web Chat UI** — browser-based chat interface, calls orchestrator via A2A
- ✅ **M365 CEA proxies to orchestrator** — receives message from Copilot, forwards to orchestrator, returns complete response as a single message (non-streaming from user's perspective)
- ✅ **Multi-agent workflow** — BlogWriter → Reviewer pipeline via Workflows
- ✅ **A2A protocol** — standard inter-agent communication
- ✅ **Any A2A client works** — `curl`, Python, custom apps can call the orchestrator

**Explicitly out of scope for v1:**
- ❌ Persistent conversation memory across sessions
- ❌ Multiple child agents beyond BlogWriter + Reviewer
- ❌ Graph API integration
- ❌ Adaptive Cards (plain text responses)
- ❌ Production auth (local dev only for v1)
- ❌ CI/CD pipeline
- ❌ Cross-framework A2A interop (e.g., Google ADK, LangChain agents)
- ❌ Workflow checkpointing or human-in-the-loop

### v2 — Future (Documented, Not Built)

| Feature | Description | Notes |
|---|---|---|
| **Streaming to Copilot UI** | Orchestrator streams tokens → CEA forwards SSE chunks → Copilot renders incrementally | Requires M365 Agents SDK streaming support (Activity protocol chunking or streaming extensions) |
| **Streaming in Web Chat** | Token-by-token rendering in Blazor chat as orchestrator streams | Blazor SignalR + SSE consumption — natural fit |
| **Conversation memory** | Persist chat history across sessions | Requires state management in Layer 1 |
| **Additional agents** | Expand beyond BlogWriter + Reviewer | Workflow graph supports arbitrary topology |
| **Production auth** | AAD/Entra ID for A2A between layers | Required for production deployment |
| **CI/CD** | Automated build, test, deploy pipeline | Standard Azure DevOps / GitHub Actions |

> **v2 streaming architecture note:** The orchestrator already streams via A2A SSE. For v2, the M365 CEA would consume the SSE stream and forward chunks to the Copilot UI in real-time instead of aggregating the full response. The Web Chat could render tokens as they arrive via Blazor's `StreamRendering` or SignalR push.

---

## 10. Phased Build Plan

### Phase 1: Orchestrator Foundation (Egon)

| # | Work Item | Owner | Description |
|---|---|---|---|
| 1.1 | Solution scaffolding | Egon | Create .sln, all three .csproj files (`Orchestrator`, `WebChat`, `CopilotAgent`), folder structure |
| 1.2 | Orchestrator project setup | Egon | ASP.NET Core host, Agent Framework NuGet refs (`Microsoft.Agents.AI.Foundry`, `Hosting.A2A.AspNetCore`, `Azure.AI.Projects`) |
| 1.3 | Foundry connection | Egon | Wire `AIProjectClient` with `DefaultAzureCredential`, register `IChatClient` via `AsIChatClient()` |
| 1.4 | Single-agent A2A endpoint | Egon | Register agent via `builder.AddAIAgent()`, expose via `app.MapA2A()`, verify card at `/v1/card` |

**Exit criteria:** `dotnet build` succeeds, Agent Card discoverable at `/a2a/orchestrator/v1/card`, single-agent generates blog via A2A message. Orchestrator runs standalone — no M365 required.

### Phase 2: Multi-Agent Workflow (Egon)

| # | Work Item | Owner | Description |
|---|---|---|---|
| 2.1 | BlogWriter agent | Egon | `ChatClientAgent` with blog-writing instructions |
| 2.2 | Reviewer agent | Egon | `ChatClientAgent` with review instructions |
| 2.3 | Workflow pipeline | Egon | `WorkflowBuilder` with BlogWriter → Reviewer, streaming via `InProcessExecution.RunStreamingAsync` |
| 2.4 | Workflow behind A2A | Egon | Wire workflow execution into the A2A-exposed agent (custom `AIAgent` or service wrapper) |
| 2.5 | End-to-end A2A test | Egon | A2A message with topic → multi-agent workflow produces a reviewed blog post via SSE |

**Exit criteria:** A2A request with topic → multi-agent workflow produces and streams a reviewed blog post. Verifiable with `curl`.

### Phase 3: Web Chat Application (Ray)

| # | Work Item | Owner | Description |
|---|---|---|---|
| 3.1 | WebChat project setup | Ray | Blazor Web App template, `Microsoft.Extensions.Http` NuGet, project added to solution |
| 3.2 | OrchestratorClient service | Ray | HTTP client calling Layer 1's A2A endpoint, SSE consumption, response aggregation |
| 3.3 | Chat page (Razor component) | Ray | Chat UI with message history, input box, send button, loading state |
| 3.4 | Styling | Ray | Clean, minimal chat interface (Bootstrap or custom CSS) |
| 3.5 | Local testing | Ray | Type topic in browser → see blog post from orchestrator |

**Exit criteria:** Open browser → type topic → see AI-generated blog post. No M365 setup required.

### Phase 4: M365 Copilot Agent (Ray)

| # | Work Item | Owner | Description |
|---|---|---|---|
| 4.1 | CopilotAgent project setup | Ray | M365 Agents SDK NuGet ref (`Microsoft.Agents.Hosting.AspNetCore`), config |
| 4.2 | AgentApplication subclass | Ray | `BlogCopilotAgent : AgentApplication` with `OnActivity` routing — thin proxy only |
| 4.3 | OrchestratorA2AClient | Ray | HTTP client: discovers Agent Card, sends A2A messages, aggregates SSE response |
| 4.4 | Bot registration | Ray | Azure Bot registration, manifest for Teams/Copilot |
| 4.5 | Local testing with Playground | Ray | Test with `teamsapptester` (M365 Agents Playground) |
| 4.6 | Teams channel testing | Ray | Send message in Teams → see blog post from orchestrator |

**Exit criteria:** Message in Teams → blog post returned to user via Orchestrator A2A. Response is complete (non-streaming to user — v2 feature).

### Phase 5: Testing & Polish (Winston + Team)

| # | Work Item | Owner | Description |
|---|---|---|---|
| 5.1 | Layer 1 unit tests | Winston | Workflow execution, agent config, A2A endpoint responses |
| 5.2 | Layer 2 unit tests | Winston | OrchestratorClient, Chat page rendering |
| 5.3 | Layer 3 unit tests | Winston | AgentApplication routing, A2A client, message formatting |
| 5.4 | Integration tests | Winston | Web Chat → A2A → Orchestrator, CEA → A2A → Orchestrator end-to-end |
| 5.5 | Error handling | Egon/Ray | Timeouts, LLM failures, A2A connection errors, SSE stream interruption |
| 5.6 | README & docs | Venkman | Setup instructions, Foundry provisioning guide, A2A testing guide |

**Exit criteria:** Tests pass, error cases handled, docs complete.

---

## 11. Open Questions

1. ~~**Azure OpenAI model** — GPT-4o or GPT-4.1?~~ **Resolved:** GPT-4o confirmed.
2. **Bot registration** — Does the team already have an Azure Bot resource, or do we need to create one? (Layer 3 only)
3. **M365 tenant** — Is the dev tenant configured for custom agent sideloading? (Layer 3 only)
4. **Foundry Project** — Has a Foundry Project been provisioned? Confirm the endpoint URL.
5. **A2A auth in production** — What authentication scheme for A2A between layers in production? (Local dev uses no auth.)
6. **Workflow complexity for v1** — Should we start with a single-agent A2A endpoint (simpler) and add the BlogWriter → Reviewer workflow in Phase 2? Or wire the full workflow from the start?
7. **Web Chat styling** — Any specific design requirements, or keep it minimal for v1?

---

## 12. Decision Log

| # | Decision | Rationale |
|---|---|---|
| D1 | ~~HTTP API between layers~~ **→ A2A protocol** (v1.1) | Industry standard, streaming support, agent discovery, future interop |
| D2 | ~~Semantic Kernel agents~~ **→ Agent Framework agents** (v1.2) | Agent Framework is the successor to SK; uses `AIAgent`/`ChatClientAgent`, not `ChatCompletionAgent` |
| D3 | ~~`ChatCompletionAgent`~~ **→ `ChatClientAgent` via `AIProjectClient.AsAIAgent()`** (v1.2) | Agent Framework pattern — wraps any `IChatClient`, code-first, no external state |
| D4 | ~~`AgentGroupChat`~~ **→ Agent Framework Workflows** (v1.2) | Graph-based `WorkflowBuilder` with `Executor`/`Edge`, type-safe, streaming, superstep execution |
| D5 | ~~Layer 1 = Agent Framework, Layer 2 = M365 Agents SDK~~ **→ Three-layer architecture** (v1.3) | Layer 1 = Orchestrator (core), Layer 2 = Web Chat (Blazor), Layer 3 = M365 CEA (proxy) |
| D6 | ~~Two projects~~ **→ Three projects in one solution** (v1.3) | `Orchestrator`, `WebChat`, `CopilotAgent` — shared solution, independent deployment |
| D7 | v1 scope limited to blog generation | Ship fast, validate the architecture, expand later |
| D8 | ~~Agent Framework for ALL agents~~ **→ Agent Framework for Layer 1 only** (v1.2) | Layer 3 is M365 Agents SDK, Layer 2 is plain Blazor |
| D9 | **A2A protocol for all client → orchestrator communication** (v1.1, refined v1.3) | `MapA2A()` on server, HTTP client on consumers. Both Layer 2 and Layer 3 use same A2A endpoint. |
| D10 | **Microsoft AI Foundry Project** (v1.1) | Standalone project, `DefaultAzureCredential` auth (not API keys) |
| D11 | ~~`InvokeStreamingAsync`~~ **→ `RunStreamingAsync` / workflow streaming** (v1.2) | Agent Framework API: `agent.RunStreamingAsync()` or `InProcessExecution.RunStreamingAsync(workflow)` |
| D12 | **GPT-4o model confirmed** (v1.1) | User confirmed, deployed via Foundry Project |
| D13 | ~~`ActivityHandler`~~ **→ `AgentApplication`** for Layer 3 (v1.2) | M365 Agents SDK modern pattern: route-based `OnActivity`/`OnMessage`, not overridden handler methods |
| D14 | **Orchestrator is standalone — core product** (v1.3) | Independently deployable, no M365 dependency. Any A2A client can call it. |
| D15 | **Blazor Web App for chat UI** (v1.3) | Native .NET, no JS build toolchain, Interactive Server render mode. Simplest option for a .NET solution. |
| D16 | **M365 CEA is thin proxy only** (v1.3) | No business logic, no LLM calls, no orchestration. Receives activity → forwards to orchestrator → returns response. |
| D17 | **Streaming to Copilot UI is v2** (v1.3) | v1 returns complete response as single message. v2 streams tokens through CEA to Copilot in real-time. |

---

*Reviewed by: Venkman (Lead). v1.3 revision — standalone orchestrator, three-layer architecture, web chat UI, v1/v2 scope split. Pending team feedback.*
