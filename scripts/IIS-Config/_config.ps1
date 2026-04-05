# ============================================================================
# _config.ps1 — Shared IIS Configuration for TSIC Application
# ============================================================================
# Dot-source this file from any Setup or Deployment script:
#   . "$PSScriptRoot\..\_config.ps1" -Environment Dev
#
# Provides: $Config hashtable with all names, paths, and hostnames.
# ============================================================================

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Dev'
)

# ---------------------------------------------------------------------------
# Unified names (same on every server)
# ---------------------------------------------------------------------------
$Config = @{
    ApiPoolName     = 'TSIC.Api'
    AngularPoolName = 'TSIC.App'
    ApiSiteName     = 'TSIC.Api'
    AngularSiteName = 'TSIC.App'
    DatabaseName    = 'TSICV5'
    SqlInstance     = '.\SS2016'
}

# ---------------------------------------------------------------------------
# Per-environment settings (only paths and hostnames differ)
# ---------------------------------------------------------------------------
$EnvSettings = @{
    Dev = @{
        ApiHostname     = 'devapi.teamsportsinfo.com'
        AngularHostname = 'dev.teamsportsinfo.com'
        BasePath        = 'C:\Websites'
        StaticsPath     = 'C:\Websites\TSIC-STATICS'
        AspNetEnv       = 'Development'
    }
    Prod = @{
        ApiHostname     = 'api.teamsportsinfo.com'
        AngularHostname = 'teamsportsinfo.com'
        BasePath        = 'D:\Websites'
        StaticsPath     = 'E:\Websites\TSIC-STATICS'
        AspNetEnv       = 'Production'
    }
}

# ---------------------------------------------------------------------------
# Merge environment into Config
# ---------------------------------------------------------------------------
$Env = $EnvSettings[$Environment]

$Config.Environment     = $Environment
$Config.ApiHostname     = $Env.ApiHostname
$Config.AngularHostname = $Env.AngularHostname
$Config.BasePath        = $Env.BasePath
$Config.StaticsPath     = $Env.StaticsPath
$Config.ApiPath         = Join-Path $Env.BasePath $Config.ApiSiteName
$Config.AngularPath     = Join-Path $Env.BasePath $Config.AngularSiteName
$Config.AspNetEnv       = $Env.AspNetEnv

# ---------------------------------------------------------------------------
# Display (when sourced interactively)
# ---------------------------------------------------------------------------
function Show-Config {
    Write-Host ""
    Write-Host "TSIC IIS Configuration — $($Config.Environment)" -ForegroundColor Cyan
    Write-Host "  API Pool/Site:  $($Config.ApiPoolName)" -ForegroundColor White
    Write-Host "  API Hostname:   $($Config.ApiHostname)" -ForegroundColor White
    Write-Host "  API Path:       $($Config.ApiPath)" -ForegroundColor White
    Write-Host "  Angular Pool:   $($Config.AngularPoolName)" -ForegroundColor White
    Write-Host "  Angular Host:   $($Config.AngularHostname)" -ForegroundColor White
    Write-Host "  Angular Path:   $($Config.AngularPath)" -ForegroundColor White
    Write-Host "  Statics:        $($Config.StaticsPath)" -ForegroundColor White
    Write-Host "  SQL:            $($Config.SqlInstance) / $($Config.DatabaseName)" -ForegroundColor White
    Write-Host ""
}
