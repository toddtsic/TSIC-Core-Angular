# ============================================================================
# Publish.ps1 - Build and deploy to local IIS (Dev / TSIC-SEDONA)
# ============================================================================
# Single script: builds .NET API + Angular, deploys to C:\Websites,
# restarts IIS, fixes DB login, warms up API.
#
# Usage:
#   .\Publish.ps1
# ============================================================================

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Configuration (Dev - all local, C:\ drive)
# ---------------------------------------------------------------------------
$ApiPoolName     = 'claude-api'
$AngularPoolName = 'claude-app'
$ApiSiteName     = 'claude-api'
$AngularSiteName = 'claude-app'
$ApiTarget       = 'C:\Websites\claude-api'
$AngularTarget   = 'C:\Websites\claude-app'
$BackupsPath     = 'C:\Websites\Backups'
$SqlInstance     = '.\SS2016'
$ApiHostname     = 'devapi.teamsportsinfo.com'
$AngularHostname = 'dev.teamsportsinfo.com'
$AspNetEnv       = 'Development'

$RepoRoot    = (Resolve-Path "$PSScriptRoot\..\..").Path
$SolutionDir = Join-Path $RepoRoot "TSIC-Core-Angular"
$ProjectPath = Join-Path $SolutionDir "src\backend\TSIC.API\TSIC.API.csproj"
$AngularPath = Join-Path $SolutionDir "src\frontend\tsic-app"
$PublishRoot = Join-Path $RepoRoot "publish"
$ApiPublish  = Join-Path $PublishRoot "api"
$AngPublish  = Join-Path $PublishRoot "angular"
$WebConfigApiSrc = Join-Path $PSScriptRoot "..\web.config.api"
$WebConfigAngSrc = Join-Path $PSScriptRoot "..\web.config.angular"
$FixLoginSql     = Join-Path $PSScriptRoot "Fix-IIS-DbLogin.sql"

$PreservedDirs = @('logs', 'keys')

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TSIC Publish - Dev (TSIC-SEDONA)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API target:     $ApiTarget" -ForegroundColor Yellow
Write-Host "  Angular target: $AngularTarget" -ForegroundColor Yellow
Write-Host "  Hostnames:      $ApiHostname / $AngularHostname" -ForegroundColor Yellow
Write-Host ""

# Transcript logging
$transcriptStarted = $false
try {
    $logDir = Join-Path $PublishRoot "build-logs"
    if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $logPath = Join-Path $logDir ("publish-dev-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
    Start-Transcript -Path $logPath -Append | Out-Null
    Write-Host ("Logging to: {0}" -f $logPath) -ForegroundColor DarkGray
    $transcriptStarted = $true
} catch {
    Write-Host "Transcript could not be started; continuing without file logging." -ForegroundColor Yellow
}

try {

# ── Step 1: Build .NET API ──────────────────────────────────────────
Write-Host "Step 1: Building .NET API..." -ForegroundColor Yellow

if (!(Test-Path $ApiPublish)) { New-Item -ItemType Directory -Path $ApiPublish -Force | Out-Null }
Get-ChildItem -Path $ApiPublish -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

Push-Location $SolutionDir
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) { Write-Error "NuGet restore failed!"; exit 1 }

    dotnet build --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed!"; exit 1 }

    dotnet publish $ProjectPath --configuration Release --output $ApiPublish
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

# ── Step 2: Build Angular ───────────────────────────────────────────
Write-Host "Step 2: Building Angular..." -ForegroundColor Yellow

Push-Location $AngularPath
try {
    if (!(Test-Path "node_modules")) {
        Write-Host "  Installing npm dependencies..." -ForegroundColor Cyan
        npm install
        if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed!"; exit 1 }
    }

    # Stamp build version
    try { $gitHash = (git rev-parse --short HEAD 2>$null) } catch { $gitHash = "unknown" }
    if (-not $gitHash) { $gitHash = "unknown" }
    $buildStamp = "v$(Get-Date -Format 'yyMMdd.HHmm').$gitHash"
    Write-Host "  Build version: $buildStamp" -ForegroundColor White

    $envDir = Join-Path $AngularPath "src\environments"
    Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: '$buildStamp'"
        Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
    }

    npm run build -- --configuration production
    if ($LASTEXITCODE -ne 0) { Write-Error "Angular build failed!"; exit 1 }
} finally {
    # Always reset environment files
    $envDir = Join-Path $AngularPath "src\environments"
    Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: 'dev'"
        Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
    }
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

# ── Step 3: Stop IIS sites ──────────────────────────────────────────
Write-Host "Step 3: Stopping IIS sites..." -ForegroundColor Yellow
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (Get-Module WebAdministration) {
    try {
        foreach ($site in @($ApiSiteName, $AngularSiteName)) {
            if (Get-Website -Name $site -ErrorAction SilentlyContinue) {
                Stop-Website -Name $site
                Write-Host "  Stopped website: $site" -ForegroundColor White
            }
        }
        foreach ($pool in @($ApiPoolName, $AngularPoolName)) {
            if (Get-WebAppPoolState -Name $pool -ErrorAction SilentlyContinue) {
                Stop-WebAppPool -Name $pool -ErrorAction SilentlyContinue
                Write-Host "  Stopped app pool: $pool" -ForegroundColor White
            }
        }
    } catch {
        Write-Host "  Could not stop sites/pools: $_" -ForegroundColor Yellow
    }
    Start-Sleep -Seconds 2
} else {
    Write-Host "  WebAdministration module not available - stop sites manually" -ForegroundColor Yellow
}
Write-Host ""

