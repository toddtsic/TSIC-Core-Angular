# =============================================================================
# Deploy-Dev.ps1 -- build and deploy dev-api / dev-app (dev.teamsportsinfo.com)
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Ops\Deploy-Dev.ps1                # both sites
#   .\scripts\TSICDev-Ops\Deploy-Dev.ps1 -SkipApi       # Angular only (dev-api untouched)
#   .\scripts\TSICDev-Ops\Deploy-Dev.ps1 -SkipAngular   # API only     (dev-app untouched)
#
# Ported from scripts\1-Build-And-Deploy-Local.ps1 (SEDONA). Same steps, same
# guards, and the SAME shared primitives: _deploy-common.ps1, 1a-Build-DotNet-API.ps1
# and 1b-Build-Angular.ps1 stay in scripts\ because the PRODUCTION deploy and
# rollback on PHOENIX use them too -- so a dev deploy still rehearses exactly what
# runs on prod. Differences from the original:
#   * Settings come from TSICDev-Config.ps1, not IIS-Config-Dev\_config.ps1.
#   * Refuses to build anything but a clean, current master (see Step 0).
#   * Seq URL is read from the source appsettings, so a first deploy into an
#     empty site folder still logs its deploy events.
#   * No database step. Restore-Databases.ps1 owns the dev-api database user.
#   * No Set-StrictMode: the shared prod helpers were not written for it, and
#     strict mode would leak into them and change their behaviour.
#
# Step 9 checks https://dev / devapi. This box's hosts file maps both names to
# 127.0.0.1 until the DNS change, so the check hits THIS box, not SEDONA.
#
# Rollback:  .\scripts\TSICDev-Ops\Rollback-Dev.ps1  [-SkipApi|-SkipAngular]
# =============================================================================

#Requires -RunAsAdministrator

