# ============================================================================
# 03-Deploy-To-IIS.ps1 — Deploy built API + Angular to IIS
# ============================================================================
# Stops IIS sites, copies build outputs, patches appsettings for Prod,
# starts IIS sites, fixes DB login, and warms up the API.
#
# Prerequisites: Run 01-Build-DotNet-API.ps1 and 02-Build-Angular.ps1 first.
#
# Usage:
#   .\03-Deploy-To-IIS.ps1 -Environment Dev
#   .\03-Deploy-To-IIS.ps1 -Environment Prod
#   .\03-Deploy-To-IIS.ps1 -Environment Dev -SkipBuild
# ============================================================================

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Dev',

    [switch]$SkipBuild,
    [switch]$SkipDbLogin,
    [switch]$SkipWarmup
)

. "$PSScriptRoot\..\_config.ps1" -Environment $Environment

$PublishRoot = "$PSScriptRoot\..\..\..\publish"
$ApiSource = Join-Path $PublishRoot "api"
$AngularSource = Join-Path $PublishRoot "angular"

# Prod patching config
$DevApiHost  = 'devapi.teamsportsinfo.com'
$DevAppHost  = 'dev.teamsportsinfo.com'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TSIC Deploy to IIS ($Environment)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API target:     $($Config.ApiPath)" -ForegroundColor Yellow
Write-Host "  Angular target: $($Config.AngularPath)" -ForegroundColor Yellow
Write-Host "  Hostnames:      $($Config.ApiHostname) / $($Config.AngularHostname)" -ForegroundColor Yellow
Write-Host ""

# Transcript logging
$transcriptStarted = $false
try {
    $logDir = Join-Path $PublishRoot "build-logs"
    if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $logPath = Join-Path $logDir ("deploy-$Environment-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
    Start-Transcript -Path $logPath -Append | Out-Null
    Write-Host ("Logging to: {0}" -f $logPath) -ForegroundColor DarkGray
    $transcriptStarted = $true
} catch {
    Write-Host "Transcript could not be started; continuing without file logging." -ForegroundColor Yellow
}

try {

# ── Step 1: Build (optional) ──────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "Step 1a: Building .NET API..." -ForegroundColor Yellow
    & "$PSScriptRoot\01-Build-DotNet-API.ps1"
    if ($LASTEXITCODE -ne 0) { Write-Host "API build failed!" -ForegroundColor Red; exit 1 }
    Write-Host ""

    Write-Host "Step 1b: Building Angular ($Environment)..." -ForegroundColor Yellow
    & "$PSScriptRoot\02-Build-Angular.ps1" -Environment $Environment
    if ($LASTEXITCODE -ne 0) { Write-Host "Angular build failed!" -ForegroundColor Red; exit 1 }
    Write-Host ""
}
else {
    Write-Host "Step 1: Build — SKIPPED (-SkipBuild)" -ForegroundColor Yellow
    Write-Host ""
}

# ── Step 2: Verify build outputs exist ────────────────────────────────
if (!(Test-Path $ApiSource)) {
    Write-Host "API build output not found: $ApiSource" -ForegroundColor Red
    Write-Host "Run 01-Build-DotNet-API.ps1 first." -ForegroundColor Red
    exit 1
}
if (!(Test-Path $AngularSource)) {
    Write-Host "Angular build output not found: $AngularSource" -ForegroundColor Red
    Write-Host "Run 02-Build-Angular.ps1 first." -ForegroundColor Red
    exit 1
}

# ── Step 3: Stop IIS sites ──────────────────────────────────────────
Write-Host "Step 2: Stopping IIS sites..." -ForegroundColor Yellow
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (Get-Module WebAdministration) {
    try {
        foreach ($siteName in @($Config.ApiSiteName, $Config.AngularSiteName)) {
            if (Get-Website -Name $siteName -ErrorAction SilentlyContinue) {
                Stop-Website -Name $siteName
                Write-Host "  Stopped website: $siteName" -ForegroundColor White
            }
        }
        foreach ($poolName in @($Config.ApiPoolName, $Config.AngularPoolName)) {
            if (Get-WebAppPoolState -Name $poolName -ErrorAction SilentlyContinue) {
                Stop-WebAppPool -Name $poolName -ErrorAction SilentlyContinue
                Write-Host "  Stopped app pool: $poolName" -ForegroundColor White
            }
        }
    } catch {
        Write-Host "  Could not stop sites/pools: $_" -ForegroundColor Yellow
    }
    Start-Sleep -Seconds 2
} else {
    Write-Host "  WebAdministration module not available — stop sites manually" -ForegroundColor Yellow
}
Write-Host ""

# ── Step 4: Deploy files ────────────────────────────────────────────
Write-Host "Step 3: Deploying files..." -ForegroundColor Yellow

# Deploy API (preserve logs/, keys/, FirebaseAuth_*)
$apiTarget = $Config.ApiPath
if (!(Test-Path $apiTarget)) { New-Item -ItemType Directory -Path $apiTarget -Force | Out-Null }
Write-Host "  Clearing $apiTarget (preserving logs/, keys/, FirebaseAuth_*)..." -ForegroundColor White
Get-ChildItem $apiTarget -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notin @('logs', 'keys') -and $_.Name -notlike 'FirebaseAuth_*.json' } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  Copying API files..." -ForegroundColor White
Copy-Item "$ApiSource\*" $apiTarget -Recurse -Force

