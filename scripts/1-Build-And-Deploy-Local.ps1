# ============================================================================
# 1-Build-And-Deploy-Local.ps1 - Build and deploy to local IIS (dev-api/dev-app)
# ============================================================================
# Builds, mirrors into C:\Websites, restarts the pools, and verifies the sites
# actually answer before saying so.
#
#   .\1-Build-And-Deploy-Local.ps1               # both
#   .\1-Build-And-Deploy-Local.ps1 -SkipApi      # Angular only  (dev-api untouched)
#   .\1-Build-And-Deploy-Local.ps1 -SkipAngular  # API only      (dev-app untouched)
#
# A skipped site is not built, not stopped, not backed up, not mirrored and not
# restarted - so a frontend-only change never bounces the API, and vice versa.
# This mirrors production, where each staging folder's Go.ps1 already deploys
# exactly one site (API Go.ps1 -> Recycle -SkipAngular, and the converse).
#
# This is the REHEARSAL for the production deploy. Every guard here exists in
# IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1, and both call the same
# shared primitives in _deploy-common.ps1 - so what you exercise here is what
# runs on PHOENIX, including the skip paths.
#
# NOTE: dev-api / dev-app serve client-facing dev.teamsportsinfo.com.
#
# Rollback:  .\Rollback-Local.ps1  [-SkipApi|-SkipAngular]
# ============================================================================

#Requires -RunAsAdministrator

# Site SWITCHES, not paths. A deploy target is a site NAME that resolves to
# exactly one literal directory (see Assert-TsicSafeTarget) - never a free-form
# path an operator can point somewhere else.
param(
    [switch]$SkipApi,
    [switch]$SkipAngular
)

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

$doApi     = -not $SkipApi
$doAngular = -not $SkipAngular

# Everything below iterates this list. A skipped site simply is not in it, so
# there is no branch anywhere that can half-skip a site.
$targets = @()
if ($doApi) {
    $targets += [pscustomobject]@{
        Site = $ApiSite; Live = $ApiLive; Publish = $ApiPublish
        Ex   = (Get-TsicExclusions -Site $ApiSite)
        Url  = "https://$ApiHost/api/jobs/tsic"; Retries = 6
        Backup = ''
    }
}
if ($doAngular) {
    $targets += [pscustomobject]@{
        Site = $AngularSite; Live = $AngularLive; Publish = $AngPublish
        Ex   = (Get-TsicExclusions -Site $AngularSite)
        Url  = "https://$AngularHost/"; Retries = 3
        Backup = ''
    }
}

$StartedAt = Get-Date
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$SeqUrl    = $null
$SiteLabel = ($targets | ForEach-Object { $_.Site }) -join ' + '

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  TSIC Build and Deploy - LOCAL IIS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (-not $targets.Count) {
    Write-Host "  Nothing to deploy (-SkipApi and -SkipAngular both set)." -ForegroundColor Yellow
    Write-Host ""
    exit 0
}
Write-Host "  Deploying: $SiteLabel" -ForegroundColor Yellow
if ($SkipApi)     { Write-Host "  Skipping API     - dev-api will not be stopped or modified." -ForegroundColor DarkGray }
if ($SkipAngular) { Write-Host "  Skipping Angular - dev-app will not be stopped or modified." -ForegroundColor DarkGray }
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
# command already typed out - scoped to the sites this run actually touched, so
# an Angular-only deploy never suggests rolling the API back. exit 1. The
# success banner at the bottom is only reached when nothing calls this.
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

    $taken = @($targets | Where-Object { $_.Backup })

    if ($LiveUntouched) {
        Write-Host "  Live folders were NOT modified. The box is still serving the previous build." -ForegroundColor Yellow
    } else {
        Write-Host "  THE SITE MAY BE DOWN. Live folders were modified: $SiteLabel" -ForegroundColor Red
        Write-Host "  Roll back with:" -ForegroundColor Yellow

        $rb = @()
        if ($SkipApi)     { $rb += '-SkipApi' }
        if ($SkipAngular) { $rb += '-SkipAngular' }
        if ($taken.Count) { $rb += "-Timestamp $Timestamp" }
        Write-Host "    $PSScriptRoot\Rollback-Local.ps1 $($rb -join ' ')".TrimEnd() -ForegroundColor White
    }
    Write-Host ""
    Write-Host "  Transcript: $logPath" -ForegroundColor Yellow
    Write-Host ""

    Send-TsicDeployEvent -SeqUrl $SeqUrl -Site $SiteLabel -Outcome Failed `
        -Environment $AspNetEnv -FailedStep $Step -BuildStamp $BuildStamp -GitHash $GitHash `
        -BackupName (($taken | ForEach-Object { $_.Backup }) -join ', ') -LogPath $logPath `
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

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site $SiteLabel -Outcome Started `
    -Environment $AspNetEnv -BuildStamp $BuildStamp -GitHash $GitHash -LogPath $logPath

