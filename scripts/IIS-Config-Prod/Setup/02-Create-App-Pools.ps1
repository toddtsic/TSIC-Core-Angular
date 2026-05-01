# ============================================================================
# 02-Create-App-Pools.ps1 — Create claude-api and claude-app application pools
# ============================================================================
# Idempotent. Configures: ApplicationPoolIdentity, LoadUserProfile=true,
# No Managed Runtime, 64-bit.
#
# LoadUserProfile MUST be true so ASP.NET Core can persist DataProtection keys
# under the pool's user profile. Without it, keys are ephemeral and every IIS
# recycle invalidates auth cookies and antiforgery tokens.
# ============================================================================

#Requires -RunAsAdministrator

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Prod'
)

. "$PSScriptRoot\..\_config.ps1" -Environment $Environment

Write-Host ""
Write-Host "[Step 2] Creating application pools ($Environment)..." -ForegroundColor Green

Import-Module WebAdministration -ErrorAction Stop

foreach ($poolName in @($Config.ApiPoolName, $Config.AngularPoolName)) {
    $poolPath = "IIS:\AppPools\$poolName"
    if (Test-Path $poolPath) {
        Write-Host "  App pool '$poolName' already exists." -ForegroundColor DarkGray
    }
    else {
        New-WebAppPool -Name $poolName -Force | Out-Null
        Write-Host "  Created app pool '$poolName'." -ForegroundColor Green
    }

    # Configure: ApplicationPoolIdentity, LoadUserProfile, No Managed Runtime, 64-bit
    Set-ItemProperty $poolPath -Name processModel.identityType -Value 4         # ApplicationPoolIdentity
    Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true  # DataProtection keys persist
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ""            # No managed runtime
    Set-ItemProperty $poolPath -Name enable32BitAppOnWin64 -Value $false        # 64-bit
    Write-Host "  Configured '$poolName': ApplicationPoolIdentity, LoadUserProfile=true, No Managed Runtime, 64-bit" -ForegroundColor White
}

Write-Host "[Step 2] Complete." -ForegroundColor Green
