# ============================================================================
# 1-Build-And-Deploy-Prod.ps1 - Build on Sedona, stage to TSIC-PHOENIX
# ============================================================================
# Builds .NET API + Angular (with prod URL patching), deploys to STAGING
# folders on \\204.17.37.202\Websites via SMB share, drops a Go.ps1 wrapper
# in each staging folder.
#
# After this script completes, RDP to TSIC-PHOENIX and run Go.ps1 ONCE, from
# either staging folder. Both wrappers are identical and carry the scope of this
# build, so one run swaps everything that was staged: stop pools, back up live,
# mirror staging -> live, restart, verify.
#
# Run it once. Do NOT run it from both folders expecting two halves - that was
# the old flow, and it left production serving a new API under an old frontend
# in between.
#
# Usage:
#   .\1-Build-And-Deploy-Prod.ps1              # Build + stage both
#   .\1-Build-And-Deploy-Prod.ps1 -SkipApi     # Build + stage Angular only
#   .\1-Build-And-Deploy-Prod.ps1 -SkipAngular # Build + stage API only
# ============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$SkipApi,
    [switch]$SkipAngular
)

$ErrorActionPreference = "Stop"

# ── Host guard ───────────────────────────────────────────────────────
# The prod deploy BUILDS on TSIC-SEDONA and stages over SMB to TSIC-PHOENIX.
# The share check below would catch a box that cannot reach PHOENIX, but any
# other dev box with the share mapped could otherwise build and push a prod
# payload. Pin the build host explicitly - fail loud, first, before any build.
$ExpectedHost = 'TSIC-SEDONA'
if ($env:COMPUTERNAME -ne $ExpectedHost) {
    Write-Host ""
    Write-Host "REFUSING to run: the prod build/stage runs only on $ExpectedHost." -ForegroundColor Red
    Write-Host "  Current host: $env:COMPUTERNAME" -ForegroundColor Red
    Write-Host "  Nothing was built or staged." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# ---------------------------------------------------------------------------
# Configuration (from shared _config.ps1)
# ---------------------------------------------------------------------------
. "$PSScriptRoot\IIS-Config-Prod\_config.ps1" -Environment Prod
. "$PSScriptRoot\_deploy-common.ps1"

$ProdServer      = $Config.ProdServer
$ApiStaging      = $Config.DeployApiStagingPath
$AngularStaging  = $Config.DeployAngularStagingPath
$ApiHostname     = $Config.ApiHostname
$AngularHostname = $Config.AngularHostname
$ApiSite         = $Config.ApiSiteName        # claude-api
$AngularSite     = $Config.AngularSiteName    # claude-app
$AspNetEnv       = $Config.AspNetEnv          # Production

# ONE stamp for this build. It goes into the Angular footer AND into
# deploy-manifest.json, and they must agree -- computing it twice can straddle a
# minute boundary and produce a manifest that quietly disagrees with the app.
try { $GitHash = (git rev-parse --short HEAD 2>$null) } catch { $GitHash = "unknown" }
if (-not $GitHash) { $GitHash = "unknown" }
$BuildStamp = "v$(Get-Date -Format 'yyMMdd.HHmm').$GitHash"

# Angular environment overlay is handled by `fileReplacements` in angular.json
# (`--configuration production` substitutes environment.ts -> environment.production.ts
# at compile time). Backend env overlay is handled by appsettings.Production.json.
# No regex-patching of source or output files anywhere in this script.

$RepoRoot    = (Resolve-Path "$PSScriptRoot\..").Path
$SolutionDir = Join-Path $RepoRoot "TSIC-Core-Angular"
$ProjectPath = Join-Path $SolutionDir "src\backend\TSIC.API\TSIC.API.csproj"
$AngularPath = Join-Path $SolutionDir "src\frontend\tsic-app"
$PublishRoot = Join-Path $RepoRoot "publish"
$ApiPublish  = Join-Path $PublishRoot "api"
$AngPublish  = Join-Path $PublishRoot "angular"
$WebConfigApiSrc = Join-Path $PSScriptRoot "web.config.api"
$WebConfigAngSrc = Join-Path $PSScriptRoot "IIS-Config-Prod\web.config.angular"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TSIC Publish - PRODUCTION STAGING" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API staging:     $ApiStaging" -ForegroundColor Yellow
Write-Host "  Angular staging: $AngularStaging" -ForegroundColor Yellow
Write-Host "  Hostnames:       $ApiHostname / $AngularHostname" -ForegroundColor Yellow
if ($SkipApi)     { Write-Host "  Skipping API build/stage" -ForegroundColor DarkGray }
if ($SkipAngular) { Write-Host "  Skipping Angular build/stage" -ForegroundColor DarkGray }
Write-Host ""

# ── Verify share is accessible ───────────────────────────────────────
if (!(Test-Path "\\$ProdServer\Websites")) {
    Write-Host "  ERROR: Cannot access \\$ProdServer\Websites" -ForegroundColor Red
    Write-Host "  Ensure SMB share is created and credentials are mapped:" -ForegroundColor Red
    Write-Host "    net use \\$ProdServer\Websites /user:TSIC-PHOENIX\Administrator *" -ForegroundColor White
    exit 1
}
Write-Host "  Share accessible: \\$ProdServer\Websites" -ForegroundColor Green
Write-Host ""

# Transcript logging
$transcriptStarted = $false
try {
    $logDir = Join-Path $PublishRoot "build-logs"
    if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $logPath = Join-Path $logDir ("publish-prod-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
    Start-Transcript -Path $logPath -Append | Out-Null
    Write-Host ("Logging to: {0}" -f $logPath) -ForegroundColor DarkGray
    $transcriptStarted = $true
} catch {
    Write-Host "Transcript could not be started; continuing without file logging." -ForegroundColor Yellow
}

try {

# ── Step 1: Build .NET API ──────────────────────────────────────────
if (!$SkipApi) {
    Write-Host "Step 1: Building .NET API..." -ForegroundColor Yellow

    if (!(Test-Path $ApiPublish)) { New-Item -ItemType Directory -Path $ApiPublish -Force | Out-Null }
    Get-ChildItem -Path $ApiPublish -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

    Push-Location $SolutionDir
    try {
        dotnet restore
        if ($LASTEXITCODE -ne 0) { Write-Error "NuGet restore failed!"; exit 1 }

        dotnet build --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) { Write-Error "Build failed!"; exit 1 }

        # Scope native assets to Windows (framework-dependent). A RID-less publish
        # also drags in SkiaSharp's linux/osx natives, which never load on IIS - pure
        # bloat. Parity with the local build (1a-Build-DotNet-API.ps1 -Runtime win-x64).
        dotnet publish $ProjectPath --configuration Release --output $ApiPublish -r win-x64 --self-contained false
        if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed!"; exit 1 }
    } finally {
        Pop-Location
    }

    # Apply and validate web.config template
    if (Test-Path $WebConfigApiSrc) {
        $wcDest = Join-Path $ApiPublish 'web.config'
        Copy-Item $WebConfigApiSrc $wcDest -Force
        $raw = Get-Content $wcDest -Raw
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success -and $m.Value.Length -ne $raw.Length) {
            Set-Content -Path $wcDest -Value $m.Value -Encoding UTF8
        }
        [void]([xml](Get-Content $wcDest -Raw))
    }

    Write-Host "  API build complete." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Step 1: Skipped (API)" -ForegroundColor DarkGray
    Write-Host ""
}

# ── Step 2: Build Angular (with prod URL patching) ──────────────────
if (!$SkipAngular) {
    Write-Host "Step 2: Building Angular (prod URLs)..." -ForegroundColor Yellow

    Push-Location $AngularPath
    try {
        if (!(Test-Path "node_modules")) {
            Write-Host "  Installing npm dependencies..." -ForegroundColor Cyan
            npm install
            if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed!"; exit 1 }
        }

        # Stamp the build version computed once at the top of this script.
        Write-Host "  Build version: $BuildStamp" -ForegroundColor White

        $envDir = Join-Path $AngularPath "src\environments"
        Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
            $content = Get-Content $_.FullName -Raw
            $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: '$BuildStamp'"
            Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
        }

        # NOTE: No URL patching here. environment.production.ts already contains
        # the prod URLs; angular.json fileReplacements swaps it in at compile time
        # under `--configuration production`. (The previous regex-patch + reset
        # loop was clobbering environment.production.ts back to devapi on every
        # run because the reset blanket-swapped claude-api -> devapi across ALL
        # files, not just the temporarily-patched ones.)
        $env:NO_COLOR = '1'
        npm run build -- --configuration production
        if ($LASTEXITCODE -ne 0) { Write-Error "Angular build failed!"; exit 1 }
    } finally {
        # Reset only the buildVersion stamp - leave URLs alone (they were never
        # patched in the first place under the new model).
        $envDir = Join-Path $AngularPath "src\environments"
        Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
            $content = Get-Content $_.FullName -Raw
            $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: 'dev'"
            Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
        }
        Write-Host "  Environment files reset (buildVersion only)." -ForegroundColor DarkGray
        Pop-Location
    }

    # Copy build output to publish directory
    if (!(Test-Path $AngPublish)) { New-Item -ItemType Directory -Path $AngPublish -Force | Out-Null }
    Get-ChildItem -Path $AngPublish -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

    $distPath = Join-Path $AngularPath "dist\tsic-app"
    if (Test-Path (Join-Path $distPath "browser")) { $distPath = Join-Path $distPath "browser" }
    if (!(Test-Path $distPath)) { Write-Error "Angular build output not found at: $distPath"; exit 1 }
    Copy-Item "$distPath\*" $AngPublish -Recurse -Force

    # Apply web.config template
    if (Test-Path $WebConfigAngSrc) {
        $wcDest = Join-Path $AngPublish 'web.config'
        Copy-Item $WebConfigAngSrc $wcDest -Force
        $raw = Get-Content $wcDest -Raw
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success -and $m.Value.Length -ne $raw.Length) {
            Set-Content -Path $wcDest -Value $m.Value -Encoding UTF8
        }
        [void]([xml](Get-Content $wcDest -Raw))
    }

    Write-Host "  Angular build complete." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Step 2: Skipped (Angular)" -ForegroundColor DarkGray
    Write-Host ""
}

