# ============================================================================
# _config.ps1 — IIS Configuration for TSIC Development (TSIC-SEDONA)
# ============================================================================
# Dot-source this file from any Setup or Deployment script:
#   . "$PSScriptRoot\..\_config.ps1"
#
# Provides: $Config hashtable with all names, paths, and hostnames.
# This file is Dev-only. See IIS-Config-Prod for production.
# ============================================================================

$Config = @{
    Environment     = 'Dev'
    ApiPoolName     = 'dev-api'
    AngularPoolName = 'dev-app'
    ApiSiteName     = 'dev-api'
    AngularSiteName = 'dev-app'
    ApiHostname     = 'devapi.teamsportsinfo.com'
    AngularHostname = 'dev.teamsportsinfo.com'
    BasePath        = 'C:\Websites'
    ApiPath         = 'C:\Websites\dev-api'
    AngularPath     = 'C:\Websites\dev-app'
    StaticsPath     = 'C:\Websites\TSIC-STATICS'
    BackupsPath     = 'C:\Websites\Backups'
    DeployApiPath     = 'C:\Websites\dev-api'
    DeployAngularPath = 'C:\Websites\dev-app'
    DeployBackupsPath = 'C:\Websites\Backups'
    DatabaseName    = 'TSICV5'
    SqlInstance     = '.\SS2016'
    AspNetEnv       = 'Development'
}

function Show-Config {
    Write-Host ""
    Write-Host "TSIC IIS Configuration - Dev (TSIC-SEDONA)" -ForegroundColor Cyan
    Write-Host "  API Pool/Site:  $($Config.ApiPoolName)" -ForegroundColor White
    Write-Host "  API Hostname:   $($Config.ApiHostname)" -ForegroundColor White
    Write-Host "  API Path:       $($Config.ApiPath)" -ForegroundColor White
    Write-Host "  Angular Pool:   $($Config.AngularPoolName)" -ForegroundColor White
    Write-Host "  Angular Host:   $($Config.AngularHostname)" -ForegroundColor White
    Write-Host "  Angular Path:   $($Config.AngularPath)" -ForegroundColor White
    Write-Host "  Statics:        $($Config.StaticsPath)" -ForegroundColor White
    Write-Host "  Backups:        $($Config.BackupsPath)" -ForegroundColor White
    Write-Host "  SQL:            $($Config.SqlInstance) / $($Config.DatabaseName)" -ForegroundColor White
    Write-Host ""
}
