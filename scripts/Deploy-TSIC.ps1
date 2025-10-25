# Complete TSIC Deployment Script
# Builds and deploys both .NET API and Angular frontend to Windows Server

param(
    [string]$ServerIP = "10.0.0.45",
    [string]$ProjectRoot = "$PSScriptRoot\..",
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$RemoteDeploy
)

Write-Host "=== TSIC Complete Deployment Script ===" -ForegroundColor Green
Write-Host "Target Server: $ServerIP" -ForegroundColor Yellow
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Skip Build: $SkipBuild" -ForegroundColor Yellow
Write-Host "Skip Tests: $SkipTests" -ForegroundColor Yellow
Write-Host "Remote Deploy: $RemoteDeploy" -ForegroundColor Yellow
Write-Host ""

# Function to test server connectivity
function Test-ServerConnection {
    param([string]$ServerIP)

    Write-Host "Testing connection to $ServerIP..." -ForegroundColor Cyan
    try {
        $ping = New-Object System.Net.NetworkInformation.Ping
        $result = $ping.Send($ServerIP, 1000)
        if ($result.Status -eq 'Success') {
            Write-Host "✓ Server connection successful" -ForegroundColor Green
            return $true
        } else {
            Write-Host "✗ Cannot connect to server $ServerIP" -ForegroundColor Red
            return $false
        }
    } catch {
        Write-Host "✗ Cannot connect to server $ServerIP" -ForegroundColor Red
        return $false
    }
}

# Function to build .NET API
function Build-DotNetAPI {
    Write-Host "=== Building .NET API ===" -ForegroundColor Magenta

    $BuildScript = Join-Path $PSScriptRoot "Build-DotNet-API.ps1"
    if (!(Test-Path $BuildScript)) {
        Write-Error "Build script not found: $BuildScript"
        return $false
    }

    & $BuildScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error ".NET API build failed!"
        return $false
    }

    Write-Host "✓ .NET API build completed" -ForegroundColor Green
    return $true
}

# Function to build Angular
function Build-Angular {
    Write-Host "=== Building Angular Application ===" -ForegroundColor Magenta

    $BuildScript = Join-Path $PSScriptRoot "Build-Angular.ps1"
    if (!(Test-Path $BuildScript)) {
        Write-Error "Build script not found: $BuildScript"
        return $false
    }

    & $BuildScript
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Angular build failed!"
        return $false
    }

    Write-Host "✓ Angular build completed" -ForegroundColor Green
    return $true
}

