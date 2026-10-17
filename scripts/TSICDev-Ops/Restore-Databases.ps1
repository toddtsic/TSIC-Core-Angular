# =============================================================================
# Restore-Databases.ps1 -- restore TSICV5 and/or TSICLogs from PHOENIX's hourly backups
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Ops\Restore-Databases.ps1
#
# Interactive:
#   1. Restore TSICV5?   [Y/n]  -> pick a backup (Enter = latest)
#   2. Restore TSICLogs? [Y/n]  -> pick a backup (Enter = same hour as the TSICV5
#                                  pick, or latest if TSICV5 was skipped)
#   3. Every chosen backup is verified (right database, readable end to end)
#      BEFORE anything is replaced. Then a summary, and you type RESTORE.
#
# Per database:
#   SINGLE_USER WITH ROLLBACK IMMEDIATE (kicks dev-api) -> RESTORE WITH REPLACE
#   -> MULTI_USER, all in one connection. A failed restore puts the database
#   back to MULTI_USER itself. Then the dev-api database user and its grants are
#   re-created (prod's backup only carries prod's pool users), recovery model is
#   forced to SIMPLE (this box takes no log backups), and a pass/fail check runs.
#
# Backups: pushed hourly by PHOENIX (task Push_Backups_To_OVH) into
# $TSICDev.BackupDir. A file still being uploaded is held open by sshd; the
# exclusive-open test is what tells it apart from a finished one. A finished
# file much smaller than the one before it is flagged as truncated.
#
# Exit code 0 = everything chosen was restored and passed its check.
# =============================================================================

#Requires -RunAsAdministrator

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TSICDev-Config.ps1')

$pool = $TSICDev.ApiPoolUser

