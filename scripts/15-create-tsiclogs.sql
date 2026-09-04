/* =============================================================================
   15 - TSICLogs: usage logging database

   Creates (if absent) the TSICLogs database on the SAME instance as TSICV5,
   the [logs] schema, four seeded lookup tables, and the logs.AppUsage fact
   table. Idempotent: safe to re-run in every environment.

     * logs.AppUsage is NEVER dropped by this script. Append-only asset with
       indefinite retention. Schema changes ship as ALTERs in a later script.
     * Lookup seeds are upserted on every run (ids stable, names corrected).
     * No FKs into TSICV5 -- cross-db FKs are impossible and undesired.
     * TSICV5 is not touched. logs.AppLog (dead, legacy) stays where it is.
     * OccurredAt is written by the application: server-local Arizona time
       (site convention, no DST), request START.

   SCHEMA IS THE REVIEWED v1 DESIGN, UNCHANGED. 16 columns as specified.
   Four deviations, each one directed:

   1. File location. C:\DBFiles on TSIC-SEDONA, E:\DBFiles on TSIC-PHOENIX --
      the house convention (TSICV5 lives in C:\DBFiles here). Mapped from
      SERVERPROPERTY('MachineName'), so no per-box editing; an unrecognised
      host aborts rather than guessing. The instance default path is NOT used.

   2. File size and growth. 4096 MB data / 1024 MB growth, 512 MB log /
      256 MB growth. Sized from measurement, not projection: Seq reports
      50,003 request-logged events/day (2026-09-03), so ~18M rows/yr and
      ~1.5-2.5 GB/yr compressed. 4 GB covers two years with no autogrowth.
      Instant file initialization is OFF on this instance, so CREATE DATABASE
      will zero 4 GB up front -- about a minute. It has not hung.

   3. No DEFAULT constraints anywhere. The writer supplies every value. A
      default lets an unmapped SqlBulkCopy column silently become "unknown"
      instead of failing the batch, which is precisely the failure this table
      exists to make visible.

   4. Collation is copied from TSICV5 rather than inherited from the instance.
      They match today (both SQL_Latin1_General_CP1_CI_AS); copying makes the
      cross-db report joins safe on any box rather than on this one.
============================================================================= */

SET NOCOUNT ON;
GO

-- -----------------------------------------------------------------------------
-- 1. Database
-- -----------------------------------------------------------------------------
-- Host-mapped, so prod needs no edit. An unknown host is a hard stop rather
-- than a silent default -- same principle as Program.cs refusing to start
-- without ASPNETCORE_ENVIRONMENT. Add a WHEN for any new/DR box.
DECLARE @Machine sysname = CAST(SERVERPROPERTY('MachineName') AS sysname);

DECLARE @DBFilePath nvarchar(260) =
    CASE @Machine
        WHEN N'TSIC-PHOENIX' THEN N'E:\DBFiles\'
        WHEN N'TSIC-SEDONA'  THEN N'C:\DBFiles\'
    END;

IF @DBFilePath IS NULL
BEGIN
    RAISERROR(N'Unknown host ''%s'' -- add it to the @DBFilePath map in section 1 before running.', 16, 1, @Machine);
    SET NOEXEC ON;   -- stops every LATER batch too; RETURN would only exit this one
END

DECLARE @DataSizeMB   int = 4096;
DECLARE @DataGrowthMB int = 1024;
DECLARE @LogSizeMB    int = 512;
DECLARE @LogGrowthMB  int = 256;

DECLARE @Collation sysname =
    (SELECT collation_name FROM sys.databases WHERE name = N'TSICV5');

IF @Collation IS NULL
BEGIN
    SET @Collation = CAST(SERVERPROPERTY('Collation') AS sysname);
    PRINT '!! TSICV5 not found on this instance -- falling back to the instance';
    PRINT '   collation (' + @Collation + '). Cross-db joins may conflict.';
END

