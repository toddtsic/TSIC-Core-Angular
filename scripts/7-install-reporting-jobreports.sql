-- ============================================================================
-- 7-install-reporting-jobreports.sql
--
-- Single-file install for the new `reporting.JobReports` model. Idempotent
-- end-to-end. Re-runnable.
--
-- Three sections:
--   Section 1: Clear vestigial reporting tables
--                - reporting.ReportCatalogue   (parallel-catalog idea, abandoned)
--                - reporting.GlobalSettings    (kill-switch idea, never wired)
--   Section 2: Create reporting.JobReports (idempotent)
--   Section 3: Populate reporting.JobReports from legacy menus
--                (Jobs.JobMenus + Jobs.JobMenu_Items)
--
-- After this script:
--   * The reporting schema retains its ~120 stored procedures (the actual
--     report logic) plus exactly ONE table: reporting.JobReports.
--   * JobReports is the per-(job, role) source of truth going forward.
--     SU edits land in JobReports directly. Legacy menus stay where they are
--     for now, but new system does not write back to them.
--   * Job-clone must clone reporting.JobReports rows alongside other artifacts.
--   * Re-scaffold EF after running this script — entities for ReportCatalogue
--     and GlobalSettings disappear, JobReports appears:
--       .\scripts\3) RE-Scaffold-Db-Entities.ps1
--
-- Apply with:
--   sqlcmd -S .\SS2016 -d <db> -I -i "scripts/7-install-reporting-jobreports.sql"
-- ============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ============================================================================
-- Section 1: Drop vestigial reporting tables
-- ============================================================================
-- Both confirmed as leaf tables (no inbound FKs from other tables) so DROP
-- proceeds cleanly. They will not be re-introduced by this script.

IF OBJECT_ID('reporting.ReportCatalogue', 'U') IS NOT NULL
BEGIN
    DROP TABLE reporting.ReportCatalogue;
    PRINT 'Dropped reporting.ReportCatalogue';
END
ELSE
    PRINT 'reporting.ReportCatalogue not present - skipped';
GO

IF OBJECT_ID('reporting.GlobalSettings', 'U') IS NOT NULL
BEGIN
    DROP TABLE reporting.GlobalSettings;
    PRINT 'Dropped reporting.GlobalSettings';
END
ELSE
    PRINT 'reporting.GlobalSettings not present - skipped';
GO

-- ============================================================================
-- Section 2: Create reporting.JobReports (idempotent)
-- ============================================================================
--
-- One row per (Job, Role, Controller, Action). Each row carries enough data
-- to render and run the report without joining other tables:
--
--   Title       -- display text in reports library (from JobMenu_Items.Text)
--   IconName    -- (from JobMenu_Items.IconName)
--   Controller  -- e.g. 'Reporting', 'Home'
--   Action      -- full route + query string from legacy menu, e.g.
--                  'ExportStoredProcedureResults?spName=[reporting].[Foo]&bUseJobId=true'
--                  or 'Get_JobPlayer_Transactions' (T1) or 'ShowJobInvoices' (Home/T1)
--   Kind        -- 'StoredProcedure' (Action starts with 'ExportStoredProcedureResults?')
--                  or 'CrystalReport' (anything else). Derived at populate time.
--   GroupLabel  -- legacy L1 parent's Text (e.g. 'Reports', 'Recruiting'). Drives
--                  tab grouping in the library; SU may edit per (Job, Role).
--   SortOrder   -- legacy JobMenu_Items.Index for stable display order
--   Active      -- per (Job, Role) on/off, mirrors legacy JobMenu_Items.Active
--
-- Source of truth post-install: this table. Legacy is the seed only.

