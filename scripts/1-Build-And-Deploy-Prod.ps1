# Build and deploy to PRODUCTION IIS
# Same as 1-Build-And-Deploy-Local.ps1 but:
#   - Targets E:\Websites (prod server drive)
#   - Patches Angular environment URLs: dev → bear (before build)
#   - Patches appsettings paths: C:\Websites → E:\Websites (after deploy)
#   - Patches appsettings URLs: dev → bear (after deploy)

param(
    [string]$ApiTarget = "E:\Websites\TSIC.Api",
    [string]$AngularTarget = "E:\Websites\TSIC.App",
    [string]$ApiSiteName = "TSIC.Api",
    [string]$AngularSiteName = "TSIC.App"
)

# ── Production hostnames ─────────────────────────────────────────────
# Change these when you retire legacy "www" and switch to real prod names
$ProdAppHost  = "bear.teamsportsinfo.com"
$ProdApiHost  = "bearapi.teamsportsinfo.com"
$DevAppHost   = "dev.teamsportsinfo.com"
$DevApiHost   = "devapi.teamsportsinfo.com"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TSIC Build and Deploy (PRODUCTION)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "API target:     $ApiTarget" -ForegroundColor Yellow
Write-Host "Angular target: $AngularTarget" -ForegroundColor Yellow
Write-Host "Hostnames:      $ProdAppHost / $ProdApiHost" -ForegroundColor Yellow
Write-Host ""