Import-Module WebAdministration -ErrorAction SilentlyContinue
if (-not (Get-Module WebAdministration)) {
    # The old script printed a yellow warning here and carried on to CLEAR the
    # live folders while the sites were still serving. Never again.
    Stop-DeployWithFailure -Step "Preflight (WebAdministration unavailable)" -LiveUntouched `
        -Detail "Cannot manage IIS, so the pools cannot be stopped. Deploying now would rewrite live folders under a running worker."
}

foreach ($t in $targets) {
    try {
        Assert-TsicSafeTarget -Site $t.Site -Path $t.Live -BasePath $BasePath | Out-Null
    } catch {
        Stop-DeployWithFailure -Step "Preflight (unsafe target)" -Detail $_.Exception.Message -LiveUntouched
    }
    Write-Host "  Target verified: $($t.Live)" -ForegroundColor Green

    if (-not (Test-TsicBackupSpace -Source $t.Live -BackupsPath $BackupsPath)) {
        Stop-DeployWithFailure -Step "Preflight (disk space)" -LiveUntouched
    }
}
Write-Host ""

# ── Step 1: Build .NET API ──────────────────────────────────────────
if ($doApi) {
    Write-Host "Step 1: Building .NET API..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "1a-Build-DotNet-API.ps1")
    if ($LASTEXITCODE -ne 0) {
        Stop-DeployWithFailure -Step "Step 1 (API build)" -LiveUntouched
    }

    # Apply the canonical web.config to the PUBLISH folder, not the live folder,
    # so what we validate and mirror is exactly what will be served. (Env-agnostic:
    # ASPNETCORE_ENVIRONMENT lives on the dev-api app pool, set by
    # IIS-Config-Dev/Setup/07-Apply-Secrets.ps1.)
    $apiConfigSrc = Join-Path $PSScriptRoot "web.config.api"
    if (-not (Test-Path $apiConfigSrc)) {
        Stop-DeployWithFailure -Step "Step 1 (web.config.api missing)" -Detail "Not found: $apiConfigSrc" -LiveUntouched
    }
    Copy-Item $apiConfigSrc (Join-Path $ApiPublish "web.config") -Force
    Write-Host "  API build complete." -ForegroundColor Green
} else {
    Write-Host "Step 1: Skipped (API)" -ForegroundColor DarkGray
}
Write-Host ""

# ── Step 2: Build Angular (staging configuration) ───────────────────
if ($doAngular) {
    Write-Host "Step 2: Building Angular (configuration=staging)..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "1b-Build-Angular.ps1") -Configuration staging -BuildStamp $BuildStamp
    if ($LASTEXITCODE -ne 0) {
        Stop-DeployWithFailure -Step "Step 2 (Angular build)" -LiveUntouched
    }
    Write-Host "  Angular build complete." -ForegroundColor Green
} else {
    Write-Host "Step 2: Skipped (Angular)" -ForegroundColor DarkGray
}
Write-Host ""

# ── Step 3: Stamp and validate the payloads ─────────────────────────
# "Get-ChildItem returned something" is not "this is a deployable build". The
# manifest pins the file count, so a half-written publish folder is caught HERE
# - before the pools stop and before /MIR deletes from live everything the
# partial payload happens to lack.
Write-Host "Step 3: Stamping and validating payloads..." -ForegroundColor Yellow
foreach ($t in $targets) {
    New-TsicManifest -Path $t.Publish -Site $t.Site -Environment $AspNetEnv `
        -GitHash $GitHash -BuildStamp $BuildStamp | Out-Null

    $bad = Test-TsicPayload -Path $t.Publish -Site $t.Site
    if ($bad) {
        Stop-DeployWithFailure -Step "Step 3 ($($t.Site) payload)" -Detail $bad -LiveUntouched
    }
}
Write-Host "  Payloads valid ($BuildStamp)." -ForegroundColor Green
Write-Host ""

