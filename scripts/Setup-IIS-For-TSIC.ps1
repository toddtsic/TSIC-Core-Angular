# IIS Setup Script for TSIC Deployment
# Run this script on your Windows Server (10.0.0.45) as Administrator

Write-Host "Setting up IIS for TSIC Application..." -ForegroundColor Green

# Install required IIS features
Write-Host "Installing IIS features..." -ForegroundColor Cyan
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-StaticContent, IIS-WebServerManagementTools, IIS-DefaultDocument, IIS-NetFxExtensibility45, IIS-ASPNET45, IIS-WebSockets

# Install URL Rewrite module if not present
Write-Host "Checking for URL Rewrite module..." -ForegroundColor Cyan
$urlRewritePath = "${env:ProgramFiles}\IIS\Microsoft Web Farm Framework\rewrite.dll"
if (!(Test-Path $urlRewritePath)) {
    Write-Host "URL Rewrite module not found. Please download and install from:" -ForegroundColor Yellow
    Write-Host "https://www.iis.net/downloads/microsoft/url-rewrite" -ForegroundColor Yellow
}

# Import WebAdministration module
Import-Module WebAdministration

# Create directories
Write-Host "Creating website directories..." -ForegroundColor Cyan
$apiPath = "C:\inetpub\wwwroot\tsic-api"
$angularPath = "C:\inetpub\wwwroot\tsic-app"

New-Item -ItemType Directory -Path $apiPath -Force | Out-Null
New-Item -ItemType Directory -Path $angularPath -Force | Out-Null

# Configure API Site (Port 5000)
Write-Host "Configuring API site..." -ForegroundColor Cyan
if (!(Test-Path IIS:\AppPools\TSIC-API-Pool)) {
    New-WebAppPool -Name "TSIC-API-Pool" -Force
    Set-ItemProperty IIS:\AppPools\TSIC-API-Pool -Name processModel.identityType -Value ApplicationPoolIdentity
}

if (!(Test-Path IIS:\Sites\TSIC-API)) {
    New-Website -Name "TSIC-API" -PhysicalPath $apiPath -Port 5000 -ApplicationPool "TSIC-API-Pool" -Force
} else {
    Set-ItemProperty IIS:\Sites\TSIC-API -Name physicalPath -Value $apiPath
    Set-ItemProperty IIS:\Sites\TSIC-API -Name applicationPool -Value "TSIC-API-Pool"
}

# Configure Angular Site (Default website on port 80)
Write-Host "Configuring Angular site..." -ForegroundColor Cyan
if (!(Test-Path IIS:\AppPools\TSIC-Angular-Pool)) {
    New-WebAppPool -Name "TSIC-Angular-Pool" -Force
    Set-ItemProperty IIS:\AppPools\TSIC-Angular-Pool -Name processModel.identityType -Value ApplicationPoolIdentity
    Set-ItemProperty IIS:\AppPools\TSIC-Angular-Pool -Name managedRuntimeVersion -Value ""
}

# Configure default website for Angular
Set-ItemProperty IIS:\Sites\Default Web Site -Name physicalPath -Value $angularPath
Set-ItemProperty IIS:\Sites\Default Web Site -Name applicationPool -Value "TSIC-Angular-Pool"

# Set permissions
Write-Host "Setting directory permissions..." -ForegroundColor Cyan
$acl = Get-Acl $apiPath
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($accessRule)
Set-Acl -Path $apiPath -AclObject $acl

$acl = Get-Acl $angularPath
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($accessRule)
Set-Acl -Path $angularPath -AclObject $acl

# Configure firewall
Write-Host "Configuring Windows Firewall..." -ForegroundColor Cyan
New-NetFirewallRule -DisplayName "HTTP Inbound" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "TSIC API Inbound" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow -ErrorAction SilentlyContinue

# Start websites
Write-Host "Starting websites..." -ForegroundColor Cyan
Start-Website -Name "TSIC-API" -ErrorAction SilentlyContinue
Start-Website -Name "Default Web Site" -ErrorAction SilentlyContinue

Write-Host "IIS setup completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Configuration Summary:" -ForegroundColor Cyan
Write-Host "✓ API Site: http://10.0.0.45:5000" -ForegroundColor Green
Write-Host "✓ Angular Site: http://10.0.0.45" -ForegroundColor Green
Write-Host "✓ Firewall rules configured" -ForegroundColor Green
Write-Host "✓ Application pools created" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Run the deployment script from your development machine" -ForegroundColor White
Write-Host "2. Test both applications" -ForegroundColor White
Write-Host "3. Configure SSL if needed" -ForegroundColor White</content>
<parameter name="filePath">c:\Users\tgree\source\repos\TSIC-Core-Angular\scripts\Setup-IIS-For-TSIC.ps1