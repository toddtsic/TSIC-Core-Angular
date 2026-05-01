# ============================================================================
# 00-Remove-Sites.ps1 — Remove old TSIC.Api and TSIC.App from IIS
# ============================================================================
# ONE-TIME script to remove the old IIS sites and app pools before
# running Run-Full-Setup.ps1 to create the new dev-api/dev-app sites.
#
# This script is hardcoded to ONLY remove:
#   - Site: TSIC.Api    + App Pool: TSIC.Api
#   - Site: TSIC.App    + App Pool: TSIC.App
#
# It does NOT touch any other site, pool, or directory.
# It does NOT delete files from C:\Websites\TSIC.Api or C:\Websites\TSIC.App.
# ============================================================================

#Requires -RunAsAdministrator

Import-Module WebAdministration -ErrorAction Stop

Write-Host ""
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host "  Remove old IIS sites: TSIC.Api and TSIC.App" -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host ""

$sitesToRemove = @('TSIC.Api', 'TSIC.App')

# Stop and remove sites
foreach ($siteName in $sitesToRemove) {
    $site = Get-Website -Name $siteName -ErrorAction SilentlyContinue
    if ($site) {
        if ($site.State -eq 'Started') {
            Stop-Website -Name $siteName
            Write-Host "  Stopped site: $siteName" -ForegroundColor White
        }
        Remove-Website -Name $siteName
        Write-Host "  Removed site: $siteName" -ForegroundColor Green
    }
    else {
        Write-Host "  Site not found (already removed): $siteName" -ForegroundColor DarkGray
    }
}

# Remove app pools (same names as sites)
foreach ($poolName in $sitesToRemove) {
    $poolPath = "IIS:\AppPools\$poolName"
    if (Test-Path $poolPath) {
        $pool = Get-Item $poolPath
        if ($pool.State -eq 'Started') {
            Stop-WebAppPool -Name $poolName
            Write-Host "  Stopped pool: $poolName" -ForegroundColor White
        }
        Remove-WebAppPool -Name $poolName
        Write-Host "  Removed pool: $poolName" -ForegroundColor Green
    }
    else {
        Write-Host "  Pool not found (already removed): $poolName" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "  Done. Verify in IIS Manager that ONLY TSIC.Api and TSIC.App" -ForegroundColor Yellow
Write-Host "  were removed. All other sites should be untouched." -ForegroundColor Yellow
Write-Host ""

# Show remaining sites for verification
Write-Host "  Remaining IIS sites:" -ForegroundColor Cyan
Get-Website | ForEach-Object {
    Write-Host "    $($_.Name) ($($_.State))" -ForegroundColor White
}
Write-Host ""
