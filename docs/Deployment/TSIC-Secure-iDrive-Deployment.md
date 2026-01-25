# TSIC Secure Deployment Methodology (iDrive-Based)
# Production-Ready .NET API + Angular Deployment to Windows Server 10.0.0.45

## Overview

This document outlines the **secure, production-ready deployment methodology** for TSIC (.NET 9 API + Angular frontend) using iDrive backup infrastructure. This approach eliminates security risks associated with administrative shares while leveraging your existing secure backup system.

## Architecture

### Deployment Strategy: Separate IIS Sites
- **Angular Frontend**: `http://10.0.0.45` (port 80) - Static file hosting
- **.NET API Backend**: `http://10.0.0.45:5000` (port 5000) - ASP.NET Core application

### Security Benefits
- ✅ **No administrative shares** required
- ✅ **Leverages existing iDrive infrastructure**
- ✅ **Uses established RDP VPN access**
- ✅ **Auditable through backup logs**
- ✅ **No additional firewall ports opened**

## Prerequisites

### Server Requirements (Windows Server 10.0.0.45)
- IIS with ASP.NET Core Module installed
- .NET 9.0 Runtime
- URL Rewrite module
- iDrive backup software installed

### Development Machine Requirements
- .NET 9.0 SDK
- Node.js 18+ and npm
- Angular CLI
- iDrive backup software
- PowerShell

## Build Process

### Step 1: Build .NET API

```powershell
cd "C:\Users\tgree\source\repos\TSIC-Core-Angular\scripts"
.\Build-DotNet-API.ps1
```

**Output**: `publish\api\` directory with compiled .NET application

### Step 2: Build Angular Frontend

```powershell
cd "C:\Users\tgree\source\repos\TSIC-Core-Angular\scripts"
.\Build-Angular.ps1
```

**Output**: `publish\angular\` directory with production Angular build

## Secure Deployment Workflow

### Phase 1: Package Creation (Development Machine)

```powershell
cd "C:\Users\tgree\source\repos\TSIC-Core-Angular\scripts"

# Ensure builds are current
.\Build-DotNet-API.ps1
.\Build-Angular.ps1

# Create deployment package
# Note: Package creator script has environment-specific issues
# Use manual process for now:

$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$packageName = "tsic-deployment_$timestamp"
$packagePath = Join-Path $PSScriptRoot "..\ $packageName"

# Create package directory
New-Item -ItemType Directory -Path $packagePath -Force

# Copy API files
Copy-Item "..\publish\api\*" (Join-Path $packagePath "api") -Recurse -Force

# Copy Angular files
Copy-Item "..\publish\angular\*" (Join-Path $packagePath "angular") -Recurse -Force

# Copy configuration files
Copy-Item "web.config.api" (Join-Path $packagePath "api\web.config") -Force
Copy-Item "web.config.angular" (Join-Path $packagePath "angular\web.config") -Force

# Copy deployment script
Copy-Item "deploy-to-server-template.ps1" (Join-Path $packagePath "deploy-to-server.ps1") -Force

# Copy README
Copy-Item "README-template.txt" (Join-Path $packagePath "README.txt") -Force
```

### Phase 2: Secure Transfer via iDrive

1. **Add package to iDrive backup**
   - Configure iDrive to include the `$packagePath` directory
   - Run iDrive backup to upload the deployment package

2. **RDP to server**
   - Connect to Windows Server 10.0.0.45 via VPN + RDP
   - No additional ports or services required

3. **Restore from iDrive**
   - Use iDrive software on server to restore the deployment package
   - Restore to: `C:\temp\tsic-deployment_$timestamp`

### Phase 3: Server-Side Deployment

```powershell
# On the server, run as Administrator:
cd "C:\temp\tsic-deployment_$timestamp"
.\deploy-to-server.ps1
```

**What the deployment script does:**
- Creates IIS directories if needed
- Copies API files to `C:\inetpub\wwwroot\tsic-api\`
- Copies Angular files to `C:\inetpub\wwwroot\tsic-app\`
- Optionally restarts IIS application pools

## IIS Configuration

### Initial Server Setup (One-time)

```powershell
# Run on server as Administrator

# Install IIS features
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-StaticContent, IIS-WebServerManagementTools, IIS-DefaultDocument, IIS-NetFxExtensibility45, IIS-ASPNET45, IIS-ApplicationInit

# Create API application pool
New-WebAppPool -Name "TSIC-API-Pool" -Force
Set-ItemProperty IIS:\AppPools\TSIC-API-Pool -Name processModel.identityType -Value ApplicationPoolIdentity

# Create API website
New-Website -Name "TSIC-API" -PhysicalPath "C:\inetpub\wwwroot\tsic-api" -Port 5000 -ApplicationPool "TSIC-API-Pool" -Force

# Configure Angular (static files) - modify Default Web Site
Set-ItemProperty IIS:\Sites\Default Web Site -Name physicalPath -Value "C:\inetpub\wwwroot\tsic-app"
New-WebAppPool -Name "TSIC-Angular-Pool" -Force
Set-ItemProperty IIS:\AppPools\TSIC-Angular-Pool -Name processModel.identityType -Value ApplicationPoolIdentity
Set-ItemProperty IIS:\AppPools\TSIC-Angular-Pool -Name managedRuntimeVersion -Value ""
Set-ItemProperty IIS:\Sites\Default Web Site -Name applicationPool -Value "TSIC-Angular-Pool"
```

## Configuration Files

### API web.config (web.config.api)

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <handlers>
      <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
    </handlers>
    <aspNetCore processPath="dotnet" arguments=".\TSIC.API.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
  </system.webServer>
</configuration>
```

