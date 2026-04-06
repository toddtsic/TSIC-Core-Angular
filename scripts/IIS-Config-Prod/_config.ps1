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
    [string]$Environment = 'Prod'
)

# ---------------------------------------------------------------------------
# Unified names (same on every server)
# ---------------------------------------------------------------------------
$Config = @{
    ApiPoolName     = 'claude-api'
    AngularPoolName = 'claude-app'
    ApiSiteName     = 'claude-api'
    AngularSiteName = 'claude-app'
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
    # Go-live checklist:
    #   1. Stop TSIC-Unify-2024 (old Angular catch-all)
    #   2. Remove claude-app hostname binding from claude-app site (becomes catch-all)
    #   3. claude-api stays as the permanent API hostname
    Prod = @{
        ApiHostname     = 'claude-api.teamsportsinfo.com'
        AngularHostname = 'claude-app.teamsportsinfo.com'
        BasePath        = 'E:\Websites'
        StaticsPath     = 'E:\Websites\TSIC-STATICS'
        BackupsPath     = 'E:\Websites\Backups'
        AspNetEnv       = 'Production'
        ProdServer      = '204.17.37.202'   # TSIC-PHOENIX (hostname doesn't resolve from dev)
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

# Staging folders sit next to live folders
$Config.ApiStagingName     = "$($Config.ApiSiteName)-STAGING"
$Config.AngularStagingName = "$($Config.AngularSiteName)-STAGING"
$Config.ApiStagingPath     = Join-Path $Env.BasePath $Config.ApiStagingName
$Config.AngularStagingPath = Join-Path $Env.BasePath $Config.AngularStagingName

# For Prod deploys from dev machine: resolve paths to UNC share
# E:\Websites on TSIC-PHOENIX is shared as \\TSIC-PHOENIX\Websites
if ($Environment -eq 'Prod' -and $Config.ProdServer) {
    $share = "\\$($Config.ProdServer)\Websites"
    $Config.DeployApiPath         = "$share\$($Config.ApiSiteName)"
    $Config.DeployAngularPath     = "$share\$($Config.AngularSiteName)"
    $Config.DeployApiStagingPath  = "$share\$($Config.ApiStagingName)"
    $Config.DeployAngularStagingPath = "$share\$($Config.AngularStagingName)"
    $Config.DeployBackupsPath     = "$share\Backups"
} else {
    $Config.DeployApiPath         = $Config.ApiPath
    $Config.DeployAngularPath     = $Config.AngularPath
    $Config.DeployApiStagingPath  = $Config.ApiStagingPath
    $Config.DeployAngularStagingPath = $Config.AngularStagingPath
    $Config.DeployBackupsPath     = $Config.BackupsPath
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
    Write-Host "  API Staging:    $($Config.DeployApiStagingPath)" -ForegroundColor White
    Write-Host "  Angular Staging:$($Config.DeployAngularStagingPath)" -ForegroundColor White
    Write-Host "  Statics:        $($Config.StaticsPath)" -ForegroundColor White
    Write-Host "  Backups:        $($Config.DeployBackupsPath)" -ForegroundColor White
    Write-Host "  SQL:            $($Config.SqlInstance) / $($Config.DatabaseName)" -ForegroundColor White
    if ($Config.ProdServer) {
        Write-Host "  Prod Server:    $($Config.ProdServer)" -ForegroundColor White
    }
    Write-Host ""
}
