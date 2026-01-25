# Angular IIS Deployment Guide

## Overview

This guide explains how to deploy an Angular application to a Windows Server using Internet Information Services (IIS). Unlike .NET applications that run server-side code, Angular applications are static Single Page Applications (SPAs) that need to be served by a web server.

## Prerequisites

- Windows Server with IIS installed
- URL Rewrite module for IIS (download from Microsoft)
- Angular CLI installed locally
- Built Angular application (`ng build --prod`)

## Build Process

### 1. Build Angular Application

```bash
cd src/frontend/tsic-app
npm run build --prod
```

This creates a `dist/tsic-app/` folder with production-ready static files:
- `index.html` - Main application file
- JavaScript bundles (*.js)
- CSS bundles (*.css)
- Static assets (images, fonts, etc.)

## IIS Configuration

### 2. Install Required IIS Features

Run PowerShell as Administrator:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-StaticContent, IIS-WebServerManagementTools, IIS-DefaultDocument
```

### 3. Install URL Rewrite Module

1. Download from: https://www.iis.net/downloads/microsoft/url-rewrite
2. Install the MSI package on your server

### 4. Create Website Directory

```powershell
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\tsic-app" -Force
```

### 5. Deploy Files

Copy the Angular build output to the IIS directory:

```powershell
Copy-Item "C:\Path\To\Your\Angular\dist\tsic-app\*" "C:\inetpub\wwwroot\tsic-app\" -Recurse -Force
```

## Web.config Configuration

Create a `web.config` file in your website root directory:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <!-- Enable directory browsing (optional) -->
    <directoryBrowse enabled="false" />

    <!-- Default document -->
    <defaultDocument>
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>

    <!-- Static content configuration -->
    <staticContent>
      <mimeMap fileExtension=".json" mimeType="application/json" />
      <mimeMap fileExtension=".woff" mimeType="application/font-woff" />
      <mimeMap fileExtension=".woff2" mimeType="font/woff2" />
    </staticContent>

    <!-- URL Rewrite for Angular routing -->
    <rewrite>
      <rules>
        <rule name="Angular Routes" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>

    <!-- Security headers (optional but recommended) -->
    <httpProtocol>
      <customHeaders>
        <add name="X-Frame-Options" value="SAMEORIGIN" />
        <add name="X-Content-Type-Options" value="nosniff" />
        <add name="Referrer-Policy" value="strict-origin-when-cross-origin" />
      </customHeaders>
    </httpProtocol>

    <!-- Compression (optional) -->
    <urlCompression doStaticCompression="true" doDynamicCompression="true" />
    <httpCompression>
      <dynamicTypes>
        <add mimeType="text/*" enabled="true" />
        <add mimeType="message/*" enabled="true" />
        <add mimeType="application/javascript" enabled="true" />
        <add mimeType="application/json" enabled="true" />
      </dynamicTypes>
      <staticTypes>
        <add mimeType="text/*" enabled="true" />
        <add mimeType="message/*" enabled="true" />
        <add mimeType="application/javascript" enabled="true" />
        <add mimeType="application/json" enabled="true" />
      </staticTypes>
    </httpCompression>
  </system.webServer>

  <system.web>
    <!-- Disable session state for better performance -->
    <sessionState mode="Off" />
  </system.web>
</configuration>
```

## IIS Website Setup

### Option 1: Create New Website

```powershell
Import-Module WebAdministration

# Create new website
New-Website -Name "TSIC-App" -PhysicalPath "C:\inetpub\wwwroot\tsic-app" -Port 80 -HostHeader "yourdomain.com" -Force
```

### Option 2: Use Default Website

```powershell
# Modify default website
Set-ItemProperty IIS:\Sites\Default Web Site -name physicalPath -value "C:\inetpub\wwwroot\tsic-app"
```

## Key Configurations Explained

### URL Rewrite Rules

Angular uses client-side routing. When users navigate to routes like `/dashboard` or `/users/123`, these URLs don't correspond to actual files on the server. The URL rewrite rule ensures all routes are served by `index.html`, allowing Angular's router to handle them.

### MIME Types

IIS needs to know how to serve different file types. The configuration ensures:
- JSON files are served as `application/json`
- Web fonts are served correctly
- All Angular assets are properly handled

### Default Document

Sets `index.html` as the default document, so requests to the root URL (`/`) serve the Angular application.

## Testing Deployment

1. **Access the application**: Navigate to `http://yourdomain.com`
2. **Test routing**: Try direct URLs like `http://yourdomain.com/dashboard`
3. **Check browser developer tools**: Look for 404 errors or console issues
4. **Test API calls**: Ensure backend API endpoints are accessible

## Deployment Scripts

### Automated Deployment Script

```powershell
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
```

### IIS Setup Script

```powershell
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
# Set-ItemProperty IIS:\Sites\Default Web Site -name physicalPath -value "C:\inetpub\wwwroot\tsic-app"
```

## Troubleshooting

### Common Issues

1. **404 Errors on Refresh**: URL rewrite rules not configured properly
2. **Font Loading Issues**: MIME types not configured for font files
3. **API Calls Failing**: CORS issues or incorrect API URLs in production build
4. **Blank Page**: Base href not set correctly in index.html

### Debugging Steps

1. Check IIS logs: `C:\inetpub\logs\LogFiles\`
2. Use browser developer tools to check network requests
3. Verify web.config is in the correct location
4. Test static file serving by accessing files directly

## Performance Optimization

- Enable output caching in IIS
- Configure compression (already in web.config)
- Use CDN for static assets
- Implement lazy loading in Angular
- Enable gzip compression

## Security Considerations

- Use HTTPS in production
- Configure proper firewall rules
- Keep IIS and Windows Server updated
- Set up monitoring and logging
- Use security headers (included in web.config)

## Key Differences from .NET Applications

| Aspect | .NET Applications | Angular Applications |
|--------|------------------|---------------------|
| Runtime | Server-side code | Static files |
| Application Pool | .NET CLR | No Managed Code |
| Routing | Server-side routing | Client-side routing |
| Deployment | DLLs and config | HTML/JS/CSS files |
| Scaling | Application pool recycling | Static file caching |

## Next Steps

1. Build your Angular application: `ng build --prod`
2. Copy files to IIS directory
3. Configure web.config
4. Set up IIS website
5. Test deployment
6. Configure SSL/HTTPS
7. Set up monitoring

This guide provides a complete reference for deploying Angular applications to IIS on Windows Server.</content>
<parameter name="filePath">c:\Users\tgree\source\repos\TSIC-Core-Angular\docs\Angular-IIS-Deployment-Guide.md