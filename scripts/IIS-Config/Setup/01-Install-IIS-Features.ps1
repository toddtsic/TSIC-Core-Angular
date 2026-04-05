# ============================================================================
# 01-Install-IIS-Features.ps1 — Enable IIS + required modules
# ============================================================================
# Run as Administrator. Idempotent — skips features already enabled.
# Also checks for URL Rewrite module (required for Angular SPA routing).
# ============================================================================

#Requires -RunAsAdministrator

Write-Host ""
Write-Host "[Step 1] Installing IIS features..." -ForegroundColor Green

$features = @(
    'IIS-WebServerRole',
    'IIS-WebServer',
    'IIS-CommonHttpFeatures',
    'IIS-HttpErrors',
    'IIS-HttpLogging',
    'IIS-RequestFiltering',
    'IIS-StaticContent',
    'IIS-DefaultDocument',
    'IIS-WebServerManagementTools',
    'IIS-ManagementConsole',
    'IIS-NetFxExtensibility45',
    'IIS-ASPNET45',
    'IIS-WebSockets',
    'IIS-HttpCompressionStatic',
    'IIS-HttpCompressionDynamic'
)

foreach ($feature in $features) {
    $state = Get-WindowsOptionalFeature -Online -FeatureName $feature -ErrorAction SilentlyContinue
    if ($state -and $state.State -eq 'Enabled') {
        # Already enabled
    }
    elseif ($state) {
        Write-Host "  Enabling $feature..." -ForegroundColor Yellow
        Enable-WindowsOptionalFeature -Online -FeatureName $feature -NoRestart -ErrorAction SilentlyContinue | Out-Null
    }
    else {
        Write-Host "  Feature not available: $feature (skipping)" -ForegroundColor DarkGray
    }
}
Write-Host "  IIS features ready." -ForegroundColor Green

# Check URL Rewrite module
$urlRewriteKey = "HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite"
if (Test-Path $urlRewriteKey) {
    Write-Host "  URL Rewrite module: installed" -ForegroundColor Green
}
else {
    Write-Host "  WARNING: URL Rewrite module not detected." -ForegroundColor Yellow
    Write-Host "  Download from: https://www.iis.net/downloads/microsoft/url-rewrite" -ForegroundColor Yellow
    Write-Host "  The Angular site requires this module for SPA routing." -ForegroundColor Yellow
}

Write-Host "[Step 1] Complete." -ForegroundColor Green
