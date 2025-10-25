# IIS Configuration for Angular Deployment
# Run this script as Administrator on your Windows Server

# 1. Install IIS features if not already installed
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-StaticContent, IIS-WebServerManagementTools, IIS-DefaultDocument

# 2. Install URL Rewrite module (download from Microsoft)
# Download: https://www.iis.net/downloads/microsoft/url-rewrite
# Install the MSI package

# 3. Create website directory
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\tsic-app" -Force

# 4. Copy Angular build files
Copy-Item "C:\Path\To\Your\Angular\dist\tsic-app\*" "C:\inetpub\wwwroot\tsic-app\" -Recurse -Force

# 5. Create IIS Website (run in PowerShell with admin rights)
Import-Module WebAdministration

# Create new website
New-Website -Name "TSIC-App" -PhysicalPath "C:\inetpub\wwwroot\tsic-app" -Port 80 -HostHeader "yourdomain.com" -Force

# Or modify default website
# Set-ItemProperty IIS:\Sites\Default Web Site -name physicalPath -value "C:\inetpub\wwwroot\tsic-app"</content>
<parameter name="filePath">c:\Users\tgree\source\repos\TSIC-Core-Angular\scripts\Setup-IIS-For-Angular.ps1