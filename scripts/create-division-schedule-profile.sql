-- ============================================================
-- Division Schedule Profile + Field Preference Migration
-- Creates scheduling.DivisionScheduleProfile table and
-- adds FieldPreference column to FieldsLeagueSeason.
-- Idempotent — safe to run multiple times.
-- ============================================================

-- 1. Create scheduling schema if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'scheduling')
BEGIN
    EXEC('CREATE SCHEMA scheduling');
    PRINT 'Created schema: scheduling';
END

-- 2. Create DivisionScheduleProfile table
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'scheduling' AND TABLE_NAME = 'DivisionScheduleProfile')
BEGIN
    CREATE TABLE scheduling.DivisionScheduleProfile (
        ProfileId       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        JobId           UNIQUEIDENTIFIER NOT NULL,
        DivisionName    NVARCHAR(100)    NOT NULL,
        Placement       TINYINT          NOT NULL DEFAULT 0,   -- 0=Horizontal, 1=Sequential
        GapPattern      TINYINT          NOT NULL DEFAULT 1,   -- 0=BTB, 1=OneOnOneOff, 2=OneOnTwoOff
        InferredFromJob UNIQUEIDENTIFIER NULL,
        CreatedUtc      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        ModifiedUtc     DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_scheduling_DivisionScheduleProfile PRIMARY KEY (ProfileId),
        CONSTRAINT UQ_DivScheduleProfile_Job_DivName UNIQUE (JobId, DivisionName)
    );

    PRINT 'Created table: scheduling.DivisionScheduleProfile';
END
ELSE
    PRINT 'Table scheduling.DivisionScheduleProfile already exists — skipped.';

-- 3. Add FieldPreference column to Leagues.Fields_LeagueSeason
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'Leagues' AND TABLE_NAME = 'Fields_LeagueSeason' AND COLUMN_NAME = 'FieldPreference')
BEGIN
    ALTER TABLE Leagues.Fields_LeagueSeason
        ADD FieldPreference TINYINT NOT NULL DEFAULT 0;
        -- 0 = Normal, 1 = Preferred, 2 = Avoid

    PRINT 'Added column: Leagues.Fields_LeagueSeason.FieldPreference';
END
ELSE
    PRINT 'Column Leagues.Fields_LeagueSeason.FieldPreference already exists — skipped.';

-- 4. Verification
SELECT
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
     WHERE TABLE_SCHEMA = 'scheduling' AND TABLE_NAME = 'DivisionScheduleProfile')
        AS ProfileTableExists,
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = 'Leagues' AND TABLE_NAME = 'Fields_LeagueSeason' AND COLUMN_NAME = 'FieldPreference')
        AS FieldPreferenceColumnExists;