# ── Step 4: Stop app pools ──────────────────────────────────────────
# Only the pools in scope. Stop-TsicPool polls until the pool actually reports
# 'Stopped'; the old script slept 2 seconds and hoped, and a pool still running
# holds file locks on TSIC.API.dll - mirroring into a locked folder yields a
# half-swapped site.
Write-Host "Step 4: Stopping app pools..." -ForegroundColor Yellow
$stopped = @()
foreach ($t in $targets) {
    if (-not (Stop-TsicPool -Pool $t.Site)) {
        foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
        Stop-DeployWithFailure -Step "Step 4 (pool '$($t.Site)' would not stop)" -LiveUntouched
    }
    $stopped += $t.Site
}
Write-Host ""

# ── Step 5: Back up the live folders ────────────────────────────────
# The pools are stopped and Step 6 has not run: this is the last moment the
# pre-deploy state exists on disk. A failed backup ABORTS - a mirrored deploy
# with nothing to roll back to is strictly worse than not deploying, and live is
# still intact here, so aborting costs nothing.
Write-Host "Step 5: Backing up live folders..." -ForegroundColor Yellow
foreach ($t in $targets) {
    $b = New-TsicBackup -Source $t.Live -BackupsPath $BackupsPath -Site $t.Site -Timestamp $Timestamp
    if ($null -eq $b) {
        foreach ($s in $stopped) { Start-TsicPool -Pool $s | Out-Null }
        Stop-DeployWithFailure -Step "Step 5 ($($t.Site) backup failed)" -LiveUntouched
    }
    $t.Backup = $b
}
Write-Host ""

# ── Step 6: Mirror publish -> live ───────────────────────────────────
# Same mechanism as prod: robocopy /MIR with the ONE shared exclusion list.
# App_Data, logs and keys are excluded, so they are neither copied nor purged -
# they simply persist, pool-owned, exactly as the app left them. Everything else
# in the build output ships, FirebaseAuth_*.json included: it is a build
# artifact, not runtime state (see _deploy-common.ps1).
Write-Host "Step 6: Deploying files..." -ForegroundColor Yellow
foreach ($t in $targets) {
    $p = Get-TsicRoboPreview -Source $t.Publish -Dest $t.Live -ExcludeDirs $t.Ex.Dirs -ExcludeFiles $t.Ex.Files
    Write-Host "  $($t.Site): $($p.Copy) to copy, $($p.Delete) to remove" -ForegroundColor White

    $exit = Invoke-TsicRobocopy -Source $t.Publish -Dest $t.Live `
                -ExcludeDirs $t.Ex.Dirs -ExcludeFiles $t.Ex.Files -Quiet
    if ($exit -ge 8) {
        Stop-DeployWithFailure -Step "Step 6 ($($t.Site) sync failed, robocopy exit $exit)" `
            -Detail "$($t.Live) may be PARTIALLY updated."
    }
    Write-Host "  Deployed: $($t.Site)" -ForegroundColor Green
}
Write-Host ""

# ── Step 7: Start app pools and sites ───────────────────────────────
Write-Host "Step 7: Starting IIS..." -ForegroundColor Yellow
foreach ($t in $targets) {
    if (-not (Start-TsicPool -Pool $t.Site)) {
        Stop-DeployWithFailure -Step "Step 7 (pool '$($t.Site)' would not start)"
    }
}
foreach ($t in $targets) {
    try {
        Start-Website -Name $t.Site -ErrorAction Stop
        Write-Host "  Started site: $($t.Site)" -ForegroundColor Green
    } catch {
        if ((Get-Website -Name $t.Site).State -ne 'Started') {
            Stop-DeployWithFailure -Step "Step 7 (site '$($t.Site)' would not start)" -Detail $_.Exception.Message
        }
    }
}
Write-Host ""