IF DB_ID(N'TSICLogs') IS NULL
BEGIN
    DECLARE @sql nvarchar(max) = N'
CREATE DATABASE TSICLogs
ON PRIMARY (
    NAME       = N''TSICLogs'',
    FILENAME   = N''' + @DBFilePath + N'TSICLogs.mdf'',
    SIZE       = ' + CAST(@DataSizeMB   AS nvarchar(10)) + N'MB,
    FILEGROWTH = ' + CAST(@DataGrowthMB AS nvarchar(10)) + N'MB )
LOG ON (
    NAME       = N''TSICLogs_log'',
    FILENAME   = N''' + @DBFilePath + N'TSICLogs_1.ldf'',
    SIZE       = ' + CAST(@LogSizeMB   AS nvarchar(10)) + N'MB,
    FILEGROWTH = ' + CAST(@LogGrowthMB AS nvarchar(10)) + N'MB )
COLLATE ' + @Collation + N';';

    PRINT 'Creating TSICLogs in ' + @DBFilePath + ' (collation ' + @Collation + ')...';
    EXEC sp_executesql @sql;
END
ELSE
BEGIN
    PRINT 'TSICLogs already exists -- not recreated. Note: file path, size and';
    PRINT '  growth are NOT corrected by a re-run; they are set at creation only.';
END
GO

IF (SELECT recovery_model_desc FROM sys.databases WHERE name = N'TSICLogs') <> N'SIMPLE'
BEGIN
    PRINT 'Setting TSICLogs to SIMPLE recovery...';
    ALTER DATABASE TSICLogs SET RECOVERY SIMPLE;
END
GO

USE TSICLogs;
GO

-- -----------------------------------------------------------------------------
-- 1b. CONTEXT GUARD -- do not skip.
--
-- If CREATE DATABASE failed above, the USE also failed, and SSMS leaves the
-- session pointed at whatever database was selected in the toolbar. Every
-- batch below would then create the logs schema and five tables THERE -- in
-- master, or worse, in TSICV5. NOEXEC ON is used rather than RETURN because
-- RETURN exits only its own batch; NOEXEC suppresses every batch that follows.
-- -----------------------------------------------------------------------------
IF DB_NAME() <> N'TSICLogs'
BEGIN
    DECLARE @Where sysname = DB_NAME();
    RAISERROR(N'ABORT: session is in [%s], not TSICLogs. No objects created.', 16, 1, @Where);
    SET NOEXEC ON;
END
GO

-- -----------------------------------------------------------------------------
-- 2. Schema
-- -----------------------------------------------------------------------------
IF SCHEMA_ID(N'logs') IS NULL
    EXEC(N'CREATE SCHEMA logs AUTHORIZATION dbo;');
GO

-- -----------------------------------------------------------------------------
-- 3. Lookup tables
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'logs.AppClients', N'U') IS NULL
CREATE TABLE logs.AppClients (
    AppClientId    INT         NOT NULL CONSTRAINT PK_AppClients PRIMARY KEY,
    AppClientName  VARCHAR(30) NOT NULL CONSTRAINT UQ_AppClients_Name UNIQUE
);
GO

IF OBJECT_ID(N'logs.Platforms', N'U') IS NULL
CREATE TABLE logs.Platforms (
    PlatformId    INT         NOT NULL CONSTRAINT PK_Platforms PRIMARY KEY,
    PlatformName  VARCHAR(20) NOT NULL CONSTRAINT UQ_Platforms_Name UNIQUE
);
GO

IF OBJECT_ID(N'logs.Browsers', N'U') IS NULL
CREATE TABLE logs.Browsers (
    BrowserId    INT         NOT NULL CONSTRAINT PK_Browsers PRIMARY KEY,
    BrowserName  VARCHAR(20) NOT NULL CONSTRAINT UQ_Browsers_Name UNIQUE
);
GO

