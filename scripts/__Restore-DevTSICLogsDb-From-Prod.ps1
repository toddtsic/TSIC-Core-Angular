# =============================================================================
# __Restore-DevTSICLogsDb-From-Prod.ps1
# =============================================================================
# Restores the local dev TSICLogs (.\SS2016) from a DAILY prod backup in
# C:\DBBackups\TSIC-DAILY -- the TSICLogs twin of __Restore-DevDb-From-Prod.ps1.
#
#   .\scripts\__Restore-DevTSICLogsDb-From-Prod.ps1          # latest, asks to confirm
#   .\scripts\__Restore-DevTSICLogsDb-From-Prod.ps1 -Yes     # latest, no prompt
#   .\scripts\__Restore-DevTSICLogsDb-From-Prod.ps1 -BackupFile C:\DBBackups\TSIC-DAILY\TSICLogs_backup_2026_09_05_010000_1234567.bak
#
# Differences from the TSICV5 script, all deliberate:
#   * DAILY, not hourly. A 24-30h old backup is NORMAL here and is not warned
#     about; only one older than -MaxAgeHours (default 48) warns.
#   * Searches RECURSIVELY and filters on 'TSICLogs*.bak'. The DAILY folder is
#     shared with other databases' daily backups, and a maintenance plan may or
#     may not write per-database subfolders. Newest-file-wins alone would pick a
#     TSICV5 backup and then die at the FILELISTONLY guard.
#   * Post-restore grant is SELECT + INSERT ON SCHEMA::logs -- NOT
#     db_datareader/db_datawriter. See the .sql for why.
#   * No index re-creation step. TSICLogs indexes are created by
#     15-create-tsiclogs.sql on BOTH boxes, so they are already in the backup.
#   * Connects with -d master, and forces MULTI_USER back on in a finally.
#     Both are fixes for latent faults in the TSICV5 script; if you want them
#     there too, they need backporting -- this script does not change that one.
#
# WARNING: this is a FULL REPLACE. logs.AppUsage is append-only with indefinite
# retention -- any usage rows generated locally on this dev box are destroyed
# and are not recoverable. That is the intent (you want prod's history), but it
# is not reversible.
#
# What it does:
#   1. Picks the newest settled TSICLogs*.bak under C:\DBBackups\TSIC-DAILY
#   2. Sanity-checks its size against the previous one (catches a truncated copy
#      that FILELISTONLY still accepts -- the backup header sits at the FRONT of
#      the file, so a half-copied .bak reads as valid)
#   3. Verifies it is a TSICLogs backup (logical files TSICLogs + TSICLogs_log)
#      before touching anything
#   4. In ONE sqlcmd connection: SINGLE_USER WITH ROLLBACK IMMEDIATE, RESTORE
#      WITH REPLACE + MOVE to C:\DBFiles, back to MULTI_USER
#   5. Runs 00-postdev-tsiclogs-restore-apppooluser.sql
#   6. Smoke-checks: DB online + multi_user, dev-api user present, the logs
#      grant present, then prints the AppUsage row count and date range restored
# =============================================================================

