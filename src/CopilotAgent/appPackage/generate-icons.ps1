#Requires -Version 5.1
<#
.SYNOPSIS
    Generates placeholder PNG icons for Teams app package.
.DESCRIPTION
    Creates simple placeholder icons for the Teams app manifest:
    - color.png (192x192) - Color app icon with purple background
    - outline.png (32x32) - White outline icon on transparent background
    
    These are PLACEHOLDERS. Replace with proper branded icons before production deployment.
.EXAMPLE
    .\generate-icons.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$colorIconPath = Join-Path $scriptDir "color.png"
$outlineIconPath = Join-Path $scriptDir "outline.png"

Write-Host "Generating placeholder icons..." -ForegroundColor Cyan

try {
    Add-Type -AssemblyName System.Drawing

    # Generate color icon (192x192)
    Write-Host "Creating color.png (192x192)..."
    $colorBitmap = New-Object System.Drawing.Bitmap 192, 192
    $graphics = [System.Drawing.Graphics]::FromImage($colorBitmap)
    
    # Purple background (#5B5FC7 from manifest)
    $backgroundColor = [System.Drawing.Color]::FromArgb(91, 95, 199)
    $graphics.Clear($backgroundColor)
    
    # White "SK" text
    $font = New-Object System.Drawing.Font("Arial", 72, [System.Drawing.FontStyle]::Bold)
    $brush = [System.Drawing.Brushes]::White
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    
    $rect = New-Object System.Drawing.RectangleF 0, 0, 192, 192
    $graphics.DrawString("SK", $font, $brush, $rect, $format)
    
    $colorBitmap.Save($colorIconPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $colorBitmap.Dispose()
    
    Write-Host "✓ Created $colorIconPath" -ForegroundColor Green

    # Generate outline icon (32x32)
    Write-Host "Creating outline.png (32x32)..."
    $outlineBitmap = New-Object System.Drawing.Bitmap 32, 32
    $graphics = [System.Drawing.Graphics]::FromImage($outlineBitmap)
    
    # Transparent background
    $graphics.Clear([System.Drawing.Color]::Transparent)
    
    # White outline text
    $font = New-Object System.Drawing.Font("Arial", 14, [System.Drawing.FontStyle]::Bold)
    $brush = [System.Drawing.Brushes]::White
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    
    $rect = New-Object System.Drawing.RectangleF 0, 0, 32, 32
    $graphics.DrawString("SK", $font, $brush, $rect, $format)
    
    $outlineBitmap.Save($outlineIconPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $outlineBitmap.Dispose()
    
    Write-Host "✓ Created $outlineIconPath" -ForegroundColor Green

    Write-Host "`nPlaceholder icons generated successfully!" -ForegroundColor Green
    Write-Host "⚠️  IMPORTANT: Replace these placeholders with proper branded icons before production." -ForegroundColor Yellow

} catch {
    Write-Error "Failed to generate icons: $_"
    exit 1
}
