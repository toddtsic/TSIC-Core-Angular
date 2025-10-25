# TSIC Deployment Scripts

This directory contains PowerShell scripts for building and deploying the complete TSIC application stack (.NET API + Angular frontend) to your Windows Server.

## Scripts Overview

### Build Scripts
- `Build-DotNet-API.ps1` - Builds and publishes the .NET API
- `Build-Angular.ps1` - Builds the Angular application for production

### Deployment Scripts
- `Deploy-TSIC.ps1` - Complete deployment script (builds and deploys both components)
- `Setup-IIS-For-TSIC.ps1` - IIS configuration script (run on server)

### Configuration Files
- `web.config.api` - IIS configuration for .NET API
- `web.config.angular` - IIS configuration for Angular app

## Quick Start Deployment

### 1. Initial Server Setup
Run this on your Windows Server (10.0.0.45) as Administrator:

```powershell
# Copy the setup script to your server
# Run it to configure IIS
.\Setup-IIS-For-TSIC.ps1
```

### 2. Deploy Application
Run this from your development machine:

```powershell
# Build and deploy everything
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45"
```

## Detailed Usage

### Building Components Separately

#### Build .NET API Only
```powershell
.\Build-DotNet-API.ps1 -Configuration "Release"
```

#### Build Angular Only
```powershell
.\Build-Angular.ps1
```

### Advanced Deployment Options

```powershell
# Deploy without rebuilding
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45" -SkipBuild

# Deploy with custom configuration
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45" -Configuration "Debug"

# Skip tests during build
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45" -SkipTests
```

## Architecture

### Deployment Structure
```
Windows Server (10.0.0.45)
├── IIS Sites
│   ├── Default Web Site (Port 80) - Angular Application
│   │   └── C:\inetpub\wwwroot\tsic-app\
│   └── TSIC-API (Port 5000) - .NET API
│       └── C:\inetpub\wwwroot\tsic-api\
```

### Network Configuration
- **Angular Frontend**: `http://10.0.0.45`
- **API Backend**: `http://10.0.0.45:5000`
- **CORS**: Configured to allow Angular to call API

## Prerequisites

### Development Machine
- PowerShell 5.1 or higher
- .NET 9.0 SDK
- Node.js and npm
- Angular CLI
- Access to server file shares (`\\10.0.0.45\c$`)

### Windows Server
- Windows Server with IIS
- .NET 9.0 Runtime
- URL Rewrite module for IIS
- Administrative access

## Configuration

### Environment-Specific Settings

#### Angular Environment (src/environments/environment.prod.ts)
```typescript
export const environment = {
  production: true,
  apiUrl: 'http://10.0.0.45:5000/api'
};
```

#### .NET API CORS (Program.cs)
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins("http://10.0.0.45")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

## Troubleshooting

### Common Issues

1. **Access Denied to Server**
   - Ensure you have administrative access to `\\10.0.0.45\c$`
   - Check Windows Firewall settings

2. **Build Failures**
   - Verify all prerequisites are installed
   - Check build logs for specific errors

3. **IIS Configuration Issues**
   - Run `Setup-IIS-For-TSIC.ps1` as Administrator
   - Check IIS Manager for site status

4. **CORS Errors**
   - Verify CORS policy in API configuration
   - Check Angular environment settings

### Logs and Debugging

- **API Logs**: `C:\inetpub\wwwroot\tsic-api\logs\`
- **IIS Logs**: `C:\inetpub\logs\LogFiles\`
- **Build Logs**: Check script output in PowerShell

## Security Considerations

- Configure SSL/HTTPS in production
- Use proper firewall rules
- Set appropriate file permissions
- Keep server and applications updated
- Use strong authentication for API endpoints

## Maintenance

### Updating Deployment
1. Make code changes
2. Run deployment script: `.\Deploy-TSIC.ps1`
3. Test both applications
4. Monitor logs for issues

### Backup Strategy
- Backup IIS configuration
- Backup application files
- Backup databases (if applicable)
- Document configuration changes

## Support

For issues with these scripts:
1. Check the troubleshooting section
2. Review script logs
3. Verify prerequisites
4. Test individual components

The deployment methodology is designed to be repeatable, automated, and reliable for your TSIC application stack.</content>
<parameter name="filePath">c:\Users\tgree\source\repos\TSIC-Core-Angular\scripts\README.md