# Copy web.config and stamp ASPNETCORE_ENVIRONMENT
$apiConfigSrc = Join-Path $PSScriptRoot "..\web.config.api"
if (Test-Path $apiConfigSrc) {
    $webConfigDest = Join-Path $apiTarget "web.config"
    Copy-Item $apiConfigSrc $webConfigDest -Force
    $content = Get-Content $webConfigDest -Raw
    $content = $content -replace '__ASPNET_ENV__', $Config.AspNetEnv
    Set-Content $webConfigDest $content -NoNewline -Encoding UTF8
    Write-Host "  web.config: ASPNETCORE_ENVIRONMENT = $($Config.AspNetEnv)" -ForegroundColor White
}

# Deploy Angular
$angularTarget = $Config.AngularPath
if (!(Test-Path $angularTarget)) { New-Item -ItemType Directory -Path $angularTarget -Force | Out-Null }
Write-Host "  Clearing $angularTarget..." -ForegroundColor White
Get-ChildItem $angularTarget -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  Copying Angular files..." -ForegroundColor White
Copy-Item "$AngularSource\*" $angularTarget -Recurse -Force

# Flatten browser/ subfolder if needed (Angular 17+)
$angularIndex = Join-Path $angularTarget "index.html"
$angularBrowser = Join-Path $angularTarget "browser"
if (!(Test-Path $angularIndex) -and (Test-Path (Join-Path $angularBrowser "index.html"))) {
    Write-Host "  Flattening Angular 'browser' subfolder..." -ForegroundColor White
    Copy-Item (Join-Path $angularBrowser "*") $angularTarget -Recurse -Force
    Remove-Item $angularBrowser -Recurse -Force -ErrorAction SilentlyContinue
}

# Copy web.config
$angularConfigSrc = Join-Path $PSScriptRoot "..\web.config.angular"
if (Test-Path $angularConfigSrc) {
    Copy-Item $angularConfigSrc (Join-Path $angularTarget "web.config") -Force
}

Write-Host "  Files deployed!" -ForegroundColor Green
Write-Host ""