IF OBJECT_ID(N'logs.DeviceClasses', N'U') IS NULL
CREATE TABLE logs.DeviceClasses (
    DeviceClassId    INT         NOT NULL CONSTRAINT PK_DeviceClasses PRIMARY KEY,
    DeviceClassName  VARCHAR(20) NOT NULL CONSTRAINT UQ_DeviceClasses_Name UNIQUE
);
GO

-- -----------------------------------------------------------------------------
-- 4. Seeds (upsert: stable ids, names corrected on drift)
-- -----------------------------------------------------------------------------
MERGE logs.AppClients AS t
USING (VALUES (0, 'unknown'), (1, 'tsic-teams'), (2, 'tsic-events'), (3, 'tsic-web'))
      AS s (AppClientId, AppClientName)
ON t.AppClientId = s.AppClientId
WHEN MATCHED AND t.AppClientName <> s.AppClientName
    THEN UPDATE SET AppClientName = s.AppClientName
WHEN NOT MATCHED THEN INSERT (AppClientId, AppClientName)
    VALUES (s.AppClientId, s.AppClientName);
GO

MERGE logs.Platforms AS t
USING (VALUES (0, 'unknown'), (1, 'ios'), (2, 'android'), (3, 'web'))
      AS s (PlatformId, PlatformName)
ON t.PlatformId = s.PlatformId
WHEN MATCHED AND t.PlatformName <> s.PlatformName
    THEN UPDATE SET PlatformName = s.PlatformName
WHEN NOT MATCHED THEN INSERT (PlatformId, PlatformName)
    VALUES (s.PlatformId, s.PlatformName);
GO

MERGE logs.Browsers AS t
USING (VALUES (0, 'unknown'), (1, 'chrome'), (2, 'safari'), (3, 'edge'),
              (4, 'firefox'), (5, 'webview'), (6, 'other'))
      AS s (BrowserId, BrowserName)
ON t.BrowserId = s.BrowserId
WHEN MATCHED AND t.BrowserName <> s.BrowserName
    THEN UPDATE SET BrowserName = s.BrowserName
WHEN NOT MATCHED THEN INSERT (BrowserId, BrowserName)
    VALUES (s.BrowserId, s.BrowserName);
GO

MERGE logs.DeviceClasses AS t
USING (VALUES (0, 'unknown'), (1, 'phone'), (2, 'tablet'), (3, 'desktop'))
      AS s (DeviceClassId, DeviceClassName)
ON t.DeviceClassId = s.DeviceClassId
WHEN MATCHED AND t.DeviceClassName <> s.DeviceClassName
    THEN UPDATE SET DeviceClassName = s.DeviceClassName
WHEN NOT MATCHED THEN INSERT (DeviceClassId, DeviceClassName)
    VALUES (s.DeviceClassId, s.DeviceClassName);
GO

