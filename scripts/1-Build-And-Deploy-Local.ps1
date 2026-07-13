# ============================================================================
# 1-Build-And-Deploy-Local.ps1 - Build and deploy to local IIS (dev-api/dev-app)
# ============================================================================
# Builds the API + Angular, mirrors them into C:\Websites, restarts the pools.
#
# This is the REHEARSAL for the production deploy. Every guard here exists in
# IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1, and both call the same
# shared primitives in _deploy-common.ps1 - so what you exercise here is what
# runs on PHOENIX.
#
# NOTE: dev-api / dev-app serve client-facing dev.teamsportsinfo.com. This
# bounces them.
#
# Rollback:  .\Rollback-Local.ps1
# ============================================================================

#Requires -RunAsAdministrator

# No path parameters. A deploy target is a SITE NAME that resolves to exactly
# one literal directory (see Assert-TsicSafeTarget) - never a free-form path an
# operator can point somewhere else.

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\IIS-Config-Dev\_config.ps1"
. "$PSScriptRoot\_deploy-common.ps1"

$ApiSite     = $Config.ApiSiteName        # dev-api  (pool has the same name)
$AngularSite = $Config.AngularSiteName    # dev-app
$ApiLive     = $Config.ApiPath            # C:\Websites\dev-api
$AngularLive = $Config.AngularPath        # C:\Websites\dev-app
$BasePath    = $Config.BasePath           # C:\Websites
$BackupsPath = $Config.BackupsPath
$AspNetEnv   = $Config.AspNetEnv          # Staging
$ApiHost     = $Config.ApiHostname
$AngularHost = $Config.AngularHostname

$PublishRoot = Join-Path $PSScriptRoot "..\publish"
$ApiPublish  = Join-Path $PublishRoot "api"
$AngPublish  = Join-Path $PublishRoot "angular"

$ApiEx = Get-TsicExclusions -Site $ApiSite
$AngEx = Get-TsicExclusions -Site $AngularSite

$StartedAt = Get-Date
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$SeqUrl    = $null
$ApiBackup = ''
$AngBackup = ''

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  TSIC Build and Deploy - LOCAL IIS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ── Transcript ───────────────────────────────────────────────────────
# A deploy with no record is not worth having. If the transcript cannot start,
# refuse to deploy.
$logDir  = Join-Path $PublishRoot "build-logs"
if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$logPath = Join-Path $logDir ("deploy-local-$Timestamp.log")
try {
    Start-Transcript -Path $logPath | Out-Null
} catch {
    Write-Host "  ERROR: could not start transcript at $logPath" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  REFUSING to deploy without a record. Nothing was touched." -ForegroundColor Red
    exit 1
}
Write-Host "  Log: $logPath" -ForegroundColor DarkGray
Write-Host ""

