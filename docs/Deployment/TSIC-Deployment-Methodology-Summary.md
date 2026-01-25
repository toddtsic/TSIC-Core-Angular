# TSIC Complete Deployment Methodology

## Overview

This document outlines the comprehensive deployment methodology developed for publishing both the .NET API backend and Angular frontend to a local Windows Server (10.0.0.45). The methodology provides a production-ready, automated deployment pipeline for the complete TSIC application stack.

## Architecture

### Deployment Strategy: Separate IIS Sites
- **Angular Frontend**: `http://10.0.0.45` (port 80) - Main website served by IIS
- **.NET API Backend**: `http://10.0.0.45:5000` (port 5000) - API endpoints

### Benefits
- Independent deployment and scaling
- Different update cadences for frontend/backend
- Clear separation of concerns
- Easy CORS configuration
- Separate application pools for better resource management

## Files Created

### Documentation
- `docs/Complete-Deployment-Methodology.md` - This comprehensive guide
- `docs/Angular-IIS-Deployment-Guide.md` - Angular-specific IIS deployment guide

### Build Scripts (`scripts/` directory)
- `Build-DotNet-API.ps1` - Automated .NET API build and publish
- `Build-Angular.ps1` - Automated Angular production build
- `Deploy-TSIC.ps1` - Complete deployment orchestrator script
- `Setup-IIS-For-TSIC.ps1` - Server IIS configuration script
- `README.md` - Complete usage documentation

### Configuration Files
- `web.config.api` - IIS configuration for .NET API with CORS
- `web.config.angular` - IIS configuration for Angular with URL rewrite
- `environment.prod.ts` - Angular production environment configuration

## Quick Start Guide

### 1. Server Setup (Run on Windows Server 10.0.0.45)
```powershell
# Navigate to scripts directory
cd "C:\Path\To\TSIC\scripts"

# Configure IIS for both applications
.\Setup-IIS-For-TSIC.ps1
```

### 2. Deploy Application (Run from Development Machine)
```powershell
# Navigate to scripts directory
cd "C:\Users\tgree\source\repos\TSIC-Core-Angular\scripts"

# Build and deploy everything
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45"
```

## Detailed Process

### Build Phase

#### .NET API Build
```powershell
# Restore dependencies
dotnet restore

# Build solution
dotnet build --configuration Release --no-restore

# Publish API
dotnet publish src/backend/TSIC.API/TSIC.API.csproj --configuration Release --output "publish\api"
```

#### Angular Build
```powershell
# Install dependencies
npm install

# Build for production
npm run build --prod

# Output: dist/tsic-app/
```

### Deployment Phase

#### File Copy Operations
- API files → `\\10.0.0.45\c$\inetpub\wwwroot\tsic-api\`
- Angular files → `\\10.0.0.45\c$\inetpub\wwwroot\tsic-app\`

#### IIS Configuration Applied
- Separate application pools for API and frontend
- CORS enabled for API to accept Angular requests
- URL rewrite rules for Angular client-side routing
- MIME type mappings for static assets
- Security headers and compression

## Configuration Details

### IIS Site Structure
```
Windows Server (10.0.0.45)
├── IIS Application Pools
│   ├── TSIC-API-Pool (for .NET API)
│   └── TSIC-Angular-Pool (for static files)
├── IIS Sites
│   ├── Default Web Site (Port 80)
│   │   └── Physical Path: C:\inetpub\wwwroot\tsic-app
│   └── TSIC-API (Port 5000)
│       └── Physical Path: C:\inetpub\wwwroot\tsic-api
```

### Network Configuration
- **Frontend**: `http://10.0.0.45`
- **Backend API**: `http://10.0.0.45:5000`
- **Firewall**: Ports 80 and 5000 opened
- **CORS**: Configured for cross-origin requests

### Environment Configuration

#### Angular Production Environment
```typescript
export const environment = {
  production: true,
  apiUrl: 'http://10.0.0.45:5000/api'
};
```

#### .NET API CORS Policy
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

## Key Features

### Automation
- **One-command deployment**: Single script handles entire process
- **Build verification**: Scripts validate builds before deployment
- **Error handling**: Comprehensive error checking and reporting
- **Rollback ready**: File-based deployment allows easy rollback

