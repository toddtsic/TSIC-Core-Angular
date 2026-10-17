# =============================================================================
# 07-Create-SqlLogin.ps1 -- server login for the dev-api app pool
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\07-Create-SqlLogin.ps1 [-CheckOnly]
#
# Needs 01 (SQL Server) and 04 (the pool, so Windows can resolve IIS APPPOOL\dev-api).
#
# Creates the SERVER-level login [IIS APPPOOL\dev-api] only. It lives in master,
# outside the databases, so it survives every restore and is created once.
#
# The DATABASE-level users and grants inside TSICV5 and TSICLogs are NOT made
# here: a restore replaces them with prod's, so TSICDev-Ops\Restore-Databases.ps1
# re-creates them after every restore.
# =============================================================================

#Requires -RunAsAdministrator

param([switch]$CheckOnly)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

$Login = $TSICDev.ApiPoolUser

function Test-Login {
    $out = & sqlcmd -S $TSICDev.SqlInstance -E -b -W -h -1 -Q "SET NOCOUNT ON; SELECT 'login-ok' FROM sys.server_principals WHERE name = N'$Login' AND type = 'U';"
    if ($LASTEXITCODE -ne 0) { throw "Cannot query $($TSICDev.SqlInstance): $(@($out) -join ' ')" }
    (@($out) -join ' ') -match 'login-ok'
}

try {
    Assert-TSICDevBox
    Write-Host ""
    Write-Host "SQL login $Login on $($TSICDev.SqlInstance)" -ForegroundColor Cyan

    if (Test-Login) { Write-Same "login $Login exists" }
    else {
        Invoke-TSICDevChange "create login [$Login]" {
            $out = & sqlcmd -S $TSICDev.SqlInstance -E -b -Q "CREATE LOGIN [$Login] FROM WINDOWS;"
            if ($LASTEXITCODE -ne 0) { throw "CREATE LOGIN failed: $(@($out) -join ' ')" }
        }
        if (-not $CheckOnly) {
            if (-not (Test-Login)) { throw "Login $Login still missing after CREATE LOGIN." }
            Write-Ok "login $Login exists"
        }
    }

    Complete-TSICDevStep 'SQL login'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
