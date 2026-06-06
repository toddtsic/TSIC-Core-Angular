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
--                  or 'BoldReport' (starts with 'ExportBoldReport?')
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
    (N'ShowJobInvoices'),    -- live invoice viewer; mutates post-billing (chargebacks/refunds)
                             -- so users would see numbers disagreeing with printed copies they
                             -- received at billing time. Retired for ALL roles 2026-05-04.
    (N'JobRosters_MSYSA'),   -- MSYSA-format soccer roster; only ever lived on closed (_chiuso)
                             -- jobs — confirmed 0 live jobs (migrated table + legacy source).
                             -- Dead report; retired (not migrated) 2026-05-25.
    -- Tournament Rosters Packed (Bold/RDL) — retired 2026-05-31, replaced by the
    -- in-app PackedRoster Designer (SpaComponent; see @ActionMap below). Listed
    -- here so prior runs' Bold rows are scrubbed; the legacy Crystal actions are
    -- NOT excluded — they remain in @ActionMap to drive the Designer remap.
    (N'ExportBoldReport?reportName=TournamentRosterPacked&bUseJobId=true'),
    (N'ExportBoldReport?reportName=TournamentRosterPacked_CollegeCommit&bUseJobId=true'),
    -- CR-retirement Phase 0, Tier 1 (2026-06-05): legacy Crystal reports whose
    -- replacement Designer/station is runtime-verified. Removed from
    -- type1-report-catalog.ts (the regular-role frontend source) in the same change;
    -- excluded here so the SuperUser DB-sourced view drops them too. The replacement
    -- endpoints stay live, so re-listing or un-listing is fully reversible.
    -- NOTE: TournamentRosterPacked is intentionally NOT listed -- it is already
    -- remapped to the PackedRoster Designer via @ActionMap below; excluding it here
    -- would delete that Designer row instead of retiring the legacy Crystal.
    (N'Job_Club_Rosters'),                                  -- -> Roster Table Designer (Club Roster preset)
    (N'camp_daygroups'),                                    -- -> Roster Table Designer (Day Group / Camp preset)
    (N'Get_JobRosters_PackedByPositionAGNoClubPlayers'),    -- -> PackedRoster Designer (club-affiliation OFF)
    (N'Get_JobRosters_PackedByPosition_XPO'),               -- -> PackedRoster Designer (Packed XPO preset)
    (N'TournamentRecruitingReport'),                        -- -> PackedRoster Designer (recruiter mode)
    (N'TournyCheckin'),                                     -- -> Check-In live station
    (N'AmericanSelectTournyCheckin'),                       -- -> Check-In live station
    (N'Job_CampCheckin'),                                   -- -> Check-In live station
    (N'JobRosters_TryoutsCheckReport'),                     -- -> Check-In live station
    (N'ISP_CheckinFlat'),                                   -- -> Check-In live station
    -- CR-retirement Phase 0, Tier 2 (2026-06-05): Field Utilization family ->
    -- Schedule List Designer "Field Utilization" preset (runtime-verified 2026-06-05).
    (N'FieldUtilizationAcrossLeaguesTournament'),           -- -> Schedule List Designer (Field Utilization preset)
    (N'FieldUtilizationAcrossLeaguesByDateTournament'),     -- -> Schedule List Designer (Field Utilization + date)
    (N'Score_Input'),                                       -- -> Schedule List Designer (Score Entry Sheets / Blank preset)
    (N'Get_JobPlayers_STEPS'),                              -- -> Roster Table Designer (Sizes preset, was STEPS)
    (N'Get_JobRosters_RecruitingReport'),                   -- -> Roster Table Designer (Recruiting tabular preset)
    (N'Get_JobRosters_RecruitingReport_XPO'),               -- -> Roster Table Designer (Recruiting tabular preset)
    (N'Job_Rosters_NoMedical'),                             -- -> Roster Table Designer (No-Medical preset)
    (N'clubrostersNoMedicalII'),                            -- -> Roster Table Designer (Coaches preset)
    -- Camp Tier-2 (2026-06-05): folded into Roster Table Designer "camp" mode
    -- (Day/Night/Roommate group-by + stacked/packed-XPO approx layouts). Night has
    -- no data in any camp job; Day/Roommate group-by verified. User: "assume all ok."
    (N'camp_daygroups_pdf'),                                -- -> Camp Groups Designer (Day Group, stacked approx)
    (N'camp_nightgroups'),                                  -- -> Camp Groups Designer (Night Group; no data anywhere)
    (N'camp_nightgroups_pdf'),                              -- -> Camp Groups Designer (Night Group, stacked approx)
    (N'camp_roomies'),                                      -- -> Camp Groups Designer (Roommate group-by)
    (N'JobRosters_DayGroupsPackedXPO');                     -- -> PackedRoster Designer (Day Group packed XPO approx)

