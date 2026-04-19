-- ============================================================
-- Reporting Schema: reporting.ReportCatalogue
-- Idempotent: safe to run multiple times
--
-- Purpose: SuperUser-registerable Type 2 reports
-- (stored-proc to multi-tab XLSX via EPPlus).
--
-- Type 1 reports (Crystal Reports, legacy) stay hard-coded in the
-- frontend and are NOT stored here.
--
-- After running: re-scaffold EF entities from database
--   .\scripts\3) RE-Scaffold-Db-Entities.ps1
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'reporting')
    EXEC('CREATE SCHEMA reporting');
GO

-- ============================================================
-- reporting.ReportCatalogue
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'reporting' AND t.name = 'ReportCatalogue')
BEGIN
    CREATE TABLE reporting.ReportCatalogue (
        ReportId         UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Title            NVARCHAR(200)    NOT NULL,
        Description      NVARCHAR(1000)   NULL,
        IconName         NVARCHAR(50)     NULL,
        StoredProcName   NVARCHAR(200)    NOT NULL,
        ParametersJson   NVARCHAR(MAX)    NULL,
        VisibilityRules  NVARCHAR(MAX)    NULL,
        SortOrder        INT              NOT NULL DEFAULT 0,
        Active           BIT              NOT NULL DEFAULT 1,
        Modified         DATETIME         NOT NULL DEFAULT GETDATE(),
        LebUserId        NVARCHAR(450)    NULL,

        CONSTRAINT PK_ReportCatalogue PRIMARY KEY (ReportId),

        CONSTRAINT FK_ReportCatalogue_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );

    CREATE UNIQUE INDEX UX_ReportCatalogue_Title
        ON reporting.ReportCatalogue (Title);

    CREATE INDEX IX_ReportCatalogue_Active_Sort
        ON reporting.ReportCatalogue (Active, SortOrder) WHERE Active = 1;

    PRINT 'Created reporting.ReportCatalogue';
END
ELSE
    PRINT 'reporting.ReportCatalogue already exists - skipped';
GO

-- ============================================================
-- Verification
-- ============================================================
SELECT
    s.name AS [Schema],
    t.name AS [Table],
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS [Columns]
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = 'reporting'
ORDER BY t.name;
GO
