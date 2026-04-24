using SKCopilotV2.WebChat.Components;
using SKCopilotV2.WebChat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register the A2A client for calling the orchestrator
builder.Services.AddHttpClient<OrchestratorClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5100");
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
