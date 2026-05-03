-- ============================================================================
-- 7-install-reporting-catalog.sql
--
-- Single-file install for the Type 2 report catalog (`reporting.ReportCatalogue`).
-- Idempotent end-to-end: schema, table, columns, all 45 seed rows, and the
-- per-row Description + CategoryCode backfill. Safe to re-run.
--
-- Structure:
--   Section 1:  Schema + table (DDL) + idempotent column adds/widens
--   Section 2:  SP availability check (45 SPs)
--   Section 3a: Seed (32 job-scoped + 13 cross-customer SU-only)
--   Section 3b: Backfill rich Description + CategoryCode for every seed row
--               (descriptions derived from live sproc bodies on TSICV5)
--   Section 4:  Final state (per-row SP status)
--
-- Apply with:
--   sqlcmd -S .\SS2016 -d <db> -I -i "scripts/7-install-reporting-catalog.sql"
--
-- After the table is first created, re-scaffold EF entities so the backend
-- sees the new table:
--   .\scripts\3) RE-Scaffold-Db-Entities.ps1
--
-- Type 1 reports (Crystal Reports, hard-coded) are NOT in this table — they
-- live in src/app/core/reporting/type1-report-catalog.ts on the frontend.
--
-- Rollback: seed rows are identifiable by LebUserId IS NULL.
--   -- Drop only the SU cross-customer rows
--   DELETE FROM reporting.ReportCatalogue
--   WHERE LebUserId IS NULL AND SortOrder >= 1000;
--
--   -- Drop everything seeded (preserves user-added rows)
--   DELETE FROM reporting.ReportCatalogue WHERE LebUserId IS NULL;
--
--   -- Confirm no user-added rows before dropping the table:
--   SELECT * FROM reporting.ReportCatalogue WHERE LebUserId IS NOT NULL;
-- ============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ============================================================================
-- Section 1: Schema + table (DDL, idempotent)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'reporting')
    EXEC('CREATE SCHEMA reporting');
GO

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

-- ----------------------------------------------------------------------------
-- Idempotent column adds / widens (for installs created before these landed)
-- ----------------------------------------------------------------------------

-- CategoryCode: presentation-layer grouping for the reports library UI.
-- Allowed values are enforced in code (see frontend ReportCategory union).
IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('reporting.ReportCatalogue')
      AND name = 'CategoryCode'
)
BEGIN
    ALTER TABLE reporting.ReportCatalogue
        ADD CategoryCode NVARCHAR(50) NULL;
    PRINT 'Added reporting.ReportCatalogue.CategoryCode';
END
ELSE
    PRINT 'reporting.ReportCatalogue.CategoryCode already exists - skipped';
GO

-- Description widened to NVARCHAR(MAX) so summaries can carry enough context
-- to support an "inactivate this report?" decision (~30-50 words).
IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('reporting.ReportCatalogue')
      AND name = 'Description'
      AND max_length <> -1   -- -1 = MAX; legacy column was NVARCHAR(1000) = 2000 bytes
)
BEGIN
    ALTER TABLE reporting.ReportCatalogue
        ALTER COLUMN Description NVARCHAR(MAX) NULL;
    PRINT 'Widened reporting.ReportCatalogue.Description to NVARCHAR(MAX)';
END
ELSE
    PRINT 'reporting.ReportCatalogue.Description already NVARCHAR(MAX) - skipped';
GO

-- ============================================================================
-- Section 2: Verify referenced stored procedures exist
-- 45 total: 32 job-scoped + 13 cross-customer SU-only.
-- ============================================================================

SET NOCOUNT ON;

DECLARE @SpCheck TABLE (SpName NVARCHAR(200) NOT NULL PRIMARY KEY);

INSERT INTO @SpCheck VALUES
    -- Job-scoped (32 — bUseJobId=true, no role gate)
    ('reporting.Customer_RegistrationAccounting_Records'),
    ('reporting.Schedule_Export_ToTournyMachine'),
    ('reporting.Schedule_Export_Teams_ToTournyMachine'),
    ('reporting.JobClubRepContacts'),
    ('reporting.AmericanSelectEvaluationSheet'),
    ('reporting.ThePlayers_PlayerShowCaseExport_IncludeInactive'),
    ('reporting.ThePlayers_PlayerShowCaseExportII'),
    ('reporting.CathyCampBreakoutPerJob_PreviousMonth'),
    ('utility.MaxExposure_CountsAtYear_ByTeam'),
    ('reporting.PlayerParentMailingData'),
    ('utility.MaxExposure_ClubCoaches'),
    ('reporting.CathyCampCheckinWithVacAndMedform'),
    ('reporting.StepsCoaches_Export'),
    ('reporting.JobPushTeamResultsSummary'),
    ('adn.monthlycustomerrollups'),
    ('reporting.RegsaverRegistrants_Charlie'),
    ('reporting.CustomerEndUsersDump'),
    ('reporting.ExportReportsHistory'),
    ('reporting.ThePlayers_PlayerShowCaseExport'),
    ('reporting.GetJobTransactionRollup'),
    ('reporting.AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam'),
    ('reporting.StaffExport'),
    ('utility.JobCloneQA'),
    ('reporting.CathyCampBreakoutPerJob_AmtPaid'),
    ('utility.sibling_report'),
    ('reporting.TournamentExportClubrepsAndCoachesEmail'),
    ('utility.AmericanSelect_CountsAtYear_ByTeam'),
    ('utility.Schedule_QA_Tourny'),
    ('reporting.TournyYearOverYearClubTeams'),
    ('reporting.TLCContactsHx'),
    ('utility.AmericanSelect_CountsAtYear_ByTeamII'),
    ('reporting.RefAssignmentQA'),
    -- Cross-customer SuperUser-only (13 — bUseJobId=false, requiresRoles=Superuser)
    ('reporting.ClubRepContacts-All'),
    ('reporting.JobKeyAttributes-ALL'),
    ('reporting.TournamentKeyAttributes-ALL'),
    ('reporting.RegsaverRegistrants_ALL'),
    ('reporting.RegsaverPurchases_ALL_Rawdata'),
    ('utility.GetSuspiciousArbs'),
    ('utility.ExpiringBulletins'),
    ('utility.PlayerRegistrationBulletinsQA'),
    ('utility.TeamRegistrationBulletinsQA'),
    ('reporting.NewTsicJobsWithTxs'),
    ('reporting.JobAdminFeesAll'),
    ('adn.GetLastMonthsGrandTotals'),
    ('adn.ReconcileNuvei');

