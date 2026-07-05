# Build and deploy to local IIS
# Runs 1a (API) + 1b (Angular) then deploys directly to C:\Websites
# Skips iDrive packaging - use 1-Build-And-Package.ps1 for remote deployment

param(
    [string]$ApiTarget = "C:\Websites\dev-api",
    [string]$AngularTarget = "C:\Websites\dev-app",
    [string]$ApiSiteName = "dev-api",
    [string]$AngularSiteName = "dev-app"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TSIC Build and Deploy (Local IIS)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "API target:     $ApiTarget" -ForegroundColor Yellow
Write-Host "Angular target: $AngularTarget" -ForegroundColor Yellow
Write-Host ""

# Transcript logging
$transcriptStarted = $false
try {
    $logDir = Join-Path $PSScriptRoot "..\publish\build-logs"
    if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $logPath = Join-Path $logDir ("deploy-local-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
    Start-Transcript -Path $logPath -Append | Out-Null
    Write-Host ("Logging to: {0}" -f $logPath) -ForegroundColor DarkGray
    $transcriptStarted = $true
} catch {
    Write-Host "Transcript could not be started; continuing without file logging." -ForegroundColor Yellow
}

try {

# ── Step 1: Build .NET API ──────────────────────────────────────────
Write-Host "Step 1: Building .NET API..." -ForegroundColor Yellow
$scriptPath = Join-Path $PSScriptRoot "1a-Build-DotNet-API.ps1"
& $scriptPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "API build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "API build complete!" -ForegroundColor Green
Write-Host ""

# ── Step 2: Build Angular (Staging configuration) ──────────────────
Write-Host "Step 2: Building Angular (configuration=staging)..." -ForegroundColor Yellow
$scriptPath = Join-Path $PSScriptRoot "1b-Build-Angular.ps1"
& $scriptPath -Configuration staging
if ($LASTEXITCODE -ne 0) {
    Write-Host "Angular build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Angular build complete!" -ForegroundColor Green
Write-Host ""

# ── Step 3: Stop IIS sites ──────────────────────────────────────────
Write-Host "Step 3: Stopping IIS sites..." -ForegroundColor Yellow
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (Get-Module WebAdministration) {
    try {
        if (Get-Website -Name $ApiSiteName -ErrorAction SilentlyContinue) {
            Stop-Website -Name $ApiSiteName
            Write-Host "  Stopped website: $ApiSiteName" -ForegroundColor White
        }
        if (Get-Website -Name $AngularSiteName -ErrorAction SilentlyContinue) {
            Stop-Website -Name $AngularSiteName
            Write-Host "  Stopped website: $AngularSiteName" -ForegroundColor White
        }
        # Stop app pools (same name as sites)
        if (Get-WebAppPoolState -Name $ApiSiteName -ErrorAction SilentlyContinue) {
            Stop-WebAppPool -Name $ApiSiteName -ErrorAction SilentlyContinue
            Write-Host "  Stopped app pool: $ApiSiteName" -ForegroundColor White
        }
        if (Get-WebAppPoolState -Name $AngularSiteName -ErrorAction SilentlyContinue) {
            Stop-WebAppPool -Name $AngularSiteName -ErrorAction SilentlyContinue
            Write-Host "  Stopped app pool: $AngularSiteName" -ForegroundColor White
        }
    } catch {
        Write-Host "  Could not stop sites/pools: $_" -ForegroundColor Yellow
    }
    Start-Sleep -Seconds 2
} else {
    Write-Host "  WebAdministration module not available - stop sites manually" -ForegroundColor Yellow
}
Write-Host ""

# ── Step 4: Deploy files ────────────────────────────────────────────
$PublishRoot = Join-Path $PSScriptRoot "..\publish"
$ApiSource = Join-Path $PublishRoot "api"
$AngularSource = Join-Path $PublishRoot "angular"

# Deploy API
Write-Host "Step 4: Deploying files..." -ForegroundColor Yellow
if (!(Test-Path $ApiSource)) {
    Write-Host "  API build output not found: $ApiSource" -ForegroundColor Red
    exit 1
}
if (!(Test-Path $ApiTarget)) {
    New-Item -ItemType Directory -Path $ApiTarget -Force | Out-Null
}
Write-Host "  Clearing $ApiTarget (preserving logs/, keys/, FirebaseAuth_*)..." -ForegroundColor White
Get-ChildItem $ApiTarget -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin @('logs', 'keys') -and $_.Name -notlike 'FirebaseAuth_*.json' } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  Copying API files..." -ForegroundColor White
Copy-Item "$ApiSource\*" $ApiTarget -Recurse -Force

# Nothing under App_Data should ship: AdnMonthEnd is a runtime cache the API regenerates on
# demand, and Help is retired backend content (migrated to frontend static assets, f94e80eb).
# A dirty publish carries these, and this admin-run Copy-Item seeds admin-owned files the app
# pool cannot delete/overwrite — which 500s the ADN month-end import. Strip the whole folder so
# the pool recreates its cache (pool-owned) at runtime. Setup grants the pool Modify on
# App_Data\AdnMonthEnd (03-Create-Directories.ps1).
$apiAppData = Join-Path $ApiTarget "App_Data"
if (Test-Path $apiAppData) {
    Remove-Item $apiAppData -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  Stripped non-shipping App_Data (runtime cache + retired Help)" -ForegroundColor White
}

# Copy canonical web.config template (env-agnostic; ASPNETCORE_ENVIRONMENT lives
# on the dev-api app pool, set during setup by IIS-Config-Dev/Setup/07-Apply-Secrets.ps1).
$apiConfigSrc = Join-Path $PSScriptRoot "web.config.api"
if (Test-Path $apiConfigSrc) {
    Copy-Item $apiConfigSrc (Join-Path $ApiTarget "web.config") -Force
} else {
    Write-Host "  web.config.api template not found at $apiConfigSrc" -ForegroundColor Red
    exit 1
}

# Deploy Angular
if (!(Test-Path $AngularSource)) {
    Write-Host "  Angular build output not found: $AngularSource" -ForegroundColor Red
    exit 1
}
if (!(Test-Path $AngularTarget)) {
    New-Item -ItemType Directory -Path $AngularTarget -Force | Out-Null
}
Write-Host "  Clearing $AngularTarget..." -ForegroundColor White
Get-ChildItem $AngularTarget -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  Copying Angular files..." -ForegroundColor White
Copy-Item "$AngularSource\*" $AngularTarget -Recurse -Force

# Flatten browser/ subfolder if needed (Angular 17+)
$angularIndex = Join-Path $AngularTarget "index.html"
$angularBrowser = Join-Path $AngularTarget "browser"
if (!(Test-Path $angularIndex) -and (Test-Path (Join-Path $angularBrowser "index.html"))) {
    Write-Host "  Flattening Angular 'browser' subfolder..." -ForegroundColor White
    Copy-Item (Join-Path $angularBrowser "*") $AngularTarget -Recurse -Force
    Remove-Item $angularBrowser -Recurse -Force -ErrorAction SilentlyContinue
}

# Copy web.config template if available
$angularConfigSrc = Join-Path $PSScriptRoot "web.config.angular"
if (Test-Path $angularConfigSrc) {
    Copy-Item $angularConfigSrc (Join-Path $AngularTarget "web.config") -Force
}

Write-Host "  Files deployed!" -ForegroundColor Green
Write-Host ""

# ── Step 5: Start IIS sites ─────────────────────────────────────────
# HARDENED: existence-test via Test-Path on IIS:\ drive (reliable across
# PS sessions); always attempt Start, verify final state, retry once on
# transient failure, and FAIL THE BUILD if a target site/pool ends up
# anything other than Started. Previous version used Get-WebAppPoolState
# as a truthy guard and skipped Start when it returned null — leaving
# both sites stopped after a successful deploy.
Write-Host "Step 5: Starting IIS sites..." -ForegroundColor Yellow
if (Get-Module WebAdministration) {
    function Start-IISTarget {
        param(
            [Parameter(Mandatory)] [ValidateSet('AppPool','Site')] [string] $Kind,
            [Parameter(Mandatory)] [string] $Name
        )
        $iisPath = if ($Kind -eq 'AppPool') { "IIS:\AppPools\$Name" } else { "IIS:\Sites\$Name" }
        if (-not (Test-Path $iisPath)) {
            Write-Host "  $Kind '$Name' does not exist - skipping" -ForegroundColor Yellow
            return $true
        }
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            try {
                if ($Kind -eq 'AppPool') { Start-WebAppPool -Name $Name -ErrorAction Stop }
                else                     { Start-Website   -Name $Name -ErrorAction Stop }
            } catch {
                # Already-running raises an error on some PS hosts; tolerate it
                if ($_.Exception.Message -notmatch 'already started|already running') {
                    Write-Host "  Start $Kind '$Name' attempt ${attempt}: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            }
            Start-Sleep -Milliseconds 500
            $state = if ($Kind -eq 'AppPool') {
                (Get-Item $iisPath -ErrorAction SilentlyContinue).State
            } else {
                (Get-Website -Name $Name -ErrorAction SilentlyContinue).State
            }
            if ($state -eq 'Started') {
                Write-Host "  Started ${Kind}: $Name" -ForegroundColor White
                return $true
            }
            Write-Host "  $Kind '$Name' state after attempt ${attempt}: $state" -ForegroundColor Yellow
        }
        Write-Host "  FAILED to start ${Kind}: $Name" -ForegroundColor Red
        return $false
    }

    $ok = $true
    # Pools first, then sites that depend on them
    $ok = (Start-IISTarget -Kind AppPool -Name $ApiSiteName)     -and $ok
    $ok = (Start-IISTarget -Kind AppPool -Name $AngularSiteName) -and $ok
    $ok = (Start-IISTarget -Kind Site    -Name $ApiSiteName)     -and $ok
    $ok = (Start-IISTarget -Kind Site    -Name $AngularSiteName) -and $ok
    if (-not $ok) {
        Write-Host "  One or more IIS targets failed to start - investigate before declaring deploy successful" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  WebAdministration module not available - start sites manually" -ForegroundColor Yellow
    exit 1
}
Write-Host ""

# ── Step 6: Ensure IIS app pool has DB access ─────────────────────────
# After a database restore, the IIS app pool login gets orphaned.
# This idempotently ensures the login + user mapping exists.
Write-Host "Step 6: Ensuring IIS app pool DB login..." -ForegroundColor Yellow
$fixLoginSql = Join-Path $PSScriptRoot "00-postdev-db-restore-apppooluser.sql"
if (Test-Path $fixLoginSql) {
    try {
        sqlcmd -S ".\SS2016" -E -i $fixLoginSql -b
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  DB login verified for IIS APPPOOL\$ApiSiteName" -ForegroundColor Green
        } else {
            Write-Host "  sqlcmd returned exit code $LASTEXITCODE - check SQL output above" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  Could not run 00-postdev-db-restore-apppooluser.sql: $_" -ForegroundColor Yellow
        Write-Host "  If login fails after deploy, run scripts\00-postdev-db-restore-apppooluser.sql manually in SSMS" -ForegroundColor Yellow
    }
} else {
    Write-Host "  00-postdev-db-restore-apppooluser.sql not found - skipping DB login check" -ForegroundColor Yellow
}
Write-Host ""

# ── Step 7: Warmup request ────────────────────────────────────────────
Write-Host "Step 7: Warming up API (triggers JIT compilation)..." -ForegroundColor Yellow
Start-Sleep -Seconds 3  # Give IIS a moment to spin up the worker process
try {
    # Hit API root to trigger ASP.NET startup + JIT (Swagger is dev-only, won't exist in IIS)
    $null = Invoke-WebRequest -Uri "https://devapi.teamsportsinfo.com/api/jobs/tsic" -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
    Write-Host "  API warmed up!" -ForegroundColor Green
} catch {
    Write-Host "  Warmup request failed (app may still be starting): $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "  First manual request will be slow while JIT compiles." -ForegroundColor Yellow
}
Write-Host ""

# ── Done ─────────────────────────────────────────────────────────────
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUCCESS! Deployed to local IIS." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API:     https://devapi.teamsportsinfo.com" -ForegroundColor Green
Write-Host "  Angular: https://dev.teamsportsinfo.com" -ForegroundColor Green

} finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
    try { Set-Location -Path $PSScriptRoot } catch {}
    Write-Host ("Returned to: {0}" -f (Get-Location)) -ForegroundColor DarkGray
}
