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
-- Usage: Open SSMS → connect as sysadmin → run this script
-- ============================================================================

USE [TSICV5];

-- Create server login if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'IIS APPPOOL\dev-api')
    CREATE LOGIN [IIS APPPOOL\dev-api] FROM WINDOWS;

-- Create or re-map database user
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'IIS APPPOOL\dev-api')
    CREATE USER [IIS APPPOOL\dev-api] FOR LOGIN [IIS APPPOOL\dev-api];
ELSE
    ALTER USER [IIS APPPOOL\dev-api] WITH LOGIN = [IIS APPPOOL\dev-api];

-- Grant read/write access
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\dev-api];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\dev-api];

PRINT 'IIS APPPOOL\dev-api login fixed for TSICV5.';
