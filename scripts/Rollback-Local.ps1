# ============================================================================
# Rollback-Local.ps1 — Restore a previous build to local IIS (dev-api / dev-app)
# ============================================================================
# Reads the timestamped backups written by 1-Build-And-Deploy-Local.ps1 (Step 3.5)
# and mirrors one back over the live folder.
#
# Usage:
#   .\Rollback-Local.ps1                          # newest backup, both sites
#   .\Rollback-Local.ps1 -SkipAngular             # API only
#   .\Rollback-Local.ps1 -Timestamp 20260713-104500
#
# NOTE: this bounces dev-api / dev-app, which serve client-facing
# dev.teamsportsinfo.com. Same blast radius as a routine local deploy.
# ============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$SkipApi,
    [switch]$SkipAngular,
    [string]$Timestamp
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\IIS-Config-Dev\_config.ps1"
. "$PSScriptRoot\_backup-common.ps1"

$BackupsPath = $Config.BackupsPath
$ApiPool     = $Config.ApiPoolName        # dev-api
$AngularPool = $Config.AngularPoolName    # dev-app
$ApiLive     = $Config.ApiPath            # C:\Websites\dev-api
$AngularLive = $Config.AngularPath        # C:\Websites\dev-app
$ApiHostname = $Config.ApiHostname

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  LOCAL ROLLBACK (dev-api / dev-app)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Import-Module WebAdministration -ErrorAction Stop

# ── Resolve which backup to restore for each site ───────────────────
# Exact-prefix match: C:\Websites\Backups also holds a stale claude-api-* dir
# that has nothing to do with the dev sites.
function Resolve-Backup {
    param(
        [Parameter(Mandatory)] [string] $Prefix,
        [string] $Stamp
    )

    if ($Stamp) {
        $dir = Join-Path $BackupsPath "$Prefix-$Stamp"
        if (!(Test-Path $dir)) {
            Write-Host "  ERROR: no backup at $dir" -ForegroundColor Red
            return $null
        }
        return (Get-Item $dir)
    }

    $latest = Get-ChildItem $BackupsPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^$([regex]::Escape($Prefix))-\d{8}-\d{6}$" } |
        Sort-Object Name -Descending | Select-Object -First 1

    if (!$latest) {
        Write-Host "  ERROR: no $Prefix-* backups found in $BackupsPath" -ForegroundColor Red
        Write-Host "  Backups are written by 1-Build-And-Deploy-Local.ps1 (Step 3.5)." -ForegroundColor Yellow
        return $null
    }
    return $latest
}

$plan = @()

if (!$SkipApi) {
    $b = Resolve-Backup -Prefix $ApiPool -Stamp $Timestamp
    if (!$b) { exit 1 }
    $plan += [pscustomobject]@{
        Site   = $ApiPool;  Pool = $ApiPool;  Backup = $b;  Live = $ApiLive
        XD     = $TsicApiXD; XF  = $TsicApiXF
    }
}
if (!$SkipAngular) {
    $b = Resolve-Backup -Prefix $AngularPool -Stamp $Timestamp
    if (!$b) { exit 1 }
    $plan += [pscustomobject]@{
        Site   = $AngularPool; Pool = $AngularPool; Backup = $b; Live = $AngularLive
        XD     = $TsicAngularXD; XF = $TsicAngularXF
    }
}

if (!$plan.Count) {
    Write-Host "  Nothing to roll back (-SkipApi and -SkipAngular both set)." -ForegroundColor Yellow
    exit 0
}

# ── Confirm ──────────────────────────────────────────────────────────
foreach ($p in $plan) {
    $age = [math]::Round(((Get-Date) - $p.Backup.CreationTime).TotalHours, 1)
    Write-Host "  $($p.Site):" -ForegroundColor Yellow
    Write-Host "    restore: $($p.Backup.Name)  (taken $age h ago)" -ForegroundColor White
    Write-Host "    into:    $($p.Live)" -ForegroundColor White
    if ($p.XD.Count -or $p.XF.Count) {
        Write-Host "    keeping: $((@($p.XD) + @($p.XF)) -join ', ')" -ForegroundColor DarkGray
    }
}
Write-Host ""
Write-Host "  This MIRRORS the backup over the live folder. Press Enter to proceed, Ctrl+C to abort." -ForegroundColor Red
Read-Host
Write-Host ""

# ── Step 1: Stop pools ───────────────────────────────────────────────
Write-Host "Step 1: Stopping app pools..." -ForegroundColor Yellow
$stopped = @()
foreach ($p in $plan) {
    if (!(Stop-TsicPool -Pool $p.Pool)) {
        Write-Host ""
        Write-Host "  ABORTING - live folders untouched. Restarting what was stopped." -ForegroundColor Red
        foreach ($s in $stopped) { Start-TsicPool -Pool $s }
        exit 1
    }
    $stopped += $p.Pool
}
Write-Host ""

# ── Step 2: Mirror backup → live ─────────────────────────────────────
# Same exclusions as the backup that produced this folder. Without them /MIR
# would delete App_Data and FirebaseAuth_*.json from live as "extras" — they
# are absent from the backup precisely because they were excluded going in.
Write-Host "Step 2: Restoring..." -ForegroundColor Yellow
$failed = $false
foreach ($p in $plan) {
    Write-Host "  $($p.Backup.Name) -> $($p.Live)" -ForegroundColor White
    $exit = Invoke-TsicRobocopy -Source $p.Backup.FullName -Dest $p.Live `
                -ExcludeDirs $p.XD -ExcludeFiles $p.XF
    if ($exit -ge 8) {
        Write-Host "  ERROR: restore of $($p.Site) failed (robocopy exit $exit)." -ForegroundColor Red
        Write-Host "  $($p.Live) may be PARTIALLY restored - do not assume either build is intact." -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "  Restored: $($p.Site)" -ForegroundColor Green
    }
}
Write-Host ""

# ── Step 3: Start pools ──────────────────────────────────────────────
Write-Host "Step 3: Starting app pools..." -ForegroundColor Yellow
foreach ($p in $plan) { Start-TsicPool -Pool $p.Pool }

if (!$SkipApi -and !$failed) {
    Write-Host ""
    Write-Host "  Warming up API..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    try {
        $null = Invoke-WebRequest -Uri "https://$ApiHostname/api/jobs/tsic" -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
        Write-Host "  API warmed up." -ForegroundColor Green
    } catch {
        Write-Host "  Warmup failed (app may still be starting): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Write-Host ""
if ($failed) {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  ROLLBACK FAILED - see errors above." -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
foreach ($p in $plan) {
    Write-Host "  $($p.Site) rolled back to $($p.Backup.Name)" -ForegroundColor Green
}
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