# --- Databases ---------------------------------------------------------------
# PostSql runs connected to the restored database. Checks are label -> boolean
# SQL expression, each evaluated after PostSql; every one must be true.
$Databases = @(
    [pscustomobject]@{
        Name        = 'TSICV5'
        LogicalData = 'TSIC2015V6'
        LogicalLog  = 'TSIC2015V6_log'
        DataFile    = 'TSICV5.mdf'
        LogFile     = 'TSICV5_1.ldf'
        Warning     = $null
        # db_datareader/db_datawriter cover SELECT/INSERT/UPDATE/DELETE only;
        # EXECUTE is for the stored procedures (reporting.* and others).
        # The two indexes are dev-only: Jobs.Registrations ships from prod with no
        # nonclustered indexes, and the admin candidate search (AM-004) goes from
        # 6.3s to 0.7s with them. IF NOT EXISTS makes this a no-op once prod has them.
        PostSql     = @"
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$pool')
BEGIN
    RAISERROR(N'Server login [$pool] does not exist on this instance -- create it, then re-run.', 16, 1);
    RETURN;
END
IF (SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()) <> N'SIMPLE'
BEGIN
    ALTER DATABASE CURRENT SET RECOVERY SIMPLE;
    PRINT 'Recovery model set to SIMPLE.';
END
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$pool')
BEGIN
    CREATE USER [$pool] FOR LOGIN [$pool];
    PRINT 'Created database user [$pool].';
END
ALTER ROLE db_datareader ADD MEMBER [$pool];
ALTER ROLE db_datawriter ADD MEMBER [$pool];
GRANT EXECUTE TO [$pool];
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Registrations_UserId' AND object_id = OBJECT_ID('Jobs.Registrations'))
    CREATE NONCLUSTERED INDEX IX_Registrations_UserId ON Jobs.Registrations (UserId) INCLUDE (RoleId, jobID, bActive);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Registrations_Family_UserId' AND object_id = OBJECT_ID('Jobs.Registrations'))
    CREATE NONCLUSTERED INDEX IX_Registrations_Family_UserId ON Jobs.Registrations (Family_UserId);
"@
        Checks      = [ordered]@{
            'online, multi-user'           = "EXISTS (SELECT 1 FROM sys.databases WHERE name = DB_NAME() AND state_desc = 'ONLINE' AND user_access_desc = 'MULTI_USER')"
            'recovery model SIMPLE'        = "(SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()) = 'SIMPLE'"
            'dev-api database user'        = "EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$pool')"
            'dev-api in db_datareader'     = "IS_ROLEMEMBER('db_datareader', N'$pool') = 1"
            'dev-api in db_datawriter'     = "IS_ROLEMEMBER('db_datawriter', N'$pool') = 1"
            'dev-api has EXECUTE'          = "EXISTS (SELECT 1 FROM sys.database_permissions p JOIN sys.database_principals d ON d.principal_id = p.grantee_principal_id WHERE d.name = N'$pool' AND p.class = 0 AND p.permission_name = 'EXECUTE' AND p.state = 'G')"
            'index IX_Registrations_UserId' = "EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Registrations_UserId' AND object_id = OBJECT_ID('Jobs.Registrations'))"
            'index IX_Registrations_Family_UserId' = "EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Registrations_Family_UserId' AND object_id = OBJECT_ID('Jobs.Registrations'))"
        }
        Report      = "SET NOCOUNT ON; SELECT 'Jobs.Registrations: ' + CAST(SUM(rows) AS varchar(20)) + ' rows' FROM sys.partitions WHERE object_id = OBJECT_ID('Jobs.Registrations') AND index_id IN (0, 1);"
    },
    [pscustomobject]@{
        Name        = 'TSICLogs'
        LogicalData = 'TSICLogs'
        LogicalLog  = 'TSICLogs_log'
        DataFile    = 'TSICLogs.mdf'
        LogFile     = 'TSICLogs_1.ldf'
        Warning     = 'logs.AppUsage is append-only: usage rows generated on this box are destroyed, not recoverable.'
        # The grant is deliberately SELECT + INSERT on SCHEMA::logs and nothing
        # else -- the same as 15-create-tsiclogs.sql grants. No UPDATE, DELETE or
        # EXECUTE, so a bug in the metering code cannot rewrite or erase the
        # append-only history. Do not widen it to match TSICV5.
        PostSql     = @"
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$pool')
BEGIN
    RAISERROR(N'Server login [$pool] does not exist on this instance -- create it, then re-run.', 16, 1);
    RETURN;
END
IF (SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()) <> N'SIMPLE'
BEGIN
    ALTER DATABASE CURRENT SET RECOVERY SIMPLE;
    PRINT 'Recovery model set to SIMPLE.';
END
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$pool')
BEGIN
    CREATE USER [$pool] FOR LOGIN [$pool];
    PRINT 'Created database user [$pool].';
END
GRANT SELECT, INSERT ON SCHEMA::logs TO [$pool];
"@
        Checks      = [ordered]@{
            'online, multi-user'             = "EXISTS (SELECT 1 FROM sys.databases WHERE name = DB_NAME() AND state_desc = 'ONLINE' AND user_access_desc = 'MULTI_USER')"
            'recovery model SIMPLE'          = "(SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()) = 'SIMPLE'"
            'dev-api database user'          = "EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$pool')"
            'dev-api SELECT + INSERT on logs' = "(SELECT COUNT(DISTINCT p.permission_name) FROM sys.database_permissions p JOIN sys.database_principals d ON d.principal_id = p.grantee_principal_id WHERE d.name = N'$pool' AND p.class = 3 AND p.major_id = SCHEMA_ID('logs') AND p.state = 'G' AND p.permission_name IN ('SELECT', 'INSERT')) = 2"
        }
        Report      = "SET NOCOUNT ON; SELECT 'logs.AppUsage: ' + CAST(COUNT(*) AS varchar(20)) + ' rows' + ISNULL(', ' + CONVERT(varchar(16), MIN(OccurredAt), 120) + ' .. ' + CONVERT(varchar(16), MAX(OccurredAt), 120), '') FROM logs.AppUsage;"
    }
)

# --- Helpers -----------------------------------------------------------------

