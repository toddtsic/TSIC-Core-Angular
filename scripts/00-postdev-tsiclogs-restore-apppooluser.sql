-- =============================================================================
-- Post-restore: re-create local IIS app pool db user in TSICLogs
-- =============================================================================
-- Run after restoring TSICLogs from a prod backup on this dev box.
-- Server-level logins survive a restore; db-level users do NOT (they come from
-- the backup, which only contains prod's app pool user IIS APPPOOL\claude-api).
--
-- Scope: this dev box has one IIS API pool: dev-api. This is the TSICLogs twin
-- of 00-postdev-db-restore-apppooluser.sql (which does the same for TSICV5).
--
-- The grant is deliberately NOT db_datareader/db_datawriter. It is exactly what
-- section 6 of 15-create-tsiclogs.sql grants: SELECT + INSERT on SCHEMA::logs,
-- nothing else. INSERT for the fact rows, SELECT because the writer resolves
-- lookup ids and the report services read. No UPDATE, no DELETE, no EXECUTE --
-- a bug in the metering code cannot rewrite or erase the append-only history.
-- Do not "fix" this by widening it to match the TSICV5 script.
--
-- Symptom if skipped:
--   SQL log: "Login failed for user 'IIS APPPOOL\dev-api'. Reason: Failed to
--   open the explicitly specified database 'TSICLogs'." (error 4060 / 18456
--   state 38). Usage metering writes fail; the SuperDirector usage widgets and
--   any endpoint reading logs.AppUsage 500.
-- =============================================================================

USE TSICLogs;
GO

-- -----------------------------------------------------------------------------
-- CONTEXT GUARD -- do not skip.
-- If the USE failed (db missing, still RESTORING), the session is left pointed
-- at whatever database was selected. Without this, the GRANT below would land
-- on master -- or on TSICV5 -- against a schema that may not even exist there.
-- -----------------------------------------------------------------------------
IF DB_NAME() <> N'TSICLogs'
BEGIN
    DECLARE @Where sysname = DB_NAME();
    RAISERROR(N'ABORT: session is in [%s], not TSICLogs. Nothing granted.', 16, 1, @Where);
    SET NOEXEC ON;
END
GO

-- -----------------------------------------------------------------------------
-- Recovery model. Set to SIMPLE at creation (15-create-tsiclogs.sql); a restore
-- carries the model from the backup, so this is normally already correct. It is
-- re-asserted because a dev box has no log backups -- if prod ever moves to FULL
-- this box would grow an unbounded .ldf with nothing to truncate it.
-- -----------------------------------------------------------------------------
IF (SELECT recovery_model_desc FROM sys.databases WHERE name = N'TSICLogs') <> N'SIMPLE'
BEGIN
    PRINT 'Setting TSICLogs to SIMPLE recovery...';
    ALTER DATABASE TSICLogs SET RECOVERY SIMPLE;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IIS APPPOOL\dev-api')
BEGIN
    RAISERROR(N'Server login [IIS APPPOOL\dev-api] does not exist on this instance -- create it first. Nothing granted; the API cannot write usage rows.', 16, 1);
    SET NOEXEC ON;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IIS APPPOOL\dev-api')
BEGIN
    CREATE USER [IIS APPPOOL\dev-api] FOR LOGIN [IIS APPPOOL\dev-api];
    PRINT 'Created [IIS APPPOOL\dev-api] in TSICLogs.';
END
ELSE
    PRINT '[IIS APPPOOL\dev-api] already present -- skipped.';
GO

-- Idempotent: GRANT is safe to re-run, and runs even if the user pre-existed.
GRANT SELECT, INSERT ON SCHEMA::logs TO [IIS APPPOOL\dev-api];
PRINT 'Granted SELECT, INSERT ON SCHEMA::logs to dev-api app pool user.';
GO

-- Leave the session in a normal state whichever path was taken above.
SET NOEXEC OFF;
GO
