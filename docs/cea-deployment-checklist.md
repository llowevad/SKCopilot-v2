# M365 Custom Engine Agent (CEA) — Deployment Checklist

> **Living document.** Last updated: 2026-04-23  
> **Applies to:** SKCopilot-v2 CopilotAgent (Layer 3)  
> **Bot App ID:** `<your-bot-app-id>`  
> **Tenant:** `<your-tenant-id>`

---

## 1. Azure AD (Entra ID) App Registration

| # | Requirement | Details | Status |
|---|-------------|---------|--------|
| 1.1 | **App Registration exists** | Register app in Entra ID. Record Application (client) ID and Directory (tenant) ID. | ✅ DONE |
| 1.2 | **Supported account types** | SingleTenant for org-internal CEA. Azure now rejects MultiTenant bot creation. | ✅ DONE |
| 1.3 | **Application ID URI** | Must be set to `api://<bot-app-id>` (e.g., `api://<your-bot-app-id>`). Required for SSO token validation. | 🔄 JUST FIXED |
| 1.4 | **Expose an API — `access_as_user` scope** | Create delegated scope: `api://<bot-app-id>/access_as_user`. Display name: "Access as user". Admin+User consent. | 🔄 JUST FIXED |
| 1.5 | **Pre-authorized client applications** | Add ALL 7 M365 client app IDs authorized for the `access_as_user` scope: | 🔄 JUST FIXED |
|     | | `1fec8e78-bce4-4aaf-ab1b-5451cc387264` — Teams desktop/mobile | |
|     | | `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` — Teams web | |
|     | | `d3590ed6-52b3-4102-aeff-aad2292ab01c` — Outlook desktop | |
|     | | `bc59ab01-8403-45c6-8796-ac3ef710b3e3` — Teams iOS | |
|     | | `0ec893e0-5785-4de6-99da-4edcb3613e91` — Teams Android | |
|     | | `27922004-5251-4030-b22d-91ecd9a37ea4` — Teams macOS | |
|     | | `4765445b-32c6-49b0-83e6-1d93765276ca` — Office.com / M365 web | |
| 1.6 | **Redirect URI** | Add Web redirect URI: `https://token.botframework.com/.auth/web/redirect`. Required for Bot Framework OAuth/SSO flow. | ❌ MISSING |
| 1.7 | **Client secret** | Generated and stored in Azure App Service settings (Connections config). | ✅ DONE |
| 1.8 | **API Permissions (delegated)** | At minimum: `openid`, `profile`, `User.Read` (Microsoft Graph). Add more as needed for CEA features. | ⚠️ UNKNOWN |
| 1.9 | **Admin consent granted** | Tenant admin must grant consent for all API permissions. Required before end users can interact. | ⚠️ UNKNOWN |
| 1.10 | **Implicit grant / hybrid flows** | Enable `ID tokens` under Authentication → Implicit grant if needed for SSO. | ⚠️ UNKNOWN |

---

## 2. Azure Bot Service

| # | Requirement | Details | Status |
|---|-------------|---------|--------|
| 2.1 | **Bot resource exists** | `<your-bot-name>` in `<your-resource-group>`. SKU: F0. | ✅ DONE |
| 2.2 | **App ID matches** | Bot uses `<your-bot-app-id>`. | ✅ DONE |
| 2.3 | **Messaging endpoint** | `https://<your-copilot-app>.azurewebsites.net/api/messages` | ✅ DONE |
| 2.4 | **Microsoft Teams channel enabled** | Required for Teams delivery AND Copilot. | ✅ DONE |
| 2.5 | **M365 Copilot channel** | Copilot messages route through the Teams channel infrastructure. Verify the bot shows as available in Copilot after app package upload. No separate "M365Extensions" channel needed. | ⚠️ UNKNOWN |
| 2.6 | **WebChat / DirectLine channels** | Useful for testing but not required for Copilot. | ✅ DONE |
| 2.7 | **OAuth connection settings** | If using SSO, an OAuth connection must be configured on the Bot resource pointing to the app registration. Connection name must match `AgentApplication.UserAuthorization.Handlers.SSO.Settings.AzureBotOAuthConnectionName` in appsettings. | ❌ MISSING (if SSO needed) |
| 2.8 | **Bot type = SingleTenant** | Must match the app registration account type. | ✅ DONE |

---

## 3. App Package Manifest

