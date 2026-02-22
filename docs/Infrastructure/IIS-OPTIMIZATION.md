# IIS Optimization — TSIC Application

**Status**: Applied
**Date**: 2026-02-22
**Applies to**: Dev (`dev.teamsportsinfo.com`) and Prod (`teamsportsinfo.com`)

---

## Architecture

```
Browser → IIS (HTTPS/SNI) → ASP.NET Core Module V2 (in-process) → Kestrel → App
Browser → IIS (HTTPS/SNI) → Static Files (Angular SPA)
```

- **Hosting model**: `inprocess` — IIS hosts the .NET runtime directly (no reverse-proxy hop)
- **App pools**: `TSIC.Api` and `TSIC.App`, both 64-bit, No Managed Runtime, ApplicationPoolIdentity
- **Setup script**: `scripts/Setup-IIS-Server.ps1 -Environment Dev|Prod`

---

## Optimizations Applied

### 1. Stdout Logging Disabled

**Files**: `web.config`, `scripts/web.config.api`

```xml
<aspNetCore ... stdoutLogEnabled="false" ... />
```

Stdout logging writes to disk on every request. With Seq handling structured logging,
this is unnecessary overhead. Only flip to `true` temporarily to diagnose startup
failures where the process crashes before connecting to Seq.

### 2. Static File Caching (Angular)

**File**: `scripts/web.config.angular`

```xml
<staticContent>
    <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="365.00:00:00" />
</staticContent>
```

Angular production builds use content-hashed filenames (`main.abc123.js`), so aggressive
caching is safe — deploying new code automatically busts the cache because the filename
changes. Without this header, browsers re-download JS/CSS bundles on every navigation.

### 3. Response Compression (API)

**File**: `Program.cs`

```csharp
// Service registration
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes;
});

// Middleware (before UseRouting)
app.UseResponseCompression();
```

Compresses JSON API responses with Brotli (fastest) and Gzip (smallest). Typical JSON
compresses ~70%, reducing bandwidth and improving perceived speed on slower connections.

### 4. Deploy Warmup Request

**File**: `scripts/1-Build-And-Deploy-Local.ps1`

The deploy script now fires a warmup HTTP request after starting IIS sites. This
eliminates the cold-start penalty on the first real user request.

**Why cold start is slow**: When IIS starts the worker process, no .NET code has
actually executed yet. The first HTTP request triggers:

1. JIT compilation (IL → native machine code for every method called)
2. DI container initialization (100+ service registrations)
3. EF Core model building (entity scanning, query compilation)
4. Serilog/Seq sink connection
5. Identity framework schema validation

This takes 3–10 seconds. The warmup request absorbs that cost during deployment so
the first real request is fast.

### 5. Deprecated Scripts Removed

Deleted `Setup-IIS-For-Angular.ps1` and `Setup-IIS-For-TSIC.ps1` — both were marked
deprecated and the old TSIC script granted `FullControl` to `IIS_IUSRS` (security risk).
The consolidated `Setup-IIS-Server.ps1` correctly uses `ReadAndExecute` with targeted
`Modify` only on `logs/`, `keys/`, and statics directories.

---

## App Pool Configuration

For dev environments, ensure the API app pool won't shut down during idle periods:

| Setting | Value | Why |
|---|---|---|
| Idle Timeout | 0 (disabled) | Prevents cold start after inactivity |
| Start Mode | AlwaysRunning | Process starts with IIS, not on first request |
| Recycling | 29h (default) | Acceptable; warmup request mitigates |

These are set in IIS Manager > Application Pools > Advanced Settings, not in scripts
(machine-specific, not deployed).

---

## Troubleshooting

### App feels slow after deploy
Expected — cold start. The warmup step in the deploy script should handle this.
If it fails (cert issue, timeout), the first manual request absorbs the penalty.

### App feels slow after sitting idle
Check app pool idle timeout in IIS Manager. Set to 0 to disable.

### Need to debug an IIS startup crash
Temporarily flip `stdoutLogEnabled="true"` in the deployed `web.config` (not the
source file). Check `C:\Websites\TSIC.Api\logs\stdout_*.log`. Flip back when done.

### API responses are large/slow
Verify response compression is working:
```
curl -H "Accept-Encoding: gzip" -I https://devapi.teamsportsinfo.com/api/some-endpoint
```
Look for `Content-Encoding: gzip` or `br` in the response headers.

---

## Logging

Structured logging via Serilog → Seq. Stdout file logging is disabled (see optimization #1).

Full details: [SEQ-LOGGING.md](SEQ-LOGGING.md)

---

## File Reference

| File | Purpose |
|---|---|
| `scripts/Setup-IIS-Server.ps1` | One-time IIS setup (pools, sites, certs, SQL login) |
| `scripts/1-Build-And-Deploy-Local.ps1` | Build + deploy to local IIS with warmup |
| `scripts/web.config.api` | API web.config template (deployed by build script) |
| `scripts/web.config.angular` | Angular web.config template (SPA routing + caching) |
| `TSIC.API/web.config` | Source web.config (used by debugger/dev server) |
| `TSIC.API/Program.cs` | Response compression middleware registration |
