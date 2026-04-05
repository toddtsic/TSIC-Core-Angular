# ============================================================================
# 07-Apply-Secrets.ps1 — Set app pool environment variables for secrets
# ============================================================================
# Applies secrets from a local (untracked) file to the API app pool.
# These environment variables are how .NET reads secrets in IIS
# (equivalent to User Secrets in local development).
#
# Usage:
#   .\07-Apply-Secrets.ps1
#   .\07-Apply-Secrets.ps1 -SecretsFile "C:\path\to\app-pool-secrets.ps1"
#
# The secrets file must define a hashtable like:
#   $envVars = @{
#       "AWS_ACCESS_KEY_ID"    = "AKIA..."
#       "Anthropic__ApiKey"    = "sk-ant-..."
#       ...
#   }
#
# Template: See docs/Security/iis-env-secrets-setup.md for the full list.
# ============================================================================

#Requires -RunAsAdministrator

param(
    [string]$SecretsFile
)

. "$PSScriptRoot\..\_config.ps1"

Write-Host ""
Write-Host "[Step 7] Applying app pool environment variables (Dev)..." -ForegroundColor Green

# Resolve secrets file path
if (-not $SecretsFile) {
    $candidates = @(
        (Join-Path $PSScriptRoot "app-pool-secrets.ps1")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $SecretsFile = (Resolve-Path $candidate).Path
            break
        }
    }
}

if (-not $SecretsFile -or -not (Test-Path $SecretsFile)) {
    Write-Host "  WARNING: No secrets file found." -ForegroundColor Yellow
    Write-Host "  Looked for: app-pool-secrets.ps1 (in Setup/ alongside this script)" -ForegroundColor Yellow
    Write-Host "  Provide via -SecretsFile parameter." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Required environment variables:" -ForegroundColor Yellow
    Write-Host "    AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION" -ForegroundColor White
    Write-Host "    VI_DEV_CLIENT_ID, VI_DEV_SECRET, VI_PROD_CLIENT_ID, VI_PROD_SECRET" -ForegroundColor White
    Write-Host "    ADN_SANDBOX_LOGINID, ADN_SANDBOX_TRANSACTIONKEY" -ForegroundColor White
    Write-Host "    USLAX_API_BASE, USLAX_CLIENT_ID, USLAX_SECRET, USLAX_USERNAME, USLAX_PASSWORD" -ForegroundColor White
    Write-Host "    Anthropic__ApiKey" -ForegroundColor White
    Write-Host ""
    Write-Host "  See docs/Security/iis-env-secrets-setup.md for template and instructions." -ForegroundColor Yellow
    throw "No secrets file found. Provide via -SecretsFile parameter or place app-pool-secrets.ps1 in Setup/."
}

Write-Host "  Secrets file: $SecretsFile" -ForegroundColor White

# The secrets file defines $envVars hashtable only — no execution logic.
. $SecretsFile

# Verify $envVars was defined by the sourced file
if (-not $envVars -or $envVars.Count -eq 0) {
    throw "Secrets file did not define `$envVars hashtable."
}

# Check for placeholder values
if ($envVars.Values -match "FILL_ME|YOUR_") {
    throw "Secrets file contains placeholder values (FILL_ME / YOUR_). Replace all before running."
}

# Add ASPNETCORE_ENVIRONMENT (from _config.ps1, not the secrets file)
$envVars["ASPNETCORE_ENVIRONMENT"] = $Config.AspNetEnv
Write-Host "  ASPNETCORE_ENVIRONMENT = $($Config.AspNetEnv)" -ForegroundColor White

Write-Host "  Applying $($envVars.Count) environment variables to '$($Config.ApiPoolName)'..." -ForegroundColor Yellow

$appcmd = "$env:SystemRoot\System32\inetsrv\appcmd.exe"
if (-not (Test-Path $appcmd)) {
    throw "appcmd.exe not found at $appcmd"
}

Import-Module WebAdministration -ErrorAction Stop

if (-not (Test-Path "IIS:\AppPools\$($Config.ApiPoolName)")) {
    throw "App pool '$($Config.ApiPoolName)' does not exist. Run 02-Create-App-Pools.ps1 first."
}

foreach ($kvp in $envVars.GetEnumerator()) {
    $key = $kvp.Key
    $value = $kvp.Value
    Write-Host "  Setting: $key" -ForegroundColor Gray
    try {
        & $appcmd clear config -section:system.applicationHost/applicationPools "/[name='$($Config.ApiPoolName)'].environmentVariables.[name='$key']" 2>&1 | Out-Null
        & $appcmd set config -section:system.applicationHost/applicationPools "/+[name='$($Config.ApiPoolName)'].environmentVariables.[name='$key',value='$value']" 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "Failed to set ${key}" }
    } catch { Write-Warning "Failed to set ${key}: $_" }
}

Write-Host "  Environment variables applied." -ForegroundColor Green

# Recycle app pool
Write-Host "  Recycling app pool '$($Config.ApiPoolName)'..." -ForegroundColor Yellow
Restart-WebAppPool -Name $Config.ApiPoolName
Start-Sleep -Seconds 2
Write-Host "  App pool recycled." -ForegroundColor Green

# Verify
Write-Host "  Verifying configuration..." -ForegroundColor Yellow
$configPath = "IIS:\AppPools\$($Config.ApiPoolName)"
$envVarCollection = Get-ItemProperty $configPath -Name environmentVariables -ErrorAction SilentlyContinue
if ($null -eq $envVarCollection -or $null -eq $envVarCollection.Collection) {
    Write-Warning "Could not read environment variables collection."
} else {
    $appliedVarNames = @()
    foreach ($item in $envVarCollection.Collection) { if ($item.name) { $appliedVarNames += $item.name } }
    $missing = @(); foreach ($key in $envVars.Keys) { if ($appliedVarNames -notcontains $key) { $missing += $key } }
    if ($missing.Count -eq 0) {
        Write-Host "  All $($envVars.Count) variables verified." -ForegroundColor Green
    } else {
        Write-Warning "Missing variables: $($missing -join ', ')"
    }
}

Write-Host "[Step 7] Complete." -ForegroundColor Green
