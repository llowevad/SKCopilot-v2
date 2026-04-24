<#
.SYNOPSIS
    Diagnoses Teams/M365 Copilot connectivity issues for SKCopilot-v2 CopilotAgent.
.DESCRIPTION
    Checks Azure Bot, Entra ID app registration, App Service, Teams channel,
    and provides a Teams deep link for direct testing.
#>

[CmdletBinding()]
param(
    [string]$BotName,
    [string]$ResourceGroup,
    [string]$AppId,
    [string]$Endpoint
)

# Load defaults from .env
$envVars = & "$PSScriptRoot\Load-Env.ps1"
if (-not $BotName)       { $BotName       = $envVars["BOT_NAME"]          ?? "<your-bot-name>" }
if (-not $ResourceGroup) { $ResourceGroup = $envVars["RESOURCE_GROUP"]    ?? "<your-resource-group>" }
if (-not $AppId)         { $AppId         = $envVars["BOT_APP_ID"]        ?? "<your-bot-app-id>" }
if (-not $Endpoint) {
    $domain = $envVars["COPILOT_APP_DOMAIN"] ?? "<your-copilot-app>.azurewebsites.net"
    $Endpoint = "https://$domain"
}

$ErrorActionPreference = "Continue"
$pass = 0; $fail = 0; $warn = 0

