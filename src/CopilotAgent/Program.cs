// Layer 3: M365 Copilot Agent — Custom Engine Agent (thin proxy)
// Receives Activity messages from Copilot/Teams → forwards to orchestrator via A2A → returns response.
// Contains NO business logic, NO LLM calls, NO agent orchestration.

using Microsoft.Agents.Builder;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using SKCopilotV2.CopilotAgent;
using SKCopilotV2.CopilotAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// M365 Agents SDK registration
builder.Services.AddHttpClient();
builder.AddAgentApplicationOptions();
builder.AddAgent<BlogCopilotAgent>();
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// ASP.NET JWT Bearer auth for Bot Framework Service and Entra ID tokens.
// Reads TokenValidation config section (Audiences, TenantId) to validate inbound JWTs.
// Without this, BFS token probes fail silently and Teams/M365 traffic never reaches the endpoint.
builder.Services.AddAgentAspNetAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// Register A2A client for calling Layer 1 orchestrator
builder.Services.AddHttpClient<OrchestratorA2AClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5100");
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// Request logging middleware — traces every inbound request for debugging
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestTrace");
    logger.LogInformation(
        ">>> INBOUND {Method} {Path} from {RemoteIp} | Auth: {AuthHeader}",
        context.Request.Method,
        context.Request.Path,
        context.Connection.RemoteIpAddress,
        context.Request.Headers.Authorization.FirstOrDefault()?.Substring(0, Math.Min(50, context.Request.Headers.Authorization.FirstOrDefault()?.Length ?? 0)) ?? "(none)");
    
    await next();
    
    logger.LogInformation(
        "<<< RESPONSE {StatusCode} for {Method} {Path}",
        context.Response.StatusCode,
        context.Request.Method,
        context.Request.Path);
});

// Enable ASP.NET authentication and authorization middleware (must be before endpoint mapping)
app.UseAuthentication();
app.UseAuthorization();

// Health check for Azure warmup probe
app.MapGet("/", () => Results.Ok(new { status = "healthy", service = "CopilotAgent" }));

// Diagnostic endpoint to verify /api/messages is reachable
app.MapGet("/api/messages", () => Results.Ok(new { status = "reachable", endpoint = "/api/messages", note = "POST with Bot Framework activity to use" }));

// No-auth diagnostic endpoint — catches ANY POST to prove traffic is arriving.
// If Teams traffic hits this but not /api/messages, the issue is auth.
// If Teams traffic doesn't hit this either, the issue is routing/app-install.
app.MapPost("/api/ping", async (HttpContext ctx) =>
{
    var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Diagnostic");
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    logger.LogWarning(
        "🏓 PING received: {Method} {Path} | ContentType: {CT} | BodyLength: {Len} | Auth: {Auth}",
        ctx.Request.Method,
        ctx.Request.Path,
        ctx.Request.ContentType,
        body.Length,
        ctx.Request.Headers.Authorization.FirstOrDefault()?.Substring(0, Math.Min(50, ctx.Request.Headers.Authorization.FirstOrDefault()?.Length ?? 0)) ?? "(none)");
    return Results.Ok(new { status = "pong", bodyLength = body.Length, timestamp = DateTime.UtcNow });
}).AllowAnonymous();

// M365 Agents SDK endpoint — receives activities from Copilot/Teams channels
// Uses SDK endpoint mapping with auth enforcement in production
app.MapAgentEndpoints(requireAuth: !app.Environment.IsDevelopment());

app.Run();
