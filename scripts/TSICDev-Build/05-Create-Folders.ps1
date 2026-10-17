# =============================================================================
# 05-Create-Folders.ps1 -- site, statics and deploy-backup folders, with permissions
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\05-Create-Folders.ps1 [-CheckOnly]
#
# Run after 04 (the dev-api pool identity must exist before it can be granted).
#
#   C:\Websites\dev-api, dev-app     IIS_IUSRS read + execute (the sites serve from here)
#   dev-api\logs, keys,              dev-api pool MODIFY: runtime logs, the Data
#     App_Data\AdnMonthEnd           Protection key ring, and the ADN month-end cache
#                                    (the import rewrites + deletes it, so Modify)
#   TSIC-STATICS-DEV\BannerFiles,    dev-api pool MODIFY: user uploads land here.
#     RegFileUploads,                The four FileStorage:* folders the API reads and
#     Headshots-AllRegistrants,      writes. RegFileUploads stays EMPTY on this box
#     Store-Sku-Images               (no med forms / vaccine cards here, Todd 10-17).
#   C:\Websites\Backups              Deploy-Dev.ps1's pre-deploy snapshots (admin only)
#
# Only adds missing grants; never removes anything.
# =============================================================================

#Requires -RunAsAdministrator

param([switch]$CheckOnly)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

$Pool = $TSICDev.ApiPoolUser
$ApiWritable     = @('logs', 'keys', 'App_Data\AdnMonthEnd') | ForEach-Object { Join-Path $TSICDev.ApiPath $_ }
$StaticsWritable = @('BannerFiles', 'RegFileUploads', 'Headshots-AllRegistrants', 'Store-Sku-Images') | ForEach-Object { Join-Path $TSICDev.StaticsDir $_ }

try {
    Assert-TSICDevBox
    Write-Host ""
    Write-Host "Folders" -ForegroundColor Cyan
    foreach ($d in @($TSICDev.ApiPath, $TSICDev.AppPath) + $ApiWritable + @($TSICDev.StaticsDir) + $StaticsWritable + @($TSICDev.WebBackupsDir)) {
        Confirm-TSICDevFolder $d
    }

    if ($CheckOnly -and $script:TSICDevDrift -gt 0) {
        Write-Host "  (permissions not checked: folders above are missing)" -ForegroundColor DarkGray
        Complete-TSICDevStep 'Folders'
    }

    Write-Host ""
    Write-Host "Permissions" -ForegroundColor Cyan
    foreach ($d in @($TSICDev.ApiPath, $TSICDev.AppPath)) {
        Grant-TSICDevAccess -Path $d -Identity 'BUILTIN\IIS_IUSRS' -Rights 'ReadAndExecute, Synchronize'
    }
    foreach ($d in $ApiWritable + $StaticsWritable) {
        Grant-TSICDevAccess -Path $d -Identity $Pool -Rights 'Modify, Synchronize'
    }

    Complete-TSICDevStep 'Folders'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
