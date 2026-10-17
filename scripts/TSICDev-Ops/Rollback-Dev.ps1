# =============================================================================
# Rollback-Dev.ps1 -- put a previous build back on dev-api / dev-app
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Ops\Rollback-Dev.ps1                            # newest backup, both sites
#   .\scripts\TSICDev-Ops\Rollback-Dev.ps1 -SkipAngular               # API only
#   .\scripts\TSICDev-Ops\Rollback-Dev.ps1 -Timestamp 20261016-153000 # a specific backup
#   .\scripts\TSICDev-Ops\Rollback-Dev.ps1 -List                      # show backups, change nothing
#
# Reads the snapshots Deploy-Dev.ps1 takes before every deploy (Step 5, 3 kept
# per site) and mirrors one back over the live folder. Ported from
# scripts\Rollback-Local.ps1; same shared primitives (_deploy-common.ps1) as the
# production rollback on PHOENIX.
# =============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$SkipApi,
    [switch]$SkipAngular,
    [string]$Timestamp,
    [switch]$List
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TSICDev-Config.ps1')
try { Assert-TSICDevBox } catch { Write-Host "REFUSING to run: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
. (Join-Path $TSICDev.RepoRoot 'scripts\_deploy-common.ps1')

$ApiSite     = $TSICDev.ApiPool
$AngularSite = $TSICDev.AppPool
$ApiLive     = $TSICDev.ApiPath
$AngularLive = $TSICDev.AppPath
$BasePath    = $TSICDev.WebsitesDir
$BackupsPath = $TSICDev.WebBackupsDir
$AspNetEnv   = $TSICDev.AspNetEnv
$StartedAt   = Get-Date

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  ROLLBACK ($ApiSite / $AngularSite)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Import-Module WebAdministration -ErrorAction Stop

# --- -List: show what is available, change nothing ------------------------------
if ($List) {
    foreach ($s in @($ApiSite, $AngularSite)) {
        Write-Host "  $s" -ForegroundColor Yellow
        $found = @(Get-ChildItem -LiteralPath $BackupsPath -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match (Get-TsicBackupPattern -Site $s) } | Sort-Object Name -Descending)
        if (-not $found.Count) { Write-Host "    (none)" -ForegroundColor DarkGray; continue }
        foreach ($b in $found) {
            $m = Get-TsicManifest -Path $b.FullName
            $what = if ($m) { "$($m.buildStamp)  ($($m.fileCount) files)" } else { '(no manifest)' }
            Write-Host "    $($b.Name)   $what" -ForegroundColor White
        }
    }
    Write-Host ""
    exit 0
}

function Resolve-Backup {
    param([Parameter(Mandatory = $true)] [string] $Site, [string] $Stamp)
    if ($Stamp) {
        $dir = Join-Path $BackupsPath "$Site-$Stamp"
        if (-not (Test-Path -LiteralPath $dir)) { Write-Host "  ERROR: no backup at $dir" -ForegroundColor Red; return $null }
        return (Get-Item -LiteralPath $dir)
    }
    $latest = Get-ChildItem -LiteralPath $BackupsPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match (Get-TsicBackupPattern -Site $Site) } |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $latest) {
        Write-Host "  ERROR: no $Site-* backups in $BackupsPath (Deploy-Dev.ps1 Step 5 writes them)." -ForegroundColor Red
        return $null
    }
    return $latest
}

$plan = @()
if (-not $SkipApi) {
    $b = Resolve-Backup -Site $ApiSite -Stamp $Timestamp
    if (-not $b) { exit 1 }
    $plan += [pscustomobject]@{ Site = $ApiSite; Backup = $b; Live = $ApiLive; Url = "https://$($TSICDev.ApiHost)/api/jobs/tsic"; Ex = (Get-TsicExclusions -Site $ApiSite) }
}
if (-not $SkipAngular) {
    $b = Resolve-Backup -Site $AngularSite -Stamp $Timestamp
    if (-not $b) { exit 1 }
    $plan += [pscustomobject]@{ Site = $AngularSite; Backup = $b; Live = $AngularLive; Url = "https://$($TSICDev.AppHost)/"; Ex = (Get-TsicExclusions -Site $AngularSite) }
}
if (-not $plan.Count) { Write-Host "  Nothing to roll back (-SkipApi and -SkipAngular both set)." -ForegroundColor Yellow; exit 0 }