PRINT '=== Stored Procedure availability ===';
SELECT
    SpName,
    CASE WHEN OBJECT_ID(SpName, 'P') IS NULL THEN 'MISSING' ELSE 'OK' END AS Status
FROM @SpCheck
ORDER BY Status DESC, SpName;

IF EXISTS (SELECT 1 FROM @SpCheck WHERE OBJECT_ID(SpName, 'P') IS NULL)
BEGIN
    PRINT '';
    PRINT 'WARNING: one or more SPs are missing. Seed will still insert those catalog rows,';
    PRINT 'but the matching reports will fail at execution until the SPs exist or the';
    PRINT 'catalog rows are updated / removed.';
    PRINT '';
END

-- ============================================================================
-- Section 3a: Seed rows (idempotent on Title)
-- 32 job-scoped (sortOrder 10-320, NULL Params/Rules)
-- 13 cross-customer SU-only (sortOrder 1000-1130, bUseJobId=false + requiresRoles)
-- ============================================================================

DECLARE @ParamsCrossCustomer NVARCHAR(200) = N'{"bUseJobId":false}';
DECLARE @RulesSuperUser      NVARCHAR(200) = N'{"requiresRoles":["Superuser"]}';

DECLARE @Seed TABLE (
    Title           NVARCHAR(200)  NOT NULL PRIMARY KEY,
    Description     NVARCHAR(1000) NULL,
    IconName        NVARCHAR(50)   NULL,
    StoredProcName  NVARCHAR(200)  NOT NULL,
    ParametersJson  NVARCHAR(MAX)  NULL,
    VisibilityRules NVARCHAR(MAX)  NULL,
    SortOrder       INT            NOT NULL
);

