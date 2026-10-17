# TSICDev-Build -- everything that builds TSIC-OVH-DEV

Each script sets up one layer of the box. Run them in order, in an **elevated**
PowerShell. Every script is safe to re-run: it changes only what differs.

| # | Script | Sets up |
|---|---|---|
| 01 | `01-Install-SqlServer.ps1` | SQL Server 2019 Developer, instance `SS2016`, data folder |
| 02 | `02-Install-IIS.ps1` | IIS, URL Rewrite 2.1, .NET Hosting Bundle |
| 03 | `03-Import-Certificate.ps1 -PfxPath <pfx>` | `*.teamsportsinfo.com` certificate (asks for the password) |
| 04 | `04-Create-AppPools.ps1` | `dev-api`, `dev-app` pools |
| 05 | `05-Create-Folders.ps1` | site, statics and deploy-backup folders + permissions |
| 06 | `06-Create-Sites.ps1` | `dev-api`, `dev-app` sites, bindings, certificate binding |
| 07 | `07-Create-SqlLogin.ps1` | SQL login for the `dev-api` pool |
| 08 | `08-Setup-Phoenix-Sftp.ps1` | sshd, `phoenix` SFTP account, chroot, keys |
| 09 | `09-Install-Seq.ps1 [-TemplatesDir <dir>]` | ARR proxy, Seq service, `Seq` site for seq.teamsportsinfo.com, retention (asks for the Seq admin password) |
| 10 | `10-Create-Statics-Site.ps1` | `dev-statics` site for devstatics.teamsportsinfo.com over `C:\Websites\TSIC-STATICS-DEV` (needs the iDrive statics restore first; uses prod's own web.config rules) |
| -- | `Apply-Dev-Secrets.ps1` | secrets + `ASPNETCORE_ENVIRONMENT` on `dev-api` (from `app-pool-secrets.ps1`) |

Then, from `..\TSICDev-Ops`: `Restore-Databases.ps1`, then `Deploy-Dev.ps1`.

Production deploy: `..\TSICDev-Ops\Deploy-Prod.ps1` here (build + publish to
`C:\TSIC\Phoenix\deploy`), then on PHOENIX in `E:\TSIC-Incoming`:
`.\Pull-DevFiles.ps1`, then `.\Stage-PulledDevFiles.ps1`, then `Go.ps1` as always.
Fallback: SEDONA's `scripts\1-Build-And-Deploy-Prod.ps1`, unchanged.

## Checking a built box

Every script takes `-CheckOnly`: it reports what it **would** change and changes
nothing. On a correctly built box every line reads `same` and each script ends
`box matches the script` (exit 0). A `WOULD` line means the box and the script
disagree (exit 2) -- find out which one is right before running it for real.

## Settings

All names, paths and versions live in `..\TSICDev-Ops\TSICDev-Config.ps1`.

## Files that are not in git

- `app-pool-secrets.ps1` (this folder) -- gitignored. The only copy of the dev
  secrets on the box. Kept in step with what `Apply-Dev-Secrets.ps1` applies.
- `FirebaseAuth_TSICEvents.json` and `FirebaseAuth_TSICTeams.json` in
  `TSIC-Core-Angular\src\backend\TSIC.API\` -- gitignored Google service-account
  files. The build copies them into the API output; without them Staging cannot
  send push notifications (Seq startup line `firebase: ... fileExists=false`).
  Copied from SEDONA 10-17. Note: Staging's database is a prod copy, so its
  `Device_Teams` push tokens are REAL parents' devices.

## Not scripted yet

- .NET SDK and Node.js (needed by `Deploy-Dev.ps1` to build). Present on the box
  (SDK 10.0.402, Node 24.22.0) but installed by hand.
- Temporary until the `dev` / `devapi` DNS change: two `127.0.0.1` lines in this
  box's hosts file so deploy checks hit this box, not SEDONA. Remove them after
  the DNS change.

ASCII only in every script here -- see the note at the top of `TSICDev-Config.ps1`.