-- CR -> SP-Excel conversions. Maps a report's legacy (Crystal) action to its
-- SP-Excel action so the populate below emits a StoredProcedure row pointing at
-- the reporting_migrate sproc (script 11). One row per converted report.
-- TitleOverride (optional): forces a new display title regardless of what the
-- legacy menu rows said — used when the migration also renames the report
-- (e.g. opaque "(JS)" suffix → semantic "(CC)" for College Commit).
DECLARE @ActionMap TABLE (
    LegacyAction  NVARCHAR(250) NOT NULL PRIMARY KEY,
    NewAction     NVARCHAR(250) NOT NULL,
    NewGroupLabel NVARCHAR(100) NOT NULL,  -- a category code from report-categories.ts; drives the library bucket
    TitleOverride NVARCHAR(250) NULL,
    NewKind       NVARCHAR(20)  NULL       -- forces Kind when set (e.g. 'SpaComponent' for interactive tools
                                           -- whose NewAction is an in-app route, not an Export* endpoint).
                                           -- NULL = infer from the NewAction prefix (SP / Bold / Crystal).
);
INSERT INTO @ActionMap (LegacyAction, NewAction, NewGroupLabel, TitleOverride) VALUES
    (N'TournamentRecruitingReport_DataDump',
     N'ExportStoredProcedureResults?spName=reporting_migrate.JobRosters_ExportTournament&bUseJobId=true',
     N'Recruiting', NULL),
    (N'JobStaff_Excel',
     N'ExportStoredProcedureResults?spName=reporting_migrate.Get_Staff_ForExcelExport&bUseJobId=true',
     N'Rosters', NULL),
    (N'Get_JobPlayer_Transactions',
     N'ExportStoredProcedureResults?spName=reporting_migrate.Get_Player_Transactions_ForExcelExport&bUseJobId=true',
     N'Financials', NULL),
    (N'Get_DiscountedPlayers',
     N'ExportStoredProcedureResults?spName=reporting_migrate.Get_DiscountedPlayers&bUseJobId=true',
     N'Financials', NULL),
    (N'PlayerStats_ParisiExportExcel',
     N'ExportStoredProcedureResults?spName=reporting_migrate.PlayerStats_ParisiResults&bUseJobId=true',
     N'Rosters', NULL),
    (N'Get_TeamFieldDistribution',
     N'ExportStoredProcedureResults?spName=reporting_migrate.TeamsFieldDistribution&bUseJobId=true',
     N'Schedules', NULL),
    (N'Mobile_JobUsers',
     N'ExportStoredProcedureResults?spName=reporting_migrate.UsageByJobAndRegistrant&bUseJobId=true',
     N'Administration', NULL),
    (N'League_Teams',
     N'ExportStoredProcedureResults?spName=reporting_migrate.League_Teams&bUseJobId=true',
     N'Administration', NULL),
    (N'Get_CustomerPlayers1',
     N'ExportStoredProcedureResults?spName=reporting_migrate.Get_Players_ForExcelExport_AllCustomers&bUseJobId=true',
     N'Rosters', NULL),
    (N'Get_JobRosters_RecruitingReport_DumpExcel',
     N'ExportStoredProcedureResults?spName=reporting_migrate.JobRosters_RecruitingReport_DumpExcel&bUseJobId=true',
     N'Recruiting', NULL),
    (N'Get_JobRosters_RecruitingReport_Public_DumpExcel',
     N'ExportStoredProcedureResults?spName=reporting_migrate.JobRosters_RecruitingReport_Public&bUseJobId=true',
     N'Recruiting', NULL),
    (N'Get_JobPlayers_STEPS_Excel',
     N'ExportStoredProcedureResults?spName=reporting_migrate.Get_JobPlayers_STEPS_Excel&bUseJobId=true',
     N'Rosters', NULL),
    (N'JobPlayers_YJ_Excel',
     N'ExportStoredProcedureResults?spName=reporting_migrate.JobPlayers_YJ_Excel&bUseJobId=true',
     N'Rosters', NULL),
    (N'Get_JobPlayers_E120_Excel',
     N'ExportStoredProcedureResults?spName=reporting_migrate.Get_JobPlayers_E120_Excel&bUseJobId=true',
     N'Rosters', NULL),
    (N'Get_JobPlayers_Liberty_Excel',
     N'ExportStoredProcedureResults?spName=reporting_migrate.Get_JobPlayers_Liberty_Excel&bUseJobId=true',
     N'Rosters', NULL),
    (N'camp_datadump',
     N'ExportStoredProcedureResults?spName=reporting_migrate.camp_datadump&bUseJobId=true',
     N'Camp', NULL),
    (N'camp_excelexport_long',
     N'ExportStoredProcedureResults?spName=reporting_migrate.camp_excelexport_long&bUseJobId=true',
     N'Camp', NULL),
    (N'camp_excelexport_short',
     N'ExportStoredProcedureResults?spName=reporting_migrate.camp_excelexport_short&bUseJobId=true',
     N'Camp', NULL),
    (N'camp_excelexport_veryshort',
     N'ExportStoredProcedureResults?spName=reporting_migrate.camp_excelexport_veryshort&bUseJobId=true',
     N'Camp', NULL),
    (N'camp_excelexport_daygroups',
     N'ExportStoredProcedureResults?spName=reporting_migrate.camp_excelexport_daygroups&bUseJobId=true',
     N'Camp', NULL),
    (N'camp_excelexport_roomies',
     N'ExportStoredProcedureResults?spName=reporting_migrate.camp_excelexport_roomies&bUseJobId=true',
     N'Camp', NULL),
    (N'camp_excelexport_room_position',
     N'ExportStoredProcedureResults?spName=reporting_migrate.camp_excelexport_room_position&bUseJobId=true',
     N'Camp', NULL),
    (N'camp_excelexport_summer',
     N'ExportStoredProcedureResults?spName=reporting_migrate.camp_excelexport_summer&bUseJobId=true',
     N'Camp', NULL);

