# ============================================================================
# Recycle-After-Deploy.ps1 — Run ON TSIC-PHOENIX after Publish.ps1 stages
# ============================================================================
# Stops app pools, mirrors staging → live via robocopy (only changed files),
# restarts pools, and warms up the API.
#
# Usage (on TSIC-PHOENIX):
#   .\Recycle-After-Deploy.ps1               # Swap both API + Angular
#   .\Recycle-After-Deploy.ps1 -SkipApi      # Swap Angular only
#   .\Recycle-After-Deploy.ps1 -SkipAngular  # Swap API only
# ============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$SkipApi,
    [switch]$SkipAngular
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\..\_config.ps1" -Environment Prod

$ApiPoolName     = $Config.ApiPoolName
$AngularPoolName = $Config.AngularPoolName
$ApiLive         = $Config.ApiPath             # E:\Websites\claude-api
$AngularLive     = $Config.AngularPath         # E:\Websites\claude-app
$ApiStaging      = $Config.ApiStagingPath      # E:\Websites\claude-api-STAGING
$AngularStaging  = $Config.AngularStagingPath  # E:\Websites\claude-app-STAGING
$BackupsPath     = $Config.BackupsPath
$ApiHostname     = $Config.ApiHostname

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Post-Deploy: Stop, Sync, Restart" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Import-Module WebAdministration -ErrorAction Stop

# ── Preflight: verify staging folders have content ───────────────────
$swapApi     = !$SkipApi
$swapAngular = !$SkipAngular

if ($swapApi) {
    if (!(Test-Path $ApiStaging) -or !(Get-ChildItem $ApiStaging -ErrorAction SilentlyContinue)) {
        Write-Host "  WARNING: API staging folder is empty or missing: $ApiStaging" -ForegroundColor Red
        Write-Host "  Run Publish.ps1 first. Skipping API." -ForegroundColor Red
        $swapApi = $false
    }
}
if ($swapAngular) {
    if (!(Test-Path $AngularStaging) -or !(Get-ChildItem $AngularStaging -ErrorAction SilentlyContinue)) {
        Write-Host "  WARNING: Angular staging folder is empty or missing: $AngularStaging" -ForegroundColor Red
        Write-Host "  Run Publish.ps1 first. Skipping Angular." -ForegroundColor Red
        $swapAngular = $false
    }
}

if (!$swapApi -and !$swapAngular) {
    Write-Host "  Nothing to swap. Exiting." -ForegroundColor Yellow
    exit 0
}

if ($swapApi)     { Write-Host "  Will sync: API     ($ApiStaging -> $ApiLive)" -ForegroundColor Yellow }
if ($swapAngular) { Write-Host "  Will sync: Angular ($AngularStaging -> $AngularLive)" -ForegroundColor Yellow }
Write-Host ""

# ── Step 1: Stop app pools ──────────────────────────────────────────
Write-Host "Step 1: Stopping app pools..." -ForegroundColor Yellow

$poolsToRestart = @()
if ($swapApi)     { $poolsToRestart += $ApiPoolName }
if ($swapAngular) { $poolsToRestart += $AngularPoolName }

foreach ($pool in $poolsToRestart) {
    $state = (Get-WebAppPoolState -Name $pool -ErrorAction SilentlyContinue).Value
    if ($state -eq 'Started') {
        Stop-WebAppPool -Name $pool
        Write-Host "  Stopped: $pool" -ForegroundColor Green
    } elseif ($state) {
        Write-Host "  Already stopped: $pool ($state)" -ForegroundColor DarkGray
    } else {
        Write-Host "  App pool not found: $pool" -ForegroundColor Red
    }
}

# Wait for pools to fully stop (must reach 'Stopped', not just leave 'Started')
foreach ($pool in $poolsToRestart) {
    $attempts = 0
    while ($attempts -lt 30) {
        $state = (Get-WebAppPoolState -Name $pool -ErrorAction SilentlyContinue).Value
        if ($state -eq 'Stopped') { break }
        Start-Sleep -Seconds 1
        $attempts++
    }
    if ($state -ne 'Stopped') {
        Write-Host "  WARNING: $pool did not stop after 30s (state: $state)" -ForegroundColor Red
    }
}
Write-Host ""

