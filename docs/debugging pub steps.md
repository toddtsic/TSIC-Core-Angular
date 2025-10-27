# Debugging Publish/Deploy Steps (TSIC API + Angular)

This note documents a reliable way to build, package, deploy, and verify the API/Angular app, plus quick checks to catch stale or mismatched server bits (like SqlClient) immediately.

## Context
- Central package management is enabled via `Directory.Packages.props`.
- SqlClient should be 5.2.2. Locally, publish output contains `Microsoft.Data.SqlClient.dll` with AssemblyVersion 5.0.0.0 (expected for package 5.x), and native SNI in `runtimes/win-x64/native`.
- The server error “Could not load file or assembly 'Microsoft.Data.SqlClient, Version=5.0.0.0'” was caused by a stale `Microsoft.Data.SqlClient.dll` 5.1.6 deployed on IIS, not a build/package version issue.

## Build + Package (Dev machine)
Run from `repo/scripts`:

```powershell
# From repo/scripts
.\n1-Build-And-Package.ps1
```

This performs:
1) Build + publish API to `publish\api`
2) Build Angular to `publish\angular`
3) Create timestamped deployment folder under `tsic-deployment-current\YYYYMMDD-HHMMSS` with:
   - `api\` (API artifacts)
   - `angular\` (Angular static files)
   - `deploy-to-server.ps1` (copy of template with validation)
   - `README.txt`

## Quick local verification (publish output)
- Ensure these exist in `publish\api`:
  - `Microsoft.Data.SqlClient.dll`
  - `runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll`
  - `TSIC.API.deps.json` (references `"Microsoft.Data.SqlClient": "5.2.2"`)

Optional sanity command (PowerShell) in `publish\api`:

```powershell
Get-Item .\Microsoft.Data.SqlClient.dll | % { $_.VersionInfo | Select FileVersion, ProductVersion }
Select-String -Path .\TSIC.API.deps.json -Pattern '"Microsoft.Data.SqlClient":\s*"5.2.2"' -Context 0,3
Test-Path .\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll
```

## Restore + Deploy (Server)
1) Restore the latest `tsic-deployment-current\YYYYMMDD-HHMMSS` from iDrive to the server.
2) From inside that restored folder, run the deploy script (it stops sites, backs up old deployments, copies fresh files, and validates):

```powershell
# In restored package folder that contains api\ and angular\
.\ndeploy-to-server.ps1 -DriveOverride D -ApiTargetOverride 'D:\Websites\TSIC-API-CP' -AngularTargetOverride 'D:\Websites\TSIC-Angular-CP'
```

The script now prints a validation block, for example:
- Microsoft.Data.SqlClient.dll FileVersion and ProductVersion
- Whether `deps.json` references `Microsoft.Data.SqlClient` 5.2.2
- Presence of native SNI (x64/x86)
- App pool 32-bit flag (for awareness)

If you see `5.1.6` after deploy, you deployed an old package or didn’t clear the site folder.

## Must-do checks on the server (when errors happen)
- Verify IIS site physical path points where you expect:

```powershell
Import-Module WebAdministration
(Get-Website -Name 'TSIC-API-CP').PhysicalPath
```

- Inspect the deployed SqlClient version and deps.json (should show 5.2.2):

```powershell
Get-Item 'D:\Websites\TSIC-API-CP\Microsoft.Data.SqlClient.dll' | % { $_.VersionInfo | Select FileVersion, ProductVersion }
Select-String -Path 'D:\Websites\TSIC-API-CP\TSIC.API.deps.json' -Pattern '"Microsoft.Data.SqlClient":\s*"5.2.2"' -Context 0,3
Test-Path 'D:\Websites\TSIC-API-CP\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll'
```

- App pool bitness (should generally be 64-bit):

```powershell
Import-Module WebAdministration
Get-ItemProperty 'IIS:\AppPools\TSIC-API-Pool' -Name enable32BitAppOnWin64
# If True, consider disabling:
# Set-ItemProperty 'IIS:\AppPools\TSIC-API-Pool' -Name enable32BitAppOnWin64 -Value False
# Restart-WebAppPool 'TSIC-API-Pool'
```

## Clean redeploy procedure (when a stale DLL is suspected)
```powershell
Import-Module WebAdministration
Stop-Website 'TSIC-API-CP'
Remove-Item 'D:\Websites\TSIC-API-CP\*' -Recurse -Force
Copy-Item '.\api\*' 'D:\Websites\TSIC-API-CP' -Recurse -Force
Start-Website 'TSIC-API-CP'
iisreset
```
Re-check version and deps.json afterwards.

## Common pitfalls
- Deploying an older timestamped package from iDrive rather than the latest one
- Not fully clearing the site folder, leaving stale DLLs behind (e.g., SqlClient 5.1.6)
- IIS site pointing to an unexpected physical path
- App pool forced into 32-bit while only x64 native SNI is present

## After deployment
- Test login from the Angular app and tail API logs:

```powershell
Get-Content 'D:\Websites\TSIC-API-CP\logs\*.log' | Select-Object -Last 100
```

- If CORS/auth errors remain, share the last 100 log lines and browser console output.

## TL;DR
- The build and local publish were correct (SqlClient 5.2.2). The server failure was a stale 5.1.6 DLL in IIS site folder. Clear the folder, deploy the latest package, and use the deploy script’s validation output to confirm the right bits are live before testing.