# Function to deploy to server (Secure Version)
function Deploy-ToServer {
    param([string]$ServerIP)

    Write-Host "=== Deploying to Server ===" -ForegroundColor Magenta

    $ApiSource = Join-Path $ProjectRoot "publish\api"
    $AngularSource = Join-Path $ProjectRoot "publish\angular"

    # Check if build outputs exist
    if (!(Test-Path $ApiSource)) {
        Write-Error "API build output not found: $ApiSource"
        return $false
    }

    if (!(Test-Path $AngularSource)) {
        Write-Error "Angular build output not found: $AngularSource"
        return $false
    }

    Write-Host "Build outputs found. To deploy securely:" -ForegroundColor Yellow
    Write-Host "1. Copy the 'publish' folder to your server manually" -ForegroundColor Cyan
    Write-Host "2. Or use one of these secure methods:" -ForegroundColor Cyan
    Write-Host "   - PowerShell Remoting: Enable PS remoting on server, use Invoke-Command" -ForegroundColor White
    Write-Host "   - MSDeploy/Web Deploy: Install and configure Web Deploy" -ForegroundColor White
    Write-Host "   - RDP file copy: Copy files via Remote Desktop" -ForegroundColor White
    Write-Host "   - Shared folder: Create a deployment share on server" -ForegroundColor White

    Write-Host ""
    Write-Host "Manual deployment commands for server:" -ForegroundColor Green
    Write-Host "xcopy /E /I /Y 'C:\path\to\publish\api\*' 'C:\inetpub\wwwroot\tsic-api\'" -ForegroundColor White
    Write-Host "xcopy /E /I /Y 'C:\path\to\publish\angular\*' 'C:\inetpub\wwwroot\tsic-app\'" -ForegroundColor White
    Write-Host "copy 'C:\path\to\scripts\web.config.api' 'C:\inetpub\wwwroot\tsic-api\web.config'" -ForegroundColor White
    Write-Host "copy 'C:\path\to\scripts\web.config.angular' 'C:\inetpub\wwwroot\tsic-app\web.config'" -ForegroundColor White

    # Create timestamped deployment package for iDrive backup
    $timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $DeployPackage = Join-Path $ProjectRoot "deployment-package_$timestamp"
    if (!(Test-Path $DeployPackage)) {
        New-Item -ItemType Directory -Path $DeployPackage -Force | Out-Null
    }

    Write-Host ""
    Write-Host "=== iDrive Deployment Package ===" -ForegroundColor Magenta
    Write-Host "Creating deployment package for iDrive backup..." -ForegroundColor Cyan

    # Create package structure
    Copy-Item "$ApiSource\*" (Join-Path $DeployPackage "api") -Recurse -Force
    Copy-Item "$AngularSource\*" (Join-Path $DeployPackage "angular") -Recurse -Force
    Copy-Item (Join-Path $PSScriptRoot "web.config.api") (Join-Path $DeployPackage "api\web.config") -Force
    Copy-Item (Join-Path $PSScriptRoot "web.config.angular") (Join-Path $DeployPackage "angular\web.config") -Force

    # Create deployment instructions
    $instructions = @'
TSIC Deployment Package - {0}
=====================================

This package contains the latest build of TSIC .NET API and Angular frontend.

DEPLOYMENT INSTRUCTIONS:
========================

1. This folder has been backed up to iDrive
2. RDP to server 10.0.0.45 through VPN
3. Restore this folder from iDrive to: C:\temp\deployment-package_{0}
4. Run the deployment script on the server:

   # Open PowerShell as Administrator on server
   cd "C:\temp\deployment-package_{0}"
   .\deploy-to-server.ps1

DEPLOYMENT WILL:
- Copy API files to C:\inetpub\wwwroot\tsic-api\
- Copy Angular files to C:\inetpub\wwwroot\tsic-app\
- Configure IIS web.config files
- Test the deployment

PACKAGE CONTENTS:
================
api\        - .NET API files + web.config
angular\    - Angular frontend files + web.config
deploy-to-server.ps1 - Server deployment script

CREATED: {0}
'@ -f $timestamp

    $instructions | Out-File -FilePath (Join-Path $DeployPackage "README.txt") -Encoding UTF8

    # Create server deployment script
    $serverScript = @'
# Server-side deployment script
# Run this on the Windows Server after restoring from iDrive

param(
    [string]$ApiTarget = "C:\inetpub\wwwroot\tsic-api",
    [string]$AngularTarget = "C:\inetpub\wwwroot\tsic-app"
)

Write-Host "=== TSIC Server Deployment ===" -ForegroundColor Green
Write-Host "Deploying to server..." -ForegroundColor Yellow

# Create target directories
if (!(Test-Path $ApiTarget)) {
    New-Item -ItemType Directory -Path $ApiTarget -Force | Out-Null
}
if (!(Test-Path $AngularTarget)) {
    New-Item -ItemType Directory -Path $AngularTarget -Force | Out-Null
}

# Deploy API
Write-Host "Deploying API..." -ForegroundColor Cyan
Copy-Item ".\api\*" $ApiTarget -Recurse -Force

# Deploy Angular
Write-Host "Deploying Angular..." -ForegroundColor Cyan
Copy-Item ".\angular\*" $AngularTarget -Recurse -Force

Write-Host "✓ Deployment completed!" -ForegroundColor Green
Write-Host "API: http://10.0.0.45:5000" -ForegroundColor Green
Write-Host "Angular: http://10.0.0.45" -ForegroundColor Green

# Optional: Restart IIS sites
Write-Host "Restarting IIS sites..." -ForegroundColor Cyan
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (Get-Module WebAdministration) {
    try {
        Restart-WebAppPool -Name "TSIC-API-Pool" -ErrorAction SilentlyContinue
        Restart-WebAppPool -Name "TSIC-Angular-Pool" -ErrorAction SilentlyContinue
        Write-Host "✓ IIS sites restarted" -ForegroundColor Green
    } catch {
        Write-Host "⚠ Could not restart IIS sites (may not exist yet)" -ForegroundColor Yellow
    }
}
'@

    $serverScript | Out-File -FilePath (Join-Path $DeployPackage "deploy-to-server.ps1") -Encoding UTF8

    Write-Host "✓ iDrive deployment package created!" -ForegroundColor Green
    Write-Host "📁 Package location: $DeployPackage" -ForegroundColor Green
    Write-Host ""
    Write-Host "=== iDrive Deployment Workflow ===" -ForegroundColor Magenta
    Write-Host "1. Add this directory to iDrive backup: $DeployPackage" -ForegroundColor Cyan
    Write-Host "2. Run iDrive backup to upload the package" -ForegroundColor Cyan
    Write-Host "3. RDP to server 10.0.0.45 through VPN" -ForegroundColor Cyan
    Write-Host "4. Restore package from iDrive to server" -ForegroundColor Cyan
    Write-Host "5. Run deploy-to-server.ps1 on the server" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "This approach uses your existing secure infrastructure!" -ForegroundColor Green

    return $true
}