# Runs T-SQL and returns the non-blank output lines. Throws with sqlcmd's own
# output on any failure (sqlcmd writes its errors to stdout, so that is the reason).
function Invoke-Tsql {
    param([Parameter(Mandatory = $true)][string]$Sql, [string]$Database = 'master')
    $out = & sqlcmd -S $TSICDev.SqlInstance -d $Database -E -b -W -h -1 -s '|' -Q $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed (exit $LASTEXITCODE) on [$Database]:`n$(@($out) -join "`n")"
    }
    @($out | Where-Object { $_ -and $_.Trim() })
}

# An upload in progress holds the file open, so an exclusive open fails.
function Test-FileSettled {
    param([IO.FileInfo]$File)
    try {
        $fs = [IO.File]::Open($File.FullName, 'Open', 'Read', 'None')
        $fs.Dispose()
        return $true
    } catch {
        return $false
    }
}

function Read-YesNo {
    param([string]$Prompt)
    while ($true) {
        $a = Read-Host "$Prompt [Y/n]"
        if ([string]::IsNullOrWhiteSpace($a) -or $a -match '^(y|yes)$') { return $true }
        if ($a -match '^(n|no)$') { return $false }
        Write-Host "  Answer y or n." -ForegroundColor Yellow
    }
}

function Format-Age {
    param([datetime]$Taken)
    $age = (Get-Date) - $Taken
    if ($age.TotalHours -lt 48) { return ('{0:N1} h' -f $age.TotalHours) }
    return ('{0:N1} d' -f $age.TotalDays)
}

# Backups for one database, newest first, each with Status ok / uploading / truncated?.
# File names look like TSICV5_backup_2026_10_16_135900_9447535.bak; the time in
# the name is when PHOENIX took the backup.
function Get-BackupList {
    param([string]$DbName)
    $re = '^' + [regex]::Escape($DbName) + '_backup_(\d{4})_(\d{2})_(\d{2})_(\d{2})(\d{2})(\d{2})_\d+\.bak$'
    $rows = foreach ($f in @(Get-ChildItem -LiteralPath $TSICDev.BackupDir -File)) {
        $m = [regex]::Match($f.Name, $re)
        if (-not $m.Success) { continue }
        $g = $m.Groups
        $taken = [datetime]::new([int]$g[1].Value, [int]$g[2].Value, [int]$g[3].Value, [int]$g[4].Value, [int]$g[5].Value, [int]$g[6].Value)
        [pscustomobject]@{ File = $f; Taken = $taken; HourKey = $taken.ToString('yyyy-MM-dd HH'); Status = '' }
    }
    $rows = @($rows | Sort-Object Taken -Descending)
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $r = $rows[$i]
        if (-not (Test-FileSettled $r.File)) { $r.Status = 'uploading'; continue }
        # Consecutive backups of one database are within a few MB of each other.
        # The backup header sits at the front of the file, so a cut-short upload
        # still passes FILELISTONLY; the size is the cheap early tell.
        if ($i + 1 -lt $rows.Count -and $r.File.Length -lt ($rows[$i + 1].File.Length * 0.5)) {
            $r.Status = 'truncated?'
            continue
        }
        $r.Status = 'ok'
    }
    $rows
}