# ---------------------------------------------------------------------------
# Any failure lands here: red banner naming the step, a Seq event, the rollback
# command already typed out, exit 1. The success banner at the bottom is only
# reached when nothing calls this.
# ---------------------------------------------------------------------------
function Stop-DeployWithFailure {
    param(
        [Parameter(Mandatory)] [string] $Step,
        [string] $Detail = '',
        [switch] $LiveUntouched
    )

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  DEPLOY FAILED - $Step" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    if ($Detail) { Write-Host "  $Detail" -ForegroundColor Red }
    Write-Host ""
    if ($LiveUntouched) {
        Write-Host "  Live folders were NOT modified. The box is still serving the previous build." -ForegroundColor Yellow
    } else {
        Write-Host "  THE SITE MAY BE DOWN. Live folders were modified." -ForegroundColor Red
        Write-Host "  Roll back with:" -ForegroundColor Yellow
        if ($ApiBackup -or $AngBackup) {
            Write-Host "    $PSScriptRoot\Rollback-Local.ps1 -Timestamp $Timestamp" -ForegroundColor White
        } else {
            Write-Host "    $PSScriptRoot\Rollback-Local.ps1" -ForegroundColor White
        }
    }
    Write-Host ""
    Write-Host "  Transcript: $logPath" -ForegroundColor Yellow
    Write-Host ""

    Send-TsicDeployEvent -SeqUrl $SeqUrl -Site "$ApiSite + $AngularSite" -Outcome Failed `
        -Environment $AspNetEnv -FailedStep $Step -BuildStamp $BuildStamp -GitHash $GitHash `
        -BackupName "$ApiBackup" -LogPath $logPath `
        -DurationSec ([int]((Get-Date) - $StartedAt).TotalSeconds)

    exit 1
}

try {

# ── Step 0: Preflight - prove the targets BEFORE anything is touched ─
Write-Host "Step 0: Preflight..." -ForegroundColor Yellow

# Seq and the build stamp are resolved FIRST, from pure file reads, so that even
# a preflight abort below is reportable and carries the build it was attempting.
$SeqUrl = Get-TsicSeqUrl -AppRoot $ApiLive -AspNetEnv $AspNetEnv
if ($SeqUrl) { Write-Host "  Seq: $SeqUrl" -ForegroundColor DarkGray }
else         { Write-Host "  Seq: not configured - deploy events will not be logged." -ForegroundColor DarkGray }

# One stamp for this deploy: the Angular footer and deploy-manifest.json must
# agree, so it is computed ONCE here and passed down to the Angular build.
try { $GitHash = (git rev-parse --short HEAD 2>$null) } catch { $GitHash = "unknown" }
if (-not $GitHash) { $GitHash = "unknown" }
$BuildStamp = "v$(Get-Date -Format 'yyMMdd.HHmm').$GitHash"
Write-Host "  Build: $BuildStamp" -ForegroundColor DarkGray

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site "$ApiSite + $AngularSite" -Outcome Started `
    -Environment $AspNetEnv -BuildStamp $BuildStamp -GitHash $GitHash -LogPath $logPath

Import-Module WebAdministration -ErrorAction SilentlyContinue
if (-not (Get-Module WebAdministration)) {
    # The old script printed a yellow warning here and carried on to CLEAR the
    # live folders while the sites were still serving. Never again.
    Stop-DeployWithFailure -Step "Preflight (WebAdministration unavailable)" -LiveUntouched `
        -Detail "Cannot manage IIS, so the pools cannot be stopped. Deploying now would rewrite live folders under a running worker."
}

try {
    Assert-TsicSafeTarget -Site $ApiSite     -Path $ApiLive     -BasePath $BasePath | Out-Null
    Assert-TsicSafeTarget -Site $AngularSite -Path $AngularLive -BasePath $BasePath | Out-Null
} catch {
    Stop-DeployWithFailure -Step "Preflight (unsafe target)" -Detail $_.Exception.Message -LiveUntouched
}
Write-Host "  Targets verified: $ApiLive, $AngularLive" -ForegroundColor Green

if (-not (Test-TsicBackupSpace -Source $ApiLive     -BackupsPath $BackupsPath)) {
    Stop-DeployWithFailure -Step "Preflight (disk space)" -LiveUntouched
}
if (-not (Test-TsicBackupSpace -Source $AngularLive -BackupsPath $BackupsPath)) {
    Stop-DeployWithFailure -Step "Preflight (disk space)" -LiveUntouched
}
Write-Host ""

# ── Step 1: Build .NET API ──────────────────────────────────────────
Write-Host "Step 1: Building .NET API..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot "1a-Build-DotNet-API.ps1")
if ($LASTEXITCODE -ne 0) {
    Stop-DeployWithFailure -Step "Step 1 (API build)" -LiveUntouched
}

# Apply the canonical web.config to the PUBLISH folder, not the live folder, so
# what we validate and mirror is exactly what will be served. (Env-agnostic:
# ASPNETCORE_ENVIRONMENT lives on the dev-api app pool, set by
# IIS-Config-Dev/Setup/07-Apply-Secrets.ps1.)
$apiConfigSrc = Join-Path $PSScriptRoot "web.config.api"
if (-not (Test-Path $apiConfigSrc)) {
    Stop-DeployWithFailure -Step "Step 1 (web.config.api missing)" -Detail "Not found: $apiConfigSrc" -LiveUntouched
}
Copy-Item $apiConfigSrc (Join-Path $ApiPublish "web.config") -Force
Write-Host "  API build complete." -ForegroundColor Green
Write-Host ""

# ── Step 2: Build Angular (staging configuration) ───────────────────
Write-Host "Step 2: Building Angular (configuration=staging)..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot "1b-Build-Angular.ps1") -Configuration staging -BuildStamp $BuildStamp
if ($LASTEXITCODE -ne 0) {
    Stop-DeployWithFailure -Step "Step 2 (Angular build)" -LiveUntouched
}
Write-Host "  Angular build complete." -ForegroundColor Green
Write-Host ""

# ── Step 3: Stamp and validate the payloads ─────────────────────────
# "Get-ChildItem returned something" is not "this is a deployable build". The
# manifest pins the file count, so a half-written publish folder is caught HERE
# - before the pools stop and before /MIR deletes from live everything the
# partial payload happens to lack.
Write-Host "Step 3: Stamping and validating payloads..." -ForegroundColor Yellow

New-TsicManifest -Path $ApiPublish -Site $ApiSite     -Environment $AspNetEnv `
    -GitHash $GitHash -BuildStamp $BuildStamp | Out-Null
New-TsicManifest -Path $AngPublish -Site $AngularSite -Environment $AspNetEnv `
    -GitHash $GitHash -BuildStamp $BuildStamp | Out-Null

$bad = Test-TsicPayload -Path $ApiPublish -Site $ApiSite
if ($bad) { Stop-DeployWithFailure -Step "Step 3 (API payload)" -Detail $bad -LiveUntouched }

$bad = Test-TsicPayload -Path $AngPublish -Site $AngularSite
if ($bad) { Stop-DeployWithFailure -Step "Step 3 (Angular payload)" -Detail $bad -LiveUntouched }

Write-Host "  Payloads valid ($BuildStamp)." -ForegroundColor Green
Write-Host ""

# ── Step 4: Stop app pools ──────────────────────────────────────────
# Stop-TsicPool polls until the pool actually reports 'Stopped'. The old script
# slept 2 seconds and hoped; a pool still running holds file locks on
# TSIC.API.dll, and mirroring into a locked folder yields a half-swapped site.
Write-Host "Step 4: Stopping app pools..." -ForegroundColor Yellow
$stopped = @()
foreach ($pool in @($ApiSite, $AngularSite)) {
    if (-not (Stop-TsicPool -Pool $pool)) {
        foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
        Stop-DeployWithFailure -Step "Step 4 (pool '$pool' would not stop)" -LiveUntouched
    }
    $stopped += $pool
}
Write-Host ""

# ── Step 5: Back up the live folders ────────────────────────────────
# The pools are stopped and Step 6 has not run: this is the last moment the
# pre-deploy state exists on disk. A failed backup ABORTS - a mirrored deploy
# with nothing to roll back to is strictly worse than not deploying, and live is
# still intact here, so aborting costs nothing.
Write-Host "Step 5: Backing up live folders..." -ForegroundColor Yellow

$ApiBackup = New-TsicBackup -Source $ApiLive -BackupsPath $BackupsPath -Site $ApiSite -Timestamp $Timestamp
if ($null -eq $ApiBackup) {
    foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
    Stop-DeployWithFailure -Step "Step 5 (API backup failed)" -LiveUntouched
}

$AngBackup = New-TsicBackup -Source $AngularLive -BackupsPath $BackupsPath -Site $AngularSite -Timestamp $Timestamp
if ($null -eq $AngBackup) {
    foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
    Stop-DeployWithFailure -Step "Step 5 (Angular backup failed)" -LiveUntouched
}
Write-Host ""

# ── Step 6: Mirror publish -> live ───────────────────────────────────
# Same mechanism as prod: robocopy /MIR with the ONE shared exclusion list.
# App_Data and FirebaseAuth_*.json are excluded, so they are neither copied nor
# purged - they simply persist, pool-owned, exactly as the app left them.
Write-Host "Step 6: Deploying files..." -ForegroundColor Yellow

foreach ($m in @(
    @{ Name = $ApiSite;     Src = $ApiPublish; Dst = $ApiLive;     Ex = $ApiEx },
    @{ Name = $AngularSite; Src = $AngPublish; Dst = $AngularLive; Ex = $AngEx }
)) {
    $p = Get-TsicRoboPreview -Source $m.Src -Dest $m.Dst -ExcludeDirs $m.Ex.Dirs -ExcludeFiles $m.Ex.Files
    Write-Host "  $($m.Name): $($p.Copy) to copy, $($p.Delete) to remove" -ForegroundColor White

    $exit = Invoke-TsicRobocopy -Source $m.Src -Dest $m.Dst `
                -ExcludeDirs $m.Ex.Dirs -ExcludeFiles $m.Ex.Files -Quiet
    if ($exit -ge 8) {
        Stop-DeployWithFailure -Step "Step 6 ($($m.Name) sync failed, robocopy exit $exit)" `
            -Detail "$($m.Dst) may be PARTIALLY updated."
    }
    Write-Host "  Deployed: $($m.Name)" -ForegroundColor Green
}
Write-Host ""

# ── Step 7: Start app pools and sites ───────────────────────────────
Write-Host "Step 7: Starting IIS..." -ForegroundColor Yellow
foreach ($pool in @($ApiSite, $AngularSite)) {
    if (-not (Start-TsicPool -Pool $pool)) {
        Stop-DeployWithFailure -Step "Step 7 (pool '$pool' would not start)"
    }
}
foreach ($site in @($ApiSite, $AngularSite)) {
    try {
        Start-Website -Name $site -ErrorAction Stop
        Write-Host "  Started site: $site" -ForegroundColor Green
    } catch {
        if ((Get-Website -Name $site).State -ne 'Started') {
            Stop-DeployWithFailure -Step "Step 7 (site '$site' would not start)" -Detail $_.Exception.Message
        }
    }
}
Write-Host ""

# ── Step 8: Ensure the app pool still has a DB login ────────────────
# After a database restore the IIS app pool login is orphaned. Idempotent.
# A failure here is not fatal on its own - Step 9 is the oracle: a broken login
# surfaces as a non-200 from the API and fails the deploy there.
Write-Host "Step 8: Verifying IIS app pool DB login..." -ForegroundColor Yellow
$fixLoginSql = Join-Path $PSScriptRoot "00-postdev-db-restore-apppooluser.sql"
if (Test-Path $fixLoginSql) {
    try {
        sqlcmd -S $Config.SqlInstance -E -i $fixLoginSql -b
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  DB login verified for IIS APPPOOL\$ApiSite" -ForegroundColor Green
        } else {
            Write-Host "  sqlcmd exit $LASTEXITCODE - see SQL output above. Step 9 will catch a real breakage." -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  Could not run 00-postdev-db-restore-apppooluser.sql: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  00-postdev-db-restore-apppooluser.sql not found - skipped." -ForegroundColor Yellow
}
Write-Host ""

# ── Step 9: Verify the sites are actually up ────────────────────────
# A 200 here proves the NEW worker booted (Program.cs throws without
# ASPNETCORE_ENVIRONMENT) and reached the database. This is the success oracle -
# the banner below is not printed unless this passes.
Write-Host "Step 9: Verifying sites are serving..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

if (-not (Test-TsicEndpoint -Url "https://$ApiHost/api/jobs/tsic")) {
    Stop-DeployWithFailure -Step "Step 9 (API is not serving)" `
        -Detail "The API did not return a success response after the deploy."
}
if (-not (Test-TsicEndpoint -Url "https://$AngularHost/" -Retries 3)) {
    Stop-DeployWithFailure -Step "Step 9 (Angular is not serving)" `
        -Detail "The frontend did not return a success response after the deploy."
}
Write-Host ""

# ── Done - earned, not printed ──────────────────────────────────────
$elapsed = [int]((Get-Date) - $StartedAt).TotalSeconds

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site "$ApiSite + $AngularSite" -Outcome Completed `
    -Environment $AspNetEnv -BuildStamp $BuildStamp -GitHash $GitHash `
    -BackupName "$ApiBackup" -LogPath $logPath -DurationSec $elapsed

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  DEPLOYED - both sites verified serving." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Build:    $BuildStamp" -ForegroundColor Green
Write-Host "  API:      https://$ApiHost" -ForegroundColor Green
Write-Host "  Angular:  https://$AngularHost" -ForegroundColor Green
Write-Host "  Took:     ${elapsed}s" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Roll back with: .\Rollback-Local.ps1" -ForegroundColor DarkGray
Write-Host ""

} finally {
    try { Stop-Transcript | Out-Null } catch { }
    try { Set-Location -Path $PSScriptRoot } catch { }
}
