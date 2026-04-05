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
        BackupsPath     = 'C:\Websites\Backups'
        AspNetEnv       = 'Development'
        ProdServer      = $null
    }
    Prod = @{
        ApiHostname     = 'api.teamsportsinfo.com'
        AngularHostname = 'teamsportsinfo.com'
        BasePath        = 'E:\Websites'
        StaticsPath     = 'E:\Websites\TSIC-STATICS'
        BackupsPath     = 'E:\Websites\Backups'
        AspNetEnv       = 'Production'
        ProdServer      = 'TSIC-PHOENIX'
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
$Config.BackupsPath     = $Env.BackupsPath
$Config.ProdServer      = $Env.ProdServer

# For Prod deploys from dev machine: resolve paths to UNC share
# E:\Websites on TSIC-PHOENIX is shared as \\TSIC-PHOENIX\Websites
if ($Environment -eq 'Prod' -and $Config.ProdServer) {
    $Config.DeployApiPath     = "\\$($Config.ProdServer)\Websites\$($Config.ApiSiteName)"
    $Config.DeployAngularPath = "\\$($Config.ProdServer)\Websites\$($Config.AngularSiteName)"
    $Config.DeployBackupsPath = "\\$($Config.ProdServer)\Websites\Backups"
} else {
    $Config.DeployApiPath     = $Config.ApiPath
    $Config.DeployAngularPath = $Config.AngularPath
    $Config.DeployBackupsPath = $Config.BackupsPath
}

# ---------------------------------------------------------------------------
# Display (when sourced interactively)
# ---------------------------------------------------------------------------
function Show-Config {
    Write-Host ""
    Write-Host "TSIC IIS Configuration — $($Config.Environment)" -ForegroundColor Cyan
    Write-Host "  API Pool/Site:  $($Config.ApiPoolName)" -ForegroundColor White
    Write-Host "  API Hostname:   $($Config.ApiHostname)" -ForegroundColor White
    Write-Host "  API Path:       $($Config.ApiPath)" -ForegroundColor White
    Write-Host "  Deploy API:     $($Config.DeployApiPath)" -ForegroundColor White
    Write-Host "  Angular Pool:   $($Config.AngularPoolName)" -ForegroundColor White
    Write-Host "  Angular Host:   $($Config.AngularHostname)" -ForegroundColor White
    Write-Host "  Angular Path:   $($Config.AngularPath)" -ForegroundColor White
    Write-Host "  Deploy Angular: $($Config.DeployAngularPath)" -ForegroundColor White
    Write-Host "  Statics:        $($Config.StaticsPath)" -ForegroundColor White
    Write-Host "  Backups:        $($Config.DeployBackupsPath)" -ForegroundColor White
    Write-Host "  SQL:            $($Config.SqlInstance) / $($Config.DatabaseName)" -ForegroundColor White
    if ($Config.ProdServer) {
        Write-Host "  Prod Server:    $($Config.ProdServer)" -ForegroundColor White
    }
    Write-Host ""
}
