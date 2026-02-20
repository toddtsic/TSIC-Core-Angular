# Build and deploy to local IIS
# Runs 1a (API) + 1b (Angular) then deploys directly to C:\Websites
# Skips iDrive packaging - use 1-Build-And-Package.ps1 for remote deployment

param(
    [string]$ApiTarget = "C:\Websites\TSIC.Api",
    [string]$AngularTarget = "C:\Websites\TSIC.App",
    [string]$ApiSiteName = "TSIC.Api",
    [string]$AngularSiteName = "TSIC.App"
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

# ── Step 2: Build Angular ───────────────────────────────────────────
Write-Host "Step 2: Building Angular..." -ForegroundColor Yellow
$scriptPath = Join-Path $PSScriptRoot "1b-Build-Angular.ps1"
& $scriptPath
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
Write-Host "  Clearing $ApiTarget (preserving logs/ and keys/)..." -ForegroundColor White
Get-ChildItem $ApiTarget -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin @('logs', 'keys') } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  Copying API files..." -ForegroundColor White
Copy-Item "$ApiSource\*" $ApiTarget -Recurse -Force

# Copy web.config template if available
$apiConfigSrc = Join-Path $PSScriptRoot "web.config.api"
if (Test-Path $apiConfigSrc) {
    Copy-Item $apiConfigSrc (Join-Path $ApiTarget "web.config") -Force
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
Write-Host "Step 5: Starting IIS sites..." -ForegroundColor Yellow
if (Get-Module WebAdministration) {
    try {
        if (Get-WebAppPoolState -Name $ApiSiteName -ErrorAction SilentlyContinue) {
            Start-WebAppPool -Name $ApiSiteName -ErrorAction SilentlyContinue
            Write-Host "  Started app pool: $ApiSiteName" -ForegroundColor White
        }
        if (Get-WebAppPoolState -Name $AngularSiteName -ErrorAction SilentlyContinue) {
            Start-WebAppPool -Name $AngularSiteName -ErrorAction SilentlyContinue
            Write-Host "  Started app pool: $AngularSiteName" -ForegroundColor White
        }
        if (Get-Website -Name $ApiSiteName -ErrorAction SilentlyContinue) {
            Start-Website -Name $ApiSiteName
            Write-Host "  Started website: $ApiSiteName" -ForegroundColor White
        }
        if (Get-Website -Name $AngularSiteName -ErrorAction SilentlyContinue) {
            Start-Website -Name $AngularSiteName
            Write-Host "  Started website: $AngularSiteName" -ForegroundColor White
        }
    } catch {
        Write-Host "  Could not start sites/pools: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "  WebAdministration module not available - start sites manually" -ForegroundColor Yellow
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
