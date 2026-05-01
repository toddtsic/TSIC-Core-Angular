-- =============================================================================
-- Post-restore: re-create local IIS app pool db users in TSICV5
-- =============================================================================
-- Run after restoring TSICV5 from a prod backup on this dev box.
-- Server-level logins survive a restore; db-level users do NOT (they come from
-- the backup, which only contains prod's app pool users TSIC-API / TSIC-API2).
--
-- Symptom if skipped:
--   SQL log: "Login failed for user 'IIS APPPOOL\dev-api'. Reason: Failed to
--   open the explicitly specified database 'TSICV5'." (event 18456)
--   App: EF ConnectionError on /api/auth/login (and every other endpoint)
-- =============================================================================

USE TSICV5;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IIS APPPOOL\dev-api')
BEGIN
    CREATE USER [IIS APPPOOL\dev-api] FOR LOGIN [IIS APPPOOL\dev-api];
    ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\dev-api];
    ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\dev-api];
    PRINT 'Created [IIS APPPOOL\dev-api] with db_datareader + db_datawriter.';
END
ELSE
    PRINT '[IIS APPPOOL\dev-api] already present — skipped.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IIS APPPOOL\claude-api')
BEGIN
    CREATE USER [IIS APPPOOL\claude-api] FOR LOGIN [IIS APPPOOL\claude-api];
    ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\claude-api];
    ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\claude-api];
    PRINT 'Created [IIS APPPOOL\claude-api] with db_datareader + db_datawriter.';
END
ELSE
    PRINT '[IIS APPPOOL\claude-api] already present — skipped.';
GO

-- -----------------------------------------------------------------------------
-- EXECUTE permission for stored procedures (e.g. reporting.CustomerJobRevenueRollups).
-- db_datareader/db_datawriter cover SELECT/INSERT/UPDATE/DELETE only — NOT EXEC.
-- Idempotent: GRANT is safe to re-run, and runs even if the user pre-existed above.
--
-- Symptom if skipped:
--   "The EXECUTE permission was denied on the object '<sproc>', database 'TSICV5'..."
--   Hits any endpoint that calls a sproc (tools/customer-job-revenue, etc.).
-- -----------------------------------------------------------------------------
GRANT EXECUTE TO [IIS APPPOOL\dev-api];
GRANT EXECUTE TO [IIS APPPOOL\claude-api];
PRINT 'Granted EXECUTE (db-wide) to dev-api and claude-api app pool users.';
GO