-- ---- Job-scoped reports (32) ----
-- Discovered from 2025-2027 legacy menu system, ordered by JobCount desc.
-- Per-row VisibilityRules and ParametersJson seeded as NULL — populate via the
-- SuperUser catalog editor or follow-up UPDATEs as needed.
INSERT INTO @Seed (Title, Description, IconName, StoredProcName, ParametersJson, VisibilityRules, SortOrder) VALUES
    ('All Customer Jobs Accounting Records',                                'Cross-customer accounting rollup',                         'cash-stack',        'reporting.Customer_RegistrationAccounting_Records',                     NULL, NULL,  10),
    ('Export Schedule (TournyMachine)',                                     'Schedule export in TournyMachine format',                  'calendar-week',     'reporting.Schedule_Export_ToTournyMachine',                              NULL, NULL,  20),
    ('Export Teams (TournyMachine)',                                        'Scheduled teams export in TournyMachine format',           'box-arrow-up',      'reporting.Schedule_Export_Teams_ToTournyMachine',                        NULL, NULL,  30),
    ('Club Reps',                                                           'Club rep contacts for the job',                            'people',            'reporting.JobClubRepContacts',                                           NULL, NULL,  40),
    ('Evaluation Report',                                                   'American Select player evaluation sheet',                  'clipboard-check',   'reporting.AmericanSelectEvaluationSheet',                                NULL, NULL,  50),
    ('Active and Inactive Player Data',                                     'Player showcase export including inactive players',        'person-lines-fill', 'reporting.ThePlayers_PlayerShowCaseExport_IncludeInactive',              NULL, NULL,  60),
    ('Rostered Players Data',                                               'Rostered players showcase export',                         'person-check',      'reporting.ThePlayers_PlayerShowCaseExportII',                            NULL, NULL,  70),
    ('Custom Accounting Report',                                            'Cathy camp breakout for previous month',                   'calculator',        'reporting.CathyCampBreakoutPerJob_PreviousMonth',                        NULL, NULL,  80),
    ('Counts Year over Year',                                               'Team counts year over year',                               'bar-chart',         'utility.MaxExposure_CountsAtYear_ByTeam',                                NULL, NULL,  90),
    ('Parents Mailing Data',                                                'Mailing list for player parents',                          'envelope',          'reporting.PlayerParentMailingData',                                      NULL, NULL, 100),
    ('Player Club Coaches',                                                 'Club coaches associated with players',                     'person-badge',      'utility.MaxExposure_ClubCoaches',                                        NULL, NULL, 110),
    ('Check-In with VacCard and MedForm Status',                            'Check-in roster including vaccination + medical status',   'clipboard-data',    'reporting.CathyCampCheckinWithVacAndMedform',                            NULL, NULL, 120),
    ('Club Coaches Details',                                                'Detailed export of STEPS club coaches',                    'person-vcard',      'reporting.StepsCoaches_Export',                                          NULL, NULL, 130),
    ('Pushes Per Event',                                                    'Team push notification summary per event',                 'bell',              'reporting.JobPushTeamResultsSummary',                                    NULL, NULL, 140),
    ('Last Month Cross-Customer Revenue',                                   'ADN monthly customer revenue rollup',                      'cash',              'adn.monthlycustomerrollups',                                             NULL, NULL, 150),
    ('RegSaver Registrants',                                                'Registrants discovered via RegSaver flow',                 'person-plus',       'reporting.RegsaverRegistrants_Charlie',                                  NULL, NULL, 160),
    ('Customer Contact Export',                                             'Customer end-user contact export',                         'person-rolodex',    'reporting.CustomerEndUsersDump',                                         NULL, NULL, 170),
    ('Export History',                                                      'Audit log of prior report exports',                        'clock-history',     'reporting.ExportReportsHistory',                                         NULL, NULL, 180),
    ('Rostered Players Data (Legacy)',                                      'Prior version of rostered players showcase export',        'archive',           'reporting.ThePlayers_PlayerShowCaseExport',                              NULL, NULL, 190),
    ('Transaction Rollup',                                                  'Job-level transaction rollup',                             'receipt',           'reporting.GetJobTransactionRollup',                                      NULL, NULL, 200),
    ('Customer Registrations by Job / Agegroup / Division / Team',          'American Select registration detail grid',                 'grid-3x3',          'reporting.AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam',  NULL, NULL, 210),
    ('Staff Export',                                                        'Staff list export',                                        'people-fill',       'reporting.StaffExport',                                                  NULL, NULL, 220),
    ('Job QA',                                                              'Job clone QA diagnostic export',                           'check2-square',     'utility.JobCloneQA',                                                     NULL, NULL, 230),
    ('Players Amount Paid By Camp',                                         'Cathy camp payment breakout by player',                    'currency-dollar',   'reporting.CathyCampBreakoutPerJob_AmtPaid',                              NULL, NULL, 240),
    ('Sibling Report',                                                      'Sibling relationship report',                              'diagram-2',         'utility.sibling_report',                                                 NULL, NULL, 250),
    ('Team Contact Emails',                                                 'Clubrep + coach email export for tournament teams',        'envelope-at',       'reporting.TournamentExportClubrepsAndCoachesEmail',                      NULL, NULL, 260),
    ('Team Counts - All Regions YTD',                                       'Team counts across all regions, year to date',             'bar-chart-steps',   'utility.AmericanSelect_CountsAtYear_ByTeam',                             NULL, NULL, 270),
    ('Schedule QA Results',                                                 'Tournament schedule QA diagnostics',                       'list-check',        'utility.Schedule_QA_Tourny',                                             NULL, NULL, 280),
    ('Historical Tourney Participation by Club',                            'Multi-year tournament team counts by club',                'graph-up',          'reporting.TournyYearOverYearClubTeams',                                  NULL, NULL, 290),
    ('Player Contact History',                                              'TLC contact history for a player',                         'journal-text',      'reporting.TLCContactsHx',                                                NULL, NULL, 300),
    ('Team Counts - Summary Rollup',                                        'Summary-level year-over-year team counts',                 'pie-chart',         'utility.AmericanSelect_CountsAtYear_ByTeamII',                           NULL, NULL, 310),
    ('Referee Assignment QA',                                               'Referee assignment QA diagnostic',                         'clipboard-check',   'reporting.RefAssignmentQA',                                              NULL, NULL, 320);

-- ---- Cross-customer SuperUser-only reports (13) ----
-- bUseJobId=false (no @jobId param to the SP) and requiresRoles=Superuser
-- (library hides from non-SU; ReportingController.export-sp also hard-rejects
-- non-SU when bUseJobId=false, so URL-fetch is also blocked).
INSERT INTO @Seed (Title, Description, IconName, StoredProcName, ParametersJson, VisibilityRules, SortOrder) VALUES
    ('ClubRep Contacts (All Customers)',          'Club rep contact dump across all customers',                         'people',              'reporting.ClubRepContacts-All',                  @ParamsCrossCustomer, @RulesSuperUser, 1000),
    ('Job Key Attributes (All Customers)',        'Per-job key attributes across all customers',                        'list-columns',        'reporting.JobKeyAttributes-ALL',                 @ParamsCrossCustomer, @RulesSuperUser, 1010),
    ('Tournament Keys (All Customers)',           'Per-tournament key attributes across all customers',                 'trophy',              'reporting.TournamentKeyAttributes-ALL',          @ParamsCrossCustomer, @RulesSuperUser, 1020),
    ('RegSaver Registrants (All)',                'Cross-customer registrants discovered via RegSaver flow',            'person-plus',         'reporting.RegsaverRegistrants_ALL',              @ParamsCrossCustomer, @RulesSuperUser, 1030),
    ('RegSaver Purchases - Raw',                  'Raw RegSaver purchases data dump across all customers',              'currency-dollar',     'reporting.RegsaverPurchases_ALL_Rawdata',        @ParamsCrossCustomer, @RulesSuperUser, 1040),
    ('Suspicious ARBs',                           'List of ARB subscriptions that look suspicious',                     'exclamation-triangle','utility.GetSuspiciousArbs',                       @ParamsCrossCustomer, @RulesSuperUser, 1050),
    ('Expiring Bulletins (3 months)',             'Job bulletins whose expiry date is within 3 months',                 'clock-history',       'utility.ExpiringBulletins',                       @ParamsCrossCustomer, @RulesSuperUser, 1060),
    ('Expired Player Registration Bulletins',     'Active sites whose player-reg bulletins have already expired',       'person-x',            'utility.PlayerRegistrationBulletinsQA',           @ParamsCrossCustomer, @RulesSuperUser, 1070),
    ('Expired Team Registration Bulletins',       'Active sites whose team-reg bulletins have already expired',         'shield-x',            'utility.TeamRegistrationBulletinsQA',             @ParamsCrossCustomer, @RulesSuperUser, 1080),
    ('New Jobs Last Month (with txs)',            'Jobs created in the prior month that already have transactions',     'plus-square',         'reporting.NewTsicJobsWithTxs',                    @ParamsCrossCustomer, @RulesSuperUser, 1100),
    ('Job Admin Fees Summary',                    'Cross-customer summary of admin fees collected',                     'cash-coin',           'reporting.JobAdminFeesAll',                       @ParamsCrossCustomer, @RulesSuperUser, 1110),
    ('Last Month Grand Totals',                   'ADN grand totals for the prior month',                               'calculator',          'adn.GetLastMonthsGrandTotals',                    @ParamsCrossCustomer, @RulesSuperUser, 1120),
    ('ADN-Nuvei Reconcile',                       'Side-by-side reconciliation of ADN vs Nuvei prior month batches',    'arrow-left-right',    'adn.ReconcileNuvei',                              @ParamsCrossCustomer, @RulesSuperUser, 1130);

