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
npm run build --prod
if ($LASTEXITCODE -ne 0) {
    Write-Error "Angular build failed!"
    exit 1
}

# Ensure output directory exists
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Copy build output to publish directory
Write-Host "Copying build output..." -ForegroundColor Cyan
$DistPath = Join-Path $AngularPath "dist\tsic-app"
if (!(Test-Path $DistPath)) {
    Write-Error "Build output not found at: $DistPath"
    exit 1
}

# Clean output directory
Get-ChildItem -Path $OutputPath -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

# Copy files
Copy-Item "$DistPath\*" $OutputPath -Recurse -Force

Write-Host "Angular build completed successfully!" -ForegroundColor Green
Write-Host "Output location: $OutputPath" -ForegroundColor Green

# List published files
Write-Host "Published files:" -ForegroundColor Cyan
Get-ChildItem $OutputPath -Name | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
if ((Get-ChildItem $OutputPath).Count -gt 10) {
    Write-Host "  ... and $((Get-ChildItem $OutputPath).Count - 10) more files" -ForegroundColor Gray
}