# --- Prove BOTH ends: destination is our live site, source is one of our backups ---
try {
    foreach ($p in $plan) {
        Assert-TsicSafeTarget -Site $p.Site -Path $p.Live -BasePath $BasePath | Out-Null
        Assert-TsicSafeBackup -Site $p.Site -BackupPath $p.Backup.FullName -BackupsRoot $BackupsPath | Out-Null
    }
} catch {
    Write-Host "  ROLLBACK REFUSED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Nothing was touched." -ForegroundColor Yellow
    exit 1
}

# --- Show exactly what will happen (robocopy /L writes nothing), then confirm ----
foreach ($p in $plan) {
    $age = [math]::Round(((Get-Date) - $p.Backup.CreationTime).TotalHours, 1)
    $m   = Get-TsicManifest -Path $p.Backup.FullName
    Write-Host "  $($p.Site)" -ForegroundColor Yellow
    Write-Host "    restore:  $($p.Backup.Name)   (taken $age h ago)" -ForegroundColor White
    if ($m) { Write-Host "    build:    $($m.buildStamp)   git $($m.gitHash)   built $($m.builtUtc) UTC" -ForegroundColor White }
    Write-Host "    into:     $($p.Live)" -ForegroundColor White
    $preview = Get-TsicRoboPreview -Source $p.Backup.FullName -Dest $p.Live -ExcludeDirs $p.Ex.Dirs -ExcludeFiles $p.Ex.Files
    Write-Host "    changes:  $($preview.Copy) files to restore, $($preview.Delete) to remove" -ForegroundColor White
    Write-Host ""
}
$a = Read-Host "Type ROLLBACK to mirror these backups over the live folders"
if ($a -ne 'ROLLBACK') { Write-Host "Aborted. Nothing changed."; exit 1 }
Write-Host ""

$SeqUrl = Get-TsicSeqUrl -AppRoot $ApiLive -AspNetEnv $AspNetEnv

# --- Step 1: Stop pools -------------------------------------------------------------
Write-Host "Step 1: Stopping app pools..." -ForegroundColor Yellow
$stopped = @()
foreach ($p in $plan) {
    if (-not (Stop-TsicPool -Pool $p.Site)) {
        Write-Host "  ABORTING - live folders untouched. Restarting what was stopped." -ForegroundColor Red
        foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
        exit 1
    }
    $stopped += $p.Site
}
Write-Host ""

# --- Step 2: Mirror backup -> live (same exclusions the backup was taken with) -----
Write-Host "Step 2: Restoring..." -ForegroundColor Yellow
$failed = $false
foreach ($p in $plan) {
    Write-Host "  $($p.Backup.Name) -> $($p.Live)" -ForegroundColor White
    $exit = Invoke-TsicRobocopy -Source $p.Backup.FullName -Dest $p.Live -ExcludeDirs $p.Ex.Dirs -ExcludeFiles $p.Ex.Files -Quiet
    if ($exit -ge 8) {
        Write-Host "  ERROR: restore of $($p.Site) failed (robocopy exit $exit); $($p.Live) may be PARTIALLY restored." -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "  Restored: $($p.Site)" -ForegroundColor Green
    }
}
Write-Host ""

# --- Step 3: Start pools -------------------------------------------------------------
Write-Host "Step 3: Starting app pools..." -ForegroundColor Yellow
foreach ($p in $plan) { if (-not (Start-TsicPool -Pool $p.Site)) { $failed = $true } }
Write-Host ""

# --- Step 4: Verify -------------------------------------------------------------------
if (-not $failed) {
    Write-Host "Step 4: Verifying sites are serving..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    foreach ($p in $plan) { if (-not (Test-TsicEndpoint -Url $p.Url)) { $failed = $true } }
    Write-Host ""
}

$elapsed  = [int]((Get-Date) - $StartedAt).TotalSeconds
$restored = ($plan | ForEach-Object { $_.Backup.Name }) -join ', '
$label    = ($plan | ForEach-Object { $_.Site }) -join ' + '

if ($failed) {
    Send-TsicDeployEvent -SeqUrl $SeqUrl -Site $label -Outcome Failed -Environment $AspNetEnv `
        -FailedStep 'Rollback' -BackupName $restored -DurationSec $elapsed
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  ROLLBACK FAILED - see errors above. THE SITE MAY BE DOWN." -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site $label -Outcome RolledBack -Environment $AspNetEnv `
    -BackupName $restored -DurationSec $elapsed
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  ROLLED BACK - verified serving." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
foreach ($p in $plan) {
    $m = Get-TsicManifest -Path $p.Live
    Write-Host "  $($p.Site) -> $($p.Backup.Name)$(if ($m) { " ($($m.buildStamp))" })" -ForegroundColor Green
}
Write-Host ""
exit 0
