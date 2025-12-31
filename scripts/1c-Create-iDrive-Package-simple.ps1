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
    Write-Host "Please run .\1a-Build-DotNet-API.ps1 first" -ForegroundColor Yellow
    exit 1
}

if (!(Test-Path $AngularSource)) {
    Write-Error "Angular build output not found: $AngularSource"
    Write-Host "Please run .\Build-Angular.ps1 first" -ForegroundColor Yellow
    exit 1
}

# Create deployment package with fixed name for iDrive backup
$DeployPackageRoot = Join-Path $ProjectRoot "tsic-deployment-current"
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$DeployPackage = Join-Path $DeployPackageRoot $Timestamp

Write-Host "Creating deployment package: $DeployPackage" -ForegroundColor Cyan

# Ensure root directory exists
if (!(Test-Path $DeployPackageRoot)) {
    New-Item -ItemType Directory -Path $DeployPackageRoot -Force | Out-Null
}

# Clean old deployment packages (keep last 3)
Write-Host "Cleaning old deployment packages (keeping last 3)..." -ForegroundColor Yellow
Get-ChildItem $DeployPackageRoot -Directory | 
    Sort-Object Name -Descending |
    Select-Object -Skip 3 |
    Remove-Item -Recurse -Force

# Create fresh timestamped directory and subfolders
New-Item -ItemType Directory -Path $DeployPackage -Force | Out-Null
$ApiDest = Join-Path $DeployPackage "api"
$AngularDest = Join-Path $DeployPackage "angular"
if (!(Test-Path $ApiDest)) { New-Item -ItemType Directory -Path $ApiDest -Force | Out-Null }
if (!(Test-Path $AngularDest)) { New-Item -ItemType Directory -Path $AngularDest -Force | Out-Null }

# Copy build outputs (ensure destination directories exist first to avoid Copy-Item container/leaf issues)
Write-Host "Copying API files..." -ForegroundColor White
Copy-Item "$ApiSource\*" $ApiDest -Recurse -Force

Write-Host "Copying Angular files..." -ForegroundColor White
Copy-Item "$AngularSource\*" $AngularDest -Recurse -Force

# Copy web.config files
Write-Host "Copying configuration files..." -ForegroundColor White
Copy-Item (Join-Path $ScriptDir "web.config.api") (Join-Path $ApiDest "web.config") -Force
Copy-Item (Join-Path $ScriptDir "web.config.angular") (Join-Path $AngularDest "web.config") -Force

# Ensure both web.config files are well-formed XML (strip any accidental trailer after </configuration>)
function Set-WebConfigWellFormed {
    param([string]$Path)
    if (!(Test-Path $Path)) { return }
    try {
        $raw = Get-Content $Path -Raw -ErrorAction Stop
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success) {
            if ($m.Value.Length -ne $raw.Length) {
                # Trim trailing junk and rewrite
                Set-Content -Path $Path -Value $m.Value -Encoding UTF8
            }
        }
        # Quick well-formedness check
        [void]([xml](Get-Content $Path -Raw))
    } catch {
        Write-Warning ("web.config at '{0}' was not well-formed before fix: {1}" -f $Path, $_.Exception.Message)
        # Last attempt: write only up to closing tag if present
        if ($m -and $m.Success) {
            Set-Content -Path $Path -Value $m.Value -Encoding UTF8
        }
    }
}

Set-WebConfigWellFormed (Join-Path $ApiDest "web.config")
Set-WebConfigWellFormed (Join-Path $AngularDest "web.config")

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
