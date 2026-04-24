#Requires -Version 7.0
<#
.SYNOPSIS
    Registers an Azure Bot for Teams/M365 Copilot integration.
.DESCRIPTION
    Creates an Azure AD app registration and Azure Bot resource for the SKCopilot-v2 agent.
    This bot registration is required for Teams app sideloading and M365 Copilot deployment.
    
    Prerequisites:
    - Azure CLI installed and authenticated (az login)
    - Contributor role on target subscription
    - Application Administrator role in Azure AD
    
.PARAMETER DisplayName
    Display name for the bot (default: "SKCopilot-v2 Blog Assistant")
.PARAMETER AppId
    Existing Azure AD app ID to use (optional - creates new if not provided)
.PARAMETER TenantId
    Azure AD tenant ID (required)
.PARAMETER SubscriptionId
    Azure subscription ID (required)
.PARAMETER ResourceGroup
    Resource group name (from .env or parameter)
.PARAMETER Location
    Azure region (default: "westus2")
.PARAMETER BotName
    Bot resource name (from .env or parameter)
.PARAMETER MessagingEndpoint
    Bot messaging endpoint URL (default: "https://<your-copilot-app>.azurewebsites.net/api/messages")
.EXAMPLE
    .\register-bot.ps1 -TenantId "your-tenant-id" -SubscriptionId "your-subscription-id"
.EXAMPLE
    .\register-bot.ps1 -TenantId "..." -SubscriptionId "..." -AppId "existing-app-id"
#>

[CmdletBinding()]
param(
    [string]$DisplayName = "SKCopilot-v2 Blog Assistant",
    [string]$AppId,
    [string]$TenantId,
    [string]$SubscriptionId,
    [string]$ResourceGroup,
    [string]$Location = "westus2",
    [string]$BotName,
    [string]$MessagingEndpoint
)

# Load defaults from .env
$envVars = & "$PSScriptRoot\Load-Env.ps1"
if (-not $TenantId)          { $TenantId          = $envVars["AZURE_TENANT_ID"] }
if (-not $SubscriptionId)    { $SubscriptionId     = $envVars["AZURE_SUBSCRIPTION_ID"] }
if (-not $ResourceGroup)     { $ResourceGroup      = $envVars["RESOURCE_GROUP"] }
if (-not $BotName)           { $BotName            = $envVars["BOT_NAME"] }
if (-not $MessagingEndpoint) {
    $domain = $envVars["COPILOT_APP_DOMAIN"] ?? "<your-copilot-app>.azurewebsites.net"
    $MessagingEndpoint = "https://$domain/api/messages"
}

# Validate required parameters
if (-not $TenantId)       { Write-Error "TenantId is required. Set AZURE_TENANT_ID in .env or pass -TenantId"; exit 1 }
if (-not $SubscriptionId) { Write-Error "SubscriptionId is required. Set AZURE_SUBSCRIPTION_ID in .env or pass -SubscriptionId"; exit 1 }

$ErrorActionPreference = "Stop"

Write-Host "=== Azure Bot Registration for SKCopilot-v2 ===" -ForegroundColor Cyan
Write-Host ""

# Check Azure CLI
Write-Host "Checking Azure CLI..." -ForegroundColor Yellow
try {
    $azVersion = az version --output json 2>$null | ConvertFrom-Json
    Write-Host "✓ Azure CLI version: $($azVersion.'azure-cli')" -ForegroundColor Green
} catch {
    Write-Error "Azure CLI not found. Install from https://aka.ms/InstallAzureCLI"
    exit 1
}

# Set subscription
Write-Host "Setting subscription context..." -ForegroundColor Yellow
az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to set subscription. Run 'az login' first."
    exit 1
}
Write-Host "✓ Subscription set: $SubscriptionId" -ForegroundColor Green