# ── Step 3: Stage to TSIC-PHOENIX over SMB ───────────────────────────
# The old push was Copy-Item -Recurse over the public-IP SMB link with no retry
# and no verification. An interrupted copy left a plausible-looking staging
# folder, and Recycle-After-Deploy's only preflight was "is it non-empty" -- so a
# partial payload would /MIR into live and DELETE from production every file the
# partial copy happened to lack.
#
# Now: robocopy (retries, real exit code), the ONE shared exclusion list, a
# deploy-manifest.json pinning the file count, and a payload check that re-reads
# what actually landed on PHOENIX. A bad push fails HERE, on this box, before you
# ever RDP over there.
#
# No App_Data strip. The exclusion list keeps it out of every copy, so a dirty
# publish simply never reaches staging -- and, on the far end, the live App_Data
# is no longer purged by the mirror. It persists, pool-owned, untouched.
#
# No web.config or appsettings patching: the template is env-agnostic and the
# prod pool's ASPNETCORE_ENVIRONMENT=Production is the single source of truth.
# Program.cs throws at startup if it is missing -- drift surfaces loud.
Write-Host "Step 3: Staging to TSIC-PHOENIX..." -ForegroundColor Yellow

foreach ($s in @(
    if (!$SkipApi)     { @{ Site = $ApiSite;     Publish = $ApiPublish; Staging = $ApiStaging } }
    if (!$SkipAngular) { @{ Site = $AngularSite; Publish = $AngPublish; Staging = $AngularStaging } }
)) {
    # The destination leaf must literally be "<site>-STAGING". \\PHOENIX\Websites
    # also holds TSICUnify-2024 (legacy, live). A /MIR at the wrong UNC path does
    # not corrupt a folder, it empties it.
    Assert-TsicSafeStaging -Site $s.Site -Path $s.Staging | Out-Null

    $ex = Get-TsicExclusions -Site $s.Site

    # Stamp BEFORE the push so the manifest rides along and pins what should land.
    $manifest = New-TsicManifest -Path $s.Publish -Site $s.Site -Environment $AspNetEnv `
                    -GitHash $GitHash -BuildStamp $BuildStamp
    Write-Host "  $($s.Site): $($manifest.fileCount) files -> $($s.Staging)" -ForegroundColor White

    if (!(Test-Path $s.Staging)) { New-Item -ItemType Directory -Path $s.Staging -Force | Out-Null }

    $exit = Invoke-TsicRobocopy -Source $s.Publish -Dest $s.Staging `
                -ExcludeDirs $ex.Dirs -ExcludeFiles $ex.Files -Quiet
    if ($exit -ge 8) {
        Write-Host "  ERROR: staging $($s.Site) failed (robocopy exit $exit)." -ForegroundColor Red
        Write-Host "  Nothing on TSIC-PHOENIX is live yet - the live folders are untouched." -ForegroundColor Yellow
        exit 1
    }

    # Re-read what actually landed on the far end. This is the check that catches
    # a truncated SMB push.
    $bad = Test-TsicPayload -Path $s.Staging -Site $s.Site
    if ($bad) {
        Write-Host "  ERROR: $($s.Site) staging payload is not deployable - $bad" -ForegroundColor Red
        Write-Host "  The SMB push did not land intact. Re-run this script." -ForegroundColor Red
        Write-Host "  Live folders on TSIC-PHOENIX are untouched." -ForegroundColor Yellow
        exit 1
    }

    Write-Host "  $($s.Site) staged and verified ($BuildStamp)." -ForegroundColor Green
}

