# ============================================================================
# Recycle-After-Deploy.ps1 - Run ON TSIC-PHOENIX to swap staging into live
# ============================================================================
# Stops the app pools, backs up the live folders, mirrors staging -> live,
# restarts, and VERIFIES the sites are serving before it says so.
#
# Usage (on TSIC-PHOENIX, elevated):
#   .\Recycle-After-Deploy.ps1               # Swap both API + Angular
#   .\Recycle-After-Deploy.ps1 -SkipApi      # Swap Angular only
#   .\Recycle-After-Deploy.ps1 -SkipAngular  # Swap API only
#
# Normally invoked by the Go.ps1 dropped in each staging folder.
#
# Rollback:  .\Rollback-Deploy.ps1
#
# This is the ONLY script that mutates production. Every check that can fail
# runs BEFORE the mirror, while the live folders are still intact -- so a failed
# check costs you a deploy, not an outage. The success banner at the bottom is
# earned: it prints only if the pools restarted AND both sites answered.
#
# The same logic, calling the same shared primitives, is rehearsed on the dev
# box by scripts\1-Build-And-Deploy-Local.ps1.
# ============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$SkipApi,
    [switch]$SkipAngular
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\..\_config.ps1" -Environment Prod
. "$PSScriptRoot\..\_deploy-common.ps1"

$ApiSite     = $Config.ApiSiteName            # claude-api (pool has the same name)
$AngularSite = $Config.AngularSiteName        # claude-app
$ApiLive     = $Config.ApiPath                # E:\Websites\claude-api
$AngularLive = $Config.AngularPath            # E:\Websites\claude-app
$ApiStaging  = $Config.ApiStagingPath         # E:\Websites\claude-api-STAGING
$AngStaging  = $Config.AngularStagingPath     # E:\Websites\claude-app-STAGING
$BasePath    = $Config.BasePath               # E:\Websites
$BackupsPath = $Config.BackupsPath
$AspNetEnv   = $Config.AspNetEnv              # Production
$ApiHost     = $Config.ApiHostname
$AngularHost = $Config.AngularHostname

$StartedAt = Get-Date
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$SeqUrl     = $null
$BuildStamp = ''
$GitHash    = ''
$ApiBackup  = ''
$AngBackup  = ''

# What this run is being asked to swap. If a requested site turns out to be
# unusable we abort the WHOLE run -- we never quietly drop one and ship the
# other, which is how you get a new frontend talking to an old API.
$doApi     = -not $SkipApi
$doAngular = -not $SkipAngular

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PRODUCTION DEPLOY - Stop, Swap, Verify" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ── Transcript ───────────────────────────────────────────────────────
# Production with no record of what changed it is not worth having. If the
# transcript cannot start, refuse to deploy.
$logDir = Join-Path $PSScriptRoot "logs"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$logPath = Join-Path $logDir "deploy-prod-$Timestamp.log"
try {
    Start-Transcript -Path $logPath | Out-Null
} catch {
    Write-Host "  ERROR: could not start transcript at $logPath" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  REFUSING to deploy to production without a record. Nothing was touched." -ForegroundColor Red
    exit 1
}
Write-Host "  Log: $logPath" -ForegroundColor DarkGray
Write-Host ""

