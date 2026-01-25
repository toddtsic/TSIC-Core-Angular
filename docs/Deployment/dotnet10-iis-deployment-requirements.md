# .NET 10 IIS Deployment Requirements

## Overview

The TSIC-Core-Angular solution has been upgraded from .NET 9 to .NET 10. **Before deploying the upgraded application to IIS**, the server must have the .NET 10 Runtime installed.

## Required Server Updates

### 1. Install .NET 10 Hosting Bundle

The IIS server requires the **ASP.NET Core 10.0 Runtime & Hosting Bundle**.

**Download Location:**
- Official Microsoft Download: [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
- Look for: **"Hosting Bundle"** under the ASP.NET Core Runtime section

**Installation Steps:**

1. Download the Windows Hosting Bundle installer (`dotnet-hosting-10.0.x-win.exe`)
2. Run the installer with administrator privileges
3. Accept the license terms and complete the installation
4. Restart IIS after installation completes

**Command to restart IIS:**
```powershell
# Stop IIS
iisreset /stop

# Start IIS
iisreset /start
```

### 2. Verify Installation

After installing the hosting bundle, verify it's correctly installed:

```powershell
# Check installed .NET runtimes
dotnet --list-runtimes

# You should see entries like:
# Microsoft.AspNetCore.App 10.0.x
# Microsoft.NETCore.App 10.0.x
```

**Alternative verification:**
- Check the registry: `HKEY_LOCAL_MACHINE\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost`
- Verify the hosting bundle is listed in "Programs and Features"

### 3. Application Pool Configuration

No changes are required to your existing IIS Application Pool configuration. The pool should remain configured as:
- **.NET CLR Version:** No Managed Code
- **Managed Pipeline Mode:** Integrated

The .NET 10 hosting bundle handles runtime versioning automatically through the application's configuration.

## Deployment Checklist

Before deploying the .NET 10 upgraded application:

- [ ] Download .NET 10 Hosting Bundle installer
- [ ] Install the hosting bundle on the IIS server
- [ ] Restart IIS (`iisreset`)
- [ ] Verify .NET 10 runtime is installed (`dotnet --list-runtimes`)
- [ ] Stop the application pool (`Stop-WebAppPool -Name "YourAppPoolName"`)
- [ ] Deploy the upgraded application files
- [ ] Start the application pool (`Start-WebAppPool -Name "YourAppPoolName"`)
- [ ] Test the application endpoints
- [ ] Verify `/swagger/v1/swagger.json` endpoint (if using Development environment)

## Troubleshooting

### Application Won't Start After Deployment

**Error:** "500.31 - Failed to load ASP.NET Core runtime"

**Solution:**
- Verify .NET 10 Hosting Bundle is installed: `dotnet --list-runtimes`
- Ensure IIS was restarted after installing the hosting bundle
- Check the application's `web.config` points to the correct framework version

### Compatibility Notes

- **Zero impact on existing applications:** Installing .NET 10 Hosting Bundle does NOT affect existing .NET 9, 8, or earlier applications
- **Automatic version selection:** Each application continues to use the runtime version it was built with
- **Side-by-side deployment:** .NET 8, 9, and 10 runtimes coexist on the same server
- **No forced upgrades:** Applications remain on their current .NET version until explicitly rebuilt targeting a newer version
- **Safe installation:** You can install .NET 10 on a production server running .NET 9 applications without risk

## Package Updates Summary

The following framework packages were upgraded as part of the .NET 10 migration:

| Package | Previous Version | New Version |
|---------|-----------------|-------------|
| Microsoft.AspNetCore.* | 9.0.x | 10.0.0 |
| Microsoft.EntityFrameworkCore.* | 9.0.9 | 10.0.0 |
| Microsoft.Extensions.Http | 9.0.9 | 10.0.0 |
| System.Net.Http.Json | 9.0.9 | 10.0.0 |
| Microsoft.Data.SqlClient | 5.2.2 | 6.1.1 |
| Microsoft.CodeAnalysis.* | 4.8.0 | 4.14.0 |

## OpenAPI/Swagger Changes

**Important:** The application now uses Microsoft's built-in OpenAPI generator instead of Swashbuckle.

- **Swagger JSON endpoint:** Still available at `/swagger/v1/swagger.json`
- **Swagger UI:** Removed (not needed for production deployments)
- **TypeScript generation:** Fully compatible with NSwag tooling

## Additional Resources

- [ASP.NET Core 10.0 Release Notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/README.md)
- [.NET 10 Download Page](https://dotnet.microsoft.com/download/dotnet/10.0)
- [ASP.NET Core 10.0 Breaking Changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0)

## Support

For deployment issues specific to your environment, refer to:
- [Complete-Deployment-Methodology.md](Complete-Deployment-Methodology.md)
- [IIS-Setup-Guide.md](IIS-Setup-Guide.md)
- [TSIC-Deployment-Methodology-Summary.md](TSIC-Deployment-Methodology-Summary.md)