# Transcript logging
$transcriptStarted = $false
try {
    $logDir = Join-Path $PSScriptRoot "..\publish\build-logs"
    if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $logPath = Join-Path $logDir ("deploy-prod-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
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

# ── Step 2: Patch Angular environment files for prod ─────────────────
Write-Host "Step 2: Patching Angular environment files for prod..." -ForegroundColor Yellow
$envDir = Join-Path $PSScriptRoot "..\TSIC-Core-Angular\src\frontend\tsic-app\src\environments"
$envFiles = Get-ChildItem $envDir -Filter "environment*.ts"
foreach ($file in $envFiles) {
    $content = Get-Content $file.FullName -Raw
    $patched = $content -replace [regex]::Escape($DevApiHost), $ProdApiHost `
                         -replace [regex]::Escape($DevAppHost), $ProdAppHost
    if ($patched -ne $content) {
        Set-Content $file.FullName $patched -NoNewline -Encoding UTF8
        Write-Host "  Patched: $($file.Name)" -ForegroundColor White
    }
}
Write-Host ""

# ── Step 3: Build Angular ───────────────────────────────────────────
Write-Host "Step 3: Building Angular (with prod URLs)..." -ForegroundColor Yellow
$scriptPath = Join-Path $PSScriptRoot "1b-Build-Angular.ps1"
& $scriptPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "Angular build failed!" -ForegroundColor Red
    # Reset environment files even on failure
    foreach ($file in $envFiles) {
        $content = Get-Content $file.FullName -Raw
        $content = $content -replace [regex]::Escape($ProdApiHost), $DevApiHost `
                             -replace [regex]::Escape($ProdAppHost), $DevAppHost
        Set-Content $file.FullName $content -NoNewline -Encoding UTF8
    }
    Write-Host "  Environment files reset to dev" -ForegroundColor DarkGray
    exit 1
}

# Reset environment files back to dev so git stays clean
foreach ($file in $envFiles) {
    $content = Get-Content $file.FullName -Raw
    $content = $content -replace [regex]::Escape($ProdApiHost), $DevApiHost `
                         -replace [regex]::Escape($ProdAppHost), $DevAppHost
    Set-Content $file.FullName $content -NoNewline -Encoding UTF8
}
Write-Host "  Environment files reset to dev" -ForegroundColor DarkGray
Write-Host "Angular build complete!" -ForegroundColor Green
Write-Host ""

# ── Step 4: Stop IIS sites ──────────────────────────────────────────
Write-Host "Step 4: Stopping IIS sites..." -ForegroundColor Yellow
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

# ── Step 5: Deploy files ────────────────────────────────────────────
$PublishRoot = Join-Path $PSScriptRoot "..\publish"
$ApiSource = Join-Path $PublishRoot "api"
$AngularSource = Join-Path $PublishRoot "angular"

# Deploy API
Write-Host "Step 5: Deploying files..." -ForegroundColor Yellow
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

# ── Step 6: Patch appsettings for production ─────────────────────────
Write-Host "Step 6: Patching appsettings for production..." -ForegroundColor Yellow
$settingsFiles = @(
    (Join-Path $ApiTarget "appsettings.json"),
    (Join-Path $ApiTarget "appsettings.Production.json")
)
foreach ($file in $settingsFiles) {
    if (Test-Path $file) {
        $content = Get-Content $file -Raw
        $original = $content
        # Drive paths: C:\Websites → E:\Websites
        $content = $content -replace 'C:\\\\Websites', 'E:\\Websites' -replace 'C:\\Websites', 'E:\\Websites'
        # Hostnames: dev → bear
        $content = $content -replace [regex]::Escape($DevApiHost), $ProdApiHost
        $content = $content -replace [regex]::Escape($DevAppHost), $ProdAppHost
        if ($content -ne $original) {
            Set-Content $file $content -NoNewline
            Write-Host "  Patched: $(Split-Path $file -Leaf)" -ForegroundColor White
        } else {
            Write-Host "  No changes needed: $(Split-Path $file -Leaf)" -ForegroundColor DarkGray
        }
    }
}
Write-Host ""

# ── Step 7: Start IIS sites ─────────────────────────────────────────
Write-Host "Step 7: Starting IIS sites..." -ForegroundColor Yellow
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

# ── Step 8: Ensure IIS app pool has DB access ─────────────────────────
Write-Host "Step 8: Ensuring IIS app pool DB login..." -ForegroundColor Yellow
$fixLoginSql = Join-Path $PSScriptRoot "Fix-IIS-DbLogin.sql"
if (Test-Path $fixLoginSql) {
    try {
        sqlcmd -S ".\SS2016" -E -i $fixLoginSql -b
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  DB login verified for IIS APPPOOL\$ApiSiteName" -ForegroundColor Green
        } else {
            Write-Host "  sqlcmd returned exit code $LASTEXITCODE - check SQL output above" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  Could not run Fix-IIS-DbLogin.sql: $_" -ForegroundColor Yellow
        Write-Host "  If login fails after deploy, run scripts\Fix-IIS-DbLogin.sql manually in SSMS" -ForegroundColor Yellow
    }
} else {
    Write-Host "  Fix-IIS-DbLogin.sql not found - skipping DB login check" -ForegroundColor Yellow
}
Write-Host ""

# ── Step 9: Warmup request ────────────────────────────────────────────
Write-Host "Step 9: Warming up API (triggers JIT compilation)..." -ForegroundColor Yellow
Start-Sleep -Seconds 3
try {
    $null = Invoke-WebRequest -Uri "https://$ProdApiHost/swagger/v1/swagger.json" -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
    Write-Host "  API warmed up!" -ForegroundColor Green
} catch {
    Write-Host "  Warmup request failed (app may still be starting): $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "  First manual request will be slow while JIT compiles." -ForegroundColor Yellow
}
Write-Host ""

# ── Done ─────────────────────────────────────────────────────────────
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUCCESS! Deployed to PRODUCTION." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API:     https://$ProdApiHost" -ForegroundColor Green
Write-Host "  Angular: https://$ProdAppHost" -ForegroundColor Green

} finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
    try { Set-Location -Path $PSScriptRoot } catch {}
    Write-Host ("Returned to: {0}" -f (Get-Location)) -ForegroundColor DarkGray
}