# Site SWITCHES, never paths: a deploy target is a site name that resolves to
# exactly one directory (Assert-TsicSafeTarget).
param(
    [switch]$SkipApi,
    [switch]$SkipAngular
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TSICDev-Config.ps1')

# Box guard first: nothing is built or touched on the wrong machine.
try { Assert-TSICDevBox } catch {
    Write-Host ""
    Write-Host "REFUSING to run: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Nothing was built or touched." -ForegroundColor Yellow
    exit 1
}

$ScriptsDir = Join-Path $TSICDev.RepoRoot 'scripts'
. (Join-Path $ScriptsDir '_deploy-common.ps1')

$ApiSite     = $TSICDev.ApiPool
$AngularSite = $TSICDev.AppPool
$ApiLive     = $TSICDev.ApiPath
$AngularLive = $TSICDev.AppPath
$BasePath    = $TSICDev.WebsitesDir
$BackupsPath = $TSICDev.WebBackupsDir
$AspNetEnv   = $TSICDev.AspNetEnv
$ApiHost     = $TSICDev.ApiHost
$AngularHost = $TSICDev.AppHost

$PublishRoot = $TSICDev.PublishRoot
$ApiPublish  = Join-Path $PublishRoot 'api'
$AngPublish  = Join-Path $PublishRoot 'angular'

$doApi     = -not $SkipApi
$doAngular = -not $SkipAngular

# Everything below iterates this list. A skipped site is simply not in it, so
# nothing can half-skip a site.
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

$LiveTouched = $false       # set at Step 6; decides what an unexpected failure reports
$StartedAt  = Get-Date
$Timestamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$SeqUrl     = $null
$BuildStamp = ''
$GitHash    = 'unknown'
$SiteLabel  = ($targets | ForEach-Object { $_.Site }) -join ' + '

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  TSIC Build and Deploy - $($TSICDev.BoxName)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (-not $targets.Count) {
    Write-Host "  Nothing to deploy (-SkipApi and -SkipAngular both set)." -ForegroundColor Yellow
    exit 0
}
Write-Host "  Deploying: $SiteLabel" -ForegroundColor Yellow
if ($SkipApi)     { Write-Host "  Skipping API     - $ApiSite will not be stopped or modified." -ForegroundColor DarkGray }
if ($SkipAngular) { Write-Host "  Skipping Angular - $AngularSite will not be stopped or modified." -ForegroundColor DarkGray }
Write-Host ""

# --- Transcript: a deploy with no record is refused ---------------------------
$logDir = Join-Path $PublishRoot 'build-logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$logPath = Join-Path $logDir "deploy-dev-$Timestamp.log"
try {
    Start-Transcript -Path $logPath | Out-Null
} catch {
    Write-Host "  ERROR: could not start transcript at $logPath" -ForegroundColor Red
    Write-Host "  REFUSING to deploy without a record. Nothing was touched." -ForegroundColor Red
    exit 1
}
Write-Host "  Log: $logPath" -ForegroundColor DarkGray
Write-Host ""

# Any failure lands here: red banner naming the step, a Seq event, and the
# rollback command typed out for exactly the sites this run touched. exit 1.
function Stop-DeployWithFailure {
    param(
        [Parameter(Mandatory = $true)] [string] $Step,
        [string] $Detail = '',
        [switch] $LiveUntouched
    )

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  DEPLOY FAILED - $Step" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  Failed at:  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  (started $($StartedAt.ToString('HH:mm:ss')))" -ForegroundColor Red
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
        Write-Host ("    " + (Join-Path $PSScriptRoot 'Rollback-Dev.ps1') + " $($rb -join ' ')").TrimEnd() -ForegroundColor White
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

# Exit code is the verdict. Local 'Continue' because PS 5.1 turns any stderr line
# from a native command into a terminating error under 'Stop' when merged with
# 2>&1 -- and git writes progress/notes to stderr on success.
function Invoke-Git {
    $ErrorActionPreference = 'Continue'
    $out = & git -C $TSICDev.RepoRoot @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $(@($out) -join ' ')" }
    @($out | ForEach-Object { "$_" })
}

try {

# --- Step 0: Preflight - prove source and targets BEFORE anything is touched ---
Write-Host "Step 0: Preflight..." -ForegroundColor Yellow

# Seq first, from the source appsettings (what is about to ship), so even a
# preflight abort is reported.
$SeqUrl = Get-TsicSeqUrl -AppRoot $TSICDev.ApiSource -AspNetEnv $AspNetEnv
if ($SeqUrl) { Write-Host "  Seq: $SeqUrl" -ForegroundColor DarkGray }
else         { Write-Host "  Seq: not configured - deploy events will not be logged." -ForegroundColor DarkGray }

# Source: Staging is built from master only, with nothing uncommitted in the
# app source, and not behind origin. Uncommitted edits would otherwise ride
# silently into the build.
try {
    $branch = @(Invoke-Git rev-parse --abbrev-ref HEAD)[0]
    if ($branch -ne $TSICDev.DeployBranch) {
        throw "checkout is on '$branch'. Staging deploys only from '$($TSICDev.DeployBranch)': git checkout $($TSICDev.DeployBranch)"
    }
    # Content changes only. The Angular help-index generator rewrites
    # public/help/*.json with LF endings on every start/build; git stores CRLF,
    # so those files show as modified with no real change. Line endings do not
    # change what ships; content and new files do.
    $dirty = @(Invoke-Git diff --ignore-cr-at-eol --name-only HEAD -- 'TSIC-Core-Angular/src') +
             @(Invoke-Git ls-files --others --exclude-standard -- 'TSIC-Core-Angular/src' | ForEach-Object { "$_ (new, untracked)" })
    $dirty = @($dirty | Where-Object { $_ -and $_ -notmatch '^warning: ' })
    if ($dirty.Count -gt 0) {
        throw "uncommitted changes in the app source would ship in this build:`n    $($dirty -join "`n    ")"
    }
    Invoke-Git fetch --quiet origin $TSICDev.DeployBranch | Out-Null
    $behind = [int]@(Invoke-Git rev-list --count "HEAD..origin/$($TSICDev.DeployBranch)")[0]
    $ahead  = [int]@(Invoke-Git rev-list --count "origin/$($TSICDev.DeployBranch)..HEAD")[0]
    if ($behind -gt 0) {
        throw "local $($TSICDev.DeployBranch) is $behind commit(s) behind origin: git pull"
    }
    if ($ahead -gt 0) {
        Write-Host "  NOTE: $ahead local commit(s) not yet pushed to origin will be deployed." -ForegroundColor Yellow
    }
    $GitHash = @(Invoke-Git rev-parse --short HEAD)[0]
} catch {
    Stop-DeployWithFailure -Step 'Preflight (source checkout)' -Detail $_.Exception.Message -LiveUntouched
}
Write-Host "  Source: $($TSICDev.DeployBranch) @ $GitHash, clean" -ForegroundColor Green

# One stamp for this deploy: the Angular footer and deploy-manifest.json must agree.
$BuildStamp = "v$(Get-Date -Format 'yyMMdd.HHmm').$GitHash"
Write-Host "  Build: $BuildStamp" -ForegroundColor DarkGray

Send-TsicDeployEvent -SeqUrl $SeqUrl -Site $SiteLabel -Outcome Started `
    -Environment $AspNetEnv -BuildStamp $BuildStamp -GitHash $GitHash -LogPath $logPath

Import-Module WebAdministration -ErrorAction SilentlyContinue
if (-not (Get-Module WebAdministration)) {
    Stop-DeployWithFailure -Step 'Preflight (WebAdministration unavailable)' -LiveUntouched `
        -Detail 'Cannot manage IIS, so the pools cannot be stopped.'
}

foreach ($t in $targets) {
    try {
        Assert-TsicSafeTarget -Site $t.Site -Path $t.Live -BasePath $BasePath | Out-Null
    } catch {
        Stop-DeployWithFailure -Step 'Preflight (unsafe target)' -Detail $_.Exception.Message -LiveUntouched
    }
    Write-Host "  Target verified: $($t.Live)" -ForegroundColor Green
    if (-not (Test-TsicBackupSpace -Source $t.Live -BackupsPath $BackupsPath)) {
        Stop-DeployWithFailure -Step 'Preflight (disk space)' -LiveUntouched
    }
}
Write-Host ""

# --- Step 1: Build .NET API ---------------------------------------------------
if ($doApi) {
    Write-Host "Step 1: Building .NET API..." -ForegroundColor Yellow
    # win-x64 keeps SkiaSharp's linux/osx natives out. Framework-dependent.
    & (Join-Path $ScriptsDir '1a-Build-DotNet-API.ps1') -Runtime win-x64
    if ($LASTEXITCODE -ne 0) { Stop-DeployWithFailure -Step 'Step 1 (API build)' -LiveUntouched }

    # The canonical web.config goes into the PUBLISH folder, so what is validated
    # and mirrored is exactly what will be served. ASPNETCORE_ENVIRONMENT is on
    # the pool (Apply-Dev-Secrets.ps1), not in web.config.
    $apiConfigSrc = Join-Path $ScriptsDir 'web.config.api'
    if (-not (Test-Path $apiConfigSrc)) {
        Stop-DeployWithFailure -Step 'Step 1 (web.config.api missing)' -Detail "Not found: $apiConfigSrc" -LiveUntouched
    }
    Copy-Item $apiConfigSrc (Join-Path $ApiPublish 'web.config') -Force
    Write-Host "  API build complete." -ForegroundColor Green
} else {
    Write-Host "Step 1: Skipped (API)" -ForegroundColor DarkGray
}
Write-Host ""

# --- Step 2: Build Angular (staging configuration) ------------------------------
if ($doAngular) {
    Write-Host "Step 2: Building Angular (configuration=staging)..." -ForegroundColor Yellow
    & (Join-Path $ScriptsDir '1b-Build-Angular.ps1') -Configuration staging -BuildStamp $BuildStamp
    if ($LASTEXITCODE -ne 0) { Stop-DeployWithFailure -Step 'Step 2 (Angular build)' -LiveUntouched }
    Write-Host "  Angular build complete." -ForegroundColor Green
} else {
    Write-Host "Step 2: Skipped (Angular)" -ForegroundColor DarkGray
}
Write-Host ""

# --- Step 3: Stamp and validate the payloads ------------------------------------
# The manifest pins the file count, so a half-written publish folder is caught
# here - before the pools stop and before /MIR deletes what the payload lacks.
Write-Host "Step 3: Stamping and validating payloads..." -ForegroundColor Yellow
foreach ($t in $targets) {
    New-TsicManifest -Path $t.Publish -Site $t.Site -Environment $AspNetEnv `
        -GitHash $GitHash -BuildStamp $BuildStamp | Out-Null
    $bad = Test-TsicPayload -Path $t.Publish -Site $t.Site
    if ($bad) { Stop-DeployWithFailure -Step "Step 3 ($($t.Site) payload)" -Detail $bad -LiveUntouched }
}
Write-Host "  Payloads valid ($BuildStamp)." -ForegroundColor Green
Write-Host ""

# --- Step 4: Stop app pools (waits until each reports Stopped) ----------------
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

# --- Step 5: Back up the live folders --------------------------------------------
# Last moment the pre-deploy state exists on disk. A failed backup ABORTS.
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

# --- Step 6: Mirror publish -> live ------------------------------------------------
# robocopy /MIR with the ONE shared exclusion list: App_Data, logs and keys are
# neither copied nor purged - they persist, pool-owned.
Write-Host "Step 6: Deploying files..." -ForegroundColor Yellow
$LiveTouched = $true
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

# --- Step 7: Start app pools and sites --------------------------------------------
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

# --- Step 8: Verify the sites are actually up -------------------------------------
# A 200 from the API proves the new worker booted (Program.cs throws without
# ASPNETCORE_ENVIRONMENT or the JWT key) and reached the database. The success
# banner is not printed unless every site in scope passes.
Write-Host "Step 8: Verifying sites are serving..." -ForegroundColor Yellow
Start-Sleep -Seconds 3
foreach ($t in $targets) {
    if (-not (Test-TsicEndpoint -Url $t.Url -Retries $t.Retries)) {
        Stop-DeployWithFailure -Step "Step 8 ($($t.Site) did not answer)" `
            -Detail "Files deployed and pools started, but $($t.Url) did not answer. Check the API log ($ApiLive\logs) and Seq before rolling back."
    }
}
Write-Host ""

# --- Done - earned, not printed ---------------------------------------------------
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

# What is live on BOTH sites, read back from disk. After a -Skip run these can
# legitimately differ - worth seeing, not worth failing.
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
Write-Host "  Finished: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  (took ${elapsed}s)" -ForegroundColor Green
Write-Host "  Roll back with: $(Join-Path $PSScriptRoot 'Rollback-Dev.ps1')" -ForegroundColor DarkGray
Write-Host ""

# Explicit: robocopy exits 1/2/3 on SUCCESS, and a script that runs off the end
# inherits the last native exit code.
exit 0

} catch {
    # Anything unplanned still gets the banner, the transcript path and (if live
    # was being written) the rollback command -- never a bare red error.
    if ($LiveTouched) {
        Stop-DeployWithFailure -Step 'Unexpected error' -Detail $_.Exception.Message
    } else {
        Stop-DeployWithFailure -Step 'Unexpected error' -Detail $_.Exception.Message -LiveUntouched
    }
} finally {
    try { Stop-Transcript | Out-Null } catch { }
}
