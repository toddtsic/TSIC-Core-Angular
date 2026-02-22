# Seq Structured Logging

## Overview

Seq is a self-hosted log server that receives structured log events from the .NET backend via Serilog. It provides a web UI for searching, filtering, and analyzing application logs.

- **Product**: Seq by Datalust
- **License**: Individual (free, single user, 50 GB storage)
- **Storage**: Local on disk (`C:\ProgramData\Seq\Data`) — no cloud

## Architecture

```
Dev Machine (204.17.37.204)          Prod Machine (204.17.37.202)
┌──────────────┐                     ┌──────────────┐
│  TSIC.API    │                     │  TSIC.API    │
│  (Serilog)   │                     │  (Serilog)   │
└──────┬───────┘                     └──────┬───────┘
       │ localhost:5341                      │ https://seq.teamsportsinfo.com
       ▼                                     │
┌──────────────┐                             │
│  IIS Reverse │ ◄───────────────────────────┘
│  Proxy (443) │
└──────┬───────┘
       │ localhost:5341
       ▼
┌──────────────┐
│  Seq Server  │
│  (Windows    │
│   Service)   │
│  Port 5341   │
└──────────────┘
```

Both machines send logs to the single Seq instance on the dev machine.
IIS reverse proxy terminates SSL and forwards to Seq on localhost.

## Access

| Item | Value |
|------|-------|
| Web UI (local) | `http://localhost:5341` |
| Web UI (remote) | `https://seq.teamsportsinfo.com` |
| Credentials | `admin` / `xxxxx` |

## IIS Reverse Proxy Setup

The Seq web UI and ingestion API are exposed over HTTPS via an IIS reverse proxy:

- **IIS Site**: `Seq` → `C:\Websites\Seq` (empty folder, proxy only)
- **Binding**: `https://seq.teamsportsinfo.com:443` with `*.teamsportsinfo.com` wildcard cert
- **URL Rewrite rule**: Reverse proxy to `localhost:5341` with SSL offloading enabled
- **Requires**: IIS ARR (Application Request Routing) 3.0 + URL Rewrite modules
- **DNS**: A record `seq.teamsportsinfo.com` → `204.17.37.204` (Network Solutions)

Port 5341 is NOT exposed externally — all remote access goes through HTTPS 443.

## Configuration

### Backend (appsettings.json)

```json
"Seq": {
    "ServerUrl": "http://localhost:5341"
}
```

For the prod machine, `appsettings.Production.json` overrides the URL (file already created):

```json
{
  "Seq": {
    "ServerUrl": "https://seq.teamsportsinfo.com"
  }
}
```

File: `TSIC.API/appsettings.Production.json` — .NET layers this automatically when `ASPNETCORE_ENVIRONMENT=Production`.

### Serilog Sink (Program.cs)

```csharp
.WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341")
```

### Firewall

Port 5341 is blocked from external access. All traffic routes through HTTPS 443 via the IIS reverse proxy. Standard IIS HTTPS firewall rules (port 443) are already in place.

## Installation

Seq is a Windows service installed via MSI. One install per machine that **hosts** Seq (not per machine that sends logs).

1. Download MSI from [datalust.co/download](https://datalust.co/download)
2. Run installer, accept defaults
3. Seq starts automatically as a Windows service
4. Browse to `http://localhost:5341` to verify

Machines that only **send** logs (prod) need no Seq installation — just the `Serilog.Sinks.Seq` NuGet package (already included in the project).

## What Gets Logged

| Level | What triggers it |
|-------|-----------------|
| Error | Exceptions, HTTP 500 responses |
| Warning | HTTP 400 responses, slow requests (>500ms) |
| Information | All HTTP requests (method, path, status, elapsed) |
| Debug | Detailed diagnostics (dev only) |

### Serilog Level Filters (appsettings.json)

```json
"Serilog": {
    "MinimumLevel": {
        "Default": "Information",
        "Override": {
            "Microsoft.AspNetCore": "Warning",
            "Microsoft.EntityFrameworkCore": "Warning",
            "System": "Warning"
        }
    }
}
```

## Common Queries

Type these in the Seq filter bar:

| What you want | Query |
|---|---|
| All errors | `@Level = 'Error'` |
| HTTP 500s | `StatusCode >= 500` |
| Slow requests | `Elapsed > 1000` |
| Specific endpoint | `RequestPath like '/api/payment%'` |
| Combined | `@Level = 'Error' and RequestPath like '/api/auth%'` |
| By source class | `SourceContext = 'TSIC.API.Services.PaymentService'` |

## Backup

Add `C:\ProgramData\Seq\Data` to iDrive backup locations. This contains all log data and Seq configuration.

## Retention

Configure retention policies in Seq UI (Settings > Retention):
- Recommended: keep errors 90 days, everything else 14 days
- Seq manages disk space automatically based on these policies

## Previous Setup (Removed)

The project previously used a SQL Server sink (`logs.AppLog` table) with a built-in Angular log viewer. This was replaced by Seq in February 2026. The `scripts/create-logs-schema.sql` file remains for reference. The `logs.AppLog` table can be dropped from the database when convenient.