Write-Host ""

# ── Step 4: Update deployment scripts on TSIC-PHOENIX ────────────────
# Rollback-Deploy.ps1 ships WITH the deploy, so the recovery path is already on
# the box before anything can go wrong -- not typed under pressure during an
# outage. _deploy-common.ps1 goes next to _config.ps1 because Recycle and
# Rollback both dot-source it from there; if it does not land, they will not run.
Write-Host "Step 4: Updating deployment scripts on TSIC-PHOENIX..." -ForegroundColor Yellow
$remoteDeployment = "\\$ProdServer\Websites\IIS-Config-Prod\Deployment"
$remoteConfig     = "\\$ProdServer\Websites\IIS-Config-Prod"
if (!(Test-Path $remoteDeployment)) { New-Item -ItemType Directory -Path $remoteDeployment -Force | Out-Null }

$scriptPushes = @(
    @{ From = (Join-Path $PSScriptRoot "IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1"); To = $remoteDeployment }
    @{ From = (Join-Path $PSScriptRoot "IIS-Config-Prod\Deployment\Rollback-Deploy.ps1");      To = $remoteDeployment }
    @{ From = (Join-Path $PSScriptRoot "IIS-Config-Prod\_config.ps1");                         To = $remoteConfig }
    @{ From = (Join-Path $PSScriptRoot "_deploy-common.ps1");                                  To = $remoteConfig }
)
foreach ($push in $scriptPushes) {
    $name = Split-Path $push.From -Leaf
    if (!(Test-Path $push.From)) {
        Write-Host "  ERROR: $name not found at $($push.From)" -ForegroundColor Red
        exit 1
    }
    Copy-Item $push.From "$($push.To)\" -Force
    $landed = Join-Path $push.To $name
    if (!(Test-Path $landed) -or
        (Get-Item $landed).Length -ne (Get-Item $push.From).Length) {
        Write-Host "  ERROR: $name did not land intact on TSIC-PHOENIX." -ForegroundColor Red
        Write-Host "  Recycle-After-Deploy.ps1 will not run without it. Re-run this script." -ForegroundColor Red
        exit 1
    }
    Write-Host "  Pushed: $name" -ForegroundColor Green
}

