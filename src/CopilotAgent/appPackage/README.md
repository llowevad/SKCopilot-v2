# SKCopilot-v2 Teams App Package

This directory contains the Microsoft Teams app package for SKCopilot-v2, enabling deployment to Microsoft 365 Copilot and Teams channels.

## Contents

- **`manifest.json`** — Teams app manifest (v1.17 schema) with placeholder tokens
- **`color.png`** — 192x192 color app icon (generated placeholder)
- **`outline.png`** — 32x32 outline icon (generated placeholder)
- **`generate-icons.ps1`** — PowerShell script to generate placeholder icons
- **`env/.env.dev`** — Environment configuration template
- **`SKCopilotV2.zip`** — Built app package (created by `package-teams-app.ps1`)

## Prerequisites

1. **Azure Subscription** with permissions to:
   - Create App Registrations (Application Administrator role)
   - Create Azure Bot resources (Contributor role)
   
2. **Azure CLI** installed and authenticated (`az login`)

3. **PowerShell 7+** for running scripts

4. **Deployed CopilotAgent** — The bot must be deployed to Azure App Service before sideloading

## Setup Instructions

### 1. Register Azure Bot

Run the bot registration script to create the Azure AD app registration and Azure Bot resource:

```powershell
# Navigate to repository root
cd C:\CodeSamples\SKCopilot-v2

# Run bot registration script
.\scripts\register-bot.ps1 `
  -TenantId "your-tenant-id" `
  -SubscriptionId "your-subscription-id"
```

**Script Output:**
- App ID (Bot ID)
- Client Secret (save this securely!)
- Resource group name
- Bot name

**Important:** Save the Client Secret immediately — it cannot be retrieved later. Store it in:
- Azure Key Vault (recommended for production)
- .NET User Secrets (development)
- Never commit secrets to source control

### 2. Configure CopilotAgent

Update `src/CopilotAgent/appsettings.json` or use User Secrets:

```json
{
  "Connections": {
    "BotServiceConnection": {
      "Settings": {
        "ClientId": "<bot-app-id-from-registration>",
        "ClientSecret": "<from-user-secrets-or-key-vault>",
        "TenantId": "<your-azure-ad-tenant-id>"
      }
    }
  }
}
```

**Using User Secrets (recommended for development):**

```powershell
cd src\CopilotAgent
dotnet user-secrets set "Connections:BotServiceConnection:Settings:ClientId" "<bot-app-id>"
dotnet user-secrets set "Connections:BotServiceConnection:Settings:ClientSecret" "<client-secret>"
dotnet user-secrets set "Connections:BotServiceConnection:Settings:TenantId" "<tenant-id>"
```

### 3. Deploy CopilotAgent to Azure

Deploy the CopilotAgent project to Azure App Service:

```powershell
# From repository root
cd src\CopilotAgent

# Build and publish
dotnet publish -c Release -o publish

# Create deployment package
Compress-Archive -Path publish\* -DestinationPath publish.zip -Force

# Deploy to Azure App Service (using Azure CLI)
az webapp deployment source config-zip `
  --resource-group <your-resource-group> `
  --name <your-copilot-app> `
  --src publish.zip
```

**Verify Deployment:**
- Test endpoint: `https://<your-copilot-app>.azurewebsites.net/api/messages`
- Should return HTTP 405 (Method Not Allowed) for GET requests — this confirms the endpoint exists

### 4. Build Teams App Package

Generate the Teams app package (.zip) with your bot credentials:

```powershell
# From repository root
.\scripts\package-teams-app.ps1 `
  -BotId "<bot-app-id-from-registration>" `
  -BotDomain "<your-copilot-app>.azurewebsites.net"
```

**Output:** `src/CopilotAgent/appPackage/SKCopilotV2.zip`

This script:
- Replaces `${{BOT_ID}}` and `${{BOT_DOMAIN}}` placeholders in manifest.json
- Generates placeholder icons if missing
- Creates a .zip package with manifest + icons
- Validates package structure

### 5. Sideload App in Teams

**Option A: Sideload for Personal Testing**

1. Open Microsoft Teams desktop or web app
2. Navigate to **Apps** in the left sidebar
3. Click **Manage your apps** (bottom of apps list)
4. Click **Upload an app** → **Upload a custom app**
5. Select `SKCopilotV2.zip` from `src/CopilotAgent/appPackage/`
6. Click **Add** to install the bot

