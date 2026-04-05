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
    ApiPoolName     = 'claude-api'
    AngularPoolName = 'claude-app'
    ApiSiteName     = 'claude-api'
    AngularSiteName = 'claude-app'
    ApiHostname     = 'devapi.teamsportsinfo.com'
    AngularHostname = 'dev.teamsportsinfo.com'
    BasePath        = 'C:\Websites'
    ApiPath         = 'C:\Websites\claude-api'
    AngularPath     = 'C:\Websites\claude-app'
    BackupsPath     = 'C:\Websites\Backups'
    DeployApiPath     = 'C:\Websites\claude-api'
    DeployAngularPath = 'C:\Websites\claude-app'
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
    Write-Host "  Backups:        $($Config.BackupsPath)" -ForegroundColor White
    Write-Host "  SQL:            $($Config.SqlInstance) / $($Config.DatabaseName)" -ForegroundColor White
    Write-Host ""
}