# Drop Go.ps1 wrappers in staging folders. Go.ps1 mirrors the SCOPE OF THIS BUILD,
# and both folders get the SAME wrapper, so it does not matter which one you are
# standing in on PHOENIX.
#
# The wrappers used to hardcode a half-deploy each (-SkipAngular in the API folder,
# -SkipApi in the Angular folder). Running both, as the runbook said to, meant TWO
# Recycle runs -- and between them production served a new API under the old
# frontend. Recycle refuses to create that skew within a single run; the wrappers
# were creating it across two. A normal deploy is now ONE run: both sites, one
# backup pair, one pool bounce, no window.
#
# Recycle is idempotent, so running Go.ps1 from the second folder out of habit is
# harmless (identical bytes, 0 copied, 0 deleted) - it just costs a spare backup.
$recycleScript = 'E:\Websites\IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1'

$goArgs = ''
if     ($SkipApi)     { $goArgs = ' -SkipApi' }
elseif ($SkipAngular) { $goArgs = ' -SkipAngular' }
$goBody = "& '$recycleScript'$goArgs"

# Written to BOTH staging folders, always - including the one this build did not
# stage. A scoped build would otherwise leave the PREVIOUS build's wrapper behind
# in the other folder: run "-SkipApi", then stand in claude-api-STAGING on PHOENIX
# out of habit, and its stale Go.ps1 would swap the API you just told the build to
# leave alone. Both wrappers always carry THIS build's scope, so the folder you
# happen to be standing in cannot change what gets deployed.
foreach ($stagingDir in @($ApiStaging, $AngularStaging)) {
    if (Test-Path $stagingDir) {
        Set-Content (Join-Path $stagingDir 'Go.ps1') $goBody -Encoding UTF8
    }
}
Write-Host "  Go.ps1 (in both staging folders) runs: $goBody" -ForegroundColor Green