DECLARE @InsertedCount INT;

INSERT INTO reporting.ReportCatalogue (Title, Description, IconName, StoredProcName, ParametersJson, VisibilityRules, SortOrder, Active, Modified)
SELECT
    s.Title,
    s.Description,
    s.IconName,
    s.StoredProcName,
    s.ParametersJson,
    s.VisibilityRules,
    s.SortOrder,
    1,
    GETDATE()
FROM @Seed s
WHERE NOT EXISTS (
    SELECT 1 FROM reporting.ReportCatalogue c WHERE c.Title = s.Title
);

SET @InsertedCount = @@ROWCOUNT;

DECLARE @TotalCount INT = (SELECT COUNT(*) FROM reporting.ReportCatalogue);

PRINT '';
PRINT '=== Seed Result ===';
PRINT CONCAT('Inserted: ', @InsertedCount, ' row(s) (skip-existing on Title)');
PRINT CONCAT('Total rows in catalog: ', @TotalCount);

-- ============================================================================
-- Section 3b: Backfill rich Description + CategoryCode for all 45 seed rows
--
-- Descriptions derived from live sproc bodies on TSICV5 (sys.sql_modules.definition).
-- Re-runnable: pure UPDATE-by-Title. User-added rows (LebUserId IS NOT NULL) are
-- not affected unless their Title happens to match a seeded one.
--
-- Approved CategoryCode set (kept in sync with the frontend ReportCategory union):
--   Rosters         | Schedules       | Financials      | Registrations
--   Camp            | Recruiting      | Administration
-- ============================================================================

-- Title: All Customer Jobs Accounting Records
-- Sproc: reporting.Customer_RegistrationAccounting_Records
UPDATE reporting.ReportCatalogue
SET Description = N'Two-year window of registration_accounting rows. When @jobId is the magic STEPS value, returns rollup across 9 hardcoded customer IDs (STEPS family). Otherwise scopes to the @jobId customer, customer-wide across all jobs. Returns transaction date parts, paymentMethod, person, payAmt, club, agegroup, team.',
    CategoryCode = N'Financials'
WHERE Title = N'All Customer Jobs Accounting Records';

-- Title: Export Schedule (TournyMachine)
-- Sproc: reporting.Schedule_Export_ToTournyMachine
UPDATE reporting.ReportCatalogue
SET Description = N'TournyMachine schedule import format. Side effect: invokes Leagues.UpdateTournyTeamFullNames first to refresh team display names. Returns Team1, Team2, Date/Time, Field, Division, Pool, gDay, gTime for all T-vs-T games in @jobId.',
    CategoryCode = N'Schedules'
WHERE Title = N'Export Schedule (TournyMachine)';

-- Title: Export Teams (TournyMachine)
-- Sproc: reporting.Schedule_Export_Teams_ToTournyMachine
UPDATE reporting.ReportCatalogue
SET Description = N'Teams export for TournyMachine. Side effect: refreshes team full names first. Two result sets: (1) Division/Pool/Team/Gender for TournyMachine; (2) USLax export with clubrep contact (name, email, city, state, zip). T-vs-T games only, excludes Dropped Teams.',
    CategoryCode = N'Schedules'
WHERE Title = N'Export Teams (TournyMachine)';

-- Title: Club Reps
-- Sproc: reporting.JobClubRepContacts
UPDATE reporting.ReportCatalogue
SET Description = N'Active Club Rep contact list for @jobId. Returns Year, JobName, Club, LastName, FirstName, Email, Cell, Address, City, State, Zip - sorted by year desc, job, last name.',
    CategoryCode = N'Administration'
WHERE Title = N'Club Reps';

-- Title: Evaluation Report
-- Sproc: reporting.AmericanSelectEvaluationSheet
UPDATE reporting.ReportCatalogue
SET Description = N'Blank player evaluation sheet for American Select. Filters role=Player, bActive, agegroup=Registration. Returns grad_year, uniform#, Player name, Position, plus blank Physical/PositionSpecific/StickSkills/Total columns for evaluators to score.',
    CategoryCode = N'Recruiting'
WHERE Title = N'Evaluation Report';

-- Title: Active and Inactive Player Data
-- Sproc: reporting.ThePlayers_PlayerShowCaseExport_IncludeInactive
UPDATE reporting.ReportCatalogue
SET Description = N'Slim player list for @jobId INCLUDING inactive players (no bActive filter, no role filter). Returns RegistrationID, RegDate, Agegroup, Team, Name, RoleName, uniform#, bActive, shorts, t-shirt, ClubTeam.',
    CategoryCode = N'Rosters'
