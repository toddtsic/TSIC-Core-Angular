# ============================================================================
# 02-Build-Angular.ps1 — Build Angular frontend for production
# ============================================================================
# Stamps build version, builds Angular, copies output to publish/angular/,
# copies web.config template, and creates a backup zip.
#
# For Prod: patches environment URLs before build, resets after.
# ============================================================================

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Dev',

    [string]$AngularPath = "$PSScriptRoot\..\..\..\TSIC-Core-Angular\src\frontend\tsic-app",
    [string]$OutputPath = "$PSScriptRoot\..\..\..\publish\angular"
)

. "$PSScriptRoot\..\_config.ps1" -Environment $Environment

# Prod URL patching configuration
$DevApiHost  = 'devapi.teamsportsinfo.com'
$DevAppHost  = 'dev.teamsportsinfo.com'
$ProdApiHost = $Config.ApiHostname
$ProdAppHost = $Config.AngularHostname

Write-Host "Building TSIC Angular Application ($Environment)..." -ForegroundColor Green
Write-Host "Angular Path: $AngularPath" -ForegroundColor Yellow
Write-Host "Output Path: $OutputPath" -ForegroundColor Yellow

if (!(Test-Path $AngularPath)) {
    Write-Error "Angular project not found at: $AngularPath"
    exit 1
}

Push-Location $AngularPath

try {
    # Install dependencies if needed
    if (!(Test-Path "node_modules")) {
        Write-Host "Installing npm dependencies..." -ForegroundColor Cyan
        npm install
        if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed!"; exit 1 }
    }

    # Stamp build version
    Write-Host "Stamping build version..." -ForegroundColor Cyan
    try { $gitHash = (git rev-parse --short HEAD 2>$null) } catch { $gitHash = "unknown" }
    if (-not $gitHash) { $gitHash = "unknown" }
    $buildStamp = "v$(Get-Date -Format 'yyMMdd.HHmm').$gitHash"
    Write-Host "  Build version: $buildStamp" -ForegroundColor White

    $envDir = Join-Path $AngularPath "src\environments"
    Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: '$buildStamp'"
        Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
        Write-Host "  Stamped: $($_.Name)" -ForegroundColor DarkGray
    }

    # Patch environment URLs for Prod
    if ($Environment -eq 'Prod') {
        Write-Host "Patching environment URLs for production..." -ForegroundColor Yellow
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
    }

    # Build Angular
    Write-Host "Building Angular application for production..." -ForegroundColor Cyan
    npm run build -- --configuration production
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Angular build failed!"
        exit 1
    }
}
finally {
    # Always reset environment files back to dev
    $envDir = Join-Path $AngularPath "src\environments"
    Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: 'dev'"
        if ($Environment -eq 'Prod') {
            $content = $content -replace [regex]::Escape($ProdApiHost), $DevApiHost `
                                 -replace [regex]::Escape($ProdAppHost), $DevAppHost
        }
        Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
    }
    Write-Host "  Environment files reset to dev." -ForegroundColor DarkGray

    Pop-Location
}

# Ensure output directory exists and clean it
if (!(Test-Path $OutputPath)) { New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null }
Get-ChildItem -Path $OutputPath -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

# Copy build output
Write-Host "Copying build output..." -ForegroundColor Cyan
$DistPath = Join-Path $AngularPath "dist\tsic-app"
if (Test-Path (Join-Path $DistPath "browser")) { $DistPath = Join-Path $DistPath "browser" }
if (!(Test-Path $DistPath)) { Write-Error "Build output not found at: $DistPath"; exit 1 }

Copy-Item "$DistPath\*" $OutputPath -Recurse -Force

# Copy web.config template
$webConfigSrc = Join-Path $PSScriptRoot "..\web.config.angular"
if (Test-Path $webConfigSrc) {
    $dest = Join-Path $OutputPath 'web.config'
    Copy-Item $webConfigSrc $dest -Force
    try {
        $raw = Get-Content $dest -Raw
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success -and $m.Value.Length -ne $raw.Length) {
            Set-Content -Path $dest -Value $m.Value -Encoding UTF8
        }
        [void]([xml](Get-Content $dest -Raw))
        Write-Host "  web.config.angular applied and validated." -ForegroundColor Green
    } catch {
        Write-Warning "Angular web.config validation note: $($_.Exception.Message)"
    }
}

Write-Host "Angular build completed successfully!" -ForegroundColor Green

# Backup to zip
if (Test-Path $OutputPath) {
    $fileCount = (Get-ChildItem $OutputPath -Recurse -File).Count
    if ($fileCount -gt 0) {
        Write-Host "Creating backup archive..." -ForegroundColor Cyan
        $backupDir = "C:\Users\Administrator\Documents\Backups\DOTNETPublishedOutputs"
        $zipPath = Join-Path $backupDir "TSIC-app.zip"
        if (!(Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

        $tempStaging = Join-Path $env:TEMP "TSIC-App-Staging-$(Get-Date -Format 'yyyyMMddHHmmss')"
        $projectFolder = Join-Path $tempStaging "TSIC.App"
        New-Item -ItemType Directory -Path $projectFolder -Force | Out-Null
        Copy-Item -Path "$OutputPath\*" -Destination $projectFolder -Recurse -Force
        Compress-Archive -Path "$tempStaging\*" -DestinationPath $zipPath -CompressionLevel Optimal
        Remove-Item $tempStaging -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Backup archive created: $zipPath" -ForegroundColor Green
    }
}
