# TSIC iDrive Deployment Package Creator
# Creates a deployment package for secure iDrive backup and restore

Write-Host "=== TSIC iDrive Deployment Package Creator ===" -ForegroundColor Green
Write-Host "Creating secure deployment package for iDrive..." -ForegroundColor Yellow
Write-Host ""

# Get script and project paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$OutputPath = Join-Path $ProjectRoot "publish"

# Check if build outputs exist
$ApiSource = Join-Path $OutputPath "api"
$AngularSource = Join-Path $OutputPath "angular"

if (!(Test-Path $ApiSource)) {
    Write-Error "API build output not found: $ApiSource"
    Write-Host "Please run .\Build-DotNet-API.ps1 first" -ForegroundColor Yellow
    exit 1
}

if (!(Test-Path $AngularSource)) {
    Write-Error "Angular build output not found: $AngularSource"
    Write-Host "Please run .\Build-Angular.ps1 first" -ForegroundColor Yellow
    exit 1
}

# Create deployment package with fixed name for iDrive backup
$DeployPackage = Join-Path $ProjectRoot "tsic-deployment-current"

Write-Host " Creating deployment package: $DeployPackage" -ForegroundColor Cyan

# Create directory
New-Item -ItemType Directory -Path $DeployPackage -Force | Out-Null

# Copy build outputs
Write-Host "Copying API files..." -ForegroundColor White
Copy-Item "$ApiSource\*" (Join-Path $DeployPackage "api") -Recurse -Force

Write-Host "Copying Angular files..." -ForegroundColor White
Copy-Item "$AngularSource\*" (Join-Path $DeployPackage "angular") -Recurse -Force

# Copy web.config files
Write-Host "Copying configuration files..." -ForegroundColor White
Copy-Item (Join-Path $ScriptDir "web.config.api") (Join-Path $DeployPackage "api\web.config") -Force
Copy-Item (Join-Path $ScriptDir "web.config.angular") (Join-Path $DeployPackage "angular\web.config") -Force

# Copy README and deployment script
Write-Host "Creating README file..." -ForegroundColor White
$readmeContent = Get-Content (Join-Path $ScriptDir "README-template.txt") -Raw
$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$readmeContent = $readmeContent -replace '\{timestamp\}', $timestamp
$readmeContent | Out-File (Join-Path $DeployPackage "README.txt") -Encoding UTF8

Write-Host "Copying deployment script..." -ForegroundColor White
Copy-Item (Join-Path $ScriptDir "deploy-to-server-template.ps1") (Join-Path $DeployPackage "deploy-to-server.ps1") -Force

Write-Host ""
Write-Host "=== iDrive Deployment Package Ready ===" -ForegroundColor Green
Write-Host " Package created at: $DeployPackage" -ForegroundColor Green
Write-Host ""
Write-Host "=== YOUR DEPLOYMENT WORKFLOW ===" -ForegroundColor Magenta
Write-Host "1. Add '$DeployPackage' to your iDrive backup configuration" -ForegroundColor Cyan
Write-Host "2. Run iDrive backup to upload this package" -ForegroundColor Cyan
Write-Host "3. RDP to server 10.0.0.45 through VPN" -ForegroundColor Cyan
Write-Host "4. Restore package from iDrive to server" -ForegroundColor Cyan
Write-Host "5. Run deploy-to-server.ps1 on the server" -ForegroundColor Cyan
Write-Host ""
Write-Host " This uses your existing secure infrastructure!" -ForegroundColor Green
Write-Host " No additional security holes opened!" -ForegroundColor Green
