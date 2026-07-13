# ============================================================================
# Rollback-Deploy.ps1 - Restore a previous build to PRODUCTION (TSIC-PHOENIX)
# ============================================================================
# Reads the timestamped backups written by Recycle-After-Deploy.ps1 (Step 2) and
# mirrors one back over the live folder.
#
# Usage (on TSIC-PHOENIX, elevated):
#   .\Rollback-Deploy.ps1                          # newest backup, both sites
#   .\Rollback-Deploy.ps1 -SkipAngular             # API only
#   .\Rollback-Deploy.ps1 -Timestamp 20260713-104500
#   .\Rollback-Deploy.ps1 -List                    # show what's available, change nothing
#
# It tells you WHICH BUILD each backup holds and runs a robocopy /L preview of
# exactly what it would copy and delete, then waits for you to confirm. Nothing
# is written before that prompt.
#
# Rehearsed on the dev box by scripts\Rollback-Local.ps1 -- same shared
# primitives, same guards.
# ============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$SkipApi,
    [switch]$SkipAngular,
    [string]$Timestamp,
    [switch]$List
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\..\_config.ps1" -Environment Prod
. "$PSScriptRoot\..\_deploy-common.ps1"

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
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  PRODUCTION ROLLBACK (claude-api / claude-app)" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
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
            $what = if ($m) { "$($m.buildStamp)   git $($m.gitHash)   $($m.fileCount) files" }
                    else    { "(no manifest - pre-dates the manifest change)" }
            Write-Host "    $($b.Name)   $what" -ForegroundColor White
        }
    }
    Write-Host ""
    Write-Host "  Restore a specific one with: -Timestamp yyyyMMdd-HHmmss" -ForegroundColor DarkGray
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
            Write-Host "  Run with -List to see what is available." -ForegroundColor Yellow
            return $null
        }
        return (Get-Item -LiteralPath $dir)
    }

    $latest = Get-ChildItem -LiteralPath $BackupsPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match (Get-TsicBackupPattern -Site $Site) } |
        Sort-Object Name -Descending | Select-Object -First 1

    if (-not $latest) {
        Write-Host "  ERROR: no $Site-* backups in $BackupsPath" -ForegroundColor Red
        Write-Host "  Backups are written by Recycle-After-Deploy.ps1 (Step 2)." -ForegroundColor Yellow
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
# Destination is a live site of ours (Assert-TsicSafeTarget); source is one of
# OUR backups directly under the Backups root (Assert-TsicSafeBackup). That root
# ALSO holds legacy's TSICUnify-2024-* and TSICUnify-Api-* backups, and legacy is
# still running -- the anchored pattern is what keeps a rollback from mirroring
# legacy's files over our site.
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
    $from = Get-TsicManifest -Path $p.Backup.FullName
    $now  = Get-TsicManifest -Path $p.Live

    Write-Host "  $($p.Site)" -ForegroundColor Yellow
    Write-Host "    live now: $(if ($now)  { "$($now.buildStamp)   git $($now.gitHash)" }  else { 'unknown' })" -ForegroundColor White
    Write-Host "    restore:  $(if ($from) { "$($from.buildStamp)   git $($from.gitHash)" } else { 'unknown - backup pre-dates deploy-manifest.json' })" -ForegroundColor White
    Write-Host "    from:     $($p.Backup.Name)   (taken $age h ago)" -ForegroundColor White
    Write-Host "    into:     $($p.Live)" -ForegroundColor White

    $preview = Get-TsicRoboPreview -Source $p.Backup.FullName -Dest $p.Live `
                    -ExcludeDirs $p.Ex.Dirs -ExcludeFiles $p.Ex.Files
    Write-Host "    changes:  $($preview.Copy) files to restore, $($preview.Delete) to remove" -ForegroundColor White

    $keep = @($p.Ex.Dirs) + @($p.Ex.Files)
    if ($keep.Count) { Write-Host "    keeping:  $($keep -join ', ')" -ForegroundColor DarkGray }
    Write-Host ""
}

Write-Host "  This MIRRORS each backup over the live PRODUCTION folder." -ForegroundColor Red
Write-Host "  Press Enter to proceed, Ctrl+C to abort." -ForegroundColor Red
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

# ── Step 2: Mirror backup -> live ───────────────────────────────────
# Same exclusion list the backup was taken with. Without it, /MIR would delete
# App_Data and FirebaseAuth_*.json from live as "extras" -- they are absent from
# the backup precisely because they were excluded going in, and no redeploy
# recreates the Firebase credentials.
#
# This also runs as ADMIN. Were it to write App_Data back, CREATOR OWNER would
# resolve to Administrators, the cache would land admin-owned, the app pool could
# no longer rewrite it, and the ADN month-end import would 500 -- the bug
# 993e15be fixed. Excluding it is not an optimisation; it is the fix.
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
$sites    = ($plan | ForEach-Object { $_.Site }) -join ' + '
$restored = ($plan | ForEach-Object { $_.Backup.Name }) -join ', '

if ($failed) {
    Send-TsicDeployEvent -SeqUrl $SeqUrl -Site $sites -Outcome Failed -Environment $AspNetEnv `
        -FailedStep 'Rollback' -BackupName $restored -DurationSec $elapsed

    Write-Host "==================================================" -ForegroundColor Red
    Write-Host "  ROLLBACK FAILED - see errors above." -ForegroundColor Red
    Write-Host "  PRODUCTION MAY BE DOWN." -ForegroundColor Red
    Write-Host "==================================================" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site $sites -Outcome RolledBack -Environment $AspNetEnv `
    -BackupName $restored -DurationSec $elapsed

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  ROLLED BACK - production verified serving." -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Cyan
foreach ($p in $plan) {
    $m = Get-TsicManifest -Path $p.Live
    Write-Host ("  {0,-12} -> {1}  {2}" -f $p.Site, $p.Backup.Name, $(if ($m) { "($($m.buildStamp))" } else { "" })) -ForegroundColor Green
}
Write-Host ""

# robocopy exits 1/2/3 on SUCCESS; without this the script would inherit that.
exit 0