WHERE Title = N'Active and Inactive Player Data';

-- Title: Rostered Players Data
-- Sproc: reporting.ThePlayers_PlayerShowCaseExportII
UPDATE reporting.ReportCatalogue
SET Description = N'Rich player showcase export. Active players only, role=Player, no waitlists. Includes player contact, family address, DOB, Mom/Dad name+cell+email, sizes, height/weight, strong hand, club coach, school coach, Instagram, Twitter, USLaxNo, school. Showcase recruitment use.',
    CategoryCode = N'Recruiting'
WHERE Title = N'Rostered Players Data';

-- Title: Custom Accounting Report
-- Sproc: reporting.CathyCampBreakoutPerJob_PreviousMonth
UPDATE reporting.ReportCatalogue
SET Description = N'Per-team breakout for previous month: #Registrants, #CCTransactions, SumCCTransactions, CCProcessingFee (3% hardcoded), TSICFees ($5/registrant hardcoded), BalanceDueClient. Plus Monthly Total row. Camp-specific (Cathy = MD Lax Camps, U of Maryland head coach).',
    CategoryCode = N'Camp'
WHERE Title = N'Custom Accounting Report';

-- Title: Counts Year over Year
-- Sproc: utility.MaxExposure_CountsAtYear_ByTeam
UPDATE reporting.ReportCatalogue
SET Description = N'YTD team registration counts 2010-2026. HARDCODED to MaxExposure customer (502f0c58-c643-43da-9bfe-00e140f1a19b) regardless of @jobId. Years hardcoded as a list. Contains commented-out cross-DB block to retired TSIC2010_Latest. OBSOLETE CANDIDATE if MaxExposure customer is no longer active.',
    CategoryCode = N'Registrations'
WHERE Title = N'Counts Year over Year';

-- Title: Parents Mailing Data
-- Sproc: reporting.PlayerParentMailingData
UPDATE reporting.ReportCatalogue
SET Description = N'Mailing list for active players in @jobId. Returns Mom_LastName, Mom_FirstName, Dad_LastName, Dad_FirstName, family street address, city, state, zip. Sorted by Mom last name.',
    CategoryCode = N'Registrations'
WHERE Title = N'Parents Mailing Data';

-- Title: Player Club Coaches
-- Sproc: utility.MaxExposure_ClubCoaches
UPDATE reporting.ReportCatalogue
SET Description = N'Player to club-coach details for @jobId. Despite the MaxExposure prefix, NOT customer-locked - uses whatever @jobId is passed. Returns player Last/First/DOB/school/grad_year/cell/email plus club_coach name+email plus Mom/Dad contacts. Role=Player only.',
    CategoryCode = N'Rosters'
WHERE Title = N'Player Club Coaches';

-- Title: Check-In with VacCard and MedForm Status
-- Sproc: reporting.CathyCampCheckinWithVacAndMedform
UPDATE reporting.ReportCatalogue
SET Description = N'Camp check-in roster scoped to @jobId. Active players, no waitlists. Returns league, team, Last/First, position, bUploadedVaccineCard, bUploadedMedForm, owed_total, Mom/Dad name+cell. Cathy/MD Lax Camps workflow.',
    CategoryCode = N'Camp'
WHERE Title = N'Check-In with VacCard and MedForm Status';

-- Title: Club Coaches Details
-- Sproc: reporting.StepsCoaches_Export
UPDATE reporting.ReportCatalogue
SET Description = N'STEPS coach detail export. Filters role=Unassigned Adult (the STEPS coach role). Returns bActive, waiver flag, contact, address, jersey/shorts/sweatshirt/sweatpants sizes, CoachingRequests free-text, plus a count of Staff registrations the same user has in the job.',
    CategoryCode = N'Rosters'
WHERE Title = N'Club Coaches Details';

-- Title: Pushes Per Event
-- Sproc: reporting.JobPushTeamResultsSummary
UPDATE reporting.ReportCatalogue
SET Description = N'Mobile-app team-favorites tracking for @jobId. Two result sets: (1) total Device_Teams favorites count for the event; (2) per-team breakdown - Club, Agegroup, Team, Count.',
    CategoryCode = N'Administration'
WHERE Title = N'Pushes Per Event';

-- Title: Last Month Cross-Customer Revenue
-- Sproc: adn.monthlycustomerrollups
UPDATE reporting.ReportCatalogue
SET Description = N'Prior-month CC revenue rollup. Customer-grouping logic: STEPS (9 customers), YJ Tracy (5), TOTB (3) - when @jobId belongs to one, sweeps in the rest of the group. Per-job, per-customer, per-customer-group summary. Uses j.ProcessingFeePercent (default 3.5%). TSIC fees from perPlayerCharge x Count_NewPlayers + perTeamCharge x Count_NewTeams.',
    CategoryCode = N'Financials'
WHERE Title = N'Last Month Cross-Customer Revenue';

-- Title: RegSaver Registrants
-- Sproc: reporting.RegsaverRegistrants_Charlie
UPDATE reporting.ReportCatalogue
SET Description = N'Vertical Insure (RegSaver) payouts. IGNORES the @jobId param entirely - pulls all matching across a hardcoded list of 8 allowed customers (LFTC, ASL, STEPS, XPO, STEPS-CA, LLL, MD Cup, Irish Prospect). Returns per-registrant payout + monthly summary. STEPS receives Payout/2.',
    CategoryCode = N'Registrations'