function Select-Backup {
    param([string]$DbName, [object[]]$List, [string]$PreferHourKey)
    $shown = @($List | Select-Object -First 30)
    $usable = @($shown | Where-Object { $_.Status -eq 'ok' })
    if ($usable.Count -eq 0) { throw "No usable $DbName backup in $($TSICDev.BackupDir)." }
    $default = $usable[0]
    if ($PreferHourKey) {
        $same = @($usable | Where-Object { $_.HourKey -eq $PreferHourKey })
        if ($same.Count -gt 0) { $default = $same[0] }
    }
    $defaultNo = [array]::IndexOf($shown, $default) + 1

    Write-Host ""
    Write-Host ("  {0,3}  {1,-16}  {2,8}  {3,9}" -f '#', 'Taken', 'Age', 'Size') -ForegroundColor Cyan
    for ($i = 0; $i -lt $shown.Count; $i++) {
        $r = $shown[$i]
        $note = ''
        if ($i + 1 -eq $defaultNo) { $note = '<- default' }
        if ($r.Status -ne 'ok') { $note = "$($r.Status.ToUpper()) - not selectable" }
        $line = "  {0,3}  {1,-16}  {2,8}  {3,6} MB  {4}" -f ($i + 1), $r.Taken.ToString('yyyy-MM-dd HH:mm'), (Format-Age $r.Taken), [int]($r.File.Length / 1MB), $note
        if ($r.Status -eq 'ok') { Write-Host $line } else { Write-Host $line -ForegroundColor DarkGray }
    }
    if ($List.Count -gt $shown.Count) {
        Write-Host "       ($($List.Count - $shown.Count) older backups not shown)" -ForegroundColor DarkGray
    }

    while ($true) {
        $a = Read-Host "  $DbName backup # [Enter = $defaultNo]"
        if ([string]::IsNullOrWhiteSpace($a)) { return $default }
        if ($a -match '^\d+$') {
            $n = [int]$a
            if ($n -ge 1 -and $n -le $shown.Count) {
                if ($shown[$n - 1].Status -eq 'ok') { return $shown[$n - 1] }
                Write-Host "  #$n is $($shown[$n - 1].Status) - pick another." -ForegroundColor Yellow
                continue
            }
        }
        Write-Host "  Enter a number from the list, or press Enter." -ForegroundColor Yellow
    }
}

# Right database (exactly the two expected logical files) and readable end to
# end (VERIFYONLY reads the whole file). Nothing is touched.
function Test-BackupFile {
    param($Db, $Bak)
    $path = $Bak.File.FullName
    $rows = Invoke-Tsql -Sql "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$path';"
    $logical = @($rows | ForEach-Object { ($_ -split '\|')[0] })
    $expected = @($Db.LogicalData, $Db.LogicalLog)
    if ($logical.Count -ne 2 -or @(Compare-Object $logical $expected).Count -ne 0) {
        throw "$($Bak.File.Name) is not a $($Db.Name) backup: expected logical files $($expected -join ', '), found $($logical -join ', ')."
    }
    Invoke-Tsql -Sql "RESTORE VERIFYONLY FROM DISK = N'$path';" | Out-Null
}

function Restore-OneDatabase {
    param($Db, $Bak)
    $name = $Db.Name
    $data = Join-Path $TSICDev.DataDir $Db.DataFile
    $log  = Join-Path $TSICDev.DataDir $Db.LogFile

    Write-Host ""
    Write-Host "=== $name <- $($Bak.File.Name)" -ForegroundColor Cyan

    # One connection, from master: nothing can take the single-user slot between
    # steps, and this session is not itself sitting in the database.
    $sql = @"
IF DB_ID(N'$name') IS NOT NULL
    ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [$name] FROM DISK = N'$($Bak.File.FullName)'
    WITH REPLACE, FILE = 1,
         MOVE N'$($Db.LogicalData)' TO N'$data',
         MOVE N'$($Db.LogicalLog)' TO N'$log',
         STATS = 10;
ALTER DATABASE [$name] SET MULTI_USER;
"@
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & sqlcmd -S $TSICDev.SqlInstance -d master -E -b -Q $sql
    if ($LASTEXITCODE -ne 0) {
        # -b stops the batch before the MULTI_USER line, which would leave the
        # database single-user with dev-api holding the one slot. Put it back.
        $state = ''
        try { $state = (Invoke-Tsql -Sql "SET NOCOUNT ON; SELECT state_desc + '|' + user_access_desc FROM sys.databases WHERE name = N'$name';") -join ' ' } catch { }
        if ($state -match 'ONLINE\|SINGLE_USER') {
            try { Invoke-Tsql -Sql "ALTER DATABASE [$name] SET MULTI_USER;" | Out-Null; $state = 'ONLINE|MULTI_USER (restored to multi-user)' } catch { }
        }
        throw "RESTORE of $name failed. Database state now: $(if ($state) { $state } else { 'unknown' }). If it says RESTORING, $name is unusable until a restore succeeds - re-run with another backup."
    }
    $sw.Stop()
    Write-Host ("  Restored in {0:mm\:ss}." -f $sw.Elapsed) -ForegroundColor Green

    Write-Host "  Re-creating $pool access..."
    Invoke-Tsql -Database $name -Sql $Db.PostSql | ForEach-Object { Write-Host "    $_" }

    $checkSql = "SET NOCOUNT ON;`n" + (@($Db.Checks.Keys | ForEach-Object { "SELECT '$_|' + CASE WHEN $($Db.Checks[$_]) THEN 'ok' ELSE 'FAIL' END;" }) -join "`n")
    $results = @(Invoke-Tsql -Database $name -Sql $checkSql)
    $failed = 0
    foreach ($line in $results) {
        $parts = $line -split '\|'
        if ($parts[-1] -eq 'ok') { Write-Host "    ok    $($parts[0])" -ForegroundColor Green }
        else { Write-Host "    FAIL  $($parts[0])" -ForegroundColor Red; $failed++ }
    }
    if ($results.Count -ne $Db.Checks.Count) { throw "Check returned $($results.Count) results, expected $($Db.Checks.Count)." }
    if ($failed -gt 0) { throw "$name restored but $failed check(s) failed - dev-api will not work against it until fixed." }

    Invoke-Tsql -Database $name -Sql $Db.Report | ForEach-Object { Write-Host "  $_" }
}

