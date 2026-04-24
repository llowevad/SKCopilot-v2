<#
.SYNOPSIS
    Loads environment variables from the .env file at the repository root.
.DESCRIPTION
    Reads key=value pairs from .env and sets them as PowerShell variables
    in the caller's scope. Skips comments (#) and blank lines.
    Returns a hashtable of all loaded values.
.EXAMPLE
    $env = & "$PSScriptRoot\Load-Env.ps1"
    $env.BOT_APP_ID
#>

$envFile = Join-Path (Split-Path -Parent $PSScriptRoot) ".env"

$envVars = @{}

if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith("#") -and $line.Contains("=")) {
            $parts = $line -split "=", 2
            $key = $parts[0].Trim()
            $value = $parts[1].Trim()
            if ($value -and -not $value.StartsWith("<")) {
                $envVars[$key] = $value
            }
        }
    }
    if ($envVars.Count -gt 0) {
        Write-Host "✓ Loaded $($envVars.Count) values from .env" -ForegroundColor Green
    } else {
        Write-Host "⚠ .env file found but no values populated. Copy Sample.env to .env and fill in your values." -ForegroundColor Yellow
    }
} else {
    Write-Host "⚠ No .env file found. Copy Sample.env to .env and fill in your values." -ForegroundColor Yellow
}

return $envVars