WHERE Title = N'RegSaver Registrants';

-- Title: Customer Contact Export
-- Sproc: reporting.CustomerEndUsersDump
UPDATE reporting.ReportCatalogue
SET Description = N'Customer-wide contact dump (all jobs of @jobId customer). UNION of (clubreps via teams) + (players via teams). Returns year, jobName, role, name, DOB, grad year, club, team, position, school, Email, Mom_Email, Dad_Email, plus cellphones and address.',
    CategoryCode = N'Administration'
WHERE Title = N'Customer Contact Export';

-- Title: Export History
-- Sproc: reporting.ExportReportsHistory
UPDATE reporting.ReportCatalogue
SET Description = N'Audit log of report exports for @jobId. Returns jobName, Export name (sproc OR Crystal report), ExportedBy person, WhenExported timestamp. Excludes SuperUser exports. Sorted desc by export date.',
    CategoryCode = N'Administration'
WHERE Title = N'Export History';

-- Title: Rostered Players Data (Legacy)
-- Sproc: reporting.ThePlayers_PlayerShowCaseExport
UPDATE reporting.ReportCatalogue
SET Description = N'Earlier version of the rich player showcase export. Same columns as ThePlayers_PlayerShowCaseExportII PLUS honors_academic and honors_athletic. Active players, role=Player, no waitlists. Kept active by user choice - superseded by the II variant for current use.',
    CategoryCode = N'Recruiting'
WHERE Title = N'Rostered Players Data (Legacy)';

-- Title: Transaction Rollup
-- Sproc: reporting.GetJobTransactionRollup
UPDATE reporting.ReportCatalogue
SET Description = N'Job transaction rollup. Five result sets: RawData (all reg_acct rows joined to player), Paid (CC/check totals with grand total via ROLLUP), Discounted (sum fee_discount where DiscountCodeID set), Adjusted (Online Correction lines), Owed (sum owed_total). Excludes WAITLIST agegroups.',
    CategoryCode = N'Financials'
WHERE Title = N'Transaction Rollup';

-- Title: Customer Registrations by Job / Agegroup / Division / Team
-- Sproc: reporting.AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam
UPDATE reporting.ReportCatalogue
SET Description = N'Customer-wide registration grid (all jobs of @jobId customer). GROUP BY jobName, year, agegroup, team WITH ROLLUP - produces JOB ROLLUP and AGEGROUP ROLLUP marker rows. Counts active player registrations only.',
    CategoryCode = N'Registrations'
WHERE Title = N'Customer Registrations by Job / Agegroup / Division / Team';

-- Title: Staff Export
-- Sproc: reporting.StaffExport
UPDATE reporting.ReportCatalogue
SET Description = N'Active Staff registrations for @jobId. Returns LastName, FirstName, Assignment text, formatted cellphone, Email, jersey_size, shorts_size, sweatpants, shoes. Despite the title, only Staff role - does not include Coach or Unassigned Adult.',
    CategoryCode = N'Rosters'
WHERE Title = N'Staff Export';

-- Title: Job QA
-- Sproc: utility.JobCloneQA
UPDATE reporting.ReportCatalogue
SET Description = N'Single-job configuration dump for clone-validation. Returns 40+ field/value rows covering every important Job/Customer column: name, paths, expiry dates, payment options, ARB settings, fees, regform names, mobile flags, parallax text. Used to verify a cloned job carried over correctly.',
    CategoryCode = N'Administration'
WHERE Title = N'Job QA';

-- Title: Players Amount Paid By Camp
-- Sproc: reporting.CathyCampBreakoutPerJob_AmtPaid
UPDATE reporting.ReportCatalogue
SET Description = N'Per-player payment breakdown across the customer''s camp/clinic jobs in the current job year (filters jobType=''player registration for camp or clinic''). Returns jobName, agegroup, team, player name, ClubTeamName, grad_year, fee_base, fee_total, fee_discount, fee_processing, paid_total, owed_total. Camp-specific (Cathy = MD Lax Camps).',
    CategoryCode = N'Camp'
WHERE Title = N'Players Amount Paid By Camp';

-- Title: Sibling Report
-- Sproc: utility.sibling_report
UPDATE reporting.ReportCatalogue
SET Description = N'Identifies siblings in @jobId - players whose Family_UserId has more than one active player registration in the job. Returns LastName, FirstName, RegistrationID for each sibling. Useful for validating multi-sibling discounts and family-pricing.',
    CategoryCode = N'Registrations'
WHERE Title = N'Sibling Report';

-- Title: Team Contact Emails
-- Sproc: reporting.TournamentExportClubrepsAndCoachesEmail
UPDATE reporting.ReportCatalogue
SET Description = N'Team contact list for @jobId. UNION of active Club Reps + active Staff (treated as coaches). Returns Agegroup, Club, Team, Role, Last, First, Email - distinct rows. Useful for tournament mass-email. NOTE: clubrep join uses r.RegistrationId = t.teamId which is incorrect; verify clubrep rows are correct.',
    CategoryCode = N'Schedules'
WHERE Title = N'Team Contact Emails';

-- Title: Team Counts - All Regions YTD
-- Sproc: utility.AmericanSelect_CountsAtYear_ByTeam
UPDATE reporting.ReportCatalogue
SET Description = N'American Select cross-job team counts by region/grade. Filters jobName like ''American Select Lacrosse:%'' excluding Main and Showcase events. Per-region per-grade-level (Seniors/Juniors/Sophomores/Freshmen/8thGrade derived from team-name digits). Returns YTD count and final count.',
    CategoryCode = N'Recruiting'