function Stop-DeployWithFailure {
    param(
        [Parameter(Mandatory)] [string] $Step,
        [string] $Detail = '',
        [switch] $LiveUntouched
    )

    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Red
    Write-Host "  DEPLOY FAILED - $Step" -ForegroundColor Red
    Write-Host "==================================================" -ForegroundColor Red
    if ($Detail) { Write-Host "  $Detail" -ForegroundColor Red }
    Write-Host ""

    if ($LiveUntouched) {
        Write-Host "  Live folders were NOT modified." -ForegroundColor Yellow
        Write-Host "  Production is still serving the previous build." -ForegroundColor Yellow
    } else {
        Write-Host "  PRODUCTION MAY BE DOWN. Live folders were modified." -ForegroundColor Red
        Write-Host ""
        Write-Host "  Roll back with:" -ForegroundColor Yellow
        if ($ApiBackup -or $AngBackup) {
            Write-Host "    $PSScriptRoot\Rollback-Deploy.ps1 -Timestamp $Timestamp" -ForegroundColor White
        } else {
            Write-Host "    $PSScriptRoot\Rollback-Deploy.ps1" -ForegroundColor White
        }
    }
    Write-Host ""
    Write-Host "  Transcript: $logPath" -ForegroundColor Yellow
    Write-Host ""

    Send-TsicDeployEvent -SeqUrl $SeqUrl -Site (@(if ($doApi) { $ApiSite }; if ($doAngular) { $AngularSite }) -join ' + ') `
        -Outcome Failed -Environment $AspNetEnv -FailedStep $Step `
        -BuildStamp $BuildStamp -GitHash $GitHash -BackupName "$ApiBackup $AngBackup".Trim() `
        -LogPath $logPath -DurationSec ([int]((Get-Date) - $StartedAt).TotalSeconds)

    exit 1
}

try {

Import-Module WebAdministration -ErrorAction Stop

# ── Step 0: Preflight - nothing is touched until all of this passes ──
Write-Host "Step 0: Preflight..." -ForegroundColor Yellow

if (-not $doApi -and -not $doAngular) {
    Write-Host "  Nothing to swap (-SkipApi and -SkipAngular both set)." -ForegroundColor Yellow
    exit 0
}

# Seq first, from a plain file read, so even a preflight abort is reportable.
$SeqUrl = Get-TsicSeqUrl -AppRoot $ApiLive -AspNetEnv $AspNetEnv
if ($SeqUrl) { Write-Host "  Seq: $SeqUrl" -ForegroundColor DarkGray }
else         { Write-Host "  Seq: not configured - deploy events will not be logged." -ForegroundColor DarkGray }

# Prove the destinations. E:\Websites also holds TSICUnify-2024 (legacy, LIVE),
# TSICUnify-Api, TSIC-CR-2025 and TSIC-STATICS. robocopy /MIR at a wrong
# destination does not corrupt a folder, it EMPTIES it -- so "under E:\Websites"
# is not a safety property; that is exactly where legacy lives.
try {
    if ($doApi)     { Assert-TsicSafeTarget -Site $ApiSite     -Path $ApiLive     -BasePath $BasePath | Out-Null }
    if ($doAngular) { Assert-TsicSafeTarget -Site $AngularSite -Path $AngularLive -BasePath $BasePath | Out-Null }
} catch {
    Stop-DeployWithFailure -Step "Preflight (unsafe target)" -Detail $_.Exception.Message -LiveUntouched
}
if ($doApi)     { Write-Host "  Target verified: $ApiLive" -ForegroundColor Green }
if ($doAngular) { Write-Host "  Target verified: $AngularLive" -ForegroundColor Green }

# Prove the payloads. "The staging folder is non-empty" is NOT "the staging
# folder is a deployable build" -- a half-copied push over the SMB link passes
# that test, and /MIR would then delete from live every file the partial copy
# happens to lack. deploy-manifest.json pins the file count, which catches it.
if ($doApi) {
    $bad = Test-TsicPayload -Path $ApiStaging -Site $ApiSite
    if ($bad) {
        Stop-DeployWithFailure -Step "Preflight (API staging payload)" -LiveUntouched `
            -Detail "$ApiStaging -- $bad. Re-run 1-Build-And-Deploy-Prod.ps1."
    }
    $apiManifest = Get-TsicManifest -Path $ApiStaging
    $BuildStamp  = $apiManifest.buildStamp
    $GitHash     = $apiManifest.gitHash
    Write-Host "  API payload:     $($apiManifest.buildStamp)  git $($apiManifest.gitHash)  $($apiManifest.fileCount) files" -ForegroundColor Green
}
if ($doAngular) {
    $bad = Test-TsicPayload -Path $AngStaging -Site $AngularSite
    if ($bad) {
        Stop-DeployWithFailure -Step "Preflight (Angular staging payload)" -LiveUntouched `
            -Detail "$AngStaging -- $bad. Re-run 1-Build-And-Deploy-Prod.ps1."
    }
    $angManifest = Get-TsicManifest -Path $AngStaging
    $BuildStamp  = $angManifest.buildStamp
    $GitHash     = $angManifest.gitHash
    Write-Host "  Angular payload: $($angManifest.buildStamp)  git $($angManifest.gitHash)  $($angManifest.fileCount) files" -ForegroundColor Green
}

if ($doApi -and -not (Test-TsicBackupSpace -Source $ApiLive -BackupsPath $BackupsPath)) {
    Stop-DeployWithFailure -Step "Preflight (disk space)" -LiveUntouched
}
if ($doAngular -and -not (Test-TsicBackupSpace -Source $AngularLive -BackupsPath $BackupsPath)) {
    Stop-DeployWithFailure -Step "Preflight (disk space)" -LiveUntouched
}

# Show exactly what the mirror will do, before it does it. /L writes nothing.
Write-Host ""
Write-Host "  This deploy will:" -ForegroundColor Yellow
if ($doApi) {
    $p = Get-TsicRoboPreview -Source $ApiStaging -Dest $ApiLive `
            -ExcludeDirs (Get-TsicExclusions -Site $ApiSite).Dirs `
            -ExcludeFiles (Get-TsicExclusions -Site $ApiSite).Files
    Write-Host "    $ApiSite      $($p.Copy) files to copy, $($p.Delete) to remove" -ForegroundColor White
}
if ($doAngular) {
    $p = Get-TsicRoboPreview -Source $AngStaging -Dest $AngularLive `
            -ExcludeDirs (Get-TsicExclusions -Site $AngularSite).Dirs `
            -ExcludeFiles (Get-TsicExclusions -Site $AngularSite).Files
    Write-Host "    $AngularSite      $($p.Copy) files to copy, $($p.Delete) to remove" -ForegroundColor White
}
Write-Host ""

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site (@(if ($doApi) { $ApiSite }; if ($doAngular) { $AngularSite }) -join ' + ') `
    -Outcome Started -Environment $AspNetEnv -BuildStamp $BuildStamp -GitHash $GitHash -LogPath $logPath

# ── Step 1: Stop app pools ──────────────────────────────────────────
# Must reach 'Stopped', not merely leave 'Started'. A running worker still holds
# a file lock on TSIC.API.dll, and mirroring into a locked folder gives you a
# half-swapped site. The old script only WARNED here and mirrored anyway.
Write-Host "Step 1: Stopping app pools..." -ForegroundColor Yellow
$stopped = @()
foreach ($pool in @(if ($doApi) { $ApiSite }; if ($doAngular) { $AngularSite })) {
    if (-not (Stop-TsicPool -Pool $pool)) {
        foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
        Stop-DeployWithFailure -Step "Step 1 (pool '$pool' would not stop)" -LiveUntouched
    }
    $stopped += $pool
}
Write-Host ""

# ── Step 2: Back up the live folders ────────────────────────────────
# The pools are stopped and Step 3 has not run: this is the last moment the
# currently-live build exists on disk. A failed backup ABORTS -- a mirrored
# production deploy with nothing to roll back to is strictly worse than not
# deploying, and live is still intact here, so aborting is free.
Write-Host "Step 2: Backing up live folders..." -ForegroundColor Yellow

if ($doApi) {
    $ApiBackup = New-TsicBackup -Source $ApiLive -BackupsPath $BackupsPath -Site $ApiSite -Timestamp $Timestamp
    if ($null -eq $ApiBackup) {
        foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
        Stop-DeployWithFailure -Step "Step 2 (API backup failed)" -LiveUntouched
    }
}
if ($doAngular) {
    $AngBackup = New-TsicBackup -Source $AngularLive -BackupsPath $BackupsPath -Site $AngularSite -Timestamp $Timestamp
    if ($null -eq $AngBackup) {
        foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
        Stop-DeployWithFailure -Step "Step 2 (Angular backup failed)" -LiveUntouched
    }
}
Write-Host ""

# ── Step 3: Mirror staging -> live ──────────────────────────────────
# The ONE shared exclusion list. App_Data, logs and keys are excluded, so they
# are neither copied nor purged -- they persist, pool-owned, exactly as the app
# left them. (The old sync excluded logs and keys but NOT App_Data, and staging
# never had App_Data, so /MIR deleted the pool's month-end cache from production
# on every single deploy.)
#
# The old sync also carried "/XF FirebaseAuth_*.json". That is gone: the file is
# a build artifact identical on both boxes, and excluding it only kept the
# credential out of every backup. See _deploy-common.ps1.
Write-Host "Step 3: Syncing staging into live..." -ForegroundColor Yellow

foreach ($m in @(
    if ($doApi)     { @{ Site = $ApiSite;     Src = $ApiStaging; Dst = $ApiLive } }
    if ($doAngular) { @{ Site = $AngularSite; Src = $AngStaging; Dst = $AngularLive } }
)) {
    $ex = Get-TsicExclusions -Site $m.Site
    $exit = Invoke-TsicRobocopy -Source $m.Src -Dest $m.Dst `
                -ExcludeDirs $ex.Dirs -ExcludeFiles $ex.Files -Quiet
    if ($exit -ge 8) {
        Stop-DeployWithFailure -Step "Step 3 ($($m.Site) sync failed, robocopy exit $exit)" `
            -Detail "$($m.Dst) may be PARTIALLY updated. Do not assume either build is intact."
    }
    Write-Host "  Synced: $($m.Site)" -ForegroundColor Green
}
Write-Host ""

# ── Step 4: Restart app pools ───────────────────────────────────────
Write-Host "Step 4: Starting app pools..." -ForegroundColor Yellow
foreach ($pool in $stopped) {
    if (-not (Start-TsicPool -Pool $pool)) {
        Stop-DeployWithFailure -Step "Step 4 (pool '$pool' would not start)"
    }
}
Write-Host ""

# ── Step 5: Verify production is actually serving ───────────────────
# This is the success oracle. A 200 from the API proves the NEW worker booted
# (Program.cs refuses to start without ASPNETCORE_ENVIRONMENT) and reached the
# database. The old script warned on a failed warmup and then printed
# "Done. Sites are live." in green anyway.
Write-Host "Step 5: Verifying sites are serving..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

# A failure HERE is not the same as a failure in Step 3. The swap completed and
# the pools started; it is the HTTP check that came back unhappy. That is usually
# a genuinely broken site -- but it can also be the check itself (this box
# resolving its own public hostname back through NAT). So say precisely what is
# known, and do not stampede an operator into rolling back a healthy site.
$verifyNote = @(
    "The swap COMPLETED and the pools started - this is the verification step."
    "Check by hand before rolling back: open the URL from another machine."
    "  - Site genuinely down    -> roll back with the command below."
    "  - Site loads fine        -> the deploy is good and this check is wrong."
) -join "`n  "

if ($doApi) {
    if (-not (Test-TsicEndpoint -Url "https://$ApiHost/api/jobs/tsic")) {
        Stop-DeployWithFailure -Step "Step 5 (API did not answer)" -Detail $verifyNote
    }
}
if ($doAngular) {
    if (-not (Test-TsicEndpoint -Url "https://$AngularHost/" -Retries 3)) {
        Stop-DeployWithFailure -Step "Step 5 (Angular did not answer)" -Detail $verifyNote
    }
}
Write-Host ""

# ── Done - earned, not printed ──────────────────────────────────────
$elapsed = [int]((Get-Date) - $StartedAt).TotalSeconds

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site (@(if ($doApi) { $ApiSite }; if ($doAngular) { $AngularSite }) -join ' + ') `
    -Outcome Completed -Environment $AspNetEnv -BuildStamp $BuildStamp -GitHash $GitHash `
    -BackupName "$ApiBackup $AngBackup".Trim() -LogPath $logPath -DurationSec $elapsed

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  DEPLOYED - production verified serving." -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# What is live right now, read back from the live folders. These come from ONE
# build, so a mismatch means a half-deploy (an API-only or Angular-only run) --
# worth seeing, not worth failing, since -SkipApi/-SkipAngular are legitimate.
$liveApi = Get-TsicManifest -Path $ApiLive
$liveAng = Get-TsicManifest -Path $AngularLive
Write-Host "  Live now:" -ForegroundColor Green
Write-Host ("    {0,-12} {1}" -f $ApiSite,     $(if ($liveApi) { "$($liveApi.buildStamp)  git $($liveApi.gitHash)" } else { "(no manifest)" })) -ForegroundColor White
Write-Host ("    {0,-12} {1}" -f $AngularSite, $(if ($liveAng) { "$($liveAng.buildStamp)  git $($liveAng.gitHash)" } else { "(no manifest)" })) -ForegroundColor White
if ($liveApi -and $liveAng -and $liveApi.buildStamp -ne $liveAng.buildStamp) {
    Write-Host ""
    Write-Host "  NOTE: API and Angular are on DIFFERENT builds." -ForegroundColor Yellow
    Write-Host "  Expected after a -SkipApi / -SkipAngular deploy. Otherwise, investigate." -ForegroundColor Yellow
}
Write-Host ""
Write-Host "  API:      https://$ApiHost" -ForegroundColor Green
Write-Host "  Angular:  https://$AngularHost" -ForegroundColor Green
Write-Host "  Took:     ${elapsed}s" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Roll back with: $PSScriptRoot\Rollback-Deploy.ps1" -ForegroundColor DarkGray
Write-Host ""

# Explicit. robocopy exits 1/2/3 on SUCCESS (copied / extras deleted / both), and
# a script that runs off the end inherits the last native command's exit code --
# so without this, a clean deploy would report failure to anything checking it.
exit 0

} finally {
    try { Stop-Transcript | Out-Null } catch { }
}