| # | Requirement | Details | Status |
|---|-------------|---------|--------|
| 3.1 | **Schema version ≥ v1.19** | `copilotAgents` requires v1.19+, `defaultInstallScope: "copilot"` requires v1.21+. We use v1.21. | ✅ DONE |
| 3.2 | **`id` = Bot App ID** | `<your-bot-app-id>` | ✅ DONE |
| 3.3 | **`bots[0].botId` = Bot App ID** | Same as manifest `id`. | ✅ DONE |
| 3.4 | **`bots[0].scopes` includes `"copilot"`** | Required for CEA. | ✅ DONE |
| 3.5 | **`copilotAgents.customEngineAgents`** | Array with `{ "type": "bot", "id": "<bot-id>" }`. | ✅ DONE |
| 3.6 | **`defaultInstallScope: "copilot"`** | Makes Copilot the primary install target. | ✅ DONE |
| 3.7 | **`webApplicationInfo` section** | **CRITICAL for SSO/token auth.** Must include: `{ "id": "<bot-app-id>", "resource": "api://<bot-app-id>" }`. Links manifest to Entra app registration. Without this, Copilot cannot acquire tokens for the bot. | ❌ MISSING |
| 3.8 | **`validDomains`** | Must include the bot's FQDN: `<your-copilot-app>.azurewebsites.net`. | ✅ DONE |
| 3.9 | **Icons** | `color.png` (192×192), `outline.png` (32×32). | ✅ DONE |
| 3.10 | **`commandLists`** | Commands defined with `"copilot"` scope. | ✅ DONE |
| 3.11 | **`developer` URLs** | `websiteUrl`, `privacyUrl`, `termsOfUseUrl` must be valid HTTPS. | ⚠️ UNKNOWN (URLs may 404) |
| 3.12 | **No placeholder tokens** | Packaged zip must have real values, no `${{...}}` tokens. | ✅ DONE |

---

## 4. M365 Agents SDK Code (CopilotAgent)

| # | Requirement | Details | Status |
|---|-------------|---------|--------|
| 4.1 | **NuGet: `Microsoft.Agents.Hosting.AspNetCore`** | Primary SDK package for bot hosting. | ✅ DONE |
| 4.2 | **NuGet: `Microsoft.Agents.Authentication.Msal`** | MSAL auth provider. Required for `Connections` auth pattern. | ✅ DONE |
| 4.3 | **`AgentApplication` base class** | Modern M365 SDK pattern (not ActivityHandler). | ✅ DONE |
| 4.4 | **`MapPost("/api/messages", ...)`** | Endpoint for Bot Framework activities. | ✅ DONE |
| 4.5 | **`Connections` config in appsettings** | `BotServiceConnection` with `Assembly`, `Type`, `Settings` (ClientId, ClientSecret, TenantId). | ✅ DONE |
| 4.6 | **`TokenValidation.Audiences`** | Must include bot App ID in appsettings. Validates inbound JWT token audience claim. Without this, the SDK may reject valid tokens from Copilot. | ❌ MISSING |
| 4.7 | **`Connections.Settings.Scopes`** | Should include `https://api.botframework.com/.default` for Bot Framework token exchange. | ❌ MISSING |
| 4.8 | **`Connections.Settings.AuthType`** | Explicit auth type. We use `MsalAuth` via Assembly/Type, but explicit `AuthType: "ClientSecret"` may be needed. | ⚠️ UNKNOWN |
| 4.9 | **Health check endpoint** | `GET /` returns 200. | ✅ DONE |
| 4.10 | **Typing indicators** | Send typing activities to prevent 45s Copilot timeout. | ✅ DONE |
| 4.11 | **Request logging middleware** | Traces inbound requests for debugging. | ✅ DONE |

---

## 5. Azure App Service Configuration

| # | Requirement | Details | Status |
|---|-------------|---------|--------|
| 5.1 | **HTTPS enforced** | Azure App Service default. All traffic over HTTPS. | ✅ DONE |
| 5.2 | **Always On = true** | Prevents cold starts. Required on B1+ plan. | ⚠️ UNKNOWN |
| 5.3 | **App Settings — Connection config** | `Connections__BotServiceConnection__Settings__ClientId`, `__ClientSecret`, `__TenantId` set via double-underscore notation. | ✅ DONE |
| 5.4 | **App Settings — Orchestrator URL** | `Orchestrator__BaseUrl` points to Layer 1 App Service. | ✅ DONE |
| 5.5 | **No Kestrel port in production** | Azure manages ports via `ASPNETCORE_URLS`. Kestrel config only in `appsettings.Development.json`. | ✅ DONE |
| 5.6 | **CORS** | Not typically needed for bot messaging (server-to-server). Only needed if bot serves web content. | ✅ N/A |
| 5.7 | **Health check path** | Configure Azure health check to `GET /`. | ⚠️ UNKNOWN |
| 5.8 | **Managed Identity** | Consider for production instead of client secrets. ProxyAgent sample uses `UserManagedIdentity` auth type. | ⚠️ FUTURE |
| 5.9 | **Azure Key Vault** | Use [Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/general/overview) for production secrets (ClientSecret, connection strings). Provides centralized management, access auditing, and automatic rotation. Reference secrets via App Configuration or Key Vault references in App Settings. | ⚠️ RECOMMENDED |
| 5.10 | **App restart after config changes** | Always restart after updating app settings. Config changes may not auto-apply. | ⚠️ VERIFY |

---

## 6. M365 Admin / Tenant Requirements

