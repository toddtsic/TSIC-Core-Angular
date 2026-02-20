# IIS setup To‑Do for TSIC API and Angular

This checklist captures the hard‑won steps we used to get production login working, plus recommended hardening so it stays stable and secure.

## 1) IIS Application Pool identities

Both app pools should use **ApplicationPoolIdentity** (least-privilege default). Do NOT use LocalSystem.

### API app pool (`TSIC.Api`) — needs SQL Server login

The API uses Integrated Security (Windows Auth) to connect to SQL Server, so the app pool identity needs a database login.

1. IIS → Application Pools → `TSIC.Api` → Advanced Settings → Identity = ApplicationPoolIdentity
2. In SQL Server (`.\SS2016`), create a login for the virtual account and grant DB permissions:

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

### Troubleshooting: Login exists but API returns 500

If the SQL Server login already exists but the API throws `Cannot open database "TSICV5" requested by the login. The login failed for user 'IIS APPPOOL\...'`, the login is missing its **User Mapping** to the database.

**Fix in SSMS:**
1. Security → Logins → right-click the app pool login → **Properties**
2. Go to **User Mapping** page
3. Check the box next to **TSICV5**
4. In the role membership panel below, check **db_datareader** and **db_datawriter**
5. Click OK, then recycle the app pool

**Or via SQL:**
```sql
USE [TSICV5];
CREATE USER [IIS APPPOOL\TSIC.Api] FOR LOGIN [IIS APPPOOL\TSIC.Api];
EXEC sp_addrolemember N'db_datareader', N'IIS APPPOOL\TSIC.Api';
EXEC sp_addrolemember N'db_datawriter', N'IIS APPPOOL\TSIC.Api';
```

> **Note:** The app pool name in the login must match exactly (e.g., `TSIC.Api` vs `TSIC-API-CP`). Check IIS Manager → Application Pools to confirm the actual name.

### Angular app pool (`TSIC.App`) — NO SQL Server login needed

The Angular site serves static files only (HTML, JS, CSS, images). The app pool identity never connects to a database, so do **not** create a SQL Server login for `IIS APPPOOL\TSIC.App`.

1. IIS → Application Pools → `TSIC.App` → Advanced Settings → Identity = ApplicationPoolIdentity
2. .NET CLR Version = `No Managed Code`
3. No SQL Server login required — delete it if one exists

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
- Allowed origins include https://dev.teamsportsinfo.com, https://cp-ng.teamsportsinfo.com, and localhost:4200
- UseRouting → UseCors → UseAuthentication → UseAuthorization order
- Catch‑all OPTIONS endpoint ensures preflight gets headers behind IIS/ARR

### Critical: Remove the IIS CORS Module

IIS has its own CORS module that **conflicts** with ASP.NET Core's CORS middleware. If you see `No 'Access-Control-Allow-Origin' header` errors even though `Program.cs` is correctly configured, IIS is intercepting the preflight `OPTIONS` request before Kestrel ever sees it.

**Fix:** The API's `web.config` must remove the IIS CORS module so ASP.NET Core handles all CORS logic:

```xml
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <!-- Remove IIS CORS module — ASP.NET Core handles CORS -->
      <modules>
        <remove name="CorsModule" />
      </modules>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\TSIC.API.dll"
                  stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
```

This `web.config` is checked into source at `TSIC.API/web.config` so it survives `dotnet publish`. After editing the deployed copy, **recycle the app pool** for changes to take effect.

### Quick preflight test (PowerShell)

```powershell
Invoke-WebRequest -Uri 'https://devapi.teamsportsinfo.com/api/auth/login' -Method Options -Headers @{
  Origin = 'https://dev.teamsportsinfo.com'
  'Access-Control-Request-Method' = 'POST'
  'Access-Control-Request-Headers' = 'content-type, authorization'
} -UseBasicParsing
```
You should see `Access-Control-Allow-Origin`, `Access-Control-Allow-Methods`, and `Access-Control-Allow-Headers` in the response.

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

### `TSIC.Api` (API — .NET runtime)

- enable32BitAppOnWin64: **False** (run 64‑bit)
- Start Mode: **AlwaysRunning** — eliminates 3‑5s cold start when .NET + EF Core spin up after idle/recycle
- Idle Time‑out: **0** (disabled) — keeps the worker process alive; pairs with AlwaysRunning
- Recycling: keep defaults or schedule off‑hours recycle

> The IIS default `OnDemand` was designed for shared hosting with dozens of sites. On a dedicated server with one API, `AlwaysRunning` is the right choice — the ~50‑100 MB idle memory cost is negligible.

### `TSIC.App` (Angular — static files only)

- enable32BitAppOnWin64: **False**
- Start Mode: **OnDemand** (default) — no .NET runtime to warm up, so AlwaysRunning adds no benefit
- Idle Time‑out: default (20 min) is fine
- Recycling: keep defaults

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