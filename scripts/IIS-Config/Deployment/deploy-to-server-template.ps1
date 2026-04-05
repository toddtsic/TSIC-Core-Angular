# ============================================================================
# deploy-to-server-template.ps1 — Run ON the target server after file copy
# ============================================================================
# This script is meant to be run on the IIS server after copying the build
# outputs (api/ and angular/ directories) to the server.
#
# It stops IIS, deploys files, validates dependencies, starts IIS,
# fixes DB login, and restarts app pools.
#
# Usage: Copy this script alongside the api/ and angular/ directories,
#        then run on the server:
#   .\deploy-to-server-template.ps1
#   .\deploy-to-server-template.ps1 -DriveOverride "D"
# ============================================================================

param(
    [string]$DriveOverride = ""
)

# Prompt for drive letter if not provided
if ([string]::IsNullOrEmpty($DriveOverride)) {
    $DriveLetter = Read-Host "What drive will you be publishing to [C, D, E]"
    $DriveLetter = $DriveLetter.ToUpper()
} else {
    $DriveLetter = $DriveOverride.ToUpper()
}

if ($DriveLetter -notin @('C', 'D', 'E')) {
    Write-Error "Invalid drive letter. Please enter C, D, or E."
    exit 1
}

# Unified names (same everywhere)
$ApiSiteName     = "TSIC.Api"
$AngularSiteName = "TSIC.App"
$ApiPoolName     = "TSIC.Api"
$AngularPoolName = "TSIC.App"
$ApiTarget       = "${DriveLetter}:\Websites\$ApiSiteName"
$AngularTarget   = "${DriveLetter}:\Websites\$AngularSiteName"

Write-Host "=== TSIC Server Deployment ===" -ForegroundColor Green
Write-Host "API target:     $ApiTarget" -ForegroundColor Yellow
Write-Host "Angular target: $AngularTarget" -ForegroundColor Yellow
Write-Host ""

# Stop IIS
Write-Host "Stopping IIS websites..." -ForegroundColor Cyan
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
        Write-Host "  Could not stop websites/app pools: $_" -ForegroundColor Yellow
    }
    Start-Sleep -Seconds 2
}

# Create directories
if (!(Test-Path $ApiTarget)) { New-Item -ItemType Directory -Path $ApiTarget -Force | Out-Null }
if (!(Test-Path $AngularTarget)) { New-Item -ItemType Directory -Path $AngularTarget -Force | Out-Null }

# Backup existing
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$BackupRoot = "${DriveLetter}:\AngularWebsiteBackups"
if (!(Test-Path $BackupRoot)) { New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null }

$ApiPreservedDirs = @('logs', 'keys')

if ((Test-Path $ApiTarget) -and (Get-ChildItem $ApiTarget -ErrorAction SilentlyContinue)) {
    Write-Host "Backing up existing API deployment..." -ForegroundColor Cyan
    $ApiBackup = Join-Path $BackupRoot "$ApiSiteName-$Timestamp"
    New-Item -ItemType Directory -Path $ApiBackup -Force | Out-Null
    Get-ChildItem $ApiTarget -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin $ApiPreservedDirs } |
        ForEach-Object { Copy-Item $_.FullName $ApiBackup -Recurse -Force -ErrorAction SilentlyContinue }
    Get-ChildItem $ApiTarget -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin $ApiPreservedDirs } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  Backed up to: $ApiBackup" -ForegroundColor Green
}

if ((Test-Path $AngularTarget) -and (Get-ChildItem $AngularTarget -ErrorAction SilentlyContinue)) {
    Write-Host "Backing up existing Angular deployment..." -ForegroundColor Cyan
    $AngularBackup = Join-Path $BackupRoot "$AngularSiteName-$Timestamp"
    Move-Item $AngularTarget $AngularBackup -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $AngularTarget -Force | Out-Null
    Write-Host "  Backed up to: $AngularBackup" -ForegroundColor Green
}

# Deploy
Write-Host "Deploying .NET API..." -ForegroundColor Cyan
Copy-Item ".\api\*" $ApiTarget -Recurse -Force

Write-Host "Deploying Angular frontend..." -ForegroundColor Cyan
Copy-Item ".\angular\*" $AngularTarget -Recurse -Force

# Flatten browser/ subfolder if needed (Angular 17+)
$AngularIndex = Join-Path $AngularTarget "index.html"
$AngularBrowser = Join-Path $AngularTarget "browser"
if (!(Test-Path $AngularIndex) -and (Test-Path (Join-Path $AngularBrowser "index.html"))) {
    Write-Host "Flattening Angular 'browser' subfolder..." -ForegroundColor Yellow
    Copy-Item (Join-Path $AngularBrowser "*") $AngularTarget -Recurse -Force
    Remove-Item $AngularBrowser -Recurse -Force -ErrorAction SilentlyContinue
}

# Stamp ASPNETCORE_ENVIRONMENT in web.config
$webConfigPath = Join-Path $ApiTarget "web.config"
if (Test-Path $webConfigPath) {
    $content = Get-Content $webConfigPath -Raw
    if ($content -match '__ASPNET_ENV__') {
        $aspNetEnv = Read-Host "ASPNETCORE_ENVIRONMENT [Development/Production]"
        $content = $content -replace '__ASPNET_ENV__', $aspNetEnv
        Set-Content $webConfigPath $content -NoNewline -Encoding UTF8
        Write-Host "  web.config: ASPNETCORE_ENVIRONMENT = $aspNetEnv" -ForegroundColor Green
    }
}

# Validate SqlClient
Write-Host "Validating API dependencies..." -ForegroundColor Cyan
$SqlClientPath = Join-Path $ApiTarget "Microsoft.Data.SqlClient.dll"
if (Test-Path $SqlClientPath) {
    $ver = (Get-Item $SqlClientPath).VersionInfo
    Write-Host "  Microsoft.Data.SqlClient.dll: $($ver.ProductVersion)" -ForegroundColor White
} else {
    Write-Host "  WARNING: Microsoft.Data.SqlClient.dll not found" -ForegroundColor Yellow
}

# Start IIS
Write-Host "Starting IIS websites..." -ForegroundColor Cyan
if (Get-Module WebAdministration) {
    try {
        foreach ($pool in @($ApiPoolName, $AngularPoolName)) {
            if (Get-WebAppPoolState -Name $pool -ErrorAction SilentlyContinue) {
                Start-WebAppPool -Name $pool -ErrorAction SilentlyContinue
            }
        }
        foreach ($site in @($ApiSiteName, $AngularSiteName)) {
            if (Get-Website -Name $site -ErrorAction SilentlyContinue) {
                Start-Website -Name $site
            }
        }
        Write-Host "  IIS started." -ForegroundColor Green
    } catch {
        Write-Host "  Could not start IIS: $_" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Deployment completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Magenta
Write-Host "1. Verify API: https://<hostname>/swagger" -ForegroundColor White
Write-Host "2. Verify Angular: https://<hostname>" -ForegroundColor White
Write-Host "3. If DB was restored, run Fix-IIS-DbLogin.sql in SSMS" -ForegroundColor White