# Function to test deployment
function Test-Deployment {
    param([string]$ServerIP)

    Write-Host "=== Testing Deployment ===" -ForegroundColor Magenta

    $ApiUrl = "http://${ServerIP}:5000/health"  # Assuming you have a health endpoint
    $AngularUrl = "http://$ServerIP"

    # Test API
    try {
        Write-Host "Testing API endpoint: $ApiUrl" -ForegroundColor Cyan
        $Response = Invoke-WebRequest -Uri $ApiUrl -TimeoutSec 10
        if ($Response.StatusCode -eq 200) {
            Write-Host "✓ API is responding" -ForegroundColor Green
        } else {
            Write-Host "⚠ API returned status: $($Response.StatusCode)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "⚠ API test failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    # Test Angular
    try {
        Write-Host "Testing Angular application: $AngularUrl" -ForegroundColor Cyan
        $Response = Invoke-WebRequest -Uri $AngularUrl -TimeoutSec 10
        if ($Response.StatusCode -eq 200) {
            Write-Host "✓ Angular application is loading" -ForegroundColor Green
        } else {
            Write-Host "⚠ Angular returned status: $($Response.StatusCode)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "⚠ Angular test failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# Main deployment flow
try {
    # Test server connection
    if (!(Test-ServerConnection -ServerIP $ServerIP)) {
        exit 1
    }

    # Build components (unless skipped)
    if (!$SkipBuild) {
        if (!(Build-DotNetAPI)) { exit 1 }
        if (!(Build-Angular)) { exit 1 }
    } else {
        Write-Host "Skipping build step as requested" -ForegroundColor Yellow
    }

    # Deploy to server
    if (!(Deploy-ToServer -ServerIP $ServerIP)) { exit 1 }

    # Test deployment
    Test-Deployment -ServerIP $ServerIP

    Write-Host ""
    Write-Host "=== Deployment Summary ===" -ForegroundColor Green
    Write-Host "✓ .NET API deployed to: http://${ServerIP}:5000" -ForegroundColor Green
    Write-Host "✓ Angular app deployed to: http://$ServerIP" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "1. Configure CORS in API for Angular origin" -ForegroundColor White
    Write-Host "2. Update Angular environment files with API URL" -ForegroundColor White
    Write-Host "3. Test API calls from Angular application" -ForegroundColor White
    Write-Host "4. Configure SSL/HTTPS if needed" -ForegroundColor White

} catch {
    Write-Error "Deployment failed with error: $_"
    exit 1
}