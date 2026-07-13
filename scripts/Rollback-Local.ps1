# ============================================================================
# Rollback-Local.ps1 - Restore a previous build to local IIS (dev-api/dev-app)
# ============================================================================
# Reads the timestamped backups written by 1-Build-And-Deploy-Local.ps1 (Step 5)
# and mirrors one back over the live folder.
#
#   .\Rollback-Local.ps1                          # newest backup, both sites
#   .\Rollback-Local.ps1 -SkipAngular             # API only
#   .\Rollback-Local.ps1 -Timestamp 20260713-104500
#   .\Rollback-Local.ps1 -List                    # show what's available, change nothing
#
# This is the rehearsal for IIS-Config-Prod\Deployment\Rollback-Deploy.ps1.
#
# NOTE: bounces dev-api / dev-app, which serve client-facing
# dev.teamsportsinfo.com. Same blast radius as a routine local deploy.
# ============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$SkipApi,
    [switch]$SkipAngular,
    [string]$Timestamp,
    [switch]$List
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\IIS-Config-Dev\_config.ps1"
. "$PSScriptRoot\_deploy-common.ps1"

$ApiSite     = $Config.ApiSiteName
$AngularSite = $Config.AngularSiteName
$ApiLive     = $Config.ApiPath
$AngularLive = $Config.AngularPath
$BasePath    = $Config.BasePath
$BackupsPath = $Config.BackupsPath
$AspNetEnv   = $Config.AspNetEnv
$ApiHost     = $Config.ApiHostname
$AngularHost = $Config.AngularHostname

$StartedAt = Get-Date

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  LOCAL ROLLBACK (dev-api / dev-app)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Import-Module WebAdministration -ErrorAction Stop

# ── -List: show what is available and exit, changing nothing ────────
if ($List) {
    foreach ($site in @($ApiSite, $AngularSite)) {
        Write-Host "  $site" -ForegroundColor Yellow
        $pattern = Get-TsicBackupPattern -Site $site
        $found = Get-ChildItem -LiteralPath $BackupsPath -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match $pattern } | Sort-Object Name -Descending
        if (-not $found) { Write-Host "    (none)" -ForegroundColor DarkGray; continue }
        foreach ($b in $found) {
            $m = Get-TsicManifest -Path $b.FullName
            $what = if ($m) { "$($m.buildStamp)  ($($m.fileCount) files)" } else { "(no manifest - pre-dates the manifest change)" }
            Write-Host "    $($b.Name)   $what" -ForegroundColor White
        }
    }
    Write-Host ""
    exit 0
}

# ── Resolve which backup to restore for each site ───────────────────
function Resolve-Backup {
    param(
        [Parameter(Mandatory)] [string] $Site,
        [string] $Stamp
    )

    if ($Stamp) {
        $dir = Join-Path $BackupsPath "$Site-$Stamp"
        if (-not (Test-Path -LiteralPath $dir)) {
            Write-Host "  ERROR: no backup at $dir" -ForegroundColor Red
            return $null
        }
        return (Get-Item -LiteralPath $dir)
    }

    $latest = Get-ChildItem -LiteralPath $BackupsPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match (Get-TsicBackupPattern -Site $Site) } |
        Sort-Object Name -Descending | Select-Object -First 1

    if (-not $latest) {
        Write-Host "  ERROR: no $Site-* backups in $BackupsPath" -ForegroundColor Red
        Write-Host "  Backups are written by 1-Build-And-Deploy-Local.ps1 (Step 5)." -ForegroundColor Yellow
        return $null
    }
    return $latest
}

$plan = @()
if (-not $SkipApi) {
    $b = Resolve-Backup -Site $ApiSite -Stamp $Timestamp
    if (-not $b) { exit 1 }
    $plan += [pscustomobject]@{ Site = $ApiSite; Backup = $b; Live = $ApiLive; Ex = (Get-TsicExclusions -Site $ApiSite) }
}
if (-not $SkipAngular) {
    $b = Resolve-Backup -Site $AngularSite -Stamp $Timestamp
    if (-not $b) { exit 1 }
    $plan += [pscustomobject]@{ Site = $AngularSite; Backup = $b; Live = $AngularLive; Ex = (Get-TsicExclusions -Site $AngularSite) }
}
if (-not $plan.Count) {
    Write-Host "  Nothing to roll back (-SkipApi and -SkipAngular both set)." -ForegroundColor Yellow
    exit 0
}