-- Tournament Rosters Packed family -> retired Bold/RDL, replaced by the in-app
-- PackedRoster Designer (SpaComponent). Director picks columns/layout/toggles and
-- generates the PDF on the fly; the two retired RDL looks survive as starter
-- presets inside the Designer. Both legacy packed actions map to the single
-- Designer route, so the post-map dedup collapses them to ONE row per (job, role),
-- inheriting whatever job/role distribution the old packed reports had.
-- NewKind forces Kind='SpaComponent' because NewAction is an in-app route, not an
-- Export* endpoint (the prefix inference would otherwise mislabel it CrystalReport).
INSERT INTO @ActionMap (LegacyAction, NewAction, NewGroupLabel, TitleOverride, NewKind) VALUES
    (N'TournamentRosterPacked',
     N'reporting/packed-roster-designer',
     N'Rosters', N'Tournament Rosters Packed (Designer)', N'SpaComponent'),
    (N'TournamentRosterPacked_PositionSchool',
     N'reporting/packed-roster-designer',
     N'Rosters', N'Tournament Rosters Packed (Designer)', N'SpaComponent');

-- Atomic rebuild: wrap the sweep (two DELETEs) + populate (INSERT) in ONE
-- transaction so a populate failure (e.g. a remap collision raising a unique-key
-- violation) rolls the DELETEs back instead of leaving mapped reports
-- deleted-but-not-reinserted. SET XACT_ABORT ON (top of file) makes any runtime
-- error abort the batch and roll the transaction back automatically — so the
-- COMMIT below is only ever reached when all DML succeeded.
BEGIN TRANSACTION;

