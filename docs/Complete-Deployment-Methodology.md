# Complete TSIC Deployment Methodology
# .NET API + Angular Frontend to Windows Server (10.0.0.45)

## Architecture Overview

### Deployment Strategy: Separate IIS Sites
- **Angular Frontend**: `http://10.0.0.45` (port 80) - Main website
- **.NET API Backend**: `http://10.0.0.45:5000` (port 5000) - API endpoints

### Benefits of This Approach
- Independent deployment and scaling
- Different update cadences
- Clear separation of concerns
- Easy to configure CORS
- Can use different application pools

## Prerequisites

### Server Requirements
- Windows Server with IIS installed
- .NET 9.0 Runtime installed
- Node.js installed (for Angular builds)
- URL Rewrite module for IIS
- PowerShell remoting enabled (optional, for remote deployment)

### Local Development Requirements
- .NET 9.0 SDK
- Node.js and npm
- Angular CLI
- PowerShell

## Build Process

### 1. Build .NET API for Production

```powershell
# Navigate to solution directory
cd "C:\Users\tgree\source\repos\TSIC-Core-Angular\TSIC-Core-Angular"

# Restore and build
dotnet restore
dotnet build --configuration Release --no-restore

# Publish API
dotnet publish src/backend/TSIC.API/TSIC.API.csproj --configuration Release --output "C:\Deploy\api"
```

### 2. Build Angular for Production

```powershell
# Navigate to Angular project
cd "src/frontend/tsic-app"

# Build for production
npm run build --prod

# Output will be in dist/tsic-app/
```

## IIS Configuration

### Option A: Manual IIS Setup

#### Create API Site (Port 5000)
```powershell
Import-Module WebAdministration

# Create application pool for API
New-WebAppPool -Name "TSIC-API-Pool" -Force
Set-ItemProperty IIS:\AppPools\TSIC-API-Pool -Name processModel.identityType -Value ApplicationPoolIdentity

# Create API website
New-Website -Name "TSIC-API" -PhysicalPath "C:\inetpub\wwwroot\tsic-api" -Port 5000 -ApplicationPool "TSIC-API-Pool" -Force
```

#### Create Angular Site (Port 80)
```powershell
# Create application pool for Angular (static files)
New-WebAppPool -Name "TSIC-Angular-Pool" -Force
Set-ItemProperty IIS:\AppPools\TSIC-Angular-Pool -Name processModel.identityType -Value ApplicationPoolIdentity
Set-ItemProperty IIS:\AppPools\TSIC-Angular-Pool -Name managedRuntimeVersion -Value ""

# Create Angular website (modify default or create new)
Set-ItemProperty IIS:\Sites\Default Web Site -name physicalPath -value "C:\inetpub\wwwroot\tsic-app"
Set-ItemProperty IIS:\Sites\Default Web Site -name applicationPool -value "TSIC-Angular-Pool"
```

### Option B: Automated Setup Script

```powershell
# Run this on your Windows Server
param(
    [string]$ServerIP = "10.0.0.45",
    [string]$ApiPath = "C:\inetpub\wwwroot\tsic-api",
    [string]$AngularPath = "C:\inetpub\wwwroot\tsic-app"
)

# Install required IIS features
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-StaticContent, IIS-WebServerManagementTools, IIS-DefaultDocument, IIS-NetFxExtensibility45, IIS-ASPNET45

# Create directories
New-Item -ItemType Directory -Path $ApiPath -Force
New-Item -ItemType Directory -Path $AngularPath -Force

# Configure API Site
New-WebAppPool -Name "TSIC-API-Pool" -Force
Set-ItemProperty IIS:\AppPools\TSIC-API-Pool -Name processModel.identityType -Value ApplicationPoolIdentity

New-Website -Name "TSIC-API" -PhysicalPath $ApiPath -Port 5000 -ApplicationPool "TSIC-API-Pool" -Force

# Configure Angular Site
New-WebAppPool -Name "TSIC-Angular-Pool" -Force
Set-ItemProperty IIS:\AppPools\TSIC-Angular-Pool -Name processModel.identityType -Value ApplicationPoolIdentity
Set-ItemProperty IIS:\AppPools\TSIC-Angular-Pool -Name managedRuntimeVersion -Value ""

Set-ItemProperty IIS:\Sites\Default Web Site -name physicalPath -value $AngularPath
Set-ItemProperty IIS:\Sites\Default Web Site -name applicationPool -value "TSIC-Angular-Pool"

Write-Host "IIS configuration completed!"
```

