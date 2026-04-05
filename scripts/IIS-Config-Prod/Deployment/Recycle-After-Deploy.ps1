# ============================================================================
# Recycle-After-Deploy.ps1 — Run ON TSIC-PHOENIX after Publish.ps1 deploys
# ============================================================================
# Recycles app pools and warms up the API after files are deployed via SMB
# from the dev machine.
#
# Usage (on TSIC-PHOENIX):
#   E:\Websites\IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1
# ============================================================================

#Requires -RunAsAdministrator

$ApiPoolName     = 'claude-api'
$AngularPoolName = 'claude-app'
$ApiHostname     = 'claude-api.teamsportsinfo.com'

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Post-Deploy: Recycle & Warmup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Import-Module WebAdministration -ErrorAction Stop

# Recycle app pools
foreach ($pool in @($ApiPoolName, $AngularPoolName)) {
    $state = Get-WebAppPoolState -Name $pool -ErrorAction SilentlyContinue
    if ($state) {
        Restart-WebAppPool -Name $pool
        Write-Host "  Recycled app pool: $pool" -ForegroundColor Green
    } else {
        Write-Host "  App pool not found: $pool" -ForegroundColor Red
    }
}

Start-Sleep -Seconds 3

# Warm up API
Write-Host ""
Write-Host "  Warming up API..." -ForegroundColor Yellow
try {
    $null = Invoke-WebRequest -Uri "https://$ApiHostname/api/jobs/tsic" -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
    Write-Host "  API warmed up!" -ForegroundColor Green
} catch {
    Write-Host "  Warmup failed (app may still be starting): $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Done." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