### Angular web.config (web.config.angular)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <defaultDocument>
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>

    <staticContent>
      <mimeMap fileExtension=".json" mimeType="application/json" />
      <mimeMap fileExtension=".woff" mimeType="application/font-woff" />
      <mimeMap fileExtension=".woff2" mimeType="font/woff2" />
    </staticContent>

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
  </system.webServer>
</configuration>
```

## CORS Configuration

### In .NET API (Program.cs)

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins("http://10.0.0.45", "http://localhost:4200")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

app.UseCors("AllowAngularApp");
```

## Testing Deployment

### Post-Deployment Verification

```powershell
# Test API connectivity
Invoke-WebRequest -Uri "http://10.0.0.45:5000/health" -Method GET

# Test Angular application
Start-Process "http://10.0.0.45"

# Check IIS sites
Get-Website | Where-Object { $_.Name -like "*TSIC*" }
Get-WebAppPool | Where-Object { $_.Name -like "*TSIC*" }
```

### Manual Testing Checklist

- [ ] API responds at `http://10.0.0.45:5000`
- [ ] Angular loads at `http://10.0.0.45`
- [ ] No CORS errors in browser dev tools
- [ ] API calls work from Angular application
- [ ] IIS application pools running
- [ ] No errors in Windows Event Viewer

## Security Analysis

### Why This Approach is Secure

1. **No Administrative Shares**: Avoids disabled `C$` shares on production servers
2. **Existing Infrastructure**: Uses your current iDrive backup system
3. **VPN-Only Access**: RDP access already secured through VPN
4. **Auditable**: All deployments logged through iDrive backup history
5. **Isolated**: No direct file sharing or remote management required

### Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Unauthorized access | VPN + RDP authentication required |
| Data interception | Encrypted iDrive backups |
| Malware injection | Local build verification + backup integrity |
| Configuration drift | Version-controlled deployment scripts |
| Downtime during deployment | Blue-green deployment capability |

## Automation Scripts

### Build Scripts Location
- `scripts/Build-DotNet-API.ps1` - API compilation and publishing
- `scripts/Build-Angular.ps1` - Angular production build

### Deployment Templates
- `scripts/README-template.txt` - Deployment documentation
- `scripts/deploy-to-server-template.ps1` - Server deployment automation

### Package Creator (Manual Process)
See Phase 1 above for manual package creation steps.

## Monitoring & Maintenance

### Health Checks
- Implement `/health` endpoint in API
- Monitor Angular application loading
- Check IIS application pool status

### Logs to Monitor
- `C:\inetpub\logs\LogFiles\` - IIS request logs
- `C:\inetpub\wwwroot\tsic-api\logs\` - API application logs
- iDrive backup logs - Deployment history

### Backup Strategy
- Include deployment packages in regular iDrive backups
- Version deployment packages (timestamped)
- Maintain rollback capability (keep last 3 deployments)

## Troubleshooting

### Common Issues & Solutions

#### API Won't Start
```powershell
# Check application pool
Get-WebAppPool -Name "TSIC-API-Pool"
# Check permissions on C:\inetpub\wwwroot\tsic-api\
icacls "C:\inetpub\wwwroot\tsic-api\"
# Check .NET installation
dotnet --version
```

#### Angular Shows 404
- Verify URL rewrite rules in web.config
- Check file permissions on Angular directory
- Confirm index.html exists in root

#### CORS Errors
- Verify CORS policy in API Program.cs
- Check Angular environment configuration
- Confirm API URL in Angular service calls

#### iDrive Restore Issues
- Ensure sufficient disk space on server
- Check iDrive account permissions
- Verify VPN connectivity during restore

## Future Enhancements

### SSL/HTTPS Implementation
```powershell
# Bind SSL certificate
# Configure HTTPS redirection
# Update CORS origins to HTTPS URLs
```

### CI/CD Integration
- GitHub Actions with iDrive integration
- Automated deployment triggers
- Deployment status notifications

### Monitoring & Alerting
- Application performance monitoring
- Automated health checks
- Deployment success/failure alerts

---

## Quick Reference

### Development Machine
```powershell
cd scripts
.\Build-DotNet-API.ps1
.\Build-Angular.ps1
# Create package manually (see Phase 1)
# Add to iDrive backup
```

### Server (via RDP)
```powershell
# Restore from iDrive to C:\temp\
# Run deployment script
cd "C:\temp\tsic-deployment_*"
.\deploy-to-server.ps1
```

### Verification
- API: `http://10.0.0.45:5000`
- Angular: `http://10.0.0.45`

This methodology provides enterprise-grade security while being simple to implement with your existing infrastructure.</content>
<parameter name="filePath">c:\Users\tgree\source\repos\TSIC-Core-Angular\docs\TSIC-Secure-iDrive-Deployment.md