-- Drop-if-empty safety: if a prior failed run left an empty table with stale
-- column widths or constraints, drop it so the canonical shape below applies.
-- Will NOT drop a populated table — preserves SU edits across re-runs.
--
-- Wrapped in dynamic SQL so the inner SELECT against reporting.JobReports
-- isn't parsed when the table doesn't exist (fresh DB install case).
IF OBJECT_ID('reporting.JobReports', 'U') IS NOT NULL
BEGIN
    EXEC('
        IF NOT EXISTS (SELECT 1 FROM reporting.JobReports)
        BEGIN
            DROP TABLE reporting.JobReports;
            PRINT ''Dropped empty reporting.JobReports (will be recreated with current shape)'';
        END
    ');
END
GO

-- Column widths sized to 2-3x measured legacy max:
--   Action max=150  → NVARCHAR(250)
--   Controller max=9 → NVARCHAR(50)
--   GroupLabel max=12 → NVARCHAR(50)
-- These keep the unique-key index under SQL Server's 1700-byte limit
-- (16 + 900 + 100 + 500 + 100 = 1616 bytes).
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'reporting' AND t.name = 'JobReports')
BEGIN
    CREATE TABLE reporting.JobReports (
        JobReportId    UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        JobId          UNIQUEIDENTIFIER NOT NULL,
        RoleId         NVARCHAR(450)    NOT NULL,
        Title          NVARCHAR(200)    NOT NULL,
        IconName       NVARCHAR(50)     NULL,
        Controller     NVARCHAR(50)     NOT NULL,
        [Action]       NVARCHAR(250)    NOT NULL,
        Kind           NVARCHAR(20)     NOT NULL,
        GroupLabel     NVARCHAR(50)     NULL,
        SortOrder      INT              NOT NULL DEFAULT 0,
        Active         BIT              NOT NULL,
        Modified       DATETIME         NOT NULL DEFAULT GETDATE(),
        LebUserId      NVARCHAR(450)    NULL,

        CONSTRAINT PK_JobReports PRIMARY KEY (JobReportId),
        -- GroupLabel included in the unique key because legacy intentionally
        -- lists the same report under multiple L1 groups for the same (job,role)
        -- so it surfaces in multiple tabs (e.g. PlayerStats_E120 under both
        -- 'Reports' and 'Player Stats' groups). The library shows it twice on
        -- purpose; we preserve that intent.
        CONSTRAINT UX_JobReports_JobRoleActionGroup
            UNIQUE (JobId, RoleId, Controller, [Action], GroupLabel),
        CONSTRAINT FK_JobReports_Job
            FOREIGN KEY (JobId)  REFERENCES Jobs.Jobs(JobId),
        CONSTRAINT FK_JobReports_Role
            FOREIGN KEY (RoleId) REFERENCES dbo.AspNetRoles(Id),
        CONSTRAINT FK_JobReports_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );

    CREATE INDEX IX_JobReports_JobRole
        ON reporting.JobReports (JobId, RoleId, Active)
        INCLUDE (Controller, [Action], Title, IconName, Kind, GroupLabel, SortOrder);

    PRINT 'Created reporting.JobReports';
END
ELSE
    PRINT 'reporting.JobReports already exists - skipped';
GO

-- Idempotent unique-constraint shape upgrade (handles installs created before
-- GroupLabel was added to the unique key). Safe no-op when already correct.
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'UX_JobReports_JobRoleAction'
             AND parent_object_id = OBJECT_ID('reporting.JobReports'))
BEGIN
    ALTER TABLE reporting.JobReports DROP CONSTRAINT UX_JobReports_JobRoleAction;
    PRINT 'Dropped legacy UX_JobReports_JobRoleAction (will be replaced)';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
               WHERE name = 'UX_JobReports_JobRoleActionGroup'
                 AND parent_object_id = OBJECT_ID('reporting.JobReports'))
BEGIN
    ALTER TABLE reporting.JobReports
        ADD CONSTRAINT UX_JobReports_JobRoleActionGroup
        UNIQUE (JobId, RoleId, Controller, [Action], GroupLabel);
    PRINT 'Added UX_JobReports_JobRoleActionGroup';
END
ELSE
    PRINT 'UX_JobReports_JobRoleActionGroup already exists - skipped';
GO

-- ============================================================================
-- Section 3: Populate reporting.JobReports from legacy menus
-- ============================================================================
--
-- WHERE NOT EXISTS guard makes this idempotent — re-running inserts only
-- (Job, Role, Controller, Action, GroupLabel) tuples that aren't already
-- present. SU edits to existing rows are NOT overwritten by re-running.
--
-- Population scope:
--   * MenuTypeId = 6  (the reporting menu type in legacy)
--   * Year filter: ('2025','2026','2027') — current + 2 surrounding years. Adjust
--     the @ImportYears table-variable below to widen scope.
--   * Controller IN ('Reporting', 'Home/ShowJobInvoices') — the two known surfaces
--     where reports live in legacy. Extend this WHERE clause if more are found.
--   * Action exclusion list — see @ExcludeActions below for retired reports.
--
-- "Junk row" filters from the legacy menu admin (`new parent item`/`new child item`
-- placeholders that admins forgot to fill in) are excluded.
--
-- Dedup: legacy data contains accidental duplicate L2 rows (same Job/Role/L1/Action,
-- different MenuItemId — SU clicked Add twice). ROW_NUMBER picks one per
-- (Job, Role, Controller, Action, GroupLabel) tuple, preferring the Active row
-- with lowest SortOrder.

DECLARE @ImportYears TABLE ([Year] NVARCHAR(10) NOT NULL PRIMARY KEY);
INSERT INTO @ImportYears VALUES ('2025'), ('2026'), ('2027');

-- Retired reports — Actions in this list are scrubbed from reporting.JobReports
-- on every run AND skipped during populate, so re-running this script is the
-- single canonical way to retire a report. Legacy Jobs.JobMenu_Items is left
-- alone (snapshot/read-only); the boundary is enforced here.
DECLARE @ExcludeActions TABLE ([Action] NVARCHAR(250) NOT NULL PRIMARY KEY);
INSERT INTO @ExcludeActions VALUES
    (N'Get_NetUsers'),       -- obsolete network-level user count (test report ~20yr old)
    (N'ShowJobInvoices');    -- live invoice viewer; mutates post-billing (chargebacks/refunds)
                             -- so users would see numbers disagreeing with printed copies they
                             -- received at billing time. Retired for ALL roles 2026-05-04.

