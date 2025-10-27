# Complete Angular Deployment Script
# Run this on your Windows Server as Administrator

param(
    [string]$SourcePath = "C:\Path\To\Your\Angular\dist\tsic-app",
    [string]$SiteName = "TSIC-App",
    [string]$HostHeader = "yourdomain.com",
    [int]$Port = 80
)

# Stop IIS site during deployment
Stop-Website -Name $SiteName -ErrorAction SilentlyContinue

# Copy files
$destPath = "C:\inetpub\wwwroot\$SiteName"
if (!(Test-Path $destPath)) {
    New-Item -ItemType Directory -Path $destPath -Force
}

# Remove old files
Get-ChildItem -Path $destPath -Recurse | Remove-Item -Force -Recurse

# Copy new build
Copy-Item "$SourcePath\*" $destPath -Recurse -Force

# Copy web.config
Copy-Item "C:\Path\To\web.config" $destPath -Force

# Start IIS site
Start-Website -Name $SiteName

Write-Host "Angular application deployed successfully!"
Write-Host "Site URL: http://$HostHeader`:$Port"