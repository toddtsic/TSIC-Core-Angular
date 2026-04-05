# ============================================================================
# Run-Full-Setup.ps1 — Execute all setup steps in order (Dev / TSIC-SEDONA)
# ============================================================================
# Orchestrates steps 01-07 for a complete IIS server setup.
# Each step is idempotent — safe to re-run.
#
# IMPORTANT: Run 00-Remove-Sites.ps1 FIRST if old TSIC.Api/TSIC.App sites
# exist — their hostname bindings conflict with the new claude-api/claude-app.
#
# Usage:
#   .\Run-Full-Setup.ps1
#   .\Run-Full-Setup.ps1 -SkipSql -SkipFirewall
# ============================================================================

#Requires -RunAsAdministrator

param(
    [string]$SecretsFile,
    [switch]$SkipSql,
    [switch]$SkipFirewall,
    [switch]$SkipSecrets
)

. "$PSScriptRoot\..\_config.ps1"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  TSIC IIS Server Setup - Dev (TSIC-SEDONA)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Show-Config

# Step 1: IIS Features
& "$PSScriptRoot\01-Install-IIS-Features.ps1"
if ($LASTEXITCODE -ne 0) { Write-Host "Step 1 failed!" -ForegroundColor Red; exit 1 }

# Step 2: App Pools
& "$PSScriptRoot\02-Create-App-Pools.ps1"
if ($LASTEXITCODE -ne 0) { Write-Host "Step 2 failed!" -ForegroundColor Red; exit 1 }

# Step 3: Directories
& "$PSScriptRoot\03-Create-Directories.ps1"
if ($LASTEXITCODE -ne 0) { Write-Host "Step 3 failed!" -ForegroundColor Red; exit 1 }

# Step 4: IIS Sites
& "$PSScriptRoot\04-Create-IIS-Sites.ps1"
if ($LASTEXITCODE -ne 0) { Write-Host "Step 4 failed!" -ForegroundColor Red; exit 1 }

# Step 5: SQL Login
if ($SkipSql) {
    Write-Host ""
    Write-Host "[Step 5] SQL Server login - SKIPPED (-SkipSql)" -ForegroundColor Yellow
}
else {
    & "$PSScriptRoot\05-Create-SQL-Login.ps1"
}

# Step 6: Firewall
if ($SkipFirewall) {
    Write-Host ""
    Write-Host "[Step 6] Firewall rules - SKIPPED (-SkipFirewall)" -ForegroundColor Yellow
}
else {
    & "$PSScriptRoot\06-Configure-Firewall.ps1"
}

# Step 7: Secrets
if ($SkipSecrets) {
    Write-Host ""
    Write-Host "[Step 7] App pool secrets - SKIPPED (-SkipSecrets)" -ForegroundColor Yellow
}
else {
    $secretsArgs = @{}
    if ($SecretsFile) { $secretsArgs.SecretsFile = $SecretsFile }
    & "$PSScriptRoot\07-Apply-Secrets.ps1" @secretsArgs
}

# Summary
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Setup Complete - Dev" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API:     https://$($Config.ApiHostname)" -ForegroundColor Green
Write-Host "  Angular: https://$($Config.AngularHostname)" -ForegroundColor Green
Write-Host ""
Write-Host "  Next Steps:" -ForegroundColor Yellow
Write-Host "    1. Deploy application files:" -ForegroundColor White
Write-Host "       cd ..\Deployment" -ForegroundColor White
Write-Host "       .\03-Deploy-To-IIS.ps1" -ForegroundColor White
Write-Host "    2. Verify: https://$($Config.AngularHostname)" -ForegroundColor White
Write-Host "    3. Verify: https://$($Config.ApiHostname)/swagger" -ForegroundColor White
Write-Host ""