function Check([string]$Name, [bool]$Condition, [string]$Detail = "", [bool]$IsWarning = $false) {
    if ($Condition) {
        Write-Host "  ✅ $Name" -ForegroundColor Green
        if ($Detail) { Write-Host "     $Detail" -ForegroundColor Gray }
        $script:pass++
    } elseif ($IsWarning) {
        Write-Host "  ⚠️  $Name" -ForegroundColor Yellow
        if ($Detail) { Write-Host "     $Detail" -ForegroundColor Yellow }
        $script:warn++
    } else {
        Write-Host "  ❌ $Name" -ForegroundColor Red
        if ($Detail) { Write-Host "     $Detail" -ForegroundColor Red }
        $script:fail++
    }
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     SKCopilot-v2 Teams Connectivity Diagnostic             ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ─── 1. App Service Health ───
Write-Host "1. APP SERVICE HEALTH" -ForegroundColor Cyan
Write-Host "   ─────────────────" -ForegroundColor DarkGray
try {
    $health = Invoke-WebRequest -Uri "$Endpoint/" -UseBasicParsing -TimeoutSec 10
    Check "GET / → $($health.StatusCode)" ($health.StatusCode -eq 200) $health.Content
} catch {
    Check "GET / reachable" $false "Error: $($_.Exception.Message)"
}

try {
    Invoke-WebRequest -Uri "$Endpoint/api/messages" -Method POST -ContentType "application/json" -Body '{}' -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop | Out-Null
    Check "POST /api/messages → unexpected 2xx (auth may be disabled!)" $false "" $true
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    if ($status -eq 401) {
        Check "POST /api/messages → 401 (auth correctly rejecting)" $true
    } elseif ($status -eq 400) {
        Check "POST /api/messages → 400 (may indicate config issue)" $false "Expected 401 for unauthenticated request" $true
    } else {
        Check "POST /api/messages → $status" $false "Expected 401 for unauthenticated request"
    }
}

try {
    $ping = Invoke-WebRequest -Uri "$Endpoint/api/ping" -Method POST -ContentType "application/json" -Body '{"test":true}' -UseBasicParsing -TimeoutSec 10
    Check "POST /api/ping → $($ping.StatusCode) (diagnostic endpoint)" ($ping.StatusCode -eq 200) $ping.Content
} catch {
    Check "POST /api/ping reachable" $false "Diagnostic endpoint not deployed yet — deploy latest code first"
}

# ─── 2. Azure Bot Configuration ───
Write-Host ""
Write-Host "2. AZURE BOT CONFIGURATION" -ForegroundColor Cyan
Write-Host "   ───────────────────────" -ForegroundColor DarkGray
try {
    $bot = az bot show --resource-group $ResourceGroup --name $BotName -o json 2>$null | ConvertFrom-Json
    Check "Bot exists" ($null -ne $bot) "$($bot.name)"
    Check "Messaging endpoint" ($bot.properties.endpoint -eq "$Endpoint/api/messages") $bot.properties.endpoint
    Check "App ID matches" ($bot.properties.msaAppId -eq $AppId) $bot.properties.msaAppId
    Check "App type: SingleTenant" ($bot.properties.msaAppType -eq "SingleTenant") $bot.properties.msaAppType
} catch {
    Check "Azure Bot accessible" $false "Run 'az login' first"
}

# ─── 3. Teams Channel ───
Write-Host ""
Write-Host "3. TEAMS CHANNEL" -ForegroundColor Cyan
Write-Host "   ─────────────" -ForegroundColor DarkGray
try {
    $teams = az bot msteams show --resource-group $ResourceGroup --name $BotName -o json 2>$null | ConvertFrom-Json
    Check "Teams channel exists" ($null -ne $teams)
    Check "Teams channel enabled" ($teams.properties.properties.isEnabled -eq $true)
    Check "Provisioning succeeded" ($teams.properties.provisioningState -eq "Succeeded")
    
    $terms = $teams.properties.properties.acceptedTerms
    if ($terms -eq $true) {
        Check "Terms accepted" $true
    } else {
        Check "Terms accepted" $false "acceptedTerms=$terms — Go to Azure Portal > Bot > Channels > Teams and re-configure to accept terms" $true
    }
    
    Check "Commercial deployment" ($teams.properties.properties.deploymentEnvironment -eq "CommercialDeployment")
} catch {
    Check "Teams channel accessible" $false
}

# ─── 4. Entra ID App Registration ───
Write-Host ""
Write-Host "4. ENTRA ID APP REGISTRATION" -ForegroundColor Cyan
Write-Host "   ─────────────────────────" -ForegroundColor DarkGray
try {
    $app = az ad app show --id $AppId -o json 2>$null | ConvertFrom-Json
    Check "App registration exists" ($null -ne $app) $app.displayName
    Check "signInAudience: AzureADMyOrg" ($app.signInAudience -eq "AzureADMyOrg") $app.signInAudience
    
    $hasIdUri = ($app.identifierUris.Count -gt 0)
    Check "Application ID URI set" $hasIdUri ($app.identifierUris -join ", ")
    
    $hasScope = ($app.api.oauth2PermissionScopes.Count -gt 0)
    Check "Expose an API scope defined" $hasScope ($app.api.oauth2PermissionScopes.value -join ", ")
    
    $preAuthCount = $app.api.preAuthorizedApplications.Count
    Check "Pre-authorized M365 clients" ($preAuthCount -ge 7) "$preAuthCount clients (need 7)"
    
    $hasRedirect = ($app.web.redirectUris -contains "https://token.botframework.com/.auth/web/redirect")
    Check "Bot Framework redirect URI" $hasRedirect
    
    # Service principal
    $sp = az ad sp show --id $AppId -o json 2>$null | ConvertFrom-Json
    Check "Service principal exists" ($null -ne $sp)
    Check "Service principal enabled" ($sp.accountEnabled -eq $true)
} catch {
    Check "App registration accessible" $false
}

# ─── 5. Summary ───
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Results: ✅ $pass passed  ⚠️ $warn warnings  ❌ $fail failed" -ForegroundColor $(if ($fail -gt 0) { "Red" } elseif ($warn -gt 0) { "Yellow" } else { "Green" })
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

# ─── 6. Teams Deep Link Test ───
Write-Host ""
Write-Host "6. TEAMS DEEP LINK TEST (CRITICAL)" -ForegroundColor Magenta
Write-Host "   ────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""
Write-Host "   This bypasses the Teams app manifest entirely and tests" -ForegroundColor White
Write-Host "   whether the Teams channel can route messages to your bot." -ForegroundColor White
Write-Host ""
Write-Host "   Open this URL in your browser:" -ForegroundColor Yellow
Write-Host ""
Write-Host "   https://teams.microsoft.com/l/chat/0/0?users=28:$AppId" -ForegroundColor Cyan
Write-Host ""
Write-Host "   Then send a message. Check your App Service logs for:" -ForegroundColor White
Write-Host "   '>>> INBOUND POST /api/messages'" -ForegroundColor Green
Write-Host ""
Write-Host "   IF you see the request → App manifest/install is the issue" -ForegroundColor Yellow
Write-Host "   IF you see nothing    → Teams channel/bot routing is the issue" -ForegroundColor Yellow
Write-Host ""

# ─── 7. Azure Portal Actions ───
Write-Host "7. MANUAL STEPS (if diagnostics pass but Teams still fails)" -ForegroundColor Magenta
Write-Host "   ─────────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""
Write-Host "   a) ACCEPT TEAMS CHANNEL TERMS (Azure Portal):" -ForegroundColor White
Write-Host "      Portal > Bot > Channels > Microsoft Teams > click Configure" -ForegroundColor Gray
Write-Host "      Accept the terms > Save. This cannot be done via CLI." -ForegroundColor Gray
Write-Host ""
Write-Host "   b) CLEAN REINSTALL in Teams:" -ForegroundColor White
Write-Host "      1. Teams > Apps > Manage your apps" -ForegroundColor Gray
Write-Host "      2. Find 'SK Copilot v2' > ⋯ menu > Remove" -ForegroundColor Gray
Write-Host "      3. Wait 30 seconds" -ForegroundColor Gray
Write-Host "      4. Upload the NEW v1.4.0 ZIP as custom app" -ForegroundColor Gray
Write-Host "      5. Install for personal use" -ForegroundColor Gray
Write-Host ""
Write-Host "   c) VERIFY IN TEAMS (after install):" -ForegroundColor White
Write-Host "      1. Open Teams > Chat > New chat" -ForegroundColor Gray
Write-Host "      2. Search for 'SK Copilot v2 - Blog Creator'" -ForegroundColor Gray
Write-Host "      3. Send 'hello'" -ForegroundColor Gray
Write-Host "      4. Immediately check Log Stream in Azure Portal" -ForegroundColor Gray
Write-Host ""
