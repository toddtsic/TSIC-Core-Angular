# ============================================================================
# Publish.ps1 - Build and stage to production (TSIC-PHOENIX)
# ============================================================================
# Builds .NET API + Angular (with prod URL patching), deploys to STAGING
# folders on \\204.17.37.202\Websites via SMB share.
#
# After this script completes, RDP to TSIC-PHOENIX and run
# Recycle-After-Deploy.ps1 to stop app pools, swap staging → live, restart.
#
# Usage:
#   .\Publish.ps1                         # Build + stage both
#   .\Publish.ps1 -SkipApi                # Build + stage Angular only
#   .\Publish.ps1 -SkipAngular            # Build + stage API only
# ============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$SkipApi,
    [switch]$SkipAngular
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Configuration (from shared _config.ps1)
# ---------------------------------------------------------------------------
. "$PSScriptRoot\..\_config.ps1" -Environment Prod

$ProdServer      = $Config.ProdServer
$ApiStaging      = $Config.DeployApiStagingPath
$AngularStaging  = $Config.DeployAngularStagingPath
$ApiHostname     = $Config.ApiHostname
$AngularHostname = $Config.AngularHostname
$AspNetEnv       = $Config.AspNetEnv

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
} else {
    Write-Host "Step 2: Skipped (Angular)" -ForegroundColor DarkGray
    Write-Host ""
}

# ── Step 3: Deploy to STAGING folders on TSIC-PHOENIX ────────────────
Write-Host "Step 3: Deploying to STAGING folders..." -ForegroundColor Yellow

if (!$SkipApi) {
    if (!(Test-Path $ApiStaging)) { New-Item -ItemType Directory -Path $ApiStaging -Force | Out-Null }
    Write-Host "  Clearing $ApiStaging..." -ForegroundColor White
    Get-ChildItem $ApiStaging -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item "$ApiPublish\*" $ApiStaging -Recurse -Force

    # Stamp ASPNETCORE_ENVIRONMENT in web.config
    $wcDest = Join-Path $ApiStaging "web.config"
    if (Test-Path $wcDest) {
        $content = Get-Content $wcDest -Raw
        $content = $content -replace '__ASPNET_ENV__', $AspNetEnv
        Set-Content $wcDest $content -NoNewline -Encoding UTF8
        Write-Host "  web.config: ASPNETCORE_ENVIRONMENT = $AspNetEnv" -ForegroundColor White
    }

    # Patch appsettings for production paths and hostnames
    Write-Host "  Patching appsettings for production..." -ForegroundColor Yellow
    $settingsFiles = @(
        (Join-Path $ApiStaging "appsettings.json"),
        (Join-Path $ApiStaging "appsettings.Production.json")
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

    Write-Host "  API staged." -ForegroundColor Green
}

if (!$SkipAngular) {
    if (!(Test-Path $AngularStaging)) { New-Item -ItemType Directory -Path $AngularStaging -Force | Out-Null }
    Write-Host "  Clearing $AngularStaging..." -ForegroundColor White
    Get-ChildItem $AngularStaging -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item "$AngPublish\*" $AngularStaging -Recurse -Force

    # Flatten browser/ subfolder if needed (Angular 17+)
    $angIdx = Join-Path $AngularStaging "index.html"
    $angBrowser = Join-Path $AngularStaging "browser"
    if (!(Test-Path $angIdx) -and (Test-Path (Join-Path $angBrowser "index.html"))) {
        Write-Host "  Flattening Angular 'browser' subfolder..." -ForegroundColor White
        Copy-Item (Join-Path $angBrowser "*") $AngularStaging -Recurse -Force
        Remove-Item $angBrowser -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "  Angular staged." -ForegroundColor Green
}

Write-Host ""

# ── Step 4: Update deployment scripts on TSIC-PHOENIX ────────────────
Write-Host "Step 4: Updating deployment scripts on TSIC-PHOENIX..." -ForegroundColor Yellow
$remoteDeployment = "\\$ProdServer\Websites\IIS-Config-Prod\Deployment"
$remoteConfig     = "\\$ProdServer\Websites\IIS-Config-Prod"
Copy-Item (Join-Path $PSScriptRoot "Recycle-After-Deploy.ps1") "$remoteDeployment\" -Force
Copy-Item (Join-Path $PSScriptRoot "..\_config.ps1") "$remoteConfig\" -Force
Write-Host "  Updated Recycle-After-Deploy.ps1 and _config.ps1" -ForegroundColor Green

# Drop Go.ps1 wrappers in staging folders — each one only deploys its own site
$recycleScript = 'E:\Websites\IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1'

if (!$SkipApi) {
    Set-Content (Join-Path $ApiStaging 'Go.ps1') "& '$recycleScript' -SkipAngular" -Encoding UTF8
    Write-Host "  Go.ps1 -> API staging (API only)" -ForegroundColor Green
}
if (!$SkipAngular) {
    Set-Content (Join-Path $AngularStaging 'Go.ps1') "& '$recycleScript' -SkipApi" -Encoding UTF8
    Write-Host "  Go.ps1 -> Angular staging (Angular only)" -ForegroundColor Green
}

Write-Host ""

# ── Done ─────────────────────────────────────────────────────────────
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUCCESS! Staged to TSIC-PHOENIX." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Staging ready:" -ForegroundColor Green
if (!$SkipApi)     { Write-Host "    API:     $ApiStaging" -ForegroundColor Green }
if (!$SkipAngular) { Write-Host "    Angular: $AngularStaging" -ForegroundColor Green }
Write-Host ""
Write-Host "  NEXT: RDP to TSIC-PHOENIX and run Go.ps1 from each staging folder:" -ForegroundColor Yellow
if (!$SkipApi)     { Write-Host "    $ApiStaging\Go.ps1" -ForegroundColor White }
if (!$SkipAngular) { Write-Host "    $AngularStaging\Go.ps1" -ForegroundColor White }
Write-Host ""

} finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
}