## SQL Server Database Permissions (ApplicationPoolIdentity)

### Security Best Practice

The IIS Application Pool should run under **ApplicationPoolIdentity** (least-privilege account) instead of LocalSystem or NetworkService. This requires granting SQL Server database permissions to the app pool identity.

### Step-by-Step GUI Instructions

#### Part 1: Create SQL Server Login for App Pool

1. **Open SQL Server Management Studio (SSMS)**
2. **Connect** to your SQL Server instance
3. **Expand** your server → **Security** → **Logins**
4. **Right-click Logins** → **New Login...**

5. **In the Login - New dialog**:
   - Select **Windows authentication** (should be default)
   - In **Login name** field, type: `IIS AppPool\TSIC.Api`
     - Replace `TSIC.Api` with your actual app pool name from IIS
   - Click **User Mapping** in left sidebar

6. **In User Mapping page**:
   - Check the box next to your database (e.g., `TSICV5`)
   - In **Database role membership** section below, check:
     - `db_datareader` (read data)
     - `db_datawriter` (write data)
   - Click **OK**

7. **Grant EXECUTE permission** (for stored procedures):
   - Expand **Databases** → **[YourDatabase]** → **Security** → **Users**
   - Right-click `IIS AppPool\TSIC.Api` → **Properties**
   - Click **Securables** in left sidebar
   - Click **Search...** button
   - Select **All objects of the types...** → Click **OK**
   - Check **Stored Procedures** → Click **OK**
   - In bottom panel, find **Execute** row
   - Check the **Grant** column
   - Click **OK**

#### Part 2: Configure IIS App Pool Identity (GUI)

1. **Open IIS Manager**
2. Click **Application Pools** in left panel
3. **Right-click your API app pool** (e.g., `TSIC.Api`) → **Advanced Settings...**
4. Under **Process Model** section:
   - Click **Identity** row
   - Click the **...** button on the right
5. **In Application Pool Identity dialog**:
   - Select **ApplicationPoolIdentity**
   - Click **OK**
6. Click **OK** to close Advanced Settings
7. **Right-click the app pool** → **Stop**
8. **Right-click the app pool** → **Start**

#### Repeat for Angular App Pool (Optional)

If your Angular app pool also needs database access (uncommon), repeat the above steps for `IIS AppPool\TSIC.App`.

### Verification

**Test API connectivity**:
```powershell
# Should return 200 OK if SQL connection works
Invoke-WebRequest -Uri "http://localhost:5000/api/health" -UseBasicParsing
```

**Check Event Viewer** if issues occur:
- Windows Logs → Application
- Look for errors from "IIS" or ".NET Runtime"

### Permissions Summary

**What the app pool CAN do**:
- ✅ Read/write data (all API operations)
- ✅ Execute stored procedures
- ✅ Connect to database

**What the app pool CANNOT do** (security benefits):
- ❌ Drop/create tables (safer)
- ❌ Create logins/users (safer)
- ❌ Backup/restore database (safer)
- ❌ Modify database schema (safer)

### Connection String Requirements

Ensure your `appsettings.json` uses **Integrated Security**:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=TSICV5;Integrated Security=true;TrustServerCertificate=true;"
  }
}
```

**Remove** any `User ID` or `Password` parameters (Windows authentication only).

## Configuration Files

### API Web.config

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

### Angular Web.config

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

### In .NET API (Program.cs or Startup.cs)

```csharp
// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins("http://10.0.0.45", "http://localhost:4200") // Allow local dev too
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Use CORS
app.UseCors("AllowAngularApp");
```

## Environment Configuration

### Angular Environment Files

Create `src/environments/environment.prod.ts`:

```typescript
export const environment = {
  production: true,
  apiUrl: 'http://10.0.0.45:5000/api'
};
```

## Security Considerations & Deployment Methods

### ⚠️ Security Warning: Administrative Shares

**The original deployment script uses administrative shares (C$) which are often disabled on production Windows Servers for security reasons.** This approach requires:
- Administrative shares enabled
- Administrative privileges on target server
- File sharing enabled
- Firewall allowing SMB traffic

### 🔒 Secure Deployment Options

#### Option 1: Manual Deployment (Most Secure)
```powershell
# 1. Build locally (on your development machine)
.\Build-DotNet-API.ps1
.\Build-Angular.ps1

