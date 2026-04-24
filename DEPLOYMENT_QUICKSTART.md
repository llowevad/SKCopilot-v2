# M365 Teams App Deployment Quick Start

This is your fast-track guide to deploying SKCopilot-v2 to Microsoft 365 Copilot and Teams.

## Prerequisites Checklist

- [ ] Azure subscription access (Contributor role)
- [ ] Azure AD access (Application Administrator role)
- [ ] Azure CLI installed and authenticated (`az login`)
- [ ] PowerShell 7+ installed
- [ ] CopilotAgent code ready to deploy

## 5-Step Deployment

### Step 1: Register Azure Bot (5 minutes)

```powershell
cd C:\CodeSamples\SKCopilot-v2

.\scripts\register-bot.ps1 `
  -TenantId "YOUR-TENANT-ID" `
  -SubscriptionId "YOUR-SUBSCRIPTION-ID"
```

**Output:** Bot ID, Client Secret (save this!), Resource Group, Bot Name

**Important:** Copy the Client Secret immediately — it cannot be retrieved later.

### Step 2: Configure CopilotAgent (2 minutes)

Store the bot credentials securely using User Secrets (development):

```powershell
cd src\CopilotAgent

dotnet user-secrets set "Connections:BotServiceConnection:Settings:ClientId" "YOUR-BOT-ID"
dotnet user-secrets set "Connections:BotServiceConnection:Settings:ClientSecret" "YOUR-CLIENT-SECRET"
dotnet user-secrets set "Connections:BotServiceConnection:Settings:TenantId" "YOUR-TENANT-ID"
```

> ⚠️ **Production:** Use [Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/general/overview) to manage secrets and keys. Never store production credentials in appsettings.json, user secrets, or environment variables. Key Vault provides centralized secret management, access auditing, and automatic rotation.

### Step 3: Deploy CopilotAgent to Azure (10 minutes)

```powershell
cd src\CopilotAgent

# Build and publish
dotnet publish -c Release -o publish

# Create deployment package
Compress-Archive -Path publish\* -DestinationPath publish.zip -Force

# Deploy to App Service (replace with your app service name)
az webapp deployment source config-zip `
  --resource-group <your-resource-group> `
  --name <your-copilot-app> `
  --src publish.zip
```

**Verify:** Visit `https://<your-copilot-app>.azurewebsites.net/api/messages` (should return 405 Method Not Allowed)

### Step 4: Build Teams App Package (1 minute)

```powershell
cd C:\CodeSamples\SKCopilot-v2

.\scripts\package-teams-app.ps1 `
  -BotId "YOUR-BOT-ID-FROM-STEP-1" `
  -BotDomain "<your-copilot-app>.azurewebsites.net"
```

**Output:** `src/CopilotAgent/appPackage/SKCopilotV2.zip`

### Step 5: Sideload in Teams (2 minutes)

1. Open **Microsoft Teams** (desktop or web)
2. Click **Apps** (left sidebar)
3. Click **Manage your apps** (bottom of apps list)
4. Click **Upload an app** → **Upload a custom app**
5. Select `SKCopilotV2.zip` from `src/CopilotAgent/appPackage/`
6. Click **Add**

**Test:** Open the bot in Teams and send: "Write a blog post about AI"

## Testing in M365 Copilot

After sideloading in Teams:

1. Open **Microsoft 365 Copilot** (web or app)
2. Start a new conversation
3. Click the **plugins** icon
4. Enable **SK Copilot v2**
5. Ask: "Write a blog post about quantum computing"

The bot will route your request through the orchestrator (Layer 1), which coordinates the BlogWriter and Reviewer agents.

## Troubleshooting

### Bot Not Responding

**Check messaging endpoint:**
```powershell
az bot show `
  --resource-group <your-resource-group> `
  --name <your-bot-name> `
  --query "properties.endpoint"
```

Should be: `https://<your-copilot-app>.azurewebsites.net/api/messages`

**Check app service logs:**
```powershell
az webapp log tail `
  --resource-group <your-resource-group> `
  --name <your-copilot-app>
```

### Sideloading Fails

**Validate manifest:**
```powershell
Get-Content src\CopilotAgent\appPackage\manifest.json | ConvertFrom-Json
```

Should show your Bot ID (not `${{BOT_ID}}`).

**Check package contents:**
```powershell
Expand-Archive src\CopilotAgent\appPackage\SKCopilotV2.zip -DestinationPath temp-check
Get-ChildItem temp-check
# Should contain: manifest.json, color.png, outline.png
```

### Orchestrator Connection Issues

If bot responds but can't reach orchestrator:

1. Check `appsettings.json` in deployed CopilotAgent
2. Verify orchestrator URL is accessible from Azure App Service
3. Test orchestrator endpoint: `curl https://YOUR-ORCHESTRATOR/a2a/orchestrator/.well-known/agent-card.json`

## Next Steps After Deployment

1. **Replace placeholder icons** with branded versions (see `appPackage/README.md`)
2. **Configure production secrets** in Azure Key Vault
3. **Upload to org app catalog** for broader distribution (optional)
4. **Monitor usage** in Azure Portal → Bot resource → Analytics

## Architecture Reference

```
User (M365 Copilot/Teams)
    ↓
Layer 3: CopilotAgent (M365 Agents SDK thin proxy)
    ↓ A2A Protocol (SSE streaming)
Layer 1: Orchestrator (Agent Framework)
    ↓ Workflow orchestration
BlogWriter Agent + Reviewer Agent
    ↓
Microsoft AI Foundry Project (GPT-4o)
```

## Documentation

- **Full setup guide:** `src/CopilotAgent/appPackage/README.md`
- **Architecture details:** `ARCHITECTURE.md` (repo root)

## Support

For deployment issues:
- Check logs in Azure Portal
- Review appPackage/README.md troubleshooting section
- Open an issue on the [GitHub repository](https://github.com/llowevad/SKCopilot-v2/issues)

---

**Quick deployment time:** ~20 minutes from Azure Bot registration to live in M365 Copilot
