# =============================================================================
# 04-Create-AppPools.ps1 -- the dev-api and dev-app application pools
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\04-Create-AppPools.ps1 [-CheckOnly]
#
# Both pools: ApplicationPoolIdentity, No Managed Runtime (.NET 10 runs through
# the ASP.NET Core Module, not the CLR), 64-bit, and LoadUserProfile = true.
#
# LoadUserProfile matters: without it ASP.NET Core cannot persist its Data
# Protection keys under the pool's profile, and every recycle invalidates
# antiforgery tokens and anything else the keys sealed.
#
# Environment variables (secrets, ASPNETCORE_ENVIRONMENT) are NOT set here --
# Apply-Dev-Secrets.ps1 owns them.
# =============================================================================

#Requires -RunAsAdministrator

param([switch]$CheckOnly)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

# property path -> wanted value. identityType 4 = ApplicationPoolIdentity.
$Wanted = [ordered]@{
    'processModel.identityType'    = 'ApplicationPoolIdentity'
    'processModel.loadUserProfile' = $true
    'managedRuntimeVersion'        = ''
    'enable32BitAppOnWin64'        = $false
}

try {
    Assert-TSICDevBox
    Import-Module WebAdministration
    Write-Host ""
    Write-Host "Application pools" -ForegroundColor Cyan

    foreach ($pool in @($TSICDev.ApiPool, $TSICDev.AppPool)) {
        $path = "IIS:\AppPools\$pool"
        if (Test-Path $path) { Write-Same "pool $pool exists" }
        else {
            Invoke-TSICDevChange "create pool $pool" { New-WebAppPool -Name $pool | Out-Null }
            if ($CheckOnly) { continue }
        }
        foreach ($prop in $Wanted.Keys) {
            $have = Get-ItemProperty $path -Name $prop
            if ($have -is [Microsoft.IIs.PowerShell.Framework.ConfigurationAttribute]) { $have = $have.Value }
            if ("$have" -eq "$($Wanted[$prop])") { Write-Same "$pool $prop = '$have'"; continue }
            Invoke-TSICDevChange "$pool $prop '$have' -> '$($Wanted[$prop])'" { Set-ItemProperty $path -Name $prop -Value $Wanted[$prop] }
        }
        $state = (Get-WebAppPoolState -Name $pool).Value
        if ($state -ne 'Started') {
            Invoke-TSICDevChange "start pool $pool (was $state)" { Start-WebAppPool -Name $pool }
        } else { Write-Same "pool $pool started" }
    }

    Complete-TSICDevStep 'Application pools'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
