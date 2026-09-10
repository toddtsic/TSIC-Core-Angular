/*
    23-install-reporting-library.sql

    The Reports LIBRARY: the universe of reports a job COULD have. reporting.JobReports stays
    the per-(job, role) SHELF and the sole run-time entitlement; this adds the table the shelf
    is stocked FROM, and the link from each shelf row back to its library entry.

    Everything here is derived from what is already CALLED in reporting.JobReports -- the
    report key (spName for stored-proc rows, the endpoint/route for the rest), Kind,
    Controller, the canonical Action string, the most-common Title, the category the
    2026-09-08 GroupLabel pass ruled, and the job types the report is observed on. No content
    is authored here: Description and Tags are NULL and are a later, reviewable pass.

    Named ReportLibrary, NOT ReportCatalogue: 7-install-reporting-jobreports.sql Section 1
    unconditionally DROPs reporting.ReportCatalogue on every re-run.

    SCOPE and MinRoleId (Todd 2026-09-10). Two columns, one gate:
      Scope     = a FACT about the query, read from its body by the Superuser who wrote it:
                  JobOnly (filters on @jobId) | CrossJob (looks up the customer from @jobId and
                  widens to every job of that customer) | CrossWebsite (ignores or exceeds the
                  customer). Scope never gates directly; it FLOORS MinRoleId.
      MinRoleId = the POLICY, an AspNetRoles Id with hierarchy Director < SuperDirector <
                  Superuser. Director means Director and above; Superuser means Superuser only.
                  NULL means RETIRED (Todd 2026-09-10): no role qualifies, so it ranks ABOVE
                  Superuser (rank 4) in every comparison below -- never left to SQL NULL
                  semantics, which would silently skip it. A retired row keeps its metadata, is
                  on no shelf, is addable by nobody, and is the only state from which the
                  library row may be deleted (the FK from JobReports blocks delete otherwise)
                  or restored by setting a role again.
                  Defaulted from Scope (JobOnly->Director, CrossJob->SuperDirector,
                  CrossWebsite->Superuser); may be RAISED above the floor (JobCloneQA
                  is JobOnly tooling held at Superuser), never lowered
                  below it. Add and the sweep read MinRoleId only. The seed sets no NULLs.
    Raising MinRoleId SWEEPS: every shelf row whose role ranks below it is DELETED, across all
    jobs. Lowering restores nothing -- the report is simply addable again from the library.
    The seed applies the classification and performs that sweep once here; from then on the
    Superuser library editor owns both columns (rows it has stamped with LebUserId are never
    re-ruled by this script).

    IDEMPOTENT, RE-RUNNABLE. Every DDL statement is existence-guarded (including the upgrade of
    a table created with the earlier Tier column); the not-in-library DELETE finds nothing on a
    second run; the seed inserts only keys not already present; the ruling UPDATE touches only
    un-stamped rows whose values differ; the sweep finds nothing once applied; job types insert
    only missing pairs; the backfill touches only NULL links. A second run reports
    0 / 0 / 0 / 0 / 0 and changes nothing. The data phase aborts before writing if a
    transaction is already open in the window, if any of the three roles is missing, or if any
    derived category is not one of the seven codes, and rolls back if any shelf row would be
    left unlinked.

    NO OTHER SCRIPT IS RE-RUN. Script 7 is the cutover install and stays untouched.

    A shelf holds only library reports. PlayerStats_E120 (obsolete, Todd 2026-09-08/09) is not
    in the library, so its shelf rows (83 rows, 36 jobs, listed twice under 'Player Stats' and
    'Reports') are DELETED from reporting.JobReports here, inside the same transaction.

    TWO destructive statements, both inside the transaction, both previewed in a grid first:
      1. the E120 delete above;
      2. the MinRoleId sweep: Director shelf rows for the 15 reports ruled above Director
         (dev 2026-09-10: 331 rows, 237 of them Customer_RegistrationAccounting_Records on
         237 jobs at 47 customers). SuperDirector rows for Superuser-only reports: 0 today.

    After applying: re-scaffold EF (scripts/3) RE-Scaffold-Db-Entities.ps1) -- ReportLibrary,
    ReportLibraryJobTypes appear; JobReports gains ReportLibraryId + navigation.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

USE TSICV5;

-- WHERE AM I? Both boxes have a TSICV5. Dev (SEDONA) is a restored backup of prod; prod is
-- TSIC-PHOENIX's own local .SS2016. CHECK THIS FIRST GRID.
SELECT @@SERVERNAME AS Server_, DB_NAME() AS Database_, SYSDATETIME() AS RunAt;
GO

-- ============================================================================
-- Section 1: DDL (idempotent)
-- ============================================================================

IF OBJECT_ID('reporting.ReportLibrary', 'U') IS NULL
BEGIN
    CREATE TABLE reporting.ReportLibrary (
        ReportLibraryId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_ReportLibrary_Id DEFAULT NEWID(),
        -- Identity of the report: spName for StoredProcedure rows; the bare endpoint (Crystal-kind),
        -- RDL stem (Bold) or in-app route (SpaComponent) for the rest. Same extraction as
        -- 8-normalize-jobreports-grouplabels.sql.
        ReportKey        NVARCHAR(250)    NOT NULL,
        Title            NVARCHAR(200)    NOT NULL,   -- default title for a NEW shelf row
        Description      NVARCHAR(1000)   NULL,       -- content pass, later
        Tags             NVARCHAR(400)    NULL,       -- content pass, later; search only
        CategoryCode     NVARCHAR(50)     NOT NULL,   -- the seven Reports Library codes
        IconName         NVARCHAR(50)     NULL,
        Kind             NVARCHAR(20)     NOT NULL,   -- StoredProcedure | CrystalReport | BoldReport | SpaComponent
        Controller       NVARCHAR(50)     NOT NULL,
        [Action]         NVARCHAR(250)    NOT NULL,   -- CANONICAL action string written to new shelf rows
        Scope            NVARCHAR(20)     NOT NULL CONSTRAINT DF_ReportLibrary_Scope DEFAULT N'JobOnly',
        MinRoleId        NVARCHAR(450)    NULL,       -- AspNetRoles.Id; this role and above may add/hold it. NULL = RETIRED
        OwnerCustomerId  UNIQUEIDENTIFIER NULL,       -- optional: only this customer's jobs may add it
        -- No Active flag: retired = MinRoleId NULL (see header). The FK from JobReports
        -- stops a library row being deleted while any shelf still holds it.
        SortOrder        INT              NOT NULL CONSTRAINT DF_ReportLibrary_SortOrder DEFAULT 0,
        Modified         DATETIME         NOT NULL CONSTRAINT DF_ReportLibrary_Modified DEFAULT GETDATE(),
        LebUserId        NVARCHAR(450)    NULL,

        CONSTRAINT PK_ReportLibrary PRIMARY KEY (ReportLibraryId),
        CONSTRAINT UX_ReportLibrary_ReportKey UNIQUE (ReportKey),
        CONSTRAINT CK_ReportLibrary_CategoryCode CHECK (CategoryCode IN
            (N'Rosters', N'Schedules', N'Registrations', N'Financials', N'Camp', N'Recruiting', N'Administration')),
        CONSTRAINT CK_ReportLibrary_Kind CHECK (Kind IN
            (N'StoredProcedure', N'CrystalReport', N'BoldReport', N'SpaComponent')),
        CONSTRAINT CK_ReportLibrary_Scope CHECK (Scope IN (N'JobOnly', N'CrossJob', N'CrossWebsite')),
        CONSTRAINT FK_ReportLibrary_MinRole       FOREIGN KEY (MinRoleId)       REFERENCES dbo.AspNetRoles (Id),
        CONSTRAINT FK_ReportLibrary_OwnerCustomer FOREIGN KEY (OwnerCustomerId) REFERENCES Jobs.Customers (CustomerID),
        CONSTRAINT FK_ReportLibrary_LebUser       FOREIGN KEY (LebUserId)       REFERENCES dbo.AspNetUsers (Id)
    );
    PRINT 'Created reporting.ReportLibrary';
END
ELSE
    PRINT 'reporting.ReportLibrary already exists - skipped';
GO

-- Upgrade path: a ReportLibrary created before the 2026-09-10 ruling carries Tier
-- (Open | Restricted | SuperuserOnly) instead of Scope + MinRoleId. Each step is its own
-- guarded batch so a column added here resolves in the batch that fills it. The ruling in
-- Section 3 then sets Scope / MinRoleId per report, so nothing is mapped from Tier.
IF COL_LENGTH('reporting.ReportLibrary', 'Scope') IS NULL
BEGIN
    ALTER TABLE reporting.ReportLibrary ADD
        Scope NVARCHAR(20) NOT NULL CONSTRAINT DF_ReportLibrary_Scope DEFAULT N'JobOnly',
        CONSTRAINT CK_ReportLibrary_Scope CHECK (Scope IN (N'JobOnly', N'CrossJob', N'CrossWebsite'));
    PRINT 'Upgrade: added reporting.ReportLibrary.Scope';
END
GO

IF COL_LENGTH('reporting.ReportLibrary', 'MinRoleId') IS NULL
BEGIN
    -- Nullable by design (NULL = retired). Existing rows are NULL only until the Section 3
    -- ruling runs in this same script; it sets every un-stamped row.
    ALTER TABLE reporting.ReportLibrary ADD MinRoleId NVARCHAR(450) NULL;
    PRINT 'Upgrade: added reporting.ReportLibrary.MinRoleId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ReportLibrary_MinRole')
BEGIN
    ALTER TABLE reporting.ReportLibrary
        ADD CONSTRAINT FK_ReportLibrary_MinRole FOREIGN KEY (MinRoleId) REFERENCES dbo.AspNetRoles (Id);
    PRINT 'Upgrade: added FK_ReportLibrary_MinRole';
END
GO

IF COL_LENGTH('reporting.ReportLibrary', 'Tier') IS NOT NULL
BEGIN
    IF OBJECT_ID('reporting.CK_ReportLibrary_Tier', 'C') IS NOT NULL
        ALTER TABLE reporting.ReportLibrary DROP CONSTRAINT CK_ReportLibrary_Tier;
    IF OBJECT_ID('reporting.DF_ReportLibrary_Tier', 'D') IS NOT NULL
        ALTER TABLE reporting.ReportLibrary DROP CONSTRAINT DF_ReportLibrary_Tier;
    ALTER TABLE reporting.ReportLibrary DROP COLUMN Tier;
    PRINT 'Upgrade: dropped reporting.ReportLibrary.Tier';
END
GO

-- Applicability. No rows for a report = applicable to every job type.
IF OBJECT_ID('reporting.ReportLibraryJobTypes', 'U') IS NULL
BEGIN
    CREATE TABLE reporting.ReportLibraryJobTypes (
        ReportLibraryId  UNIQUEIDENTIFIER NOT NULL,
        JobTypeId        INT              NOT NULL,
        CONSTRAINT PK_ReportLibraryJobTypes PRIMARY KEY (ReportLibraryId, JobTypeId),
        CONSTRAINT FK_ReportLibraryJobTypes_Library FOREIGN KEY (ReportLibraryId) REFERENCES reporting.ReportLibrary (ReportLibraryId),
        CONSTRAINT FK_ReportLibraryJobTypes_JobType FOREIGN KEY (JobTypeId)       REFERENCES reference.JobTypes (JobTypeId)
    );
    PRINT 'Created reporting.ReportLibraryJobTypes';
END
ELSE
    PRINT 'reporting.ReportLibraryJobTypes already exists - skipped';
GO

-- Shelf -> library link. Nullable: legacy rows are backfilled below; a NULL after backfill is a
-- guard failure, not a valid state. NO cascade.
IF COL_LENGTH('reporting.JobReports', 'ReportLibraryId') IS NULL
BEGIN
    ALTER TABLE reporting.JobReports ADD ReportLibraryId UNIQUEIDENTIFIER NULL;
    PRINT 'Added reporting.JobReports.ReportLibraryId';
END
ELSE
    PRINT 'reporting.JobReports.ReportLibraryId already exists - skipped';
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_JobReports_ReportLibrary')
BEGIN
    ALTER TABLE reporting.JobReports
        ADD CONSTRAINT FK_JobReports_ReportLibrary
        FOREIGN KEY (ReportLibraryId) REFERENCES reporting.ReportLibrary (ReportLibraryId);
    PRINT 'Added FK_JobReports_ReportLibrary';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobReports_ReportLibraryId' AND object_id = OBJECT_ID('reporting.JobReports'))
BEGIN
    CREATE INDEX IX_JobReports_ReportLibraryId ON reporting.JobReports (ReportLibraryId) INCLUDE (JobId, RoleId, Active);
    PRINT 'Created IX_JobReports_ReportLibraryId';
END
GO

-- ============================================================================
-- Section 2: key every shelf row (same extraction as script 8)
-- ============================================================================
-- Sections 2-4 are ONE batch (no GO), so a RETURN in any guard ends the whole data phase.
-- The DDL above is per-statement guarded and self-committing, so it needs no transaction.

-- GUARD 0: no transaction may already be open (T-SQL transactions nest; a stale BEGIN TRAN
-- turns this batch's COMMIT into a no-op that is discarded when the window closes). Lives
-- here, not at the top of the file: a RETURN in the first batch would not stop the GO-separated
-- DDL batches from running.
IF @@TRANCOUNT > 0
BEGIN
    SELECT '*** ABORTED -- a transaction is already open in this window ***' AS Result,
           @@TRANCOUNT AS OpenTranCount,
           'Run ROLLBACK; in THIS window, then re-run this script.' AS Fix;
    RETURN;
END

-- The three admin roles, by name. Rank: Director 1 < SuperDirector 2 < Superuser 3. Any
-- other role (ApiAuthorized holds 2 ThirdPartyRosterExport rows) has no rank and is never
-- swept.
DECLARE @DirectorRoleId      NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = N'Director'),
        @SuperDirectorRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = N'SuperDirector'),
        @SuperuserRoleId     NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = N'Superuser');

-- GUARD 0b: all three roles must exist.
IF @DirectorRoleId IS NULL OR @SuperDirectorRoleId IS NULL OR @SuperuserRoleId IS NULL
BEGIN
    SELECT '*** ABORTED -- dbo.AspNetRoles is missing Director, SuperDirector or Superuser ***' AS Result,
           @DirectorRoleId AS Director_, @SuperDirectorRoleId AS SuperDirector_, @SuperuserRoleId AS Superuser_;
    RETURN;
END

-- THE RULING (Todd 2026-09-10, from the proc bodies / EF repository predicates; see the
-- header). Every key not listed is JobOnly / Director. Applied only to rows the Superuser
-- editor has not stamped (LebUserId IS NULL), so a later hand ruling survives a re-run.
IF OBJECT_ID('tempdb..#ruling') IS NOT NULL DROP TABLE #ruling;
CREATE TABLE #ruling (ReportKey NVARCHAR(250) NOT NULL PRIMARY KEY, Scope NVARCHAR(20) NOT NULL, MinRoleId NVARCHAR(450) NOT NULL);
INSERT INTO #ruling (ReportKey, Scope, MinRoleId) VALUES
    -- CrossJob: derives customerId from @jobId and widens to every job of that customer.
    (N'[reporting].[Customer_RegistrationAccounting_Records]',                     N'CrossJob',     @SuperDirectorRoleId),
    (N'reporting_migrate.Get_Players_ForExcelExport_AllCustomers',                 N'CrossJob',     @SuperDirectorRoleId),
    (N'Club_AllJobs_Rosters_NoMedical',                                            N'CrossJob',     @SuperDirectorRoleId),  -- EF: GetClubRosterRowsAsync(allCustomerJobs: true)
    (N'[reporting].[CustomerEndUsersDump]',                                        N'CrossJob',     @SuperDirectorRoleId),
    (N'[reporting].[AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam]', N'CrossJob',     @SuperDirectorRoleId),
    (N'[utility].[AmericanSelect_CountsAtYear_ByTeam]',                            N'CrossJob',     @SuperDirectorRoleId),
    (N'[utility].[AmericanSelect_CountsAtYear_ByTeamII]',                          N'CrossJob',     @SuperDirectorRoleId),
    (N'[reporting].[CathyCampBreakoutPerJob_AmtPaid]',                             N'CrossJob',     @SuperDirectorRoleId),
    (N'[reporting].[TLCContactsHx]',                                               N'CrossJob',     @SuperDirectorRoleId),
    (N'[reporting].[TournyYearOverYearClubTeams]',                                 N'CrossJob',     @SuperDirectorRoleId),
    -- CrossWebsite: ignores or exceeds the caller's customer.
    (N'[adn].[monthlycustomerrollups]',                                            N'CrossWebsite', @SuperuserRoleId),      -- customer groups, every customer
    (N'[reporting].[RegsaverRegistrants_Charlie]',                                 N'CrossWebsite', @SuperuserRoleId),      -- hard-coded 8-customer list
    (N'[utility].[MaxExposure_CountsAtYear_ByTeam]',                               N'CrossWebsite', @SuperuserRoleId),      -- hard-coded customerID, @jobId ignored for scope
    (N'[utility].[Schedule_QA_Tourny]',                                            N'CrossWebsite', @SuperuserRoleId),      -- hard-coded job compare lists
    -- JobOnly in the body, RAISED to Superuser: TSIC tooling, not a Director report.
    (N'[utility].[JobCloneQA]',                                                    N'JobOnly',      @SuperuserRoleId);
    -- [reporting].[ExportReportsHistory] (who exported what, this job) is JobOnly / Director:
    -- a client asked for exactly this report (Todd, 2026-09-10). Not listed = the default.

-- Not in the library: never seeded, and their shelf rows are deleted in Section 3.
IF OBJECT_ID('tempdb..#notInLibrary') IS NOT NULL DROP TABLE #notInLibrary;
CREATE TABLE #notInLibrary (ReportKey nvarchar(250) NOT NULL PRIMARY KEY);
INSERT INTO #notInLibrary (ReportKey) VALUES
 (N'PlayerStats_E120');   -- obsolete E120 stats entry form (Todd 2026-09-08 / 09-09)

IF OBJECT_ID('tempdb..#keyed') IS NOT NULL DROP TABLE #keyed;
SELECT x.*
INTO #keyed
FROM (
    SELECT r.JobReportId, r.JobId, r.RoleId, r.Title, r.IconName, r.Kind, r.Controller, r.[Action], r.GroupLabel, r.Active,
           CASE WHEN r.[Action] LIKE 'ExportStoredProcedureResults%'
                THEN SUBSTRING(r.[Action], CHARINDEX('spName=', r.[Action]) + 7,
                     CASE WHEN CHARINDEX('&', r.[Action], CHARINDEX('spName=', r.[Action])) > 0
                          THEN CHARINDEX('&', r.[Action], CHARINDEX('spName=', r.[Action])) - CHARINDEX('spName=', r.[Action]) - 7
                          ELSE 8000 END)
                ELSE r.[Action] END AS ReportKey
    FROM reporting.JobReports r
) x
WHERE NOT EXISTS (SELECT 1 FROM #notInLibrary t WHERE t.ReportKey = x.ReportKey);

-- One candidate library row per key, every attribute taken from the shelf rows themselves.
--   Title      : most common title for the key (ties -> alphabetical)
--   Action     : most common action string for the key, EXCEPT that a string carrying a second
--                '?' is never chosen. One legacy variant reads
--                '...&bUseDateUnscheduled=true?maxGSIMinutes=60' (8 rows): the frontend parses
--                the query string with URLSearchParams, so that row's bUseDateUnscheduled reads
--                as 'true?maxGSIMinutes=60' <> 'true' and the flag silently drops. The clean
--                6-row variant is the one that actually works.
--   Category   : the GroupLabel ruled by script 8. Every key must resolve to exactly one code
--                (guard below); a key with an invalid or ambiguous label aborts the run.
--   SortOrder  : rank by how many jobs carry the report today (most common first).
IF OBJECT_ID('tempdb..#candidate') IS NOT NULL DROP TABLE #candidate;
;WITH TitleMode AS (
    SELECT ReportKey, Title,
           ROW_NUMBER() OVER (PARTITION BY ReportKey ORDER BY COUNT(*) DESC, Title) AS rn
    FROM #keyed GROUP BY ReportKey, Title
), ActionMode AS (
    SELECT ReportKey, [Action],
           ROW_NUMBER() OVER (PARTITION BY ReportKey
                              ORDER BY CASE WHEN [Action] LIKE '%?%?%' THEN 1 ELSE 0 END, COUNT(*) DESC, [Action]) AS rn
    FROM #keyed GROUP BY ReportKey, [Action]
), IconMode AS (
    SELECT ReportKey, IconName,
           ROW_NUMBER() OVER (PARTITION BY ReportKey ORDER BY COUNT(*) DESC, IconName) AS rn
    FROM #keyed WHERE IconName IS NOT NULL GROUP BY ReportKey, IconName
), Cat AS (
    SELECT ReportKey,
           MIN(GroupLabel) AS CategoryCode,
           COUNT(DISTINCT ISNULL(GroupLabel, N'')) AS LabelCount
    FROM #keyed GROUP BY ReportKey
), Shape AS (
    SELECT ReportKey,
           MIN(Kind) AS Kind, MAX(Kind) AS KindMax,
           MIN(Controller) AS Controller, MAX(Controller) AS ControllerMax,
           COUNT(DISTINCT JobId) AS Jobs
    FROM #keyed GROUP BY ReportKey
)
SELECT s.ReportKey, t.Title, a.[Action], i.IconName, c.CategoryCode, c.LabelCount,
       s.Kind, s.KindMax, s.Controller, s.ControllerMax, s.Jobs,
       ROW_NUMBER() OVER (ORDER BY s.Jobs DESC, s.ReportKey) * 10 AS SortOrder
INTO #candidate
FROM Shape s
JOIN TitleMode  t ON t.ReportKey = s.ReportKey AND t.rn = 1
JOIN ActionMode a ON a.ReportKey = s.ReportKey AND a.rn = 1
JOIN Cat        c ON c.ReportKey = s.ReportKey
LEFT JOIN IconMode i ON i.ReportKey = s.ReportKey AND i.rn = 1;

-- GUARD 1: every key NOT YET IN THE LIBRARY must be one Kind, one Controller and one valid
-- category code -- its shelf rows are the only source for the library row about to be seeded.
-- Keys already in the library are exempt: the library row is authoritative and the shelf rows
-- only get linked, so a later editor row that files an existing report under another group on
-- one job (dev 2026-09-10 re-run test) must not abort the whole re-run.
DECLARE @bad int;
SELECT @bad = COUNT(*)
FROM #candidate c
WHERE NOT EXISTS (SELECT 1 FROM reporting.ReportLibrary l WHERE l.ReportKey = c.ReportKey)
  AND (Kind <> KindMax OR Controller <> ControllerMax OR LabelCount <> 1
       OR CategoryCode NOT IN (N'Rosters', N'Schedules', N'Registrations', N'Financials', N'Camp', N'Recruiting', N'Administration'));

IF @bad > 0
BEGIN
    SELECT '*** ABORTED -- NOTHING WAS CHANGED ***' AS Result, @bad AS KeysFailingShapeGuard;
    SELECT ReportKey, Kind, KindMax, Controller, ControllerMax, CategoryCode, LabelCount, Jobs
    FROM #candidate c
    WHERE NOT EXISTS (SELECT 1 FROM reporting.ReportLibrary l WHERE l.ReportKey = c.ReportKey)
      AND (Kind <> KindMax OR Controller <> ControllerMax OR LabelCount <> 1
           OR CategoryCode NOT IN (N'Rosters', N'Schedules', N'Registrations', N'Financials', N'Camp', N'Recruiting', N'Administration'))
    ORDER BY ReportKey;
    -- A hit here is a new anomaly: list it in #notInLibrary, or fix the shelf data by hand first.
    RETURN;
END

-- What is about to be seeded (keys not already in the library).
SELECT c.ReportKey, c.Kind, c.CategoryCode, c.Title, c.[Action], c.Jobs
FROM #candidate c
WHERE NOT EXISTS (SELECT 1 FROM reporting.ReportLibrary l WHERE l.ReportKey = c.ReportKey)
ORDER BY c.SortOrder;

-- ============================================================================
-- Section 3: seed library + applicability, backfill the shelf link -- one transaction
-- ============================================================================

DECLARE @removed int, @seeded int, @seededTypes int, @linked int, @swept int, @unlinked int;

-- What the MinRoleId sweep is about to delete: shelf rows whose role ranks below the ruled
-- minimum. Computed from the shelf as it is now (key extraction, not the link, so it is
-- right on a first run too). Expect Director rows only; SuperDirector rows for Superuser-only
-- reports were 0 on dev 2026-09-10.
SELECT ru.ReportKey, ro.Name AS ShelfRole, COUNT(*) AS Rows_, COUNT(DISTINCT r.JobId) AS Jobs,
       COUNT(DISTINCT j.CustomerId) AS Customers
FROM reporting.JobReports r
JOIN Jobs.Jobs j ON j.JobId = r.JobId
JOIN dbo.AspNetRoles ro ON ro.Id = r.RoleId
CROSS APPLY (SELECT CASE WHEN r.[Action] LIKE 'ExportStoredProcedureResults%'
                         THEN SUBSTRING(r.[Action], CHARINDEX('spName=', r.[Action]) + 7,
                              CASE WHEN CHARINDEX('&', r.[Action], CHARINDEX('spName=', r.[Action])) > 0
                                   THEN CHARINDEX('&', r.[Action], CHARINDEX('spName=', r.[Action])) - CHARINDEX('spName=', r.[Action]) - 7
                                   ELSE 8000 END)
                         ELSE r.[Action] END AS ReportKey) k
JOIN #ruling ru ON ru.ReportKey = k.ReportKey
WHERE (CASE r.RoleId   WHEN @DirectorRoleId THEN 1 WHEN @SuperDirectorRoleId THEN 2 WHEN @SuperuserRoleId THEN 3 END)
    < (CASE ru.MinRoleId WHEN @DirectorRoleId THEN 1 WHEN @SuperDirectorRoleId THEN 2 WHEN @SuperuserRoleId THEN 3 ELSE 4 END)
GROUP BY ru.ReportKey, ro.Name ORDER BY Rows_ DESC, ru.ReportKey;

-- What is about to be deleted from the shelves (reports that are not in the library).
SELECT k.ReportKey, r.GroupLabel, COUNT(*) AS Rows_, COUNT(DISTINCT r.JobId) AS Jobs
FROM reporting.JobReports r
CROSS APPLY (SELECT CASE WHEN r.[Action] LIKE 'ExportStoredProcedureResults%'
                         THEN SUBSTRING(r.[Action], CHARINDEX('spName=', r.[Action]) + 7,
                              CASE WHEN CHARINDEX('&', r.[Action], CHARINDEX('spName=', r.[Action])) > 0
                                   THEN CHARINDEX('&', r.[Action], CHARINDEX('spName=', r.[Action])) - CHARINDEX('spName=', r.[Action]) - 7
                                   ELSE 8000 END)
                         ELSE r.[Action] END AS ReportKey) k
JOIN #notInLibrary t ON t.ReportKey = k.ReportKey
GROUP BY k.ReportKey, r.GroupLabel ORDER BY k.ReportKey, r.GroupLabel;

BEGIN TRY
    BEGIN TRAN;

    -- A shelf holds only library reports: rows for anything not in the library go. Keyed the
    -- same way as #keyed (those rows were excluded from #keyed, so match the table directly).
    DELETE r
    FROM reporting.JobReports r
    CROSS APPLY (SELECT CASE WHEN r.[Action] LIKE 'ExportStoredProcedureResults%'
                             THEN SUBSTRING(r.[Action], CHARINDEX('spName=', r.[Action]) + 7,
                                  CASE WHEN CHARINDEX('&', r.[Action], CHARINDEX('spName=', r.[Action])) > 0
                                       THEN CHARINDEX('&', r.[Action], CHARINDEX('spName=', r.[Action])) - CHARINDEX('spName=', r.[Action]) - 7
                                       ELSE 8000 END)
                             ELSE r.[Action] END AS ReportKey) k
    JOIN #notInLibrary t ON t.ReportKey = k.ReportKey;
    SET @removed = @@ROWCOUNT;

    -- New rows land JobOnly / Director; the ruling below corrects the exceptions.
    INSERT INTO reporting.ReportLibrary
        (ReportKey, Title, CategoryCode, IconName, Kind, Controller, [Action], SortOrder, Scope, MinRoleId)
    SELECT c.ReportKey, c.Title, c.CategoryCode, c.IconName, c.Kind, c.Controller, c.[Action], c.SortOrder,
           N'JobOnly', @DirectorRoleId
    FROM #candidate c
    WHERE NOT EXISTS (SELECT 1 FROM reporting.ReportLibrary l WHERE l.ReportKey = c.ReportKey);
    SET @seeded = @@ROWCOUNT;

    -- Scope / MinRoleId per #ruling; everything else JobOnly / Director. Un-stamped rows only
    -- (LebUserId IS NULL = never touched by the Superuser editor), and only where a value
    -- differs, so a second run is a no-op.
    UPDATE l
    SET    l.Scope     = COALESCE(ru.Scope, N'JobOnly'),
           l.MinRoleId = COALESCE(ru.MinRoleId, @DirectorRoleId),
           l.Modified  = GETDATE()
    FROM   reporting.ReportLibrary l
    LEFT JOIN #ruling ru ON ru.ReportKey = l.ReportKey
    WHERE  l.LebUserId IS NULL
      AND (l.Scope <> COALESCE(ru.Scope, N'JobOnly')
           OR l.MinRoleId IS NULL   -- freshly upgraded, or un-stamped: the seed never leaves a NULL
           OR l.MinRoleId <> COALESCE(ru.MinRoleId, @DirectorRoleId));

    -- Applicability = the job types the report is OBSERVED on today. Widening to a family
    -- (e.g. every schedule report -> Tournament + League) is a ruling, added as explicit rows.
    INSERT INTO reporting.ReportLibraryJobTypes (ReportLibraryId, JobTypeId)
    SELECT DISTINCT l.ReportLibraryId, j.JobTypeId
    FROM #keyed k
    JOIN reporting.ReportLibrary l ON l.ReportKey = k.ReportKey
    JOIN Jobs.Jobs j ON j.JobId = k.JobId
    WHERE NOT EXISTS (SELECT 1 FROM reporting.ReportLibraryJobTypes t
                      WHERE t.ReportLibraryId = l.ReportLibraryId AND t.JobTypeId = j.JobTypeId);
    SET @seededTypes = @@ROWCOUNT;

    -- Backfill: every shelf row points at its library entry. Only NULL links are touched, so
    -- a re-run links only rows added since and leaves everything else alone.
    UPDATE r
    SET    r.ReportLibraryId = l.ReportLibraryId
    FROM   reporting.JobReports r
    JOIN   #keyed k ON k.JobReportId = r.JobReportId
    JOIN   reporting.ReportLibrary l ON l.ReportKey = k.ReportKey
    WHERE  r.ReportLibraryId IS NULL;
    SET @linked = @@ROWCOUNT;

    -- THE SWEEP: a shelf row whose role ranks below its library entry's MinRoleId goes. After
    -- the backfill so it runs off the link. Roles without a rank (ApiAuthorized) compare NULL
    -- and are never touched. Same rule the Superuser editor applies when it raises MinRoleId.
    DELETE r
    FROM   reporting.JobReports r
    JOIN   reporting.ReportLibrary l ON l.ReportLibraryId = r.ReportLibraryId
    WHERE (CASE r.RoleId   WHEN @DirectorRoleId THEN 1 WHEN @SuperDirectorRoleId THEN 2 WHEN @SuperuserRoleId THEN 3 END)
        < (CASE l.MinRoleId WHEN @DirectorRoleId THEN 1 WHEN @SuperDirectorRoleId THEN 2 WHEN @SuperuserRoleId THEN 3 ELSE 4 END);  -- NULL/unknown = retired = 4
    SET @swept = @@ROWCOUNT;

    -- GUARD 2: no shelf row may be left unlinked. Any survivor means a key the seed did not
    -- produce -- roll everything back rather than ship a half-linked shelf.
    SELECT @unlinked = COUNT(*) FROM reporting.JobReports WHERE ReportLibraryId IS NULL;
    IF @unlinked > 0
    BEGIN
        ROLLBACK;
        SELECT '*** ABORTED -- ROLLED BACK, NOTHING CHANGED ***' AS Result, @unlinked AS UnlinkedShelfRows;
        SELECT k.ReportKey, COUNT(*) AS Rows_ FROM #keyed k
        WHERE NOT EXISTS (SELECT 1 FROM reporting.ReportLibrary l WHERE l.ReportKey = k.ReportKey)
        GROUP BY k.ReportKey ORDER BY Rows_ DESC;
        RETURN;
    END

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    SELECT '*** FAILED -- ROLLED BACK, NOTHING CHANGED ***' AS Result,
           ERROR_NUMBER() AS ErrNo, ERROR_MESSAGE() AS ErrMsg;
    THROW;
END CATCH

SELECT 'COMMITTED' AS Result, @removed AS ShelfRowsRemoved, @seeded AS LibraryRowsSeeded, @seededTypes AS JobTypeRowsSeeded,
       @linked AS ShelfRowsLinked, @swept AS ShelfRowsSwept;

-- ============================================================================
-- Section 4: verify
-- ============================================================================

-- Scope x MinRole (expect dev 2026-09-10: JobOnly/Director 59, CrossJob/SuperDirector 10,
-- CrossWebsite/Superuser 4, JobOnly/Superuser 1).
SELECT l.Scope, COALESCE(ro.Name, N'(retired)') AS MinRole, COUNT(*) AS Reports_
FROM reporting.ReportLibrary l LEFT JOIN dbo.AspNetRoles ro ON ro.Id = l.MinRoleId
GROUP BY l.Scope, COALESCE(ro.Name, N'(retired)') ORDER BY Reports_ DESC;

-- Floor invariant: MinRoleId may never rank below what Scope implies (expect no rows).
SELECT l.ReportKey, l.Scope, COALESCE(ro.Name, N'(retired)') AS MinRole, 'MinRole below Scope floor' AS Problem
FROM reporting.ReportLibrary l LEFT JOIN dbo.AspNetRoles ro ON ro.Id = l.MinRoleId
WHERE (CASE l.MinRoleId WHEN @DirectorRoleId THEN 1 WHEN @SuperDirectorRoleId THEN 2 WHEN @SuperuserRoleId THEN 3 ELSE 4 END)  -- NULL/unknown = retired = 4
    < (CASE l.Scope WHEN N'JobOnly' THEN 1 WHEN N'CrossJob' THEN 2 WHEN N'CrossWebsite' THEN 3 END);

-- Sweep invariant: no shelf row ranks below its library entry's MinRoleId (expect no rows).
SELECT l.ReportKey, ro.Name AS ShelfRole, COUNT(*) AS Rows_
FROM reporting.JobReports r
JOIN reporting.ReportLibrary l ON l.ReportLibraryId = r.ReportLibraryId
JOIN dbo.AspNetRoles ro ON ro.Id = r.RoleId
WHERE (CASE r.RoleId   WHEN @DirectorRoleId THEN 1 WHEN @SuperDirectorRoleId THEN 2 WHEN @SuperuserRoleId THEN 3 END)
    < (CASE l.MinRoleId WHEN @DirectorRoleId THEN 1 WHEN @SuperDirectorRoleId THEN 2 WHEN @SuperuserRoleId THEN 3 ELSE 4 END)  -- NULL/unknown = retired = 4
GROUP BY l.ReportKey, ro.Name;

SELECT CategoryCode, COUNT(*) AS Reports_ FROM reporting.ReportLibrary GROUP BY CategoryCode ORDER BY Reports_ DESC;

-- Not-in-library reports must be absent from both tables (expect no rows).
SELECT t.ReportKey, 'still in library' AS Where_ FROM #notInLibrary t
WHERE EXISTS (SELECT 1 FROM reporting.ReportLibrary l WHERE l.ReportKey = t.ReportKey)
UNION ALL
SELECT t.ReportKey, 'still on shelf' FROM #notInLibrary t
WHERE EXISTS (SELECT 1 FROM reporting.JobReports r WHERE r.[Action] = t.ReportKey OR r.[Action] LIKE '%spName=' + t.ReportKey + '%');

-- Library rows with no active shelf row anywhere. EXPECTED after the sweep: reports that only
-- Directors held (prod 2026-09-10: AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam,
-- Club_AllJobs_Rosters_NoMedical, Get_Players_ForExcelExport_AllCustomers). They stay in the
-- library, addable by SuperDirector and above.
SELECT l.ReportKey
FROM   reporting.ReportLibrary l
WHERE  NOT EXISTS (SELECT 1 FROM reporting.JobReports r WHERE r.ReportLibraryId = l.ReportLibraryId AND r.Active = 1)
ORDER BY l.ReportKey;

-- The library as seeded: key, kind, category, title, canonical action, job types.
SELECT l.SortOrder, l.ReportKey, l.Kind, l.CategoryCode, l.Scope, COALESCE(ro.Name, N'(retired)') AS MinRole, l.Title, l.[Action],
       STUFF((SELECT ', ' + jt.JobTypeName
              FROM reporting.ReportLibraryJobTypes t JOIN reference.JobTypes jt ON jt.JobTypeId = t.JobTypeId
              WHERE t.ReportLibraryId = l.ReportLibraryId
              ORDER BY jt.JobTypeName FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') AS JobTypes
FROM   reporting.ReportLibrary l
LEFT JOIN dbo.AspNetRoles ro ON ro.Id = l.MinRoleId
ORDER BY l.SortOrder;