Write-Host ""

# ── Done ─────────────────────────────────────────────────────────────
# Nothing on TSIC-PHOENIX is live yet. This script only builds and stages; the
# live folders are untouched until Go.ps1 runs over there.
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STAGED to TSIC-PHOENIX - nothing is live yet." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Build: $BuildStamp" -ForegroundColor Green
Write-Host "  Staged and payload-verified:" -ForegroundColor Green
if (!$SkipApi)     { Write-Host "    API:     $ApiStaging" -ForegroundColor Green }
if (!$SkipAngular) { Write-Host "    Angular: $AngularStaging" -ForegroundColor Green }
Write-Host ""

# ONE run. Both staging folders carry the SAME wrapper - this build's scope - so it
# does not matter which one you are standing in. The old banner said "run Go.ps1 from
# each staging folder", and that was the bug: two runs meant production served the new
# API under the old frontend in the gap between them.
#
# The path printed is the LOCAL path on PHOENIX (E:\...), not the UNC share we staged
# over, because that is what you will actually be typing on the box.
$scopeText = 'API + Angular'
if     ($SkipApi)     { $scopeText = 'Angular only' }
elseif ($SkipAngular) { $scopeText = 'API only' }

$goFolder = $Config.ApiStagingPath
if ($SkipApi) { $goFolder = $Config.AngularStagingPath }

Write-Host "  NEXT: RDP to TSIC-PHOENIX, open an ELEVATED PowerShell, and run it ONCE:" -ForegroundColor Yellow
Write-Host ""
Write-Host "    cd $goFolder" -ForegroundColor White
Write-Host "    .\Go.ps1" -ForegroundColor White
Write-Host ""
Write-Host "  That ONE run deploys everything this build staged ($scopeText)." -ForegroundColor Yellow
Write-Host "  Do NOT also run Go.ps1 from the other staging folder." -ForegroundColor Yellow
Write-Host ""
Write-Host "  If it fails, the rollback is already on the box:" -ForegroundColor DarkGray
Write-Host "    E:\Websites\IIS-Config-Prod\Deployment\Rollback-Deploy.ps1 -List" -ForegroundColor DarkGray
Write-Host ""

# Explicit. robocopy exits 1/2/3 on SUCCESS (copied / extras deleted / both), and
# a script that runs off the end inherits the last native command's exit code --
# so without this, a clean stage would report failure to anything checking it.
exit 0

} finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
}