# ── Prove BOTH ends before showing the operator anything ────────────
# The destination is a live site (Assert-TsicSafeTarget) and the source is one of
# OUR backups sitting directly under the Backups root (Assert-TsicSafeBackup).
# That root also holds legacy's TSICUnify-* backups; an anchored pattern is what
# keeps a rollback from mirroring legacy's files over our site - or worse.
try {
    foreach ($p in $plan) {
        Assert-TsicSafeTarget -Site $p.Site -Path $p.Live -BasePath $BasePath | Out-Null
        Assert-TsicSafeBackup -Site $p.Site -BackupPath $p.Backup.FullName -BackupsRoot $BackupsPath | Out-Null
    }
} catch {
    Write-Host ""
    Write-Host "  ROLLBACK REFUSED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Nothing was touched." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# ── Show exactly what will happen, then confirm ─────────────────────
# robocopy /L writes nothing. This is the real /MIR, listed instead of executed.
foreach ($p in $plan) {
    $age = [math]::Round(((Get-Date) - $p.Backup.CreationTime).TotalHours, 1)
    $m   = Get-TsicManifest -Path $p.Backup.FullName

    Write-Host "  $($p.Site)" -ForegroundColor Yellow
    Write-Host "    restore:  $($p.Backup.Name)   (taken $age h ago)" -ForegroundColor White
    if ($m) {
        Write-Host "    build:    $($m.buildStamp)   git $($m.gitHash)   built $($m.builtUtc) UTC" -ForegroundColor White
    } else {
        Write-Host "    build:    unknown - this backup pre-dates deploy-manifest.json" -ForegroundColor DarkGray
    }
    Write-Host "    into:     $($p.Live)" -ForegroundColor White

    $preview = Get-TsicRoboPreview -Source $p.Backup.FullName -Dest $p.Live `
                    -ExcludeDirs $p.Ex.Dirs -ExcludeFiles $p.Ex.Files
    Write-Host "    changes:  $($preview.Copy) files to restore, $($preview.Delete) to remove" -ForegroundColor White

    $keep = @($p.Ex.Dirs) + @($p.Ex.Files)
    if ($keep.Count) { Write-Host "    keeping:  $($keep -join ', ')" -ForegroundColor DarkGray }
    Write-Host ""
}

Write-Host "  This MIRRORS each backup over the live folder. Press Enter to proceed, Ctrl+C to abort." -ForegroundColor Red
Read-Host
Write-Host ""

$SeqUrl = Get-TsicSeqUrl -AppRoot $ApiLive -AspNetEnv $AspNetEnv

# ── Step 1: Stop pools ──────────────────────────────────────────────
Write-Host "Step 1: Stopping app pools..." -ForegroundColor Yellow
$stopped = @()
foreach ($p in $plan) {
    if (-not (Stop-TsicPool -Pool $p.Site)) {
        Write-Host ""
        Write-Host "  ABORTING - live folders untouched. Restarting what was stopped." -ForegroundColor Red
        foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
        exit 1
    }
    $stopped += $p.Site
}
Write-Host ""

# ── Step 2: Mirror backup -> live ────────────────────────────────────
# Same exclusion list the backup was taken with. Without it /MIR would delete
# App_Data and FirebaseAuth_*.json from live as "extras" - they are absent from
# the backup precisely because they were excluded going in. And this runs as
# ADMIN: were it to write App_Data back, CREATOR OWNER would resolve to
# Administrators, the pool could no longer rewrite its cache, and the ADN
# month-end import would 500 (the bug 993e15be fixed).
Write-Host "Step 2: Restoring..." -ForegroundColor Yellow
$failed = $false
foreach ($p in $plan) {
    Write-Host "  $($p.Backup.Name) -> $($p.Live)" -ForegroundColor White
    $exit = Invoke-TsicRobocopy -Source $p.Backup.FullName -Dest $p.Live `
                -ExcludeDirs $p.Ex.Dirs -ExcludeFiles $p.Ex.Files -Quiet
    if ($exit -ge 8) {
        Write-Host "  ERROR: restore of $($p.Site) failed (robocopy exit $exit)." -ForegroundColor Red
        Write-Host "  $($p.Live) may be PARTIALLY restored - do not assume either build is intact." -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "  Restored: $($p.Site)" -ForegroundColor Green
    }
}
Write-Host ""

# ── Step 3: Start pools ─────────────────────────────────────────────
Write-Host "Step 3: Starting app pools..." -ForegroundColor Yellow
foreach ($p in $plan) {
    if (-not (Start-TsicPool -Pool $p.Site)) { $failed = $true }
}
Write-Host ""

# ── Step 4: Verify ──────────────────────────────────────────────────
if (-not $failed) {
    Write-Host "Step 4: Verifying sites are serving..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    foreach ($p in $plan) {
        $url = if ($p.Site -eq $ApiSite) { "https://$ApiHost/api/jobs/tsic" } else { "https://$AngularHost/" }
        if (-not (Test-TsicEndpoint -Url $url)) { $failed = $true }
    }
    Write-Host ""
}

# ── Outcome ─────────────────────────────────────────────────────────
$elapsed  = [int]((Get-Date) - $StartedAt).TotalSeconds
$restored = ($plan | ForEach-Object { $_.Backup.Name }) -join ', '

if ($failed) {
    Send-TsicDeployEvent -SeqUrl $SeqUrl -Site (($plan | ForEach-Object { $_.Site }) -join ' + ') `
        -Outcome Failed -Environment $AspNetEnv -FailedStep 'Rollback' `
        -BackupName $restored -DurationSec $elapsed

    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  ROLLBACK FAILED - see errors above." -ForegroundColor Red
    Write-Host "  THE SITE MAY BE DOWN." -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site (($plan | ForEach-Object { $_.Site }) -join ' + ') `
    -Outcome RolledBack -Environment $AspNetEnv -BackupName $restored -DurationSec $elapsed

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  ROLLED BACK - verified serving." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
foreach ($p in $plan) {
    $m = Get-TsicManifest -Path $p.Live
    $what = if ($m) { " ($($m.buildStamp))" } else { "" }
    Write-Host "  $($p.Site) -> $($p.Backup.Name)$what" -ForegroundColor Green
}
Write-Host ""
