# ============================================================================
# 08-Set-AspNetEnv.ps1 — Set ASPNETCORE_ENVIRONMENT on the dev-api app pool
# ============================================================================
# Applies $Config.AspNetEnv (from _config.ps1) to the API app pool's
# environmentVariables collection, then recycles and verifies.
#
# Why separate from 07-Apply-Secrets.ps1: flipping the runtime profile
# (e.g. Development -> Staging so dev.teamsportsinfo.com behaves like the
# client preview) must NOT require the untracked secrets file. This script
# touches ONLY ASPNETCORE_ENVIRONMENT and leaves every other pool variable
# (AWS / ADN / VI / Anthropic / BoldReports) exactly as-is.
#
# Source of truth is _config.ps1 ($Config.AspNetEnv). Change it there; run
# this to apply. Idempotent — safe to re-run.
#
# Usage:
#   .\08-Set-AspNetEnv.ps1
# ============================================================================

#Requires -RunAsAdministrator

. "$PSScriptRoot\..\_config.ps1"

Write-Host ""
Write-Host "[Step 8] Setting ASPNETCORE_ENVIRONMENT on '$($Config.ApiPoolName)'..." -ForegroundColor Green

Import-Module WebAdministration -ErrorAction Stop

$poolPath = "IIS:\AppPools\$($Config.ApiPoolName)"
if (-not (Test-Path $poolPath)) {
    throw "App pool '$($Config.ApiPoolName)' does not exist. Run 02-Create-App-Pools.ps1 first."
}

$appcmd = "$env:SystemRoot\System32\inetsrv\appcmd.exe"
if (-not (Test-Path $appcmd)) { throw "appcmd.exe not found at $appcmd" }

$key   = 'ASPNETCORE_ENVIRONMENT'
$value = $Config.AspNetEnv
if ([string]::IsNullOrWhiteSpace($value)) { throw "`$Config.AspNetEnv is empty in _config.ps1." }

# Show current value before the change (for an audit trail in the console)
$before = $null
$coll = (Get-ItemProperty $poolPath -Name environmentVariables -ErrorAction SilentlyContinue).Collection
if ($coll) { $before = ($coll | Where-Object { $_.name -eq $key }).value }
Write-Host "  Current: $key = $before" -ForegroundColor Gray
Write-Host "  Target:  $key = $value" -ForegroundColor White

if ($before -eq $value) {
    Write-Host "  Already set to '$value' — nothing to change (recycling anyway to be safe)." -ForegroundColor DarkGray
}

# Clear then set (mirrors 07-Apply-Secrets.ps1 idiom)
& $appcmd clear config -section:system.applicationHost/applicationPools "/[name='$($Config.ApiPoolName)'].environmentVariables.[name='$key']" 2>&1 | Out-Null
& $appcmd set   config -section:system.applicationHost/applicationPools "/+[name='$($Config.ApiPoolName)'].environmentVariables.[name='$key',value='$value']" 2>&1 | Out-Null

# Recycle so the new value takes effect
Write-Host "  Recycling app pool '$($Config.ApiPoolName)'..." -ForegroundColor Yellow
Restart-WebAppPool -Name $Config.ApiPoolName
Start-Sleep -Seconds 2

# Verify the applied value
$collAfter = (Get-ItemProperty $poolPath -Name environmentVariables -ErrorAction SilentlyContinue).Collection
$after = if ($collAfter) { ($collAfter | Where-Object { $_.name -eq $key }).value } else { $null }
if ($after -eq $value) {
    Write-Host "  Verified: $key = $after" -ForegroundColor Green
} else {
    Write-Warning "Verification failed: expected '$value', got '$after'"
}

Write-Host "[Step 8] Complete." -ForegroundColor Green
Write-Host ""
Write-Host "  NOTE: '$value' makes this box behave like the client preview:" -ForegroundColor Cyan
Write-Host "    - Email suppressed + CC -> ADN sandbox (IsSandbox)" -ForegroundColor White
Write-Host "    - Auth password bypass / dev exception page / Swagger OFF" -ForegroundColor White
Write-Host "    - Links use FrontendSettings.BaseUrl for Staging (dev.teamsportsinfo.com)" -ForegroundColor White
