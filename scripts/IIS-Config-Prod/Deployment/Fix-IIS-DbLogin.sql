-- ============================================================================
-- Fix-IIS-DbLogin.sql
--
-- Run this after every TSICV5 database restore.
-- Grants the IIS app pool identity access to the restored database.
--
-- Why: SQL Server restores bring database-level users but NOT server-level
--      logins. The IIS app pool identity (a Windows virtual account) needs
--      both to connect. This is called "orphaned users."
--
-- Unified naming: uses claude-api (same pool name on Dev and Prod).
--
-- Usage: Open SSMS -> connect as sysadmin -> run this script
-- ============================================================================

USE [TSICV5];

-- Create server login if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'IIS APPPOOL\claude-api')
    CREATE LOGIN [IIS APPPOOL\claude-api] FROM WINDOWS;

-- Create or re-map database user
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'IIS APPPOOL\claude-api')
    CREATE USER [IIS APPPOOL\claude-api] FOR LOGIN [IIS APPPOOL\claude-api];
ELSE
    ALTER USER [IIS APPPOOL\claude-api] WITH LOGIN = [IIS APPPOOL\claude-api];

-- Grant read/write access
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\claude-api];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\claude-api];

-- Grant EXECUTE on stored procedures (db-wide).
-- db_datareader/db_datawriter cover SELECT/INSERT/UPDATE/DELETE only -- NOT EXEC.
-- Symptom if skipped: "EXECUTE permission was denied on the object '<sproc>'..."
-- on any endpoint that calls a sproc (e.g. tools/customer-job-revenue).
GRANT EXECUTE TO [IIS APPPOOL\claude-api];

PRINT 'IIS APPPOOL\claude-api login fixed for TSICV5.';