| # | Requirement | Details | Status |
|---|-------------|---------|--------|
| 6.1 | **Microsoft 365 Copilot license** | Tenant must have M365 Copilot licensing to enable CEA features. Without this, Copilot will not route to custom agents. | ⚠️ UNKNOWN — VERIFY |
| 6.2 | **Sideloading enabled** | Teams Admin Center → Setup policies → "Upload custom apps" = ON. | ⚠️ UNKNOWN |
| 6.3 | **App uploaded to M365 Admin Center** | Upload via Admin Center → Copilot → Agents & connectors → Upload custom agent (ZIP). | ⚠️ UNKNOWN |
| 6.4 | **Admin consent for app** | Grant admin consent for all API permissions on the app registration. | ⚠️ UNKNOWN |
| 6.5 | **Copilot extensibility enabled** | M365 Admin Center → Copilot settings → Extensibility must be turned ON. Agents must be allowed. | ⚠️ UNKNOWN |
| 6.6 | **User assignment** | Agent must be assigned to test users (or "Everyone") in admin center. | ⚠️ UNKNOWN |
| 6.7 | **App approval** | Custom agents may need approval in Org Catalog before availability. | ⚠️ UNKNOWN |

---

## 7. Network & Security

| # | Requirement | Details | Status |
|---|-------------|---------|--------|
| 7.1 | **TLS 1.2+** | Azure App Service enforces this by default. | ✅ DONE |
| 7.2 | **Outbound to Bot Framework** | App Service must reach `https://login.microsoftonline.com`, `https://api.botframework.com`, `https://smba.trafficmanager.net`. | ✅ DONE (no firewall) |
| 7.3 | **Outbound to Orchestrator** | CopilotAgent must reach Layer 1 at `https://<your-orchestrator-app>.azurewebsites.net`. | ✅ DONE |
| 7.4 | **No IP restrictions** | Bot Framework Service uses dynamic IPs. Don't restrict inbound by IP. | ✅ DONE |

---

## Priority Fix List (Ordered by Likelihood of Being Current Blocker)

### 🔴 P0 — Most Likely Blockers (Fix Immediately)

1. **Add `webApplicationInfo` to manifest** (3.7)  
   Without this, Copilot cannot establish SSO token flow to the bot. This is the single most likely reason messages never reach the endpoint — Copilot tries to acquire a token, fails silently, and never sends the activity.
   ```json
   "webApplicationInfo": {
     "id": "<your-bot-app-id>",
     "resource": "api://<your-bot-app-id>"
   }
   ```

2. **Add `TokenValidation.Audiences` to appsettings.json** (4.6)  
   SDK needs to know which audience claims to accept. Without it, valid tokens from Copilot may be rejected as unauthorized.
   ```json
   "TokenValidation": {
     "Audiences": ["<your-bot-app-id>"]
   }
   ```

3. **Add redirect URI to app registration** (1.6)  
   `https://token.botframework.com/.auth/web/redirect` — Required for Bot Framework's OAuth token exchange. Without it, the token flow breaks.

### 🟡 P1 — High Likelihood (Verify and Fix)

4. **Verify M365 Copilot licensing on tenant** (6.1)  
   If the tenant lacks Copilot license, CEA features are disabled at the platform level. No messages will route to custom agents.

5. **Verify admin consent granted** (1.9 / 6.4)  
   App permissions (openid, profile, User.Read) need admin consent before any user can trigger the agent.

6. **Verify Copilot extensibility is enabled** (6.5)  
   Admin Center → Copilot → Settings must allow custom agents / extensibility.

7. **Add `Scopes` to Connections config** (4.7)  
   ```json
   "Scopes": ["https://api.botframework.com/.default"]
   ```

8. **Verify sideloading policy** (6.2)  
   Teams Admin Center → Setup policies → Upload custom apps = ON.

### 🟢 P2 — Good Practice (Non-Blocking)

9. **Verify Always On** (5.2) — Prevents cold-start delays.
10. **Verify developer URLs** (3.11) — Privacy/terms URLs must resolve.
11. **App restart after config changes** (5.10) — Belt and suspenders.
12. **Consider Managed Identity** (5.8) — Eliminates secret rotation risk.
13. **Use Azure Key Vault** (5.9) — Centralized secret management with auditing and rotation.

---

## Quick Verification Commands

```bash
# Check if bot endpoint is reachable
curl -s https://<your-copilot-app>.azurewebsites.net/
curl -s https://<your-copilot-app>.azurewebsites.net/api/messages

# Check Azure Bot registration
az bot show --name <your-bot-name> --resource-group <your-resource-group>

# Check bot channels
az bot show --name <your-bot-name> --resource-group <your-resource-group> --msbot | Select-String "channel"

# Check app registration
az ad app show --id <your-bot-app-id>

# Verify redirect URIs
az ad app show --id <your-bot-app-id> --query "web.redirectUris"

# Check Always On
az webapp config show --name <your-copilot-app> --resource-group <your-resource-group> --query "alwaysOn"
```