WHERE Title = N'Team Counts - All Regions YTD';

-- Title: Schedule QA Results
-- Sproc: utility.Schedule_QA_Tourny
UPDATE reporting.ReportCatalogue
SET Description = N'Tournament schedule QA - 15+ result sets: Unscheduled Teams, Games per date, Teams not matching ranks, Double bookings (field + team), Games per team per day, Games per field per day, Back-to-Backs, RR Games/Div, Div Ranks, Playoffs, Teams Playing Opponent >1x, Fields Per Team, FirstToLast game-day spans. Plus extra cross-tournament club-pairing analysis when @jobId matches a HARDCODED list of 5 specific 2025 jobs (LLL Boys/Girls, LFTC, G8, MDCup, LBTS) - needs annual update.',
    CategoryCode = N'Schedules'
WHERE Title = N'Schedule QA Results';

-- Title: Historical Tourney Participation by Club
-- Sproc: reporting.TournyYearOverYearClubTeams
UPDATE reporting.ReportCatalogue
SET Description = N'Multi-year tournament team counts by club. Customer-wide for tournament-type jobs. Excludes 2021 and current year. Returns raw club x year counts plus a dynamically-pivoted year-columns table with club totals.',
    CategoryCode = N'Schedules'
WHERE Title = N'Historical Tourney Participation by Club';

-- Title: Player Contact History
-- Sproc: reporting.TLCContactsHx
UPDATE reporting.ReportCatalogue
SET Description = N'Customer-wide active-player contact dedupe (all jobs of @jobId customer). Returns Last, First, Email, Mom_Email, Dad_Email, cellphone, Mom_Cellphone, Dad_Cellphone - distinct. Title says "Contact History" but no time-series - it is a current-state contact dump. Built for TLC Lacrosse outreach.',
    CategoryCode = N'Administration'
WHERE Title = N'Player Contact History';

-- Title: Team Counts - Summary Rollup
-- Sproc: utility.AmericanSelect_CountsAtYear_ByTeamII
UPDATE reporting.ReportCatalogue
SET Description = N'Improved version of Team Counts All Regions YTD. Builds dynamic SQL via STRING_AGG with five result sets: (1) Region+Grade with per-year YTD pivots; (2) Region totals; (3) Grand total; (4) Excel-style pivot interleaving YTD and total per year; (5) Region-only pivot. Same American Select scope as variant I.',
    CategoryCode = N'Recruiting'
WHERE Title = N'Team Counts - Summary Rollup';

-- Title: Referee Assignment QA
-- Sproc: reporting.RefAssignmentQA
UPDATE reporting.ReportCatalogue
SET Description = N'Referee assignment diagnostic for @jobId. Pulls all RefGameAssigments with ref name, game info, plus minutes-to-next-game-same-day. Two result sets: raw data sorted by ref+date, and a Double-Booked Refs list (same date, same refRegId, count > 1).',
    CategoryCode = N'Schedules'
WHERE Title = N'Referee Assignment QA';

-- ---- Cross-customer SuperUser-only reports (13) ----

-- Title: ClubRep Contacts (All Customers)
-- Sproc: reporting.[ClubRepContacts-All]
UPDATE reporting.ReportCatalogue
SET Description = N'Cross-customer dedupe of every active Club Rep across all jobs in TSIC. Returns Club, sportName, UserName, #Jobs (count of active CR registrations for this user), Last, First, Email, cellphone, address. Sorted by job-count desc - surfaces repeat clubreps first.',
    CategoryCode = N'Administration'
WHERE Title = N'ClubRep Contacts (All Customers)';

-- Title: Job Key Attributes (All Customers)
-- Sproc: reporting.[JobKeyAttributes-ALL]
UPDATE reporting.ReportCatalogue
SET Description = N'Cross-customer per-job configuration dump. When @jobId is the all-zeros sentinel: every job. Otherwise: only jobs whose ExpiryAdmin > now. Returns customer, year/season, job names, expiry dates, EventStart/End, USLaxEnd, counts (CR/Teams/WLTeams/Players/Overpaid/Underpaid), JobType, adnLoginID, every key Boolean flag, perPlayer/perTeam charges, billing config.',
    CategoryCode = N'Administration'
WHERE Title = N'Job Key Attributes (All Customers)';

-- Title: Tournament Keys (All Customers)
-- Sproc: reporting.[TournamentKeyAttributes-ALL]
UPDATE reporting.ReportCatalogue
SET Description = N'Cross-customer per-job dump filtered to Tournament/League jobs. When @jobId=zero: ALL years (no expiry filter). Otherwise: jobs in current year + next year, ExpiryAdmin > now. Returns jobName, weekday-formatted EventStart/End, perTeamCharge, counts, bHideContacts, bVIEnabled, all the registration/access flags. Sorted by EventStart.',
    CategoryCode = N'Schedules'
WHERE Title = N'Tournament Keys (All Customers)';

-- Title: RegSaver Registrants (All)
-- Sproc: reporting.[RegsaverRegistrants_ALL]
UPDATE reporting.ReportCatalogue
SET Description = N'Cross-customer Vertical-Insure-Payouts reconciliation. Six result sets: RawData Players, RawData Teams, By Job, By Month, Unmatched Policy#s (in payouts but no reg/team match), VI NotYetPaid Policy#s (registrations or teams with policy but no payout yet), STEPS X-Check By Year/Month (for hardcoded 8 customers - Payout/2 to STEPS).',
    CategoryCode = N'Financials'
WHERE Title = N'RegSaver Registrants (All)';

