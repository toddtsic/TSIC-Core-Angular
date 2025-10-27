# All-in-one build and package script for TSIC deployment
# This script combines: Build-DotNet-API.ps1, Build-Angular.ps1, and Create-iDrive-Package-simple.ps1
# Run from the scripts directory

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TSIC Build and Package - All-in-One" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Begin transcript logging so we can capture all warnings/errors to a file
${origDir} = Get-Location
$transcriptStarted = $false
try {
    $logDir = Join-Path $PSScriptRoot "..\publish\build-logs"
    if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $logPath = Join-Path $logDir ("build-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
    Start-Transcript -Path $logPath -Append | Out-Null
    Write-Host ("Logging to: {0}" -f $logPath) -ForegroundColor DarkGray
    $transcriptStarted = $true
} catch {
    Write-Host "Transcript could not be started; continuing without file logging." -ForegroundColor Yellow
}

try {

# Step 1: Build .NET API
Write-Host "Step 1: Building .NET API..." -ForegroundColor Yellow
$scriptPath = Join-Path $PSScriptRoot "Build-DotNet-API.ps1"
& $scriptPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "API build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "API build complete!" -ForegroundColor Green
Write-Host ""

# Step 2: Build Angular
Write-Host "Step 2: Building Angular..." -ForegroundColor Yellow
$scriptPath = Join-Path $PSScriptRoot "Build-Angular.ps1"
& $scriptPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "Angular build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Angular build complete!" -ForegroundColor Green
Write-Host ""

# Step 3: Create iDrive Package
Write-Host "Step 3: Creating iDrive deployment package..." -ForegroundColor Yellow
$scriptPath = Join-Path $PSScriptRoot "Create-iDrive-Package-simple.ps1"
& $scriptPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "Package creation failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Package creation complete!" -ForegroundColor Green
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUCCESS! Deployment package ready." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Backup 'tsic-deployment-current' folder to iDrive" -ForegroundColor White
Write-Host "2. Restore on server 10.0.0.45" -ForegroundColor White
Write-Host "3. Run deploy-to-server.ps1 on the server" -ForegroundColor White

} finally {
    # End transcript logging and return to scripts directory for convenience
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
    try { Set-Location -Path $PSScriptRoot } catch {}
    Write-Host ("Returned to: {0}" -f (Get-Location)) -ForegroundColor DarkGray
}
