# SKCopilot-v2

> An AI-powered blog writing assistant surfaced in **Microsoft 365 Copilot**, **Microsoft Teams**, and a **web browser** — built with the Microsoft Agent Framework and M365 Agents SDK.

![Architecture Diagram](docs/diagrams/architecture.png)

---

## What is this?

SKCopilot-v2 is a three-layer reference architecture that demonstrates how to build an AI agent orchestrator and surface it across multiple channels — M365 Copilot, Teams, and a standalone web chat — without duplicating any AI logic.

A user sends a prompt like *"Write a blog about AI agents"* and receives a fully written, reviewed blog post produced by a multi-agent workflow (BlogWriter → Reviewer pipeline).

### The Core Problem This Solves

The **Microsoft Agent Framework** builds AI agents that communicate via the **A2A (Agent-to-Agent) protocol** — an open, HTTP-based JSON-RPC 2.0 standard. However, **M365 Copilot and Microsoft Teams don't speak A2A** — they use the **Bot Framework Activity protocol**, which routes through Azure Bot Service with JWT-authenticated HTTP POSTs.

```
Agent Framework (A2A protocol)  ←→  ???  ←→  M365 Copilot (Activity protocol)
```

This project bridges that gap using a **Custom Engine Agent (CEA)** as a thin protocol proxy:

```
Agent Framework (A2A)  ←→  CopilotAgent (CEA)  ←→  Azure Bot Service  ←→  M365 Copilot
```

This is the **only supported path** to surface Agent Framework agents in M365 surfaces today.

---

## Architecture

| Layer | Project | Framework | Role |
|-------|---------|-----------|------|
| **Layer 1** | `Orchestrator` | Microsoft Agent Framework | Core AI — multi-agent workflow via A2A protocol. All intelligence lives here. |
| **Layer 2** | `WebChat` | Blazor Interactive Server | Browser-based chat UI — calls the orchestrator directly, no M365 dependency. |
| **Layer 3** | `CopilotAgent` | M365 Agents SDK | Thin proxy Custom Engine Agent — receives Activities from Copilot/Teams, forwards to orchestrator via A2A. |
| **Cloud** | Azure Bot Service | Bot Framework | Channel routing, JWT token signing, message relay between M365 surfaces and the bot endpoint. |
| **Cloud** | Azure AI Foundry | GPT-4o | LLM inference for both BlogWriter and Reviewer agents. |
| **Identity** | Microsoft Entra ID | OAuth 2.0 / OIDC | App registration, token validation, tenant-scoped authentication. |

**Key principle:** The orchestrator is standalone. Layer 2 and Layer 3 are both clients of Layer 1. Neither client contains AI logic — all intelligence lives in the orchestrator.

---

## Message Flow

![Message Flow Diagram](docs/diagrams/message-flow.png)

1. **User sends a message** in Teams or M365 Copilot (e.g., *"Write a blog about AI agents"*)
2. **M365 routes to Azure Bot Service** — wraps the message in a Bot Framework Activity
3. **Bot Service authenticates and forwards** — signs a JWT, POSTs the Activity to `/api/messages`
4. **CopilotAgent validates the JWT** — checks issuer, audience, signing keys against Entra ID
5. **Immediate acknowledgment** — sends a typing indicator + "Working on your blog post..." to prevent M365 timeouts
6. **Forwards to Orchestrator via A2A** — JSON-RPC 2.0 call with SSE streaming to `/a2a/orchestrator`
7. **Multi-agent workflow runs** — BlogWriter drafts the post → Reviewer polishes it (both using GPT-4o)
8. **Response streams back** — typing indicators every ~8s keep the Teams connection alive during the 30-90s workflow
9. **Final blog post delivered** — sent back through Bot Service → displayed to the user

> For the full step-by-step walkthrough with technical details, see **[docs/Architecture-and-Flow.md](docs/Architecture-and-Flow.md)**.

---

## Tech Stack

- **.NET 8** — all three layers
- **Microsoft Agent Framework** — multi-agent orchestration (Workflows, A2A protocol)
- **Azure AI Foundry (GPT-4o)** — LLM inference via the Responses pattern
- **M365 Agents SDK** — Custom Engine Agent for Teams/Copilot channel connectivity
- **Azure Bot Service** — channel routing, JWT token signing, message relay
- **Microsoft Entra ID** — OAuth 2.0 / OIDC authentication
- **Blazor Interactive Server** — web chat UI with SSE streaming

---

## Project Structure