-- Title: RegSaver Purchases - Raw
-- Sproc: reporting.[RegsaverPurchases_ALL_Rawdata]
UPDATE reporting.ReportCatalogue
SET Description = N'RegSaver insurance purchases raw export. When @jobId=zero: every job with bOfferPlayerRegsaverInsurance=1. Otherwise: just that job. Two result sets: Player Data (per-registration policy info, family email, fee_total) and Team Data (team policies with original purchaser vs current clubrep, agegroup, team name, fee/owed/paid).',
    CategoryCode = N'Financials'
WHERE Title = N'RegSaver Purchases - Raw';

-- Title: Suspicious ARBs
-- Sproc: utility.GetSuspiciousArbs
UPDATE reporting.ReportCatalogue
SET Description = N'Cross-job ARB (recurring billing) diagnostic. Looks at every non-canceled, non-finished subscription where math doesn''t reconcile and flags four suspicion modes: playerMoved? (paid different fee than the team/agegroup roster fee), disCode? (discount applied), isDblChrg? (negative owed remainder), trueFail? (positive owed remainder = genuine billing failure), isExpired?, is1st?. When @jobId=zero: all jobs. Otherwise scoped.',
    CategoryCode = N'Administration'
WHERE Title = N'Suspicious ARBs';

-- Title: Expiring Bulletins (3 months)
-- Sproc: utility.ExpiringBulletins
UPDATE reporting.ReportCatalogue
SET Description = N'All bulletins (any job, any customer) whose EndDate is within the next 3 months. Returns jobName, title, StartDate, EndDate, IsActive. NOTE: does NOT filter on b.active = 1 - inactive bulletins also appear, despite the implied "expiring" framing.',
    CategoryCode = N'Administration'
WHERE Title = N'Expiring Bulletins (3 months)';

-- Title: Expired Player Registration Bulletins
-- Sproc: utility.PlayerRegistrationBulletinsQA
UPDATE reporting.ReportCatalogue
SET Description = N'QA: jobs whose ExpiryUsers > now AND bRegistrationAllowPlayer = 1 (still accepting players) but whose active "Player Registration:" bulletin has EndDate < now. Identifies stale bulletins on still-open registration windows.',
    CategoryCode = N'Registrations'
WHERE Title = N'Expired Player Registration Bulletins';

-- Title: Expired Team Registration Bulletins
-- Sproc: utility.TeamRegistrationBulletinsQA
UPDATE reporting.ReportCatalogue
SET Description = N'QA: jobs whose ExpiryUsers > now AND bRegistrationAllowTeam = 1 but whose active "Club Rep Team Registration" bulletin has EndDate < now. Same pattern as the Player variant, for team-reg bulletins.',
    CategoryCode = N'Registrations'
WHERE Title = N'Expired Team Registration Bulletins';

-- Title: New Jobs Last Month (with txs)
-- Sproc: reporting.NewTsicJobsWithTxs
UPDATE reporting.ReportCatalogue
SET Description = N'Identifies jobs whose first-ever active Registration_Accounting row landed in the prior calendar month. Returns jobId, jobName, minTxDate. Used to spot newly-billing jobs at month-close.',
    CategoryCode = N'Financials'
WHERE Title = N'New Jobs Last Month (with txs)';

-- Title: Job Admin Fees Summary
-- Sproc: reporting.JobAdminFeesAll
UPDATE reporting.ReportCatalogue
SET Description = N'All-time JobAdminCharges dump. Returns Year, Month, CreateDate, jobName, charge type Name, ChargeAmount, Comment. Sorted desc - newest first. NOT filtered by @jobId or by date range - full history.',
    CategoryCode = N'Financials'
WHERE Title = N'Job Admin Fees Summary';

-- Title: Last Month Grand Totals
-- Sproc: adn.GetLastMonthsGrandTotals
UPDATE reporting.ReportCatalogue
SET Description = N'Prior-month grand totals - restricted to adnLoginID = ''teamspt52'' (TSIC''s primary ADN account; other ADN customers excluded). Per-job: CC payments, CC credits/refunds, processing fees (j.ProcessingFeePercent default 3.5/100 - treated as fraction), TSIC fees (perPlayerCharge x NewPlayers + perTeamCharge x NewTeams), admin charges, GrandTotal. Plus per-customer summary and grand-total result sets.',
    CategoryCode = N'Financials'
WHERE Title = N'Last Month Grand Totals';

-- Title: ADN-Nuvei Reconcile
-- Sproc: adn.ReconcileNuvei
UPDATE reporting.ReportCatalogue
SET Description = N'Prior-month ADN-vs-Nuvei reconciliation. Three result sets: (1) Nuvei funding events matched against Nuvei batch nets; (2) Chargebacks (non-Settlement funding events); (3) ADN daily-totals from adn.txs vs Nuvei batch close. Joins ADN to Jobs by parsing invoice-number for jobAI.',
    CategoryCode = N'Financials'
WHERE Title = N'ADN-Nuvei Reconcile';

PRINT '';
PRINT '=== Backfill Result ===';
PRINT 'Description + CategoryCode applied for all 45 seeded titles.';

-- ============================================================================
-- Section 4: Final state (per-row SP existence status)
-- ============================================================================

SELECT
    c.Title,
    c.CategoryCode,
    c.StoredProcName,
    c.SortOrder,
    c.Active,
    c.ParametersJson,
    c.VisibilityRules,
    CASE WHEN OBJECT_ID(c.StoredProcName, 'P') IS NULL
         THEN 'MISSING'
         ELSE 'OK'
    END AS SpStatus
FROM reporting.ReportCatalogue c
ORDER BY c.SortOrder;

SET NOCOUNT OFF;