-- -----------------------------------------------------------------------------
-- 5. Fact table -- created if missing, NEVER dropped
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'logs.AppUsage', N'U') IS NULL
BEGIN
    PRINT 'Creating logs.AppUsage...';

    CREATE TABLE logs.AppUsage (
        Id             BIGINT IDENTITY(1,1) NOT NULL,
        OccurredAt     DATETIME2(3)      NOT NULL,   -- request START, AZ server-local
        AppClientId    INT               NOT NULL,   -- 0 = unknown
        PlatformId     INT               NOT NULL,   -- 0 = unknown
        AppVersion     VARCHAR(32)       NOT NULL,   -- '' = unknown
        [Controller]   VARCHAR(50)       NOT NULL,
        [Action]       VARCHAR(60)       NOT NULL,
        QueryString    NVARCHAR(400)     NULL,       -- allowlist-filtered, fail closed
        StatusCode     SMALLINT          NOT NULL,
        UserId         NVARCHAR(450)     NULL,       -- sub claim; NULL = anonymous
        RegId          UNIQUEIDENTIFIER  NULL,
        JobId          UNIQUEIDENTIFIER  NOT NULL,   -- Guid.Empty = no job context
        TeamId         UNIQUEIDENTIFIER  NULL,
        IsBot          BIT               NOT NULL,
        BrowserId      INT               NOT NULL,
        DeviceClassId  INT               NOT NULL,

        CONSTRAINT PK_AppUsage PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_AppUsage_AppClients
            FOREIGN KEY (AppClientId)   REFERENCES logs.AppClients (AppClientId),
        CONSTRAINT FK_AppUsage_Platforms
            FOREIGN KEY (PlatformId)    REFERENCES logs.Platforms (PlatformId),
        CONSTRAINT FK_AppUsage_Browsers
            FOREIGN KEY (BrowserId)     REFERENCES logs.Browsers (BrowserId),
        CONSTRAINT FK_AppUsage_DeviceClasses
            FOREIGN KEY (DeviceClassId) REFERENCES logs.DeviceClasses (DeviceClassId)
    ) WITH (DATA_COMPRESSION = PAGE);

    CREATE NONCLUSTERED INDEX IX_AppUsage_OccurredAt
        ON logs.AppUsage (OccurredAt)
        WITH (DATA_COMPRESSION = PAGE);

    CREATE NONCLUSTERED INDEX IX_AppUsage_JobId_OccurredAt
        ON logs.AppUsage (JobId, OccurredAt)
        WITH (DATA_COMPRESSION = PAGE);
END
ELSE
    PRINT 'logs.AppUsage exists -- untouched (append-only asset).';
GO

-- -----------------------------------------------------------------------------
-- 6. Application principal -- host-mapped, same as the file path in section 1.
--
-- The connection string uses Trusted_Connection, so the API authenticates as
-- its IIS app pool's Windows identity. SQL Server needs that identity to be
-- (a) a server LOGIN -- already true, it connects to TSICV5 -- and (b) a USER
-- in TSICLogs. Without (b), opening the database fails outright (error 4060 /
-- 18456 state 38); no amount of permission on TSICV5 helps.
--
-- SELECT and INSERT on the logs schema, nothing else. INSERT for the fact rows,
-- SELECT because the writer resolves lookup ids and the report services read.
-- A bug in the metering code cannot reach TSICV5 through this grant.
--
-- Database users do not survive a restore (same reason
-- 00-postdev-db-restore-apppooluser.sql exists for TSICV5). Re-running this
-- script after restoring TSICLogs re-creates the user and the grant.
-- -----------------------------------------------------------------------------
DECLARE @Machine sysname = CAST(SERVERPROPERTY('MachineName') AS sysname);

DECLARE @AppPool sysname =
    CASE @Machine
        WHEN N'TSIC-PHOENIX' THEN N'IIS APPPOOL\claude-api'
        WHEN N'TSIC-SEDONA'  THEN N'IIS APPPOOL\dev-api'
    END;

DECLARE @g nvarchar(max);

IF @AppPool IS NULL
    RAISERROR(N'Unknown host ''%s'' -- add its app pool to the map in section 6. Nothing granted; the API cannot write.', 16, 1, @Machine);
ELSE IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @AppPool)
    RAISERROR(N'Server login %s does not exist on this instance -- create it first. Nothing granted; the API cannot write.', 16, 1, @AppPool);
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @AppPool)
    BEGIN
        SET @g = N'CREATE USER ' + QUOTENAME(@AppPool)
               + N' FOR LOGIN ' + QUOTENAME(@AppPool) + N';';
        EXEC sp_executesql @g;
        PRINT '  Created user ' + @AppPool;
    END
    ELSE
        PRINT '  User ' + @AppPool + ' already present.';

    SET @g = N'GRANT SELECT, INSERT ON SCHEMA::logs TO ' + QUOTENAME(@AppPool) + N';';
    EXEC sp_executesql @g;

    PRINT 'TSICLogs setup complete -- SELECT, INSERT ON SCHEMA::logs granted to ' + @AppPool + '.';
END
GO

-- Leave the session in a normal state whichever path was taken above.
SET NOEXEC OFF;
GO
