# ============================================================================
# Run-Full-Setup.ps1 — Execute all setup steps in order
# ============================================================================
# Orchestrates steps 01-07 for a complete IIS server setup.
# Each step is idempotent — safe to re-run.
#
# Usage:
#   .\Run-Full-Setup.ps1 -Environment Dev
#   .\Run-Full-Setup.ps1 -Environment Prod
#   .\Run-Full-Setup.ps1 -Environment Prod -SecretsFile "C:\path\to\secrets.local.ps1"
#   .\Run-Full-Setup.ps1 -Environment Dev -SkipSql -SkipFirewall
# ============================================================================

#Requires -RunAsAdministrator

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Prod',

    [string]$SecretsFile,
    [switch]$SkipSql,
    [switch]$SkipFirewall,
    [switch]$SkipSecrets
)

. "$PSScriptRoot\..\_config.ps1" -Environment $Environment

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  TSIC IIS Server Setup — $Environment Environment" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Show-Config

$stepFailed = $false

try {

# Step 1: IIS Features
& "$PSScriptRoot\01-Install-IIS-Features.ps1"

# Step 2: App Pools
& "$PSScriptRoot\02-Create-App-Pools.ps1" -Environment $Environment

# Step 3: Directories
& "$PSScriptRoot\03-Create-Directories.ps1" -Environment $Environment

# Step 4: IIS Sites
& "$PSScriptRoot\04-Create-IIS-Sites.ps1" -Environment $Environment

# Step 5: SQL Login
if ($SkipSql) {
    Write-Host ""
    Write-Host "[Step 5] SQL Server login — SKIPPED (-SkipSql)" -ForegroundColor Yellow
}
else {
    & "$PSScriptRoot\05-Create-SQL-Login.ps1" -Environment $Environment
}

# Step 6: Firewall
if ($SkipFirewall) {
    Write-Host ""
    Write-Host "[Step 6] Firewall rules — SKIPPED (-SkipFirewall)" -ForegroundColor Yellow
}
else {
    & "$PSScriptRoot\06-Configure-Firewall.ps1"
}

# Step 7: Secrets
if ($SkipSecrets) {
    Write-Host ""
    Write-Host "[Step 7] App pool secrets — SKIPPED (-SkipSecrets)" -ForegroundColor Yellow
}
else {
    $secretsArgs = @{ Environment = $Environment }
    if ($SecretsFile) { $secretsArgs.SecretsFile = $SecretsFile }
    & "$PSScriptRoot\07-Apply-Secrets.ps1" @secretsArgs
}

} catch {
    $stepFailed = $true
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host "  SETUP FAILED — $Environment Environment" -ForegroundColor Red
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host "  Error: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Fix the issue above and re-run. Steps are idempotent." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# Summary
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Setup Complete — $Environment Environment" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API:     https://$($Config.ApiHostname)" -ForegroundColor Green
Write-Host "  Angular: https://$($Config.AngularHostname)" -ForegroundColor Green
Write-Host ""
Write-Host "  Next Steps:" -ForegroundColor Yellow
Write-Host "    1. Deploy application files:" -ForegroundColor White
Write-Host "       cd ..\Deployment" -ForegroundColor White
Write-Host "       .\Publish.ps1" -ForegroundColor White
Write-Host "    2. Verify: https://$($Config.AngularHostname)" -ForegroundColor White
Write-Host "    3. Verify: https://$($Config.ApiHostname)/swagger" -ForegroundColor White
Write-Host ""