# ── Step 8: Ensure the app pool still has a DB login ────────────────
# API only - dev-app does not talk to SQL. After a database restore the IIS app
# pool login is orphaned. Idempotent. A failure here is not fatal on its own:
# Step 9 is the oracle, and a broken login surfaces as a non-200 from the API.
if ($doApi) {
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
} else {
    Write-Host "Step 8: Skipped (API not deployed)" -ForegroundColor DarkGray
}
Write-Host ""

# ── Step 9: Verify the sites are actually up ────────────────────────
# A 200 from the API proves the NEW worker booted (Program.cs throws without
# ASPNETCORE_ENVIRONMENT) and reached the database. This is the success oracle -
# the banner below is not printed unless every site in scope passes.
Write-Host "Step 9: Verifying sites are serving..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

# A failure HERE is not a failure of the deploy itself: the files synced and the
# pools started, and it is the HTTP check that came back unhappy. Usually that
# means a genuinely broken site - but it can also be the check (this box
# resolving its own public hostname). Say what is known; don't stampede a
# rollback of a healthy site.
$verifyNote = @(
    "The deploy COMPLETED and the pools started - this is the verification step."
    "Check by hand before rolling back: open the URL in a browser."
    "  - Site genuinely down -> roll back with the command below."
    "  - Site loads fine     -> the deploy is good and this check is wrong."
) -join "`n  "

foreach ($t in $targets) {
    if (-not (Test-TsicEndpoint -Url $t.Url -Retries $t.Retries)) {
        Stop-DeployWithFailure -Step "Step 9 ($($t.Site) did not answer)" -Detail $verifyNote
    }
}
Write-Host ""

# ── Done - earned, not printed ──────────────────────────────────────
$elapsed = [int]((Get-Date) - $StartedAt).TotalSeconds

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site $SiteLabel -Outcome Completed `
    -Environment $AspNetEnv -BuildStamp $BuildStamp -GitHash $GitHash `
    -BackupName (($targets | ForEach-Object { $_.Backup }) -join ', ') `
    -LogPath $logPath -DurationSec $elapsed

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  DEPLOYED - verified serving: $SiteLabel" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Build:    $BuildStamp" -ForegroundColor Green
foreach ($t in $targets) { Write-Host "  $($t.Site):  $($t.Url)" -ForegroundColor Green }

# What is live on BOTH sites, read back from disk. After a -SkipApi/-SkipAngular
# run these can legitimately differ - worth seeing, not worth failing.
$liveApi = Get-TsicManifest -Path $ApiLive
$liveAng = Get-TsicManifest -Path $AngularLive
if ($liveApi -and $liveAng -and $liveApi.buildStamp -ne $liveAng.buildStamp) {
    Write-Host ""
    Write-Host "  NOTE: API and Angular are on DIFFERENT builds." -ForegroundColor Yellow
    Write-Host "    $ApiSite      $($liveApi.buildStamp)" -ForegroundColor Yellow
    Write-Host "    $AngularSite      $($liveAng.buildStamp)" -ForegroundColor Yellow
    Write-Host "  Expected after a -SkipApi / -SkipAngular deploy. Otherwise, investigate." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  Took:     ${elapsed}s" -ForegroundColor DarkGray
Write-Host "  Roll back with: .\Rollback-Local.ps1" -ForegroundColor DarkGray
Write-Host ""

# Explicit. robocopy exits 1/2/3 on SUCCESS (copied / extras deleted / both), and
# a script that runs off the end inherits the last native command's exit code --
# so without this, a clean deploy could report failure to anything checking it.
exit 0

} finally {
    try { Stop-Transcript | Out-Null } catch { }
    try { Set-Location -Path $PSScriptRoot } catch { }
}
