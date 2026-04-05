# ============================================================================
# Publish.ps1 - Build and deploy to production IIS (TSIC-PHOENIX)
# ============================================================================
# Single script: builds .NET API + Angular (with prod URL patching),
# deploys to \\204.17.37.202\Websites via SMB share, creates rollback backup.
#
# IIS on TSIC-PHOENIX must be recycled manually after deploy.
#
# Usage:
#   .\Publish.ps1
# ============================================================================

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Configuration (Prod - deploys to TSIC-PHOENIX via SMB share)
# ---------------------------------------------------------------------------
$ApiPoolName     = 'claude-api'
$AngularPoolName = 'claude-app'
$ApiSiteName     = 'claude-api'
$AngularSiteName = 'claude-app'
$ProdServer      = '204.17.37.202'
$ApiTarget       = "\\$ProdServer\Websites\claude-api"
$AngularTarget   = "\\$ProdServer\Websites\claude-app"
$BackupsPath     = "\\$ProdServer\Websites\Backups"
$ApiHostname     = 'claude-api.teamsportsinfo.com'
$AngularHostname = 'claude-app.teamsportsinfo.com'
$AspNetEnv       = 'Production'

# URL patching: Angular environment files have dev URLs baked in at build time
$DevApiHost  = 'devapi.teamsportsinfo.com'
$DevAppHost  = 'dev.teamsportsinfo.com'

$RepoRoot    = (Resolve-Path "$PSScriptRoot\..\..\..").Path
$SolutionDir = Join-Path $RepoRoot "TSIC-Core-Angular"
$ProjectPath = Join-Path $SolutionDir "src\backend\TSIC.API\TSIC.API.csproj"
$AngularPath = Join-Path $SolutionDir "src\frontend\tsic-app"
$PublishRoot = Join-Path $RepoRoot "publish"
$ApiPublish  = Join-Path $PublishRoot "api"
$AngPublish  = Join-Path $PublishRoot "angular"
$WebConfigApiSrc = Join-Path $PSScriptRoot "..\web.config.api"
$WebConfigAngSrc = Join-Path $PSScriptRoot "..\web.config.angular"

$PreservedDirs = @('logs', 'keys')

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TSIC Publish - PRODUCTION (TSIC-PHOENIX)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API target:     $ApiTarget" -ForegroundColor Yellow
Write-Host "  Angular target: $AngularTarget" -ForegroundColor Yellow
Write-Host "  Hostnames:      $ApiHostname / $AngularHostname" -ForegroundColor Yellow
Write-Host ""

# ── Safety gate ──────────────────────────────────────────────────────
Write-Host "  *** DEPLOYING TO PRODUCTION ***" -ForegroundColor Red
Write-Host ""
$confirm = Read-Host "  Type 'DEPLOY' to continue, anything else to abort"
if ($confirm -ne 'DEPLOY') {
    Write-Host "  Aborted." -ForegroundColor Yellow
    exit 0
}
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

# ── Step 2: Build Angular (with prod URL patching) ──────────────────
Write-Host "Step 2: Building Angular (prod URLs)..." -ForegroundColor Yellow

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

    # Patch environment URLs: dev -> prod
    Write-Host "  Patching environment URLs for production..." -ForegroundColor Yellow
    $envFiles = Get-ChildItem $envDir -Filter "environment*.ts"
    foreach ($file in $envFiles) {
        $content = Get-Content $file.FullName -Raw
        $patched = $content -replace [regex]::Escape($DevApiHost), $ApiHostname `
                             -replace [regex]::Escape($DevAppHost), $AngularHostname
        if ($patched -ne $content) {
            Set-Content $file.FullName $patched -NoNewline -Encoding UTF8
            Write-Host "    Patched: $($file.Name)" -ForegroundColor White
        }
    }

    $env:NO_COLOR = '1'
    npm run build -- --configuration production
    if ($LASTEXITCODE -ne 0) { Write-Error "Angular build failed!"; exit 1 }
} finally {
    # Always reset environment files back to dev
    $envDir = Join-Path $AngularPath "src\environments"
    Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: 'dev'"
        $content = $content -replace [regex]::Escape($ApiHostname), $DevApiHost `
                             -replace [regex]::Escape($AngularHostname), $DevAppHost
        Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
    }
    Write-Host "  Environment files reset to dev." -ForegroundColor DarkGray
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

# ── Step 3: Rollback backup ─────────────────────────────────────────
Write-Host "Step 3: Creating rollback backup on TSIC-PHOENIX..." -ForegroundColor Yellow
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

# ── Step 4: Deploy files to TSIC-PHOENIX ─────────────────────────────
Write-Host "Step 4: Deploying files to TSIC-PHOENIX..." -ForegroundColor Yellow

# API
if (!(Test-Path $ApiTarget)) { New-Item -ItemType Directory -Path $ApiTarget -Force | Out-Null }
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

# Patch appsettings for production paths and hostnames
Write-Host "  Patching appsettings for production..." -ForegroundColor Yellow
$settingsFiles = @(
    (Join-Path $ApiTarget "appsettings.json"),
    (Join-Path $ApiTarget "appsettings.Production.json")
)
foreach ($file in $settingsFiles) {
    if (Test-Path $file) {
        $content = Get-Content $file -Raw
        $original = $content
        $content = $content -replace 'C:\\\\Websites', 'E:\\Websites' -replace 'C:\\Websites', 'E:\Websites'
        $content = $content -replace [regex]::Escape($DevApiHost), $ApiHostname
        $content = $content -replace [regex]::Escape($DevAppHost), $AngularHostname
        if ($content -ne $original) {
            Set-Content $file $content -NoNewline
            Write-Host "    Patched: $(Split-Path $file -Leaf)" -ForegroundColor White
        }
    }
}

# Angular
if (!(Test-Path $AngularTarget)) { New-Item -ItemType Directory -Path $AngularTarget -Force | Out-Null }
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

Write-Host "  Files deployed!" -ForegroundColor Green
Write-Host ""

# ── Done ─────────────────────────────────────────────────────────────
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUCCESS! Published to TSIC-PHOENIX." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API:     https://$ApiHostname" -ForegroundColor Green
Write-Host "  Angular: https://$AngularHostname" -ForegroundColor Green
Write-Host ""
Write-Host "  IMPORTANT: RDP to TSIC-PHOENIX and run:" -ForegroundColor Red
Write-Host "    E:\Websites\IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1" -ForegroundColor White
Write-Host ""

} finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
}