DELETE FROM reporting.JobReports
WHERE [Action] IN (SELECT [Action] FROM @ExcludeActions);

DECLARE @ExcludedCount INT = @@ROWCOUNT;
PRINT CONCAT('Scrubbed ', @ExcludedCount, ' retired-action row(s) from reporting.JobReports');

-- Sweep both the legacy (Crystal) action AND the mapped (SP) action for mapped
-- reports, so the populate re-inserts each one fresh with the current map's
-- action + category. Catching the SP action too means later edits to a report's
-- category in @ActionMap take effect on re-run (the map owns the converted rows).
DELETE FROM reporting.JobReports
WHERE [Action] IN (SELECT LegacyAction FROM @ActionMap)
   OR [Action] IN (SELECT NewAction    FROM @ActionMap);

DECLARE @MappedSweepCount INT = @@ROWCOUNT;
PRINT CONCAT('Swept ', @MappedSweepCount, ' row(s) for mapped report(s) (legacy + converted)');

;WITH SourceMenus AS (
    SELECT
        jm.JobId,
        jm.RoleId,
        COALESCE(am.TitleOverride, jmiC.[Text])                         AS Title,
        jmiC.IconName,
        jmiC.Controller,
        COALESCE(am.NewAction, jmiC.[Action])                           AS [Action],
        COALESCE(am.NewKind,
            CASE
                WHEN COALESCE(am.NewAction, jmiC.[Action]) LIKE 'ExportStoredProcedureResults?%' THEN 'StoredProcedure'
                WHEN COALESCE(am.NewAction, jmiC.[Action]) LIKE 'ExportBoldReport?%'             THEN 'BoldReport'
                ELSE 'CrystalReport'
            END)                                                        AS Kind,
        COALESCE(am.NewGroupLabel, jmiP.[Text])                         AS GroupLabel,
        ISNULL(jmiC.[Index], 0)                                         AS SortOrder,
        jmiC.Active,
        ROW_NUMBER() OVER (
            -- Partition by the EFFECTIVE (post-map) Action + GroupLabel — i.e. the
            -- exact UX_JobReports_JobRoleActionGroup unique key. A remapped report
            -- that lived under >1 legacy parent (e.g. Discounted Players under both
            -- 'Reports' and 'Search' for SuperUser) collapses to a single forced
            -- GroupLabel; partitioning on the originals would let both survive and
            -- collide on INSERT. For non-mapped rows the COALESCEs fall back to the
            -- originals, so their dedup is unchanged.
            PARTITION BY jm.JobId, jm.RoleId, jmiC.Controller,
                         COALESCE(am.NewAction, jmiC.[Action]),
                         COALESCE(am.NewGroupLabel, jmiP.[Text])
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
    LEFT JOIN @ActionMap am
        ON am.LegacyAction = jmiC.[Action]
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

-- ----------------------------------------------------------------------------
-- Net-new SpaComponent: Check-In station (no legacy ancestor)
-- ----------------------------------------------------------------------------
-- Unlike the PackedRoster Designer, Check-In replaces no legacy report, so it
-- has no menu row to ride the @ActionMap remap. Seed it directly as a
-- cross-join of qualifying jobs × reachable roles, mirroring the two existing
-- gates so the catalogue can't diverge from them:
--   * Jobs : JobType IN (Tournament/League Scheduling, Camp Registration) —
--            the nav manifest's jobTypes gate (scripts/5) — within @ImportYears
--            (same job scope as the legacy populate above).
--   * Roles: Superuser / Director / SuperDirector — the route guard
--            (app.routes.ts: tools/checkin).
--   * Action 'tools/checkin' is the jobPath-relative SPA route; Kind is forced
--     to 'SpaComponent' so the library navigates instead of downloading.
-- 'tools/checkin' is in neither @ExcludeActions nor @ActionMap, so it is never
-- touched by the sweep DELETEs above — the NOT EXISTS guard makes this purely
-- additive, and SU edits/removals to these rows survive re-runs.
INSERT INTO reporting.JobReports (
    JobId, RoleId, Title, IconName, Controller, [Action], Kind, GroupLabel, SortOrder, Active
)
SELECT
    j.JobId, r.Id, N'Check-In', N'clipboard-check', N'Reporting', N'tools/checkin',
    N'SpaComponent', N'Registrations', 0, 1
FROM Jobs.Jobs j
INNER JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId
CROSS JOIN dbo.AspNetRoles r
WHERE jt.JobTypeName IN (N'Tournament Scheduling', N'League Scheduling', N'Camp Registration')
  AND j.[Year] IN (SELECT [Year] FROM @ImportYears)
  AND r.[Name]  IN (N'Superuser', N'Director', N'SuperDirector')
  AND NOT EXISTS (
        SELECT 1
        FROM reporting.JobReports jr
        WHERE jr.JobId      = j.JobId
          AND jr.RoleId     = r.Id
          AND jr.Controller = N'Reporting'
          AND jr.[Action]   = N'tools/checkin'
          AND ISNULL(jr.GroupLabel, N'') = N'Registrations'
  );

DECLARE @CheckinCount INT = @@ROWCOUNT;

-- ----------------------------------------------------------------------------
-- Net-new SpaComponent: Schedule List Designer (additive; retires nothing yet)
-- ----------------------------------------------------------------------------
-- The director-built replacement for the Schedule_ExportExcel report family
-- (ScheduleMaster, ScheduleByDay, ScheduleByAgDiv, FieldUtilization*, the
-- Unscored export) plus the Score_Input blank-score sheet. Seeded as a NET-NEW
-- tile (not an @ActionMap remap) so it is purely additive: the legacy schedule
-- reports keep working until each is verified reproducible in the designer and
-- retired deliberately (the catalog-removal step of the migration convention).
-- Gate mirrors where schedules exist:
--   * Jobs : JobType IN (Tournament/League Scheduling) within @ImportYears.
--   * Roles: Superuser / Director / SuperDirector (route guard, app.routes.ts).
--   * Action 'reporting/schedule-list-designer' is the jobPath-relative SPA
--     route; Kind forced to 'SpaComponent' so the library navigates, not downloads.
-- Not in @ExcludeActions nor @ActionMap, so the sweep DELETEs never touch it; the
-- NOT EXISTS guard makes it idempotent and SU edits/removals survive re-runs.
INSERT INTO reporting.JobReports (
    JobId, RoleId, Title, IconName, Controller, [Action], Kind, GroupLabel, SortOrder, Active
)
SELECT
    j.JobId, r.Id, N'Schedule List (Designer)', N'calendar-week', N'Reporting',
    N'reporting/schedule-list-designer', N'SpaComponent', N'Schedules', 0, 1
FROM Jobs.Jobs j
INNER JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId
CROSS JOIN dbo.AspNetRoles r
WHERE jt.JobTypeName IN (N'Tournament Scheduling', N'League Scheduling')
  AND j.[Year] IN (SELECT [Year] FROM @ImportYears)
  AND r.[Name]  IN (N'Superuser', N'Director', N'SuperDirector')
  AND NOT EXISTS (
        SELECT 1
        FROM reporting.JobReports jr
        WHERE jr.JobId      = j.JobId
          AND jr.RoleId     = r.Id
          AND jr.Controller = N'Reporting'
          AND jr.[Action]   = N'reporting/schedule-list-designer'
          AND ISNULL(jr.GroupLabel, N'') = N'Schedules'
  );

DECLARE @ScheduleDesignerCount INT = @@ROWCOUNT;

-- ----------------------------------------------------------------------------
-- Net-new SpaComponent: Tournament Recruiting Report (Designer) (additive)
-- ----------------------------------------------------------------------------
-- Reproduces the legacy Crystal "TournamentRecruitingReport" (college-coach
-- recruiting packet) off the same EF roster query that fuels the PackedRoster
-- Designer. Seeded as a NET-NEW tile (not an @ActionMap remap) so it is purely
-- additive: the legacy Crystal recruiting report keeps working until it is
-- verified reproducible and retired deliberately (the catalog-removal step of
-- the migration convention).
-- Gate mirrors the legacy recruiting report's visibility (PRE_LEAGUE_TOURNAMENT
-- in type1-report-catalog.ts) and the Designer route guard:
--   * Jobs : JobType IN (Tournament/League Scheduling) within @ImportYears.
--   * Roles: Superuser / Director / SuperDirector (route guard, app.routes.ts).
--   * Action 'reporting/packed-roster-designer/recruiter' is the jobPath-relative
--     SPA route that opens the Designer in recruiter mode (data.mode); Kind
--     forced to 'SpaComponent' so the library navigates, not downloads.
-- Not in @ExcludeActions nor @ActionMap, so the sweep DELETEs never touch it; the
-- NOT EXISTS guard makes it idempotent and SU edits/removals survive re-runs.
INSERT INTO reporting.JobReports (
    JobId, RoleId, Title, IconName, Controller, [Action], Kind, GroupLabel, SortOrder, Active
)
SELECT
    j.JobId, r.Id, N'Tournament Recruiting Report (Designer)', N'journal-bookmark', N'Reporting',
    N'reporting/packed-roster-designer/recruiter', N'SpaComponent', N'Recruiting', 0, 1
FROM Jobs.Jobs j
INNER JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId
CROSS JOIN dbo.AspNetRoles r
WHERE jt.JobTypeName IN (N'Tournament Scheduling', N'League Scheduling')
  AND j.[Year] IN (SELECT [Year] FROM @ImportYears)
  AND r.[Name]  IN (N'Superuser', N'Director', N'SuperDirector')
  AND NOT EXISTS (
        SELECT 1
        FROM reporting.JobReports jr
        WHERE jr.JobId      = j.JobId
          AND jr.RoleId     = r.Id
          AND jr.Controller = N'Reporting'
          AND jr.[Action]   = N'reporting/packed-roster-designer/recruiter'
          AND ISNULL(jr.GroupLabel, N'') = N'Recruiting'
  );

DECLARE @RecruiterDesignerCount INT = @@ROWCOUNT;

-- ----------------------------------------------------------------------------
-- Net-new SpaComponent: Roster Table Designer (additive; retires nothing yet)
-- ----------------------------------------------------------------------------
-- The director-built replacement for the wide-roster Crystal family (Club
-- Rosters, No-Medical/clubrostersNoMedicalII, Teamplayers-Withcoach, Rosters
-- WithClubRep, STEPS, the tabular Recruiting roster). One broad EF dataset +
-- a runtime column/group/sort/orientation config renders a full-width table
-- PDF in-process; each retired report survives as a starter preset inside the
-- Designer. Seeded as a NET-NEW tile (not an @ActionMap remap) so it is purely
-- additive: the legacy roster reports keep working until each is verified
-- reproducible in the designer and retired deliberately (the catalog-removal
-- step of the migration convention).
-- Gate mirrors the other roster/schedule designers:
--   * Jobs : JobType IN (Tournament/League Scheduling) within @ImportYears.
--   * Roles: Superuser / Director / SuperDirector (route guard, app.routes.ts).
--   * Action 'reporting/roster-table-designer' is the jobPath-relative SPA
--     route; Kind forced to 'SpaComponent' so the library navigates, not downloads.
-- Not in @ExcludeActions nor @ActionMap, so the sweep DELETEs never touch it; the
-- NOT EXISTS guard makes it idempotent and SU edits/removals survive re-runs.
INSERT INTO reporting.JobReports (
    JobId, RoleId, Title, IconName, Controller, [Action], Kind, GroupLabel, SortOrder, Active
)
SELECT
    j.JobId, r.Id, N'Roster Table (Designer)', N'card-list', N'Reporting',
    N'reporting/roster-table-designer', N'SpaComponent', N'Rosters', 0, 1
FROM Jobs.Jobs j
INNER JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId
CROSS JOIN dbo.AspNetRoles r
WHERE jt.JobTypeName IN (N'Tournament Scheduling', N'League Scheduling')
  AND j.[Year] IN (SELECT [Year] FROM @ImportYears)
  AND r.[Name]  IN (N'Superuser', N'Director', N'SuperDirector')
  AND NOT EXISTS (
        SELECT 1
        FROM reporting.JobReports jr
        WHERE jr.JobId      = j.JobId
          AND jr.RoleId     = r.Id
          AND jr.Controller = N'Reporting'
          AND jr.[Action]   = N'reporting/roster-table-designer'
          AND ISNULL(jr.GroupLabel, N'') = N'Rosters'
  );

DECLARE @RosterTableDesignerCount INT = @@ROWCOUNT;

-- ----------------------------------------------------------------------------
-- Net-new SpaComponent: Camp Groups Designer (additive; retires nothing yet)
-- ----------------------------------------------------------------------------
-- The camp-roster Crystal family (camp_daygroups(_pdf), camp_nightgroups(_pdf),
-- camp_roomies, JobRosters_DayGroupsPackedXPO) is just rosters grouped by a camp
-- field (Day Group / Night Group / Roommate), so it folds into the SAME Roster
-- Table Designer engine — this tile deep-links to `…/roster-table-designer/camp`
-- (route data.mode='camp') which opens on the camp preset (group by Day Group;
-- the director can switch to Night Group / Roommate in-place). Net-new + additive:
-- the legacy camp reports keep working until each is verified + retired.
-- Gate differs from the other roster designers — CAMP jobs, not Tournament/League:
--   * Jobs : JobType = 'Camp Registration' within @ImportYears.
--   * Roles: Superuser / Director / SuperDirector (route guard, app.routes.ts).
--   * Action 'reporting/roster-table-designer/camp' is the jobPath-relative SPA
--     route; Kind forced to 'SpaComponent' so the library navigates, not downloads.
-- Not in @ExcludeActions nor @ActionMap, so the sweep DELETEs never touch it; the
-- NOT EXISTS guard makes it idempotent and SU edits/removals survive re-runs.
INSERT INTO reporting.JobReports (
    JobId, RoleId, Title, IconName, Controller, [Action], Kind, GroupLabel, SortOrder, Active
)
SELECT
    j.JobId, r.Id, N'Camp Groups (Designer)', N'people', N'Reporting',
    N'reporting/roster-table-designer/camp', N'SpaComponent', N'Camp', 0, 1
FROM Jobs.Jobs j
INNER JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId
CROSS JOIN dbo.AspNetRoles r
WHERE jt.JobTypeName IN (N'Camp Registration')
  AND j.[Year] IN (SELECT [Year] FROM @ImportYears)
  AND r.[Name]  IN (N'Superuser', N'Director', N'SuperDirector')
  AND NOT EXISTS (
        SELECT 1
        FROM reporting.JobReports jr
        WHERE jr.JobId      = j.JobId
          AND jr.RoleId     = r.Id
          AND jr.Controller = N'Reporting'
          AND jr.[Action]   = N'reporting/roster-table-designer/camp'
          AND ISNULL(jr.GroupLabel, N'') = N'Camp'
  );

DECLARE @CampGroupsDesignerCount INT = @@ROWCOUNT;

-- All sweep + populate DML succeeded — commit the atomic rebuild.
-- (@@ROWCOUNT captured into @InsertedCount above; COMMIT would reset it.)
COMMIT TRANSACTION;

PRINT '';
PRINT '=== Populate Result ===';
PRINT CONCAT('Inserted ', @InsertedCount, ' new row(s) into reporting.JobReports (idempotent on (JobId, RoleId, Controller, Action, GroupLabel))');
PRINT CONCAT('Inserted ', @CheckinCount, ' net-new Check-In SpaComponent row(s) (jobs x roles, idempotent)');
PRINT CONCAT('Inserted ', @ScheduleDesignerCount, ' net-new Schedule List Designer SpaComponent row(s) (jobs x roles, idempotent)');
PRINT CONCAT('Inserted ', @RecruiterDesignerCount, ' net-new Recruiting Report (Designer) SpaComponent row(s) (jobs x roles, idempotent)');
PRINT CONCAT('Inserted ', @RosterTableDesignerCount, ' net-new Roster Table (Designer) SpaComponent row(s) (jobs x roles, idempotent)');
PRINT CONCAT('Inserted ', @CampGroupsDesignerCount, ' net-new Camp Groups (Designer) SpaComponent row(s) (jobs x roles, idempotent)');

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