# Create or use existing App Registration
if ([string]::IsNullOrEmpty($AppId)) {
    Write-Host "`nCreating Azure AD app registration..." -ForegroundColor Yellow
    
    $appJson = az ad app create --display-name $DisplayName --sign-in-audience "AzureADMultipleOrgs" --output json
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create app registration"
        exit 1
    }
    
    $app = $appJson | ConvertFrom-Json
    $AppId = $app.appId
    $objectId = $app.id
    
    Write-Host "✓ Created app registration: $AppId" -ForegroundColor Green
    
    # Create client secret
    Write-Host "Creating client secret..." -ForegroundColor Yellow
    $secretJson = az ad app credential reset --id $objectId --append --years 2 --output json
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create client secret"
        exit 1
    }
    
    $secret = $secretJson | ConvertFrom-Json
    $clientSecret = $secret.password
    
    Write-Host "✓ Created client secret (expires in 2 years)" -ForegroundColor Green
    
} else {
    Write-Host "`nUsing existing app registration: $AppId" -ForegroundColor Yellow
    $clientSecret = $null
}

# Create resource group if needed
Write-Host "`nChecking resource group..." -ForegroundColor Yellow
$rgExists = az group exists --name $ResourceGroup
if ($rgExists -eq "false") {
    Write-Host "Creating resource group '$ResourceGroup' in $Location..." -ForegroundColor Yellow
    az group create --name $ResourceGroup --location $Location --output none
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create resource group"
        exit 1
    }
    Write-Host "✓ Created resource group: $ResourceGroup" -ForegroundColor Green
} else {
    Write-Host "✓ Resource group exists: $ResourceGroup" -ForegroundColor Green
}

# Create Azure Bot
Write-Host "`nCreating Azure Bot resource..." -ForegroundColor Yellow
az bot create `
    --resource-group $ResourceGroup `
    --name $BotName `
    --app-type SingleTenant `
    --appid $AppId `
    --tenant-id $TenantId `
    --endpoint $MessagingEndpoint `
    --display-name $DisplayName `
    --sku F0 `
    --output none

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create bot resource. It may already exist or you may lack permissions."
    exit 1
}

Write-Host "✓ Created Azure Bot: $BotName" -ForegroundColor Green

# Enable Teams channel(usually enabled by default, but ensure it)
Write-Host "Enabling Microsoft Teams channel..." -ForegroundColor Yellow
az bot msteams create --resource-group $ResourceGroup --name $BotName --output none 2>$null
Write-Host "✓ Teams channel enabled" -ForegroundColor Green

# Summary
Write-Host "`n=== Registration Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Bot Details:" -ForegroundColor Cyan
Write-Host "  Bot Name:       $BotName"
Write-Host "  Resource Group: $ResourceGroup"
Write-Host "  App ID:         $AppId"
Write-Host "  Tenant ID:      $TenantId"
Write-Host "  Endpoint:       $MessagingEndpoint"
Write-Host ""

if ($null -ne $clientSecret) {
    Write-Host "Client Secret:" -ForegroundColor Yellow
    Write-Host "  $clientSecret" -ForegroundColor White
    Write-Host ""
    Write-Host "⚠️  IMPORTANT: Save this client secret securely NOW. It cannot be retrieved later." -ForegroundColor Red
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "  1. Store the client secret in Azure Key Vault or User Secrets"
    Write-Host "  2. Update appsettings.json or User Secrets with:"
    Write-Host "     - ClientId: $AppId"
    Write-Host "     - TenantId: $TenantId"
    Write-Host "  3. Build Teams app package with: .\scripts\package-teams-app.ps1 -BotId $AppId -BotDomain <your-copilot-app>.azurewebsites.net"
    Write-Host "  4. Deploy CopilotAgent to Azure App Service"
    Write-Host "  5. Sideload the app package in Teams or upload to org app catalog"
} else {
    Write-Host "Using existing app registration. Ensure you have the client secret stored securely." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "  1. Build Teams app package with: .\scripts\package-teams-app.ps1 -BotId $AppId -BotDomain <your-copilot-app>.azurewebsites.net"
    Write-Host "  2. Deploy CopilotAgent to Azure App Service"
    Write-Host "  3. Sideload the app package in Teams or upload to org app catalog"
}
Write-Host ""
Write-Host "Manage bot at: https://portal.azure.com/#resource/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.BotService/botServices/$BotName" -ForegroundColor Cyan