# --- Main --------------------------------------------------------------------
$done = @()
try {
    Assert-TSICDevBox
    if (-not (Test-Path -LiteralPath $TSICDev.BackupDir)) { throw "Backup folder not found: $($TSICDev.BackupDir)" }

    Write-Host ""
    Write-Host "Restore databases on $($TSICDev.SqlInstance) from $($TSICDev.BackupDir)" -ForegroundColor Cyan

    $plan = @()
    $preferHour = $null
    foreach ($db in $Databases) {
        Write-Host ""
        if (-not (Read-YesNo "Restore $($db.Name)?")) {
            Write-Host "  Skipping $($db.Name)." -ForegroundColor DarkGray
            continue
        }
        $list = @(Get-BackupList $db.Name)
        if ($list.Count -eq 0) { throw "No $($db.Name) backups in $($TSICDev.BackupDir)." }
        $bak = Select-Backup -DbName $db.Name -List $list -PreferHourKey $preferHour
        if (-not $preferHour) { $preferHour = $bak.HourKey }
        $plan += [pscustomobject]@{ Db = $db; Bak = $bak }
    }
    if ($plan.Count -eq 0) {
        Write-Host ""
        Write-Host "Nothing selected. Nothing changed."
        exit 0
    }

    Write-Host ""
    Write-Host "Verifying the chosen backups (nothing is replaced yet)..." -ForegroundColor Yellow
    foreach ($p in $plan) {
        Test-BackupFile $p.Db $p.Bak
        Write-Host "  ok  $($p.Bak.File.Name)" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "About to REPLACE:" -ForegroundColor Yellow
    foreach ($p in $plan) {
        Write-Host ("  {0,-9} <- backup taken {1} ({2} old)" -f $p.Db.Name, $p.Bak.Taken.ToString('yyyy-MM-dd HH:mm'), (Format-Age $p.Bak.Taken))
        if ($p.Db.Warning) { Write-Host "            $($p.Db.Warning)" -ForegroundColor Yellow }
    }
    Write-Host "  dev-api connections are dropped while each restore runs."
    $a = Read-Host "Type RESTORE to proceed"
    if ($a -ne 'RESTORE') {
        Write-Host "Aborted. Nothing changed."
        exit 1
    }

    foreach ($p in $plan) {
        Restore-OneDatabase $p.Db $p.Bak
        $done += $p.Db.Name
    }

    Write-Host ""
    Write-Host "DONE. Restored and checked: $($done -join ', ')." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($done.Count -gt 0) { Write-Host "Restored and checked before the failure: $($done -join ', ')." -ForegroundColor Yellow }
    exit 1
}