# ── Step 2: Backup live folders ─────────────────────────────────────
Write-Host "Step 2: Backing up live folders..." -ForegroundColor Yellow
if (!(Test-Path $BackupsPath)) { New-Item -ItemType Directory -Path $BackupsPath -Force | Out-Null }
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

if ($swapApi -and (Test-Path $ApiLive) -and (Get-ChildItem $ApiLive -ErrorAction SilentlyContinue)) {
    $apiBackup = Join-Path $BackupsPath "claude-api-$Timestamp"
    New-Item -ItemType Directory -Path $apiBackup -Force | Out-Null
    # Backup excludes preserved dirs — they're never touched
    robocopy $ApiLive $apiBackup /MIR /XD "$ApiLive\logs" "$ApiLive\keys" /XF "FirebaseAuth_*.json" /NJH /NJS /NDL /NC /NS /NP | Out-Null
    Write-Host "  API backed up to: $apiBackup" -ForegroundColor Green
}

if ($swapAngular -and (Test-Path $AngularLive) -and (Get-ChildItem $AngularLive -ErrorAction SilentlyContinue)) {
    $angBackup = Join-Path $BackupsPath "claude-app-$Timestamp"
    New-Item -ItemType Directory -Path $angBackup -Force | Out-Null
    robocopy $AngularLive $angBackup /MIR /NJH /NJS /NDL /NC /NS /NP | Out-Null
    Write-Host "  Angular backed up to: $angBackup" -ForegroundColor Green
}

# Prune old backups (keep 3 most recent per site)
foreach ($prefix in @('claude-api-', 'claude-app-')) {
    $old = Get-ChildItem $BackupsPath -Directory -Filter "$prefix*" -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -Skip 3
    foreach ($dir in $old) {
        Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Pruned old backup: $($dir.Name)" -ForegroundColor DarkGray
    }
}
Write-Host ""

# ── Step 3: Sync staging → live (robocopy /MIR) ─────────────────────
Write-Host "Step 3: Syncing staging into live..." -ForegroundColor Yellow

if ($swapApi) {
    Write-Host "  robocopy $ApiStaging -> $ApiLive (excluding logs, keys, FirebaseAuth)" -ForegroundColor White
    robocopy $ApiStaging $ApiLive /MIR /XD "$ApiLive\logs" "$ApiLive\keys" /XF "FirebaseAuth_*.json" "Go.ps1" /NJH /NJS /NDL
    # robocopy exit codes: 0=no change, 1=copied, 2=extras deleted, 3=both — all OK
    if ($LASTEXITCODE -ge 8) {
        Write-Host "  ERROR: robocopy failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
    Write-Host "  API synced." -ForegroundColor Green
}

if ($swapAngular) {
    Write-Host "  robocopy $AngularStaging -> $AngularLive" -ForegroundColor White
    robocopy $AngularStaging $AngularLive /MIR /XF "Go.ps1" /NJH /NJS /NDL
    if ($LASTEXITCODE -ge 8) {
        Write-Host "  ERROR: robocopy failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Angular synced." -ForegroundColor Green
}
Write-Host ""

# ── Step 4: Restart app pools ───────────────────────────────────────
Write-Host "Step 4: Starting app pools..." -ForegroundColor Yellow
foreach ($pool in $poolsToRestart) {
    try {
        Start-WebAppPool -Name $pool
        Write-Host "  Started: $pool" -ForegroundColor Green
    } catch {
        Write-Host "  Failed to start ${pool}: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Try manually: Start-WebAppPool $pool" -ForegroundColor Yellow
    }
}

Start-Sleep -Seconds 3

# Warm up API
if ($swapApi) {
    Write-Host ""
    Write-Host "  Warming up API..." -ForegroundColor Yellow
    try {
        $null = Invoke-WebRequest -Uri "https://$ApiHostname/api/jobs/tsic" -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
        Write-Host "  API warmed up!" -ForegroundColor Green
    } catch {
        Write-Host "  Warmup failed (app may still be starting): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Done. Sites are live." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