**Option B: Upload to Organization App Catalog**

1. Go to [Teams Admin Center](https://admin.teams.microsoft.com/)
2. Navigate to **Teams apps** → **Manage apps**
3. Click **Upload new app**
4. Select `SKCopilotV2.zip`
5. Once approved, the app will be available to your organization

### 6. Test in Microsoft 365 Copilot

After sideloading:

1. Open Microsoft 365 Copilot (web or app)
2. Start a new conversation
3. Enable **SK Copilot v2** from the plugins menu
4. Test with: "Write a blog post about AI in education"

The bot will route your message to the orchestrator, which coordinates the BlogWriter and Reviewer agents.

## Icon Customization

The included icons are **placeholders** created by `generate-icons.ps1`. For production:

1. Create branded icons:
   - **color.png** — 192x192 pixels, PNG format, brand colors
   - **outline.png** — 32x32 pixels, PNG format, white outline on transparent background

2. Replace the placeholder icons in `src/CopilotAgent/appPackage/`

3. Rebuild the package:
   ```powershell
   .\scripts\package-teams-app.ps1 -BotId "<bot-id>" -BotDomain "<domain>"
   ```

## Troubleshooting

### Bot Not Responding in Teams

1. **Check App Service Logs:**
   ```powershell
   az webapp log tail --resource-group <your-resource-group> --name <your-copilot-app>
   ```

2. **Verify Messaging Endpoint:**
   - Azure Portal → Bot resource → Configuration
   - Endpoint should be: `https://<your-copilot-app>.azurewebsites.net/api/messages`

3. **Test Bot Framework Authentication:**
   - Use [Bot Framework Emulator](https://github.com/Microsoft/BotFramework-Emulator)
   - Connect with App ID and Client Secret

### Sideloading Fails

1. **Validate Manifest:**
   ```powershell
   # Manually validate the manifest JSON
   Get-Content src\CopilotAgent\appPackage\manifest.json | ConvertFrom-Json
   ```

2. **Check Package Contents:**
   ```powershell
   Expand-Archive src\CopilotAgent\appPackage\SKCopilotV2.zip -DestinationPath temp-check
   dir temp-check
   # Should contain: manifest.json, color.png, outline.png
   ```

3. **Teams Admin Policies:**
   - Ensure your org allows custom app uploads
   - Check with Teams admin if sideloading is blocked

### Orchestrator Connection Issues

If the bot responds but can't reach the orchestrator:

1. **Check Orchestrator Configuration:**
   - Verify `appsettings.json` in CopilotAgent has correct orchestrator URL
   - For Azure deployment, update to production orchestrator endpoint

2. **Network Connectivity:**
   - Test orchestrator endpoint from Azure App Service console
   - Verify firewall rules if orchestrator is behind VNet

## File Structure

```
src/CopilotAgent/appPackage/
├── manifest.json              # Teams app manifest (with placeholders)
├── color.png                  # 192x192 color icon (generated)
├── outline.png                # 32x32 outline icon (generated)
├── generate-icons.ps1         # Icon generation script
├── SKCopilotV2.zip           # Built app package (created by build script)
├── env/
│   └── .env.dev              # Environment variable template
└── README.md                 # This file

scripts/
├── register-bot.ps1          # Azure Bot registration
└── package-teams-app.ps1     # Build app package
```

## References

- [Teams App Manifest Schema](https://learn.microsoft.com/en-us/microsoftteams/platform/resources/schema/manifest-schema)
- [Azure Bot Service Documentation](https://learn.microsoft.com/en-us/azure/bot-service/)
- [Microsoft Agents SDK](https://learn.microsoft.com/en-us/microsoft-agents-sdk/)
- [Teams App Development](https://learn.microsoft.com/en-us/microsoftteams/platform/)
- [M365 Copilot Custom Engine Agents](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/overview-custom-engine-agent)

## Support

For issues specific to SKCopilot-v2 architecture or deployment, see:
- `ARCHITECTURE.md` in repository root
- Open an issue on the [GitHub repository](https://github.com/llowevad/SKCopilot-v2/issues)