param(
    [string]$BackupFile,
    [string]$BackupDir   = 'C:\DBBackups\TSIC-DAILY',
    [int]   $MaxAgeHours = 48,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'

$SqlInstance = '.\SS2016'
$DbName      = 'TSICLogs'
$LogicalData = 'TSICLogs'
$LogicalLog  = 'TSICLogs_log'
$DataFile    = 'C:\DBFiles\TSICLogs.mdf'
$LogFile     = 'C:\DBFiles\TSICLogs_1.ldf'
$PostSql     = Join-Path $PSScriptRoot '00-postdev-tsiclogs-restore-apppooluser.sql'

if (-not (Test-Path $PostSql)) { throw "Missing post-restore script: $PostSql" }

# --- 1. Resolve backup file ---------------------------------------------------
# Backups land here by copy from prod. While a copy is in flight the file is
# held open by the copier, is only partially written, and carries a LastWriteTime
# of "now" -- so it sorts FIRST and is the one thing we must not pick. The copier
# restores the source timestamp when it finishes, which is why an in-flight file's
# LastWriteTime appears to jump backwards afterwards. An exclusive open is the
# reliable in-flight test; SQL Server does not hold .bak files between restores.
function Test-BakSettled {
    param([System.IO.FileInfo]$File)
    try {
        $fs = [IO.File]::Open($File.FullName, 'Open', 'Read', 'None')
        $fs.Close(); $fs.Dispose()
        return $true
    } catch {
        return $false
    }
}

$candidates = @()

if ($BackupFile) {
    if (-not (Test-Path $BackupFile)) { throw "Backup file not found: $BackupFile" }
    $bak = Get-Item $BackupFile
    if (-not (Test-BakSettled $bak)) {
        throw "Backup file is locked by another process -- it is most likely still being copied in from prod. Wait and re-run: $($bak.FullName)"
    }
} else {
    if (-not (Test-Path $BackupDir)) {
        throw (
            "Backup directory not found: $BackupDir`n`n" +
            "Nothing is mirroring prod's DAILY backups to this box yet. The hourly`n" +
            "mirror task (\Mirror_PROD_Backups -> Documents\Backups\Scripts\Sync-Backups.ps1)`n" +
            "copies only root-level *.bak into C:\DBBackups\TSIC-Single -- it has no /S,`n" +
            "so a TSIC-DAILY subfolder on the share is never pulled. Add a second`n" +
            "robocopy for it, and give that folder its own retention rule; the existing`n" +
            "cleanup only prunes TSIC-Single."
        )
    }

    # -Recurse handles both a flat TSIC-DAILY and a per-database subfolder layout.
    # The TSICLogs* filter matters: this folder holds other databases' daily
    # backups too, and picking newest-overall would select a TSICV5 backup.
    $candidates = @(Get-ChildItem -Path $BackupDir -Filter 'TSICLogs*.bak' -File -Recurse |
                    Sort-Object LastWriteTime -Descending)
    if (-not $candidates) {
        throw "No TSICLogs*.bak files found under $BackupDir (searched recursively). Check the maintenance plan's output folder and file naming on prod."
    }

    $bak = $candidates | Where-Object { Test-BakSettled $_ } | Select-Object -First 1
    if (-not $bak) { throw "Every TSICLogs*.bak under $BackupDir is locked -- a copy from prod is in flight. Wait and re-run." }
    if ($bak.FullName -ne $candidates[0].FullName) {
        Write-Host "NOTE: $($candidates[0].Name) is still being copied in -- falling back to the previous backup." -ForegroundColor Yellow
    }
}

# --- 1b. Size sanity check ----------------------------------------------------
# FILELISTONLY reads the backup HEADER, which sits at the front of the file, so a
# half-copied .bak passes it and then fails mid-RESTORE. Consecutive backups of
# the same database are within a few MB of each other, so a large shortfall
# against the previous one means a truncated copy.
$prev = $candidates | Where-Object { $_.FullName -ne $bak.FullName } | Select-Object -First 1
if ($prev -and $bak.Length -lt ($prev.Length * 0.5)) {
    throw (
        "Backup looks truncated -- refusing to restore.`n" +
        "  chosen   : $($bak.Name)  ($([Math]::Round($bak.Length/1MB,1)) MB)`n" +
        "  previous : $($prev.Name)  ($([Math]::Round($prev.Length/1MB,1)) MB)`n" +
        "If the shrink is genuine (a purge on prod), pass -BackupFile to override."
    )
}

$ageHours = [Math]::Round(((Get-Date) - $bak.LastWriteTime).TotalHours, 1)
$sizeMb   = [Math]::Round($bak.Length / 1MB, 1)
Write-Host ""
Write-Host "Backup : $($bak.FullName)" -ForegroundColor Cyan
Write-Host "Taken  : $($bak.LastWriteTime)  ($ageHours h ago, $sizeMb MB)" -ForegroundColor Cyan
Write-Host "Target : $DbName on $SqlInstance  ->  $DataFile" -ForegroundColor Cyan
if ($ageHours -gt $MaxAgeHours) {
    Write-Host "WARNING: this backup is older than $MaxAgeHours h -- the daily plan or the mirror may have stopped." -ForegroundColor Yellow
}
Write-Host ""

# --- 2. Verify it really is a TSICLogs backup before destroying the local DB --
$fileList = sqlcmd -S $SqlInstance -d master -E -b -W -h -1 -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$($bak.FullName)';"
if ($LASTEXITCODE -ne 0) {
    # sqlcmd writes its errors to stdout, so $fileList holds the real reason -- print it.
    throw "RESTORE FILELISTONLY failed -- file unreadable or not a SQL backup. sqlcmd said:`n$($fileList -join "`n")"
}
# -W collapses column padding to a single space, so the data row starts
# "TSICLogs C:\...". The trailing space is what stops this also matching
# TSICLogs_log. NOTE: -match on an ARRAY returns matching elements, so this is
# only valid as a boolean inside -not (...) -- see the PS 5.1 note in the runbook.
if (-not ($fileList -match "^$LogicalData ")) {
    throw "Backup does not contain expected logical file '$LogicalData' -- refusing to restore. FILELISTONLY said:`n$($fileList -join "`n")"
}
if (-not ($fileList -match "^$LogicalLog ")) {
    throw "Backup does not contain expected logical file '$LogicalLog' -- refusing to restore. FILELISTONLY said:`n$($fileList -join "`n")"
}
# The MOVE list below names exactly two files. If prod ever adds one, say so
# here rather than failing with a raw SQL error partway through the restore.
$fileCount = @($fileList | Where-Object { $_ -match '\S' }).Count
if ($fileCount -ne 2) {
    throw "Backup contains $fileCount files, expected 2 -- the MOVE list in this script only handles $LogicalData + $LogicalLog. FILELISTONLY said:`n$($fileList -join "`n")"
}

# --- 3. Confirm ---------------------------------------------------------------
if (-not $Yes) {
    Write-Host "This DESTROYS all locally-generated logs.AppUsage rows. Append-only table; not recoverable." -ForegroundColor Yellow
    $answer = Read-Host "FULL REPLACE of local $DbName. Type Y to proceed"
    if ($answer -ne 'Y' -and $answer -ne 'y') { Write-Host "Aborted."; exit 1 }
}

# --- 4. Restore (single connection: single_user -> restore -> multi_user) -----
# -d master matters: if this session's default database were $DbName, the
# SINGLE_USER slot would be held by THIS connection and the RESTORE would fail
# with "Exclusive access could not be obtained".
$restoreSql = @"
IF DB_ID('$DbName') IS NOT NULL
    ALTER DATABASE [$DbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [$DbName]
    FROM DISK = N'$($bak.FullName)'
    WITH REPLACE, FILE = 1,
        MOVE N'$LogicalData' TO N'$DataFile',
        MOVE N'$LogicalLog'  TO N'$LogFile',
        STATS = 10;
IF DATABASEPROPERTYEX('$DbName','UserAccess') <> 'MULTI_USER'
    ALTER DATABASE [$DbName] SET MULTI_USER;
"@

Write-Host "Restoring..." -ForegroundColor Yellow
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$restoreOk = $false
try {
    sqlcmd -S $SqlInstance -d master -E -b -Q $restoreSql
    $restoreOk = ($LASTEXITCODE -eq 0)
} finally {
    # The MULTI_USER statement is the LAST in the batch, so -b aborts before it
    # on any restore error and leaves the database SINGLE_USER. The dev-api pool
    # then takes that one slot and even a re-run cannot get exclusive access.
    # Put it back here rather than printing instructions and hoping.
    if (-not $restoreOk) {
        $st = sqlcmd -S $SqlInstance -d master -E -W -h -1 -Q "SET NOCOUNT ON; SELECT state_desc, user_access_desc FROM sys.databases WHERE name = '$DbName';"
        if (($st -join ' ') -match 'ONLINE.*SINGLE_USER') {
            Write-Host "Restore failed -- returning $DbName to MULTI_USER..." -ForegroundColor Yellow
            sqlcmd -S $SqlInstance -d master -E -Q "ALTER DATABASE [$DbName] SET MULTI_USER;" | Out-Null
        }
    }
}
if (-not $restoreOk) {
    Write-Host ""
    Write-Host "RESTORE FAILED." -ForegroundColor Red
    Write-Host "  If left RESTORING   : re-run this script (a clean restore clears it)." -ForegroundColor Red
    Write-Host "  If left SINGLE_USER : sqlcmd -S $SqlInstance -d master -E -Q `"ALTER DATABASE [$DbName] SET MULTI_USER`"" -ForegroundColor Red
    exit 1
}
$sw.Stop()
Write-Host ("Restore complete in {0:mm\:ss}." -f $sw.Elapsed) -ForegroundColor Green

# --- 5. Re-create local app pool user (prod backup only has claude-api) -------
Write-Host "Running 00-postdev-tsiclogs-restore-apppooluser.sql..." -ForegroundColor Yellow
sqlcmd -S $SqlInstance -d master -E -b -i $PostSql
if ($LASTEXITCODE -ne 0) {
    Write-Host "Post-restore user script FAILED -- usage metering will get 'Login failed' (4060) until it runs clean." -ForegroundColor Red
    exit 1
}

# --- 6. Smoke check -----------------------------------------------------------
$check = sqlcmd -S $SqlInstance -d master -E -b -W -h -1 -Q @"
SET NOCOUNT ON;
SELECT state_desc + '|' + user_access_desc FROM sys.databases WHERE name = '$DbName';
SELECT 'devapi-user-ok' FROM $DbName.sys.database_principals WHERE name = N'IIS APPPOOL\dev-api';
"@
# Both SELECT and INSERT on SCHEMA::logs, or the metering writer still fails.
$grant = sqlcmd -S $SqlInstance -d $DbName -E -b -W -h -1 -Q @"
SET NOCOUNT ON;
SELECT 'logs-grant-ok'
FROM sys.database_permissions p
JOIN sys.database_principals dp ON p.grantee_principal_id = dp.principal_id
WHERE dp.name = N'IIS APPPOOL\dev-api'
  AND p.class = 3 AND p.major_id = SCHEMA_ID('logs')
  AND p.state = 'G' AND p.permission_name IN ('SELECT','INSERT')
GROUP BY dp.name
HAVING COUNT(DISTINCT p.permission_name) = 2;
"@
$checkText = (@($check) + @($grant)) -join ' '
if (-not ($checkText -match 'ONLINE\|MULTI_USER') -or
    -not ($checkText -match 'devapi-user-ok') -or
    -not ($checkText -match 'logs-grant-ok')) {
    Write-Host "SMOKE CHECK FAILED:" -ForegroundColor Red
    Write-Host $checkText -ForegroundColor Red
    exit 1
}

# What actually came across. A restore that "succeeds" onto an empty prod table
# is still a problem, and it is worth seeing immediately rather than wondering
# later why the usage widgets are blank.
$stats = sqlcmd -S $SqlInstance -d $DbName -E -W -h -1 -Q @"
SET NOCOUNT ON;
SELECT CAST(COUNT(*) AS varchar(20)) + ' rows'
     + ISNULL('  ' + CONVERT(varchar(19), MIN(OccurredAt), 120)
            + ' .. ' + CONVERT(varchar(19), MAX(OccurredAt), 120), '')
FROM logs.AppUsage;
"@

Write-Host ""
Write-Host "DONE. $DbName restored from prod backup taken $($bak.LastWriteTime) ($ageHours h old)." -ForegroundColor Green
Write-Host "logs.AppUsage: $((@($stats) -join ' ').Trim())" -ForegroundColor Green
Write-Host "dev.teamsportsinfo.com and F5 debugging will reconnect on next request." -ForegroundColor Green
