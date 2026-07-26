# =============================================================================
# __Restore-DevDb-From-Prod.ps1
# =============================================================================
# Restores the local dev TSICV5 (.\SS2016) from an hourly prod backup in
# C:\DBBackups\TSIC-Single — no manual copy, no RDBMS restart, no SSMS.
#
#   .\scripts\__Restore-DevDb-From-Prod.ps1              # latest backup, asks to confirm
#   .\scripts\__Restore-DevDb-From-Prod.ps1 -Yes         # latest backup, no prompt
#   .\scripts\__Restore-DevDb-From-Prod.ps1 -BackupFile C:\DBBackups\TSIC-Single\TSICV5_backup_2026_07_25_235901_2309265.bak
#
# What it does:
#   1. Picks the newest .bak in C:\DBBackups\TSIC-Single (or the one you name)
#   2. Verifies it is a TSICV5 backup (expected logical file names) before
#      touching anything
#   3. In ONE sqlcmd connection: SINGLE_USER WITH ROLLBACK IMMEDIATE (kicks the
#      dev-api pool / F5 debugger, no service restart), RESTORE WITH REPLACE +
#      MOVE to C:\DBFiles, back to MULTI_USER. One connection = nothing can
#      steal the single-user slot between steps.
#   4. Runs 00-postdev-db-restore-apppooluser.sql (re-creates IIS APPPOOL\dev-api
#      user + GRANT EXECUTE — prod backups only contain prod's pool users)
#   5. Smoke-checks: DB online + multi_user + dev-api user present
# =============================================================================

param(
    [string]$BackupFile,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'

$SqlInstance = '.\SS2016'
$BackupDir   = 'C:\DBBackups\TSIC-Single'
$DataFile    = 'C:\DBFiles\TSICV5.mdf'
$LogFile     = 'C:\DBFiles\TSICV5_1.ldf'
$PostSql     = Join-Path $PSScriptRoot '00-postdev-db-restore-apppooluser.sql'

# --- 1. Resolve backup file ---------------------------------------------------
if ($BackupFile) {
    if (-not (Test-Path $BackupFile)) { throw "Backup file not found: $BackupFile" }
    $bak = Get-Item $BackupFile
} else {
    $bak = Get-ChildItem (Join-Path $BackupDir '*.bak') |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $bak) { throw "No .bak files found in $BackupDir" }
}

$ageHours = [Math]::Round(((Get-Date) - $bak.LastWriteTime).TotalHours, 1)
$sizeGb   = [Math]::Round($bak.Length / 1GB, 2)
Write-Host ""
Write-Host "Backup : $($bak.FullName)" -ForegroundColor Cyan
Write-Host "Taken  : $($bak.LastWriteTime)  ($ageHours h ago, $sizeGb GB)" -ForegroundColor Cyan
Write-Host "Target : TSICV5 on $SqlInstance  ->  $DataFile" -ForegroundColor Cyan
Write-Host ""

# --- 2. Verify it really is a TSICV5 backup before destroying the local DB ----
$fileList = sqlcmd -S $SqlInstance -E -b -W -h -1 -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$($bak.FullName)';"
if ($LASTEXITCODE -ne 0) { throw "RESTORE FILELISTONLY failed — file unreadable or not a SQL backup." }
if (-not ($fileList -match '^TSIC2015V6 ')) {
    throw "Backup does not contain expected logical file 'TSIC2015V6' — refusing to restore. FILELISTONLY said:`n$($fileList -join "`n")"
}

# --- 3. Confirm ---------------------------------------------------------------
if (-not $Yes) {
    $answer = Read-Host "FULL REPLACE of local TSICV5 (kicks dev site + debugger connections). Type Y to proceed"
    if ($answer -ne 'Y' -and $answer -ne 'y') { Write-Host "Aborted."; exit 1 }
}

# --- 4. Restore (single connection: single_user -> restore -> multi_user) -----
$restoreSql = @"
IF DB_ID('TSICV5') IS NOT NULL
    ALTER DATABASE TSICV5 SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE TSICV5
    FROM DISK = N'$($bak.FullName)'
    WITH REPLACE, FILE = 1,
        MOVE N'TSIC2015V6'     TO N'$DataFile',
        MOVE N'TSIC2015V6_log' TO N'$LogFile',
        STATS = 10;
IF DATABASEPROPERTYEX('TSICV5','UserAccess') <> 'MULTI_USER'
    ALTER DATABASE TSICV5 SET MULTI_USER;
"@

Write-Host "Restoring (approx 1-3 min)..." -ForegroundColor Yellow
$sw = [System.Diagnostics.Stopwatch]::StartNew()
sqlcmd -S $SqlInstance -E -b -Q $restoreSql
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "RESTORE FAILED. TSICV5 may be left in SINGLE_USER or RESTORING state." -ForegroundColor Red
    Write-Host "  If SINGLE_USER : sqlcmd -S $SqlInstance -E -Q `"ALTER DATABASE TSICV5 SET MULTI_USER`"" -ForegroundColor Red
    Write-Host "  If RESTORING   : re-run this script (a clean restore clears it)." -ForegroundColor Red
    exit 1
}
$sw.Stop()
Write-Host ("Restore complete in {0:mm\:ss}." -f $sw.Elapsed) -ForegroundColor Green

# --- 5. Re-create local app pool user (prod backup only has prod's users) -----
Write-Host "Running 00-postdev-db-restore-apppooluser.sql..." -ForegroundColor Yellow
sqlcmd -S $SqlInstance -E -b -i $PostSql
if ($LASTEXITCODE -ne 0) {
    Write-Host "Post-restore user script FAILED — dev-api will get 'Login failed' until it runs clean." -ForegroundColor Red
    exit 1
}

# --- 6. Smoke check -----------------------------------------------------------
$check = sqlcmd -S $SqlInstance -E -b -W -h -1 -Q @"
SET NOCOUNT ON;
SELECT state_desc + '|' + user_access_desc FROM sys.databases WHERE name = 'TSICV5';
SELECT 'devapi-user-ok' FROM TSICV5.sys.database_principals WHERE name = N'IIS APPPOOL\dev-api';
"@
if (($check -notmatch 'ONLINE\|MULTI_USER') -or ($check -notmatch 'devapi-user-ok')) {
    Write-Host "SMOKE CHECK FAILED: $check" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "DONE. TSICV5 restored from prod backup taken $($bak.LastWriteTime) ($ageHours h old)." -ForegroundColor Green
Write-Host "dev.teamsportsinfo.com and F5 debugging will reconnect on next request." -ForegroundColor Green