### Security
- **CORS protection**: Properly configured cross-origin policies
- **Security headers**: X-Frame-Options, Content-Type-Options, etc.
- **Application isolation**: Separate application pools
- **Firewall configuration**: Minimal required ports opened

### Performance
- **Compression**: GZIP compression enabled for static assets
- **Caching**: Client-side caching configured for static files
- **Optimization**: Production builds with tree-shaking and minification

### Monitoring
- **IIS logging**: Comprehensive request/response logging
- **Health checks**: API health endpoint support
- **Error tracking**: Detailed error logging and debugging

## Usage Examples

### Basic Deployment
```powershell
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45"
```

### Advanced Options
```powershell
# Skip building, just deploy existing builds
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45" -SkipBuild

# Use debug configuration
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45" -Configuration "Debug"

# Skip tests during build
.\Deploy-TSIC.ps1 -ServerIP "10.0.0.45" -SkipTests
```

### Individual Component Builds
```powershell
# Build only .NET API
.\Build-DotNet-API.ps1 -Configuration "Release"

# Build only Angular
.\Build-Angular.ps1
```

## Troubleshooting

### Common Issues

1. **Server Connection Failed**
   - Verify server IP and network connectivity
   - Check administrative access to `\\server\c$`
   - Ensure Windows Firewall allows file sharing

2. **IIS Configuration Errors**
   - Run setup script as Administrator
   - Verify IIS features are installed
   - Check application pool identities

3. **Build Failures**
   - Ensure all prerequisites installed (.NET SDK, Node.js)
   - Check build logs for specific errors
   - Verify source code compiles locally

4. **CORS Issues**
   - Verify Angular environment points to correct API URL
   - Check CORS policy in API configuration
   - Ensure API is running on correct port

### Logs and Debugging

- **API Logs**: `C:\inetpub\wwwroot\tsic-api\logs\stdout\`
- **IIS Logs**: `C:\inetpub\logs\LogFiles\W3SVC*\*.log`
- **Build Logs**: Script console output
- **Angular Errors**: Browser developer tools

## Maintenance and Updates

### Regular Deployment
1. Make code changes in development
2. Commit changes to repository
3. Run deployment script: `.\Deploy-TSIC.ps1`
4. Test both frontend and API
5. Monitor logs for issues

### Backup Strategy
- IIS configuration exports
- Application file backups
- Database backups (when applicable)
- Configuration documentation

## Future Enhancements

### SSL/HTTPS Setup
- Certificate installation and binding
- HTTPS redirection configuration
- SSL certificate renewal automation

### Load Balancing
- Multiple server deployment
- Load balancer configuration
- Session state management

### CI/CD Integration
- GitHub Actions deployment workflows
- Automated testing integration
- Deployment approval processes

## Prerequisites Checklist

### Development Machine
- [ ] PowerShell 5.1+
- [ ] .NET 9.0 SDK
- [ ] Node.js and npm
- [ ] Angular CLI
- [ ] Administrative access to server

### Windows Server
- [ ] Windows Server with IIS
- [ ] .NET 9.0 Runtime
- [ ] URL Rewrite module
- [ ] Administrative privileges

## Testing and Validation

### Post-Deployment Tests
1. **Frontend Access**: `http://10.0.0.45`
2. **API Health**: `http://10.0.0.45:5000/health`
3. **API Calls**: Verify Angular can call API endpoints
4. **Routing**: Test Angular client-side routing
5. **Performance**: Check load times and responsiveness

### Automated Testing
- Unit tests run during build
- Integration tests for API endpoints
- End-to-end tests for complete workflows

## Support and Documentation

- **Scripts README**: `scripts/README.md`
- **IIS Setup Guide**: `docs/Angular-IIS-Deployment-Guide.md`
- **Troubleshooting**: Check script logs and IIS event viewer
- **Configuration Reference**: All web.config and environment files documented

This methodology provides a complete, production-ready deployment solution for the TSIC application stack, enabling reliable and automated deployments to your local Windows Server infrastructure.</content>
<parameter name="filePath">c:\Users\tgree\source\repos\TSIC-Core-Angular\docs\TSIC-Deployment-Methodology-Summary.md