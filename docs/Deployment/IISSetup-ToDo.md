# IIS setup To‑Do for TSIC API and Angular

This checklist captures the hard‑won steps we used to get production login working, plus recommended hardening so it stays stable and secure.

## 1) IIS Application Pool identity (API)

Recommendation: don’t run as LocalSystem long‑term. Prefer one of:
- ApplicationPoolIdentity with SQL permissions scoped to the app DB
- A dedicated domain/service account (or gMSA) with least privilege

Using ApplicationPoolIdentity (same machine SQL):
1. IIS → Application Pools → TSIC-API-CP → Advanced Settings → Identity = ApplicationPoolIdentity
2. In SQL Server (\\.\SS2016), create a login for the virtual account and grant DB permissions:

```sql
-- Create server login for the app pool identity
CREATE LOGIN [IIS APPPOOL\TSIC-API-CP] FROM WINDOWS;

-- Map to DB and grant least privilege
USE [TSICV5];
CREATE USER [IIS APPPOOL\TSIC-API-CP] FOR LOGIN [IIS APPPOOL\TSIC-API-CP];
EXEC sp_addrolemember N'db_datareader', N'IIS APPPOOL\TSIC-API-CP';
EXEC sp_addrolemember N'db_datawriter', N'IIS APPPOOL\TSIC-API-CP';
-- If the app needs stored procedures:
GRANT EXECUTE ON SCHEMA::dbo TO [IIS APPPOOL\TSIC-API-CP];
```

Remote SQL Server? Use a domain/service account instead of the local IIS AppPool virtual account, then grant the same least‑privilege roles to that domain user.

## 2) Store secrets and connection strings outside appsettings.json

Set environment variables on the server for production overrides:
- ConnectionStrings__DefaultConnection
- JwtSettings__SecretKey
- JwtSettings__Issuer
- JwtSettings__Audience

Example (PowerShell, Machine scope):
```powershell
[System.Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Server=.\\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true', 'Machine')
[System.Environment]::SetEnvironmentVariable('JwtSettings__SecretKey', '<strong-secret-here>', 'Machine')
[System.Environment]::SetEnvironmentVariable('JwtSettings__Issuer', 'TSIC.API', 'Machine')
[System.Environment]::SetEnvironmentVariable('JwtSettings__Audience', 'TSIC.Client', 'Machine')
```
Recycle the app pool after setting them.

## 3) Persist ASP.NET Core Data Protection keys

Prevents token/cookie issues across restarts and removes ephemeral key warnings.

Code (Program.cs) — reference only, no code change required now:
```csharp
using Microsoft.AspNetCore.DataProtection;
using System.IO;

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"D:\\Websites\\TSIC-API-CP\\keys"))
    .SetApplicationName("TSIC-API");
```
Server steps:
- Create folder D:\Websites\TSIC-API-CP\keys
- Grant Modify to the app pool identity (IIS AppPool\TSIC-API-CP, or your service account)

No-code implementation you can do right now (PowerShell):

```powershell
# 1) Create the keys folder
New-Item -ItemType Directory -Path 'D:\Websites\TSIC-API-CP\keys' -Force | Out-Null

# 2) Grant Modify to the app pool identity (ApplicationPoolIdentity case)
$path = 'D:\Websites\TSIC-API-CP\keys'
$principal = 'IIS AppPool\TSIC-API-CP'
$acl = Get-Acl $path
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($principal,'Modify','ContainerInherit,ObjectInherit','None','Allow')
$acl.SetAccessRule($rule)
Set-Acl -Path $path -AclObject $acl

# If you use a domain/service account instead, replace the principal, for example:
# $principal = 'YOURDOMAIN\\svc-tsic-api'
```

Verification (after you eventually add the code snippet later):
- The folder will populate with XML files named key-<guid>.xml.
- The Data Protection warnings in API logs will disappear on startup.

## 4) CORS and preflight

Already configured in `Program.cs`:
- Allowed origins include https://cp-ng.teamsportsinfo.com and localhost:4200
- UseRouting → UseCors → UseAuthentication → UseAuthorization order
- Catch‑all OPTIONS endpoint ensures preflight gets headers behind IIS/ARR

Quick preflight test (PowerShell):
```powershell
Invoke-WebRequest -Uri 'https://cp-api.teamsportsinfo.com/api/auth/login' -Method Options -Headers @{
  Origin = 'https://cp-ng.teamsportsinfo.com'
  'Access-Control-Request-Method' = 'POST'
  'Access-Control-Request-Headers' = 'content-type, authorization'
} -UseBasicParsing
```
You should see Access-Control-Allow-Origin/Methods/Headers in the response.

## 5) IIS features and site settings

- Install URL Rewrite Module (for Angular SPA routing)
- API site auth: Anonymous enabled; Windows Authentication typically disabled (JWT is used)
- Request Filtering: allow the OPTIONS verb (default is fine; verify if customized)
- stdout logs for API: `scripts/web.config.api` enables `stdoutLogEnabled="true"` to `./logs/stdout*` — useful for diagnostics; you can disable later
- IIS logging: enable W3C logs for both sites for status code tracing

Optional environment variables (no code change) to externalize secrets:
```powershell
[System.Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Server=.\\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true', 'Machine')
[System.Environment]::SetEnvironmentVariable('JwtSettings__SecretKey', '<strong-secret-here>', 'Machine')
[System.Environment]::SetEnvironmentVariable('JwtSettings__Issuer', 'TSIC.API', 'Machine')
[System.Environment]::SetEnvironmentVariable('JwtSettings__Audience', 'TSIC.Client', 'Machine')
# Recycle the API app pool after setting these
```

## 6) App Pool settings

- enable32BitAppOnWin64: False (run 64‑bit)
- Start Mode: AlwaysRunning (optional; reduces cold starts)
- Idle Time‑out: set per your preference (default 20 min). If you want always-on, disable idle timeout.
- Recycling: keep defaults or schedule off‑hours recycle

## 7) HTTPS and bindings

- Add/renew TLS certs, bind 443 for both sites
- Optional HSTS at the API if only HTTPS is used
- If you redirect HTTP→HTTPS, ensure preflight OPTIONS is not redirected by a proxy rule

## 8) Angular site configuration

- Use the cleaned `scripts/web.config.angular` (no stray trailer lines)
- SPA rewrite rule present; excludes `/api/` so API calls pass through
- Ensure URL Rewrite is installed or that rule will fail to load

## 9) Operational checks

- After deploy, tail API stdout logs: `D:\Websites\TSIC-API-CP\logs\stdout*`
- Confirm IIS W3C logs show 200/401/403 and no unexpected 500s
- Verify `Microsoft.Data.SqlClient.dll` version in the deployed API matches the package (5.2.2) and SNI exists at `runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll`

## 10) Notes on LocalSystem

LocalSystem unblocked login because Integrated Security used the LocalSystem identity on SQL. It’s high‑privilege and hard to audit — use ApplicationPoolIdentity + SQL grants or a dedicated domain/service account instead.

---
If you want, we can add the Data Protection snippet to `Program.cs` and include an IIS script to set environment variables and ACL the keys folder as part of deployment.