# ── Step 4: Rollback backup ─────────────────────────────────────────
Write-Host "Step 4: Creating rollback backup..." -ForegroundColor Yellow
if (!(Test-Path $BackupsPath)) { New-Item -ItemType Directory -Path $BackupsPath -Force | Out-Null }
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

if ((Test-Path $ApiTarget) -and (Get-ChildItem $ApiTarget -ErrorAction SilentlyContinue)) {
    $apiBackup = Join-Path $BackupsPath "claude-api-$Timestamp"
    New-Item -ItemType Directory -Path $apiBackup -Force | Out-Null
    Get-ChildItem $ApiTarget -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin $PreservedDirs } |
        ForEach-Object { Copy-Item $_.FullName $apiBackup -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host "  API backed up to: $apiBackup" -ForegroundColor Green
}

if ((Test-Path $AngularTarget) -and (Get-ChildItem $AngularTarget -ErrorAction SilentlyContinue)) {
    $angBackup = Join-Path $BackupsPath "claude-app-$Timestamp"
    New-Item -ItemType Directory -Path $angBackup -Force | Out-Null
    Get-ChildItem $AngularTarget -Force -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName $angBackup -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host "  Angular backed up to: $angBackup" -ForegroundColor Green
}

# Prune old backups (keep 3 most recent per site)
foreach ($prefix in @('claude-api-', 'claude-app-')) {
    $old = Get-ChildItem $BackupsPath -Directory -Filter "$prefix*" -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -Skip 3
    foreach ($dir in $old) {
        Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Pruned old backup: $($dir.Name)" -ForegroundColor DarkGray
    }
}
Write-Host ""

# ── Step 5: Deploy files ────────────────────────────────────────────
Write-Host "Step 5: Deploying files..." -ForegroundColor Yellow

# API
Write-Host "  Clearing $ApiTarget (preserving logs/, keys/, FirebaseAuth_*)..." -ForegroundColor White
Get-ChildItem $ApiTarget -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notin $PreservedDirs -and $_.Name -notlike 'FirebaseAuth_*.json' } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$ApiPublish\*" $ApiTarget -Recurse -Force

# Stamp ASPNETCORE_ENVIRONMENT in web.config
$wcDest = Join-Path $ApiTarget "web.config"
if (Test-Path $wcDest) {
    $content = Get-Content $wcDest -Raw
    $content = $content -replace '__ASPNET_ENV__', $AspNetEnv
    Set-Content $wcDest $content -NoNewline -Encoding UTF8
    Write-Host "  web.config: ASPNETCORE_ENVIRONMENT = $AspNetEnv" -ForegroundColor White
}

# Angular
Write-Host "  Clearing $AngularTarget..." -ForegroundColor White
Get-ChildItem $AngularTarget -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$AngPublish\*" $AngularTarget -Recurse -Force

# Flatten browser/ subfolder if needed (Angular 17+)
$angIdx = Join-Path $AngularTarget "index.html"
$angBrowser = Join-Path $AngularTarget "browser"
if (!(Test-Path $angIdx) -and (Test-Path (Join-Path $angBrowser "index.html"))) {
    Write-Host "  Flattening Angular 'browser' subfolder..." -ForegroundColor White
    Copy-Item (Join-Path $angBrowser "*") $AngularTarget -Recurse -Force
    Remove-Item $angBrowser -Recurse -Force -ErrorAction SilentlyContinue
}

# Angular web.config (already in publish output from build step)
Write-Host "  Files deployed!" -ForegroundColor Green
Write-Host ""

# ── Step 6: Start IIS sites ─────────────────────────────────────────
Write-Host "Step 6: Starting IIS sites..." -ForegroundColor Yellow
if (Get-Module WebAdministration) {
    try {
        foreach ($pool in @($ApiPoolName, $AngularPoolName)) {
            if (Get-WebAppPoolState -Name $pool -ErrorAction SilentlyContinue) {
                Start-WebAppPool -Name $pool -ErrorAction SilentlyContinue
                Write-Host "  Started app pool: $pool" -ForegroundColor White
            }
        }
        foreach ($site in @($ApiSiteName, $AngularSiteName)) {
            if (Get-Website -Name $site -ErrorAction SilentlyContinue) {
                Start-Website -Name $site
                Write-Host "  Started website: $site" -ForegroundColor White
            }
        }
    } catch {
        Write-Host "  Could not start sites/pools: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "  WebAdministration module not available - start sites manually" -ForegroundColor Yellow
}
Write-Host ""

# ── Step 7: Fix DB login ────────────────────────────────────────────
Write-Host "Step 7: Ensuring IIS app pool DB login..." -ForegroundColor Yellow
if (Test-Path $FixLoginSql) {
    try {
        sqlcmd -S $SqlInstance -E -i $FixLoginSql -b
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  DB login verified for IIS APPPOOL\$ApiPoolName" -ForegroundColor Green
        } else {
            Write-Host "  sqlcmd returned exit code $LASTEXITCODE - check SQL output above" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  Could not run Fix-IIS-DbLogin.sql: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "  Fix-IIS-DbLogin.sql not found - skipping" -ForegroundColor Yellow
}
Write-Host ""

# ── Step 8: Warmup ──────────────────────────────────────────────────
Write-Host "Step 8: Warming up API..." -ForegroundColor Yellow
Start-Sleep -Seconds 3
try {
    $null = Invoke-WebRequest -Uri "https://$ApiHostname/api/jobs/tsic" -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
    Write-Host "  API warmed up!" -ForegroundColor Green
} catch {
    Write-Host "  Warmup failed (app may still be starting): $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# ── Done ─────────────────────────────────────────────────────────────
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUCCESS! Published to Dev." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API:     https://$ApiHostname" -ForegroundColor Green
Write-Host "  Angular: https://$AngularHostname" -ForegroundColor Green

} finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
}