DELETE FROM reporting.JobReports
WHERE [Action] IN (SELECT [Action] FROM @ExcludeActions);

DECLARE @ExcludedCount INT = @@ROWCOUNT;
PRINT CONCAT('Scrubbed ', @ExcludedCount, ' retired-action row(s) from reporting.JobReports');

;WITH SourceMenus AS (
    SELECT
        jm.JobId,
        jm.RoleId,
        jmiC.[Text]                                                     AS Title,
        jmiC.IconName,
        jmiC.Controller,
        jmiC.[Action],
        CASE
            WHEN jmiC.[Action] LIKE 'ExportStoredProcedureResults?%' THEN 'StoredProcedure'
            ELSE 'CrystalReport'
        END                                                             AS Kind,
        jmiP.[Text]                                                     AS GroupLabel,
        ISNULL(jmiC.[Index], 0)                                         AS SortOrder,
        jmiC.Active,
        ROW_NUMBER() OVER (
            PARTITION BY jm.JobId, jm.RoleId, jmiC.Controller, jmiC.[Action], jmiP.[Text]
            ORDER BY jmiC.Active DESC,             -- prefer Active=1 over Active=0
                     ISNULL(jmiC.[Index], 0) ASC,  -- prefer lower SortOrder
                     jmiC.MenuItemId ASC           -- final tiebreaker for determinism
        ) AS DedupRn
    FROM Jobs.JobMenus jm
    INNER JOIN Jobs.Jobs j
        ON jm.JobId = j.JobId
    INNER JOIN Jobs.JobMenu_Items jmiP
        ON jmiP.MenuId = jm.MenuId
       AND jmiP.ParentMenuItemId IS NULL
    INNER JOIN Jobs.JobMenu_Items jmiC
        ON jmiC.MenuId = jmiP.MenuId
       AND jmiC.ParentMenuItemId = jmiP.MenuItemId
    WHERE jm.MenuTypeId = 6
      AND j.[Year] IN (SELECT [Year] FROM @ImportYears)
      AND jmiC.[Action] IS NOT NULL
      AND jmiC.Controller IS NOT NULL
      AND (
            jmiC.Controller = 'Reporting'
         OR (jmiC.Controller = 'Home' AND jmiC.[Action] = 'ShowJobInvoices')
      )
      AND jmiP.[Text] <> 'new parent item'
      AND jmiC.[Text] <> 'new child item'
      AND jmiC.[Action] NOT IN (SELECT [Action] FROM @ExcludeActions)
)
INSERT INTO reporting.JobReports (
    JobId, RoleId, Title, IconName, Controller, [Action], Kind, GroupLabel, SortOrder, Active
)
SELECT
    s.JobId, s.RoleId, s.Title, s.IconName, s.Controller, s.[Action], s.Kind, s.GroupLabel, s.SortOrder, s.Active
FROM SourceMenus s
WHERE s.DedupRn = 1
  AND NOT EXISTS (
        SELECT 1
        FROM reporting.JobReports jr
        WHERE jr.JobId      = s.JobId
          AND jr.RoleId     = s.RoleId
          AND jr.Controller = s.Controller
          AND jr.[Action]   = s.[Action]
          AND ISNULL(jr.GroupLabel, N'') = ISNULL(s.GroupLabel, N'')
  );

DECLARE @InsertedCount INT = @@ROWCOUNT;

PRINT '';
PRINT '=== Populate Result ===';
PRINT CONCAT('Inserted ', @InsertedCount, ' new row(s) into reporting.JobReports (idempotent on (JobId, RoleId, Controller, Action, GroupLabel))');

-- ============================================================================
-- Section 4: Final state — totals + breakdown
-- ============================================================================

PRINT '';
PRINT '=== reporting.JobReports — totals ===';

SELECT COUNT(*)                          AS TotalRows,
       COUNT(DISTINCT JobId)              AS DistinctJobs,
       COUNT(DISTINCT RoleId)             AS DistinctRoles,
       COUNT(DISTINCT Controller + '/' + [Action]) AS DistinctReportEndpoints
FROM reporting.JobReports;

PRINT '';
PRINT '=== Breakdown by Kind ===';

SELECT Kind, COUNT(*) AS [Count]
FROM reporting.JobReports
GROUP BY Kind
ORDER BY Kind;

PRINT '';
PRINT '=== Breakdown by GroupLabel (top 20) ===';

SELECT TOP 20 GroupLabel, COUNT(*) AS [Count]
FROM reporting.JobReports
GROUP BY GroupLabel
ORDER BY [Count] DESC;

PRINT '';
PRINT '=== Remaining objects in reporting schema (expect: stored procs + 1 table) ===';

SELECT o.type_desc AS [Kind], COUNT(*) AS [Count]
FROM sys.objects o
JOIN sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = 'reporting' AND o.type IN ('U','V','P','FN','TF')
GROUP BY o.type_desc
ORDER BY o.type_desc;

SET NOCOUNT OFF;
