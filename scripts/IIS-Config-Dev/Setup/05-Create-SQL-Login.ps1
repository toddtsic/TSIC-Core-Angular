# ============================================================================
# 05-Create-SQL-Login.ps1 — Create SQL Server login for IIS app pool identity
# ============================================================================
# Grants the app pool identity (IIS APPPOOL\dev-api) access to the database.
# Also run after database restores to fix orphaned users.
# ============================================================================

#Requires -RunAsAdministrator

. "$PSScriptRoot\..\_config.ps1"

Write-Host ""
Write-Host "[Step 5] Creating SQL Server login for app pool identity (Dev)..." -ForegroundColor Green

$loginName = "IIS APPPOOL\$($Config.ApiPoolName)"

$sqlCheck = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '$loginName')
BEGIN
    CREATE LOGIN [$loginName] FROM WINDOWS;
    PRINT 'Created login: $loginName';
END
ELSE
    PRINT 'Login already exists: $loginName';

USE [$($Config.DatabaseName)];

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$loginName')
BEGIN
    CREATE USER [$loginName] FOR LOGIN [$loginName];
    PRINT 'Created database user: $loginName';
END
ELSE
BEGIN
    ALTER USER [$loginName] WITH LOGIN = [$loginName];
    PRINT 'Re-mapped database user: $loginName';
END

ALTER ROLE [db_datareader] ADD MEMBER [$loginName];
ALTER ROLE [db_datawriter] ADD MEMBER [$loginName];
GRANT EXECUTE TO [$loginName];
PRINT 'Granted db_datareader, db_datawriter, and EXECUTE (db-wide).';
"@

# Try Invoke-Sqlcmd first, fall back to sqlcmd
$invokeSqlAvailable = Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue
$sqlcmdAvailable = Get-Command sqlcmd -ErrorAction SilentlyContinue

if ($invokeSqlAvailable) {
    try {
        # Legacy SQLPS module (v15.0) doesn't support -TrustServerCertificate; newer SqlServer module does
        $sqlArgs = @{ ServerInstance = $Config.SqlInstance; Query = $sqlCheck }
        $cmdInfo = Get-Command Invoke-Sqlcmd
        if ($cmdInfo.Parameters.ContainsKey('TrustServerCertificate')) {
            $sqlArgs.TrustServerCertificate = $true
        }
        Invoke-Sqlcmd @sqlArgs
        Write-Host "  SQL Server login configured via Invoke-Sqlcmd." -ForegroundColor Green
    }
    catch {
        Write-Host "  ERROR: Failed to create SQL login: $_" -ForegroundColor Red
        Write-Host "  You may need to run this manually in SSMS." -ForegroundColor Yellow
    }
}
elseif ($sqlcmdAvailable) {
    $tempSql = Join-Path $env:TEMP "tsic-setup-sql.sql"
    $sqlCheck | Out-File -FilePath $tempSql -Encoding UTF8
    try {
        sqlcmd -S $Config.SqlInstance -i $tempSql -C
        Write-Host "  SQL Server login configured via sqlcmd." -ForegroundColor Green
    }
    catch {
        Write-Host "  ERROR: Failed to create SQL login: $_" -ForegroundColor Red
    }
    finally {
        Remove-Item $tempSql -ErrorAction SilentlyContinue
    }
}
else {
    Write-Host "  WARNING: Neither Invoke-Sqlcmd nor sqlcmd found." -ForegroundColor Yellow
    Write-Host "  Run the following SQL manually in SSMS:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  CREATE LOGIN [$loginName] FROM WINDOWS;" -ForegroundColor White
    Write-Host "  USE [$($Config.DatabaseName)];" -ForegroundColor White
    Write-Host "  CREATE USER [$loginName] FOR LOGIN [$loginName];" -ForegroundColor White
    Write-Host "  ALTER ROLE [db_datareader] ADD MEMBER [$loginName];" -ForegroundColor White
    Write-Host "  ALTER ROLE [db_datawriter] ADD MEMBER [$loginName];" -ForegroundColor White
    Write-Host "  GRANT EXECUTE TO [$loginName];" -ForegroundColor White
}

Write-Host "[Step 5] Complete." -ForegroundColor Green
