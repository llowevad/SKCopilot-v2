#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the Teams app package (.zip) for SKCopilot-v2.
.DESCRIPTION
    Creates a deployable Teams app package by:
    1. Replacing placeholder tokens in manifest.json
    2. Ensuring icons exist (generates if missing)
    3. Packaging manifest + icons into a .zip file
    4. Validating the package structure
    
.PARAMETER BotId
    Azure Bot app registration ID (required)
.PARAMETER BotDomain
    Bot messaging endpoint domain without protocol (default: "<your-copilot-app>.azurewebsites.net")
.PARAMETER OutputPath
    Output path for the .zip file (default: "src/CopilotAgent/appPackage/SKCopilotV2.zip")
.EXAMPLE
    .\package-teams-app.ps1 -BotId "12345678-1234-1234-1234-123456789012"
.EXAMPLE
    .\package-teams-app.ps1 -BotId "..." -BotDomain "mybot.azurewebsites.net" -OutputPath "dist/app.zip"
#>

[CmdletBinding()]
param(
    [string]$BotId,
    [string]$BotDomain,
    [string]$OutputPath = "src\CopilotAgent\appPackage\SKCopilotV2.zip"
)

# Load defaults from .env
$envVars = & "$PSScriptRoot\Load-Env.ps1"
if (-not $BotId)     { $BotId     = $envVars["BOT_APP_ID"] }
if (-not $BotDomain) { $BotDomain = $envVars["COPILOT_APP_DOMAIN"] ?? "<your-copilot-app>.azurewebsites.net" }

# Validate required parameters
if (-not $BotId) { Write-Error "BotId is required. Set BOT_APP_ID in .env or pass -BotId"; exit 1 }

$ErrorActionPreference = "Stop"

Write-Host "=== Building Teams App Package ===" -ForegroundColor Cyan
Write-Host ""

# Resolve paths
$repoRoot = Split-Path -Parent $PSScriptRoot
$appPackageDir = Join-Path $repoRoot "src\CopilotAgent\appPackage"
$manifestTemplate = Join-Path $appPackageDir "manifest.json"
$colorIcon = Join-Path $appPackageDir "color.png"
$outlineIcon = Join-Path $appPackageDir "outline.png"
$iconScript = Join-Path $appPackageDir "generate-icons.ps1"
$outputZip = Join-Path $repoRoot $OutputPath

# Validate inputs
if (-not (Test-Path $manifestTemplate)) {
    Write-Error "Manifest template not found at: $manifestTemplate"
    exit 1
}

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Bot ID:     $BotId"
Write-Host "  Bot Domain: $BotDomain"
Write-Host "  Output:     $outputZip"
Write-Host ""

# Generate icons if missing
if (-not (Test-Path $colorIcon) -or -not (Test-Path $outlineIcon)) {
    Write-Host "Icons missing. Generating placeholders..." -ForegroundColor Yellow
    if (Test-Path $iconScript) {
        & $iconScript
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to generate icons"
            exit 1
        }
    } else {
        Write-Error "Icon generation script not found at: $iconScript"
        exit 1
    }
}

# Verify icons exist
if (-not (Test-Path $colorIcon)) {
    Write-Error "Color icon not found at: $colorIcon"
    exit 1
}
if (-not (Test-Path $outlineIcon)) {
    Write-Error "Outline icon not found at: $outlineIcon"
    exit 1
}
Write-Host "✓ Icons verified" -ForegroundColor Green

# Create temp directory for processed files
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    # Process manifest.json (replace placeholders)
    Write-Host "Processing manifest.json..." -ForegroundColor Yellow
    $manifestContent = Get-Content $manifestTemplate -Raw
    $manifestContent = $manifestContent -replace '\$\{\{BOT_ID\}\}', $BotId
    $manifestContent = $manifestContent -replace '\$\{\{BOT_DOMAIN\}\}', $BotDomain
    
    $processedManifest = Join-Path $tempDir "manifest.json"
    Set-Content -Path $processedManifest -Value $manifestContent -NoNewline
    
    # Validate JSON structure
    try {
        $manifestObj = $manifestContent | ConvertFrom-Json
        Write-Host "✓ Manifest JSON is valid" -ForegroundColor Green
        Write-Host "  App Name:     $($manifestObj.name.short)" -ForegroundColor Gray
        Write-Host "  Version:      $($manifestObj.version)" -ForegroundColor Gray
        Write-Host "  Bot ID:       $($manifestObj.id)" -ForegroundColor Gray
    } catch {
        Write-Error "Manifest JSON is invalid: $_"
        exit 1
    }
    
    # Copy icons to temp directory
    Write-Host "Copying icons..." -ForegroundColor Yellow
    Copy-Item $colorIcon -Destination (Join-Path $tempDir "color.png")
    Copy-Item $outlineIcon -Destination (Join-Path $tempDir "outline.png")
    Write-Host "✓ Icons copied" -ForegroundColor Green
    
    # Create zip package
    Write-Host "Creating app package..." -ForegroundColor Yellow
    
    # Remove existing zip if present
    if (Test-Path $outputZip) {
        Remove-Item $outputZip -Force
    }
    
    # Ensure output directory exists
    $outputDir = Split-Path -Parent $outputZip
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    
    # Create zip
    Compress-Archive -Path (Join-Path $tempDir "*") -DestinationPath $outputZip -CompressionLevel Optimal
    
    if (Test-Path $outputZip) {
        $zipSize = (Get-Item $outputZip).Length
        Write-Host "✓ Package created: $outputZip ($([math]::Round($zipSize / 1KB, 2)) KB)" -ForegroundColor Green
    } else {
        Write-Error "Failed to create zip package"
        exit 1
    }
    
    # Validate package contents
    Write-Host "`nValidating package contents..." -ForegroundColor Yellow
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($outputZip)
    
    $requiredFiles = @("manifest.json", "color.png", "outline.png")
    $missingFiles = @()
    
    foreach ($file in $requiredFiles) {
        if ($null -eq ($zip.Entries | Where-Object { $_.Name -eq $file })) {
            $missingFiles += $file
        }
    }
    
    $zip.Dispose()
    
    if ($missingFiles.Count -gt 0) {
        Write-Error "Package is missing required files: $($missingFiles -join ', ')"
        exit 1
    }
    
    Write-Host "✓ Package validation passed" -ForegroundColor Green
    
    Write-Host "`n=== Package Build Complete ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Package Location: $outputZip" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "  1. Deploy CopilotAgent to Azure App Service at: https://$BotDomain"
    Write-Host "  2. Verify messaging endpoint is configured in Azure Bot: https://$BotDomain/api/messages"
    Write-Host "  3. Sideload the app package in Teams:"
    Write-Host "     - Open Teams > Apps > Manage your apps > Upload an app > Upload a custom app"
    Write-Host "     - Select the .zip file: $outputZip"
    Write-Host "  4. Or upload to your organization's app catalog for broader distribution"
    Write-Host ""
    Write-Host "To test in M365 Copilot:" -ForegroundColor Cyan
    Write-Host "  - After sideloading, the bot will appear in Copilot's plugin menu"
    Write-Host "  - Enable it in a Copilot conversation to start using blog generation"
    
} finally {
    # Cleanup temp directory
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force
    }
}