# ── Step 5: Patch appsettings for Prod ──────────────────────────────
if ($Environment -eq 'Prod') {
    Write-Host "Step 4: Patching appsettings for production..." -ForegroundColor Yellow
    $settingsFiles = @(
        (Join-Path $apiTarget "appsettings.json"),
        (Join-Path $apiTarget "appsettings.Production.json")
    )
    foreach ($file in $settingsFiles) {
        if (Test-Path $file) {
            $content = Get-Content $file -Raw
            $original = $content
            # Drive paths
            $content = $content -replace 'C:\\\\Websites', ($Config.BasePath -replace '\\', '\\') `
                                 -replace 'C:\\Websites', $Config.BasePath
            # Hostnames
            $content = $content -replace [regex]::Escape($DevApiHost), $Config.ApiHostname
            $content = $content -replace [regex]::Escape($DevAppHost), $Config.AngularHostname
            if ($content -ne $original) {
                Set-Content $file $content -NoNewline
                Write-Host "  Patched: $(Split-Path $file -Leaf)" -ForegroundColor White
            } else {
                Write-Host "  No changes needed: $(Split-Path $file -Leaf)" -ForegroundColor DarkGray
            }
        }
    }
    Write-Host ""
}

# ── Step 6: Start IIS sites ─────────────────────────────────────────
Write-Host "Step 5: Starting IIS sites..." -ForegroundColor Yellow
if (Get-Module WebAdministration) {
    try {
        foreach ($poolName in @($Config.ApiPoolName, $Config.AngularPoolName)) {
            if (Get-WebAppPoolState -Name $poolName -ErrorAction SilentlyContinue) {
                Start-WebAppPool -Name $poolName -ErrorAction SilentlyContinue
                Write-Host "  Started app pool: $poolName" -ForegroundColor White
            }
        }
        foreach ($siteName in @($Config.ApiSiteName, $Config.AngularSiteName)) {
            if (Get-Website -Name $siteName -ErrorAction SilentlyContinue) {
                Start-Website -Name $siteName
                Write-Host "  Started website: $siteName" -ForegroundColor White
            }
        }
    } catch {
        Write-Host "  Could not start sites/pools: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "  WebAdministration module not available — start sites manually" -ForegroundColor Yellow
}
Write-Host ""

# ── Step 7: Fix DB login ────────────────────────────────────────────
if (-not $SkipDbLogin) {
    Write-Host "Step 6: Ensuring IIS app pool DB login..." -ForegroundColor Yellow
    $fixLoginSql = Join-Path $PSScriptRoot "Fix-IIS-DbLogin.sql"
    if (Test-Path $fixLoginSql) {
        try {
            sqlcmd -S $Config.SqlInstance -E -i $fixLoginSql -b
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  DB login verified for IIS APPPOOL\$($Config.ApiPoolName)" -ForegroundColor Green
            } else {
                Write-Host "  sqlcmd returned exit code $LASTEXITCODE — check SQL output above" -ForegroundColor Yellow
            }
        } catch {
            Write-Host "  Could not run Fix-IIS-DbLogin.sql: $_" -ForegroundColor Yellow
            Write-Host "  Run it manually in SSMS if login fails." -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Fix-IIS-DbLogin.sql not found — skipping." -ForegroundColor Yellow
    }
    Write-Host ""
}

# ── Step 8: Warmup ──────────────────────────────────────────────────
if (-not $SkipWarmup) {
    Write-Host "Step 7: Warming up API (triggers JIT compilation)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    try {
        $warmupUrl = "https://$($Config.ApiHostname)/api/jobs/tsic"
        $null = Invoke-WebRequest -Uri $warmupUrl -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
        Write-Host "  API warmed up!" -ForegroundColor Green
    } catch {
        Write-Host "  Warmup request failed (app may still be starting): $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  First manual request will be slow while JIT compiles." -ForegroundColor Yellow
    }
    Write-Host ""
}

# ── Done ─────────────────────────────────────────────────────────────
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUCCESS! Deployed to $Environment IIS." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API:     https://$($Config.ApiHostname)" -ForegroundColor Green
Write-Host "  Angular: https://$($Config.AngularHostname)" -ForegroundColor Green

} finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
}
