# Build Angular Application Script
# Run this from the scripts directory

param(
    [string]$AngularPath = "$PSScriptRoot\..\TSIC-Core-Angular\src\frontend\tsic-app",
    [string]$OutputPath = "$PSScriptRoot\..\publish\angular"
)

Write-Host "Building TSIC Angular Application..." -ForegroundColor Green
Write-Host "Angular Path: $AngularPath" -ForegroundColor Yellow
Write-Host "Output Path: $OutputPath" -ForegroundColor Yellow

# Check if Angular project exists
if (!(Test-Path $AngularPath)) {
    Write-Error "Angular project not found at: $AngularPath"
    exit 1
}

# Navigate to Angular project
cd $AngularPath

# Check if node_modules exists, install if needed
if (!(Test-Path "node_modules")) {
    Write-Host "Installing npm dependencies..." -ForegroundColor Cyan
    npm install
    if ($LASTEXITCODE -ne 0) {
        Write-Error "npm install failed!"
        exit 1
    }
}

# Build Angular application
Write-Host "Building Angular application for production..." -ForegroundColor Cyan
npm run build -- --configuration production
if ($LASTEXITCODE -ne 0) {
    Write-Error "Angular build failed!"
    exit 1
}

# Ensure output directory exists
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Clean output directory before building
Write-Host "Cleaning old build output..." -ForegroundColor Cyan
Get-ChildItem -Path $OutputPath -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

# Copy build output to publish directory
Write-Host "Copying build output..." -ForegroundColor Cyan
$DistPath = Join-Path $AngularPath "dist\tsic-app"
# Angular 17+ may place browser assets under a nested 'browser' folder
if (Test-Path (Join-Path $DistPath "browser")) {
    $DistPath = Join-Path $DistPath "browser"
}
if (!(Test-Path $DistPath)) {
    Write-Error "Build output not found at: $DistPath"
    exit 1
}

# Clean output directory
Get-ChildItem -Path $OutputPath -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

# Copy files (ensure index.html lands at root of publish\angular for IIS default document)
Copy-Item "$DistPath\*" $OutputPath -Recurse -Force

Write-Host "Angular build completed successfully!" -ForegroundColor Green
Write-Host "Output location: $OutputPath" -ForegroundColor Green

# List published files
Write-Host "Published files:" -ForegroundColor Cyan
Get-ChildItem $OutputPath -Name | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
if ((Get-ChildItem $OutputPath).Count -gt 10) {
    Write-Host "  ... and $((Get-ChildItem $OutputPath).Count - 10) more files" -ForegroundColor Gray
}

# Place cleaned web.config for Angular in publish output as well (useful for manual deployment)
try {
    $scriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $webConfigSrc = Join-Path $scriptsDir 'web.config.angular'
    if (Test-Path $webConfigSrc) {
        $dest = Join-Path $OutputPath 'web.config'
        Copy-Item $webConfigSrc $dest -Force
        $raw = Get-Content $dest -Raw
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success -and $m.Value.Length -ne $raw.Length) {
            Set-Content -Path $dest -Value $m.Value -Encoding UTF8
        }
        [void]([xml](Get-Content $dest -Raw))
    }
} catch {
    Write-Warning ("Angular web.config validation note: {0}" -f $_.Exception.Message)
}

# Backup published output to zip (only if build was successful)
if (Test-Path $OutputPath) {
    $fileCount = (Get-ChildItem $OutputPath -Recurse -File).Count
    if ($fileCount -gt 0) {
        Write-Host "Creating backup archive..." -ForegroundColor Cyan
        $backupDir = "C:\Users\Administrator\Documents\Backups\DOTNETPublishedOutputs"
        $zipPath = Join-Path $backupDir "TSIC-app.zip"

        # Ensure backup directory exists
        if (!(Test-Path $backupDir)) {
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        }

        # Remove old zip if it exists
        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }

        # Create zip archive
        Compress-Archive -Path "$OutputPath\*" -DestinationPath $zipPath -CompressionLevel Optimal
        if ($?) {
            Write-Host "Backup archive created: $zipPath" -ForegroundColor Green
        } else {
            Write-Warning "Failed to create backup archive"
        }
    } else {
        Write-Warning "No files found in output directory, skipping backup archive"
    }
} else {
    Write-Warning "Output path does not exist, skipping backup archive"
}