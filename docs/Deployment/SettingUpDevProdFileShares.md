# Setting Up Dev/Prod File Shares & Deployment Pipeline

**Date**: 2026-03-13
**Status**: Planning — not yet implemented

---

## Overview

This document covers the transition from iDrive-based manual deployment to direct file share deployment between the dev machine and production server (TSIC-PHOENIX), plus hardening the existing DB backup file shares.

### Current State

| What | From | To | Method |
|------|------|----|--------|
| DB Backups | TSIC-PHOENIX | Dev machine (`C:\DBBackups\TSIC-Single`) | `C$` admin share (hourly) |
| Code Deploy | Dev machine | TSIC-PHOENIX | iDrive upload → RDP → restore → run script |

### Target State

| What | From | To | Method |
|------|------|----|--------|
| DB Backups | TSIC-PHOENIX | Dev machine (`C:\DBBackups\TSIC-Single`) | Named share `\\DEV-MACHINE\DBBackups` |
| Code Deploy | Dev machine | TSIC-PHOENIX (`E:\Websites`) | Named share `\\TSIC-PHOENIX\Deployments` |

---

## Network Topology

- Both machines are on the same local network, same ISP, same physical location
- TSIC-PHOENIX is reachable by hostname (sub-1ms latency)
- SMB (port 445) is already open between them (DB backups work)

---

## Part 1: Production Hostnames (Codename "Bear")

While legacy `www.teamsportsinfo.com` runs on TSIC-PHOENIX, the new Angular app uses coded hostnames to avoid conflict:

| Service | Dev Hostname | Prod Hostname |
|---------|-------------|---------------|
| Angular App | `dev.teamsportsinfo.com` | `bear.teamsportsinfo.com` |
| .NET API | `devapi.teamsportsinfo.com` | `bearapi.teamsportsinfo.com` |

At cutover (legacy retirement), change to `www.teamsportsinfo.com` / `api.teamsportsinfo.com`.

### What the prod deploy script handles automatically

The script `scripts/1-Build-And-Deploy-Prod.ps1` patches these differences at deploy time — no source file changes needed:

1. **Angular environment files** — `devapi` → `bearapi`, `dev` → `bear` (patched before build, reset after)
2. **appsettings.json paths** — `C:\Websites` → `E:\Websites` (patched after deploy)
3. **appsettings.json hostnames** — `dev`/`devapi` → `bear`/`bearapi` (patched after deploy)

### CORS origins (already added)

`Program.cs` allows all current and future origins:
- `localhost:4200` (dev server)
- `dev.teamsportsinfo.com` (dev IIS)
- `bear.teamsportsinfo.com` (prod codename)
- `www.teamsportsinfo.com` / `teamsportsinfo.com` (future cutover)

---

## Part 2: Harden Existing DB Backup Share (Dev Machine)

The DB backup from TSIC-PHOENIX currently writes to `\\DEV-MACHINE\C$\DBBackups\TSIC-Single`. The `C$` admin share exposes the **entire C: drive**.

### Steps

1. **Create named share on dev machine**
   - Right-click `C:\DBBackups\TSIC-Single` → Properties → Sharing → Advanced Sharing
   - Check "Share this folder"
   - Share name: `DBBackups`
   - Permissions: Remove `Everyone` → Add the account TSIC-PHOENIX's backup job runs as → Grant **Change**

2. **Update backup job on TSIC-PHOENIX**
   - Change target path from `\\DEV-MACHINE\C$\DBBackups\TSIC-Single` to `\\DEV-MACHINE\DBBackups`
   - Verify backups still arrive on schedule

3. **Disable admin shares (after verifying named shares work)**
   - Confirm no other tools/scripts use `C$` paths to this machine
   - Set registry key on dev machine:
     ```
     HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters
     AutoShareServer = 0 (DWORD)
     ```
   - Reboot to take effect
   - Admin shares (`C$`, `ADMIN$`) will no longer be created automatically

### Why this matters

Anyone with admin credentials and network access to the `C$` share can read/write every file on the drive — source code, credentials, database backups, everything. Named shares limit exposure to a single folder.

---

## Part 3: Create Deploy Share (TSIC-PHOENIX)

### Steps

1. **Create deploy folder on TSIC-PHOENIX**
   - `E:\Shares\Deployments` (or `E:\Deployments` — your preference)

2. **Share it**
   - Share name: `Deployments`
   - Network path: `\\TSIC-PHOENIX\Deployments`
   - Permissions: Grant **Change** to the dev machine's account only

3. **Test access from dev machine**
   ```powershell
   # Should list contents without error
   dir \\TSIC-PHOENIX\Deployments
   ```

4. **Update prod deploy script** (`scripts/1-Build-And-Deploy-Prod.ps1`)
   - Currently targets `E:\Websites` directly (assumes running on prod)
   - Update to copy via share: `\\TSIC-PHOENIX\Deployments`
   - Add a step to move from staging to `E:\Websites` (either via share or a pickup script on TSIC-PHOENIX)

5. **Disable `E$` admin share on TSIC-PHOENIX** (same registry key as above)

---

## Part 4: IIS Site Setup on TSIC-PHOENIX

When ready to go live, the prod server needs:

1. **DNS** — Point `bear.teamsportsinfo.com` and `bearapi.teamsportsinfo.com` to TSIC-PHOENIX's public IP
2. **SSL certs** — Obtain certs for both bear hostnames
3. **IIS sites** — Create two sites:
   - `TSIC.App` bound to `bear.teamsportsinfo.com:443`
   - `TSIC.Api` bound to `bearapi.teamsportsinfo.com:443`
4. **IIS app pools** — One per site, .NET CLR version = No Managed Code (for API: .NET Core)
5. **DB connection string** — Verify `.\SS2016` is correct on TSIC-PHOENIX (or update instance name)
6. **File paths** — Ensure `E:\Websites\TSIC-STATICS\BannerFiles` exists

Scripts for IIS site creation may already exist — check `scripts/` folder.

---

## Scripts Reference

| Script | Purpose | Runs On |
|--------|---------|---------|
| `scripts/1-Build-And-Deploy-Local.ps1` | Build + deploy to dev IIS (`C:\Websites`) | Dev machine |
| `scripts/1-Build-And-Deploy-Prod.ps1` | Build + deploy to prod IIS (`E:\Websites`) with hostname/path patching | Dev machine → TSIC-PHOENIX |
| `scripts/web.config.angular` | IIS config template for Angular (includes `index.html` no-cache rule) | Copied by deploy scripts |
| `scripts/web.config.api` | IIS config template for .NET API | Copied by deploy scripts |

---

## Cutover Checklist (Bear → WWW)

When retiring legacy and switching to production hostnames:

1. Update `1-Build-And-Deploy-Prod.ps1` — change 4 variables at top:
   ```powershell
   $ProdAppHost  = "www.teamsportsinfo.com"
   $ProdApiHost  = "api.teamsportsinfo.com"
   ```
2. Update IIS bindings on TSIC-PHOENIX to the new hostnames
3. Update DNS — point `www` and `api` to TSIC-PHOENIX
4. SSL certs for the new hostnames
5. CORS origins already include `www` — no code change needed

---

## Security Summary

| Risk | Before | After |
|------|--------|-------|
| Admin share exposure | `C$` / `E$` wide open to admin accounts | Disabled; named shares scoped to specific folders |
| Deploy path | Code transits iDrive cloud servers | Code stays on local network only |
| Credential scope | Admin share = full drive access | Named share = one folder per purpose |
| Manual steps | RDP → restore from iDrive → run script | One script, direct push |