# 2. Copy deployment package to server via:
#    - RDP file copy
#    - USB drive
#    - Secure file transfer

# 3. On server, run these commands:
xcopy /E /I /Y "C:\deployment\api\*" "C:\inetpub\wwwroot\tsic-api\"
xcopy /E /I /Y "C:\deployment\angular\*" "C:\inetpub\wwwroot\tsic-app\"
copy "C:\deployment\api\web.config" "C:\inetpub\wwwroot\tsic-api\web.config"
copy "C:\deployment\angular\web.config" "C:\inetpub\wwwroot\tsic-app\web.config"
```

#### Option 2: PowerShell Remoting (Secure)
```powershell
# Requires: Enable-PSRemoting -Force on target server
.\Deploy-TSIC-Secure.ps1 -ServerIP "10.0.0.45" -Credential (Get-Credential)
```

#### Option 3: Web Deploy/MSDeploy (Enterprise)
```powershell
# Install Web Deploy on server and use MSDeploy commands
msdeploy -verb:sync -source:contentPath="C:\publish\api" -dest:contentPath="C:\inetpub\wwwroot\tsic-api",computerName="10.0.0.45",userName="admin",password="password"
```

#### Option 4: CI/CD Pipeline (Recommended for Production)
- GitHub Actions with self-hosted runners
- Azure DevOps pipelines
- Jenkins with Windows agents

### 📦 Deployment Package Creation

The deployment scripts create a `deployment-package` folder containing:
```
deployment-package/
├── api/           # All .NET API files + web.config
├── angular/       # All Angular files + web.config
└── README.txt     # Deployment instructions
```

Copy this entire folder to your server and run the xcopy commands above.

## Testing Deployment

### 1. Test API Endpoints
```powershell
# Test API connectivity
Invoke-WebRequest -Uri "http://10.0.0.45:5000/api/health" -Method GET
```

### 2. Test Angular Application
```powershell
# Test Angular loading
Start-Process "http://10.0.0.45"
```

### 3. Test API Communication
- Open browser dev tools
- Check network tab for API calls
- Verify CORS headers

## Networking & Firewall

### Windows Firewall Configuration
```powershell
# Allow HTTP (port 80)
New-NetFirewallRule -DisplayName "HTTP Inbound" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow

# Allow API port (5000)
New-NetFirewallRule -DisplayName "API Inbound" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

## Monitoring & Maintenance

### IIS Logging
- Enable IIS logging for both sites
- Monitor failed requests
- Check application pool recycling

### Health Checks
- Implement health check endpoints in API
- Monitor Angular app loading times
- Set up automated monitoring

## Troubleshooting

### Common Issues
1. **API not starting**: Check application pool identity and permissions
2. **Angular routing issues**: Verify URL rewrite rules
3. **CORS errors**: Check CORS policy configuration
4. **File permissions**: Ensure IIS_IUSRS has access to directories

### Logs to Check
- `C:\inetpub\logs\LogFiles\` (IIS logs)
- `C:\inetpub\wwwroot\tsic-api\logs\` (API logs)
- Browser developer tools

## Future Enhancements

### SSL/HTTPS Setup
```powershell
# Bind SSL certificate to sites
# Configure HTTPS redirection
```

### Load Balancing
- Set up multiple servers
- Configure load balancer
- Session state management

### CI/CD Integration
- GitHub Actions deployment
- Automated testing
- Rollback procedures

This methodology provides a complete, production-ready deployment strategy for your TSIC application stack.</content>
<parameter name="filePath">c:\Users\tgree\source\repos\TSIC-Core-Angular\docs\Complete-Deployment-Methodology.md