```
SKCopilot-v2/
├── SKCopilot-v2.sln                    # Visual Studio solution
│
├── src/
│   ├── Orchestrator/                   # Layer 1 — AI Agent Framework orchestrator
│   │   ├── Program.cs                  #   Entry point, A2A endpoint at /a2a/orchestrator
│   │   ├── Agents/BlogWriterSetup.cs   #   BlogWriter agent (drafts posts)
│   │   ├── Agents/ReviewerSetup.cs     #   Reviewer agent (polishes drafts)
│   │   └── Workflows/BlogWorkflow.cs   #   Sequential pipeline: Writer → Reviewer
│   │
│   ├── WebChat/                        # Layer 2 — Blazor web chat UI
│   │   ├── Program.cs                  #   Entry point, Razor component routing
│   │   ├── Services/OrchestratorClient.cs  # A2A HTTP client (streaming + fallback)
│   │   └── Components/Pages/Chat.razor #   Chat page with message history
│   │
│   └── CopilotAgent/                   # Layer 3 — M365 Custom Engine Agent
│       ├── Program.cs                  #   Entry point, JWT auth, SDK endpoints
│       ├── BlogCopilotAgent.cs         #   Thin proxy: Activity → A2A → response
│       ├── AspNetExtensions.cs         #   JWT Bearer auth for Bot Framework tokens
│       ├── Services/OrchestratorA2AClient.cs  # A2A client with typing indicator callbacks
│       └── appPackage/                 #   Teams manifest, icons, packaging
│
├── tests/
│   ├── Orchestrator.Tests/             # Unit tests for Layer 1
│   ├── WebChat.Tests/                  # Unit tests for Layer 2
│   ├── CopilotAgent.Tests/            # Unit tests for Layer 3
│   └── Integration.Tests/             # Cross-layer integration tests
│
├── scripts/
│   ├── register-bot.ps1               # Azure Bot + Entra ID provisioning
│   ├── package-teams-app.ps1          # Teams app ZIP builder
│   └── diagnose-teams.ps1            # Connectivity diagnostic tool
│
├── docs/
│   ├── Architecture-and-Flow.md       # Full architecture & flow documentation
│   ├── cea-deployment-checklist.md    # 80+ item deployment readiness checklist
│   └── diagrams/                      # Mermaid source + rendered PNG/SVG
```

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure subscription](https://azure.microsoft.com/free/) (for AI Foundry and Bot Service)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login`)
- An Azure AI Foundry project with a GPT-4o deployment

**For M365 deployment (Layer 3) only:**
- Azure Bot registration + Entra ID app registration
- M365 Copilot license on the target tenant
- Teams admin permissions for app sideloading

---

## Getting Started

### 1. Clone the repo

```bash
git clone https://github.com/llowevad/SKCopilot-v2.git
cd SKCopilot-v2
```

### 2. Set up environment variables

```bash
cp Sample.env .env
```

Edit `.env` and fill in your Azure resource values. The `.env` file is gitignored and will never be published. All scripts in `/scripts` auto-load values from `.env`.

### 3. Configure the Orchestrator (Layer 1)

Set your Azure AI Foundry connection details in `src/Orchestrator/appsettings.json` or via environment variables:

```json
{
  "Foundry": {
    "Endpoint": "<your-azure-ai-foundry-project-endpoint>",
    "Model": "gpt-4o"
  }
}
```

### 4. Run the Orchestrator

```bash
cd src/Orchestrator
dotnet run
```

The A2A endpoint will be available at `http://localhost:5100/a2a/orchestrator`.

### 5. Run the Web Chat (Layer 2)

In a separate terminal:

```bash
cd src/WebChat
dotnet run
```

Open `https://localhost:5200` in your browser and start chatting — this calls the same A2A endpoint that M365 Copilot uses, proving the orchestrator is channel-agnostic.

### 6. (Optional) Deploy CopilotAgent to M365 (Layer 3)

This requires additional Azure infrastructure. See the **[Deployment Quick Start](DEPLOYMENT_QUICKSTART.md)** for step-by-step instructions:

1. **Register Azure Bot** — `scripts/register-bot.ps1` automates Entra ID app + Bot Service creation
2. **Configure CopilotAgent** — set ClientId, ClientSecret, TenantId in user secrets (dev) or [Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/general/overview) (production)
3. **Deploy to Azure App Service** — `dotnet publish` + deploy
4. **Package & sideload the Teams app** — `scripts/package-teams-app.ps1` builds the manifest ZIP
5. **Verify connectivity** — `scripts/diagnose-teams.ps1` checks every configuration point

> ⚠️ For a comprehensive 80+ item checklist covering Entra ID, Bot Service, manifest, SDK config, App Service, and tenant admin requirements, see **[docs/cea-deployment-checklist.md](docs/cea-deployment-checklist.md)**.

---

## Key Design Decisions

1. **Separation of concerns** — AI logic (Agent Framework) is completely separated from channel connectivity (M365 Agents SDK). The orchestrator is independently deployable and testable.

2. **Protocol bridge, not monolith** — The CEA contains zero business logic. It's a thin adapter between two protocol worlds. You can swap the orchestrator, add agents, or change the workflow without touching the M365 layer.

3. **Two independent clients** — Both WebChat and CopilotAgent call the same A2A endpoint, proving the orchestrator is truly channel-agnostic.

4. **Typing indicators for long workflows** — Multi-agent workflows take 30-90 seconds. The CEA sends typing activities every ~8 seconds to prevent Teams/Copilot from timing out.

---

## Alternatives Considered

The CEA thin proxy was chosen over three alternatives:

| Approach | Status | Why Not (Today) |
|----------|--------|-----------------|
| **Declarative Agent** (API plugin actions) | GA | Subject to Copilot orchestration — cannot guarantee invocation, may reformulate/truncate responses |
| **Copilot Studio + A2A Connector** | Preview | Subject to Studio orchestration + Power Platform performance/latency/throttling |
| **AI Foundry Agent** | GA (core) / Preview (advanced) | Core Agent Service is GA, but advanced capabilities (hosted agents, multi-agent orchestration, M365 channel connectors) are still in preview — not all pieces needed for this solution are production-ready |

The CEA was selected for: production readiness (GA), full workflow control, connection lifecycle management, channel-agnostic design, debuggability, and cost.

> 📖 For the full analysis with pros/cons tables, see the **[Alternatives Considered](docs/Architecture-and-Flow.md#alternatives-considered)** section of the architecture document.

---

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture & Flow](docs/Architecture-and-Flow.md) | Full architecture deep-dive, message flow, solution structure, alternatives analysis |
| [CEA Deployment Checklist](docs/cea-deployment-checklist.md) | 80+ item checklist covering every configuration point for M365 deployment |
| [Deployment Quick Start](DEPLOYMENT_QUICKSTART.md) | Abbreviated step-by-step M365/Teams deployment guide |
| [Architecture Proposal](ARCHITECTURE.md) | Original architecture proposal and design decisions |

---

## Running Tests

```bash
dotnet test SKCopilot-v2.sln
```

Tests cover all three layers plus cross-layer integration:
- **Orchestrator.Tests** — workflow composition, agent setup, A2A response handling
- **WebChat.Tests** — A2A client JSON-RPC construction, SSE parsing, error handling
- **CopilotAgent.Tests** — message handling, typing indicators, token validation config
- **Integration.Tests** — end-to-end pipeline: A2A request → workflow → response

---

## Deployment Topology

```
Azure App Service                     Azure Bot Service          M365 Admin
┌──────────────────────────┐     ┌──────────────────────┐    ┌─────────────────┐
│ Orchestrator  (Port 5100)│     │ Bot Registration      │    │ Teams App        │
│   └─ /a2a/orchestrator   │     │   └─ Messaging endpt  │    │   └─ manifest.zip│
│                          │     │   └─ Teams channel     │    │   └─ Sideloaded  │
│ CopilotAgent  (Port 5300)│◄────│   └─ JWT signing      │    │     or published │
│   └─ /api/messages       │     └──────────────────────┘    └─────────────────┘
│                          │              ▲                          ▲
│ WebChat       (Port 5200)│              │                          │
│   └─ Blazor UI           │         Entra ID                  Admin Center
└──────────────────────────┘     (App Registration)         (App Catalog)
```

---

## Disclaimer

This project is provided **as-is** as a reference implementation and sample for educational and demonstration purposes only. It is **not intended for production use** without thorough review, testing, and hardening appropriate to your environment.

By using this code, you accept full responsibility for any modifications, deployments, and outcomes. The authors make no warranties — express or implied — regarding the suitability, reliability, or security of this solution for any particular purpose. Use of Azure services, M365 Copilot, and related platforms is subject to their respective terms of service and licensing agreements.

> 📋 **In short:** Learn from it, build on it, but validate everything before relying on it.
