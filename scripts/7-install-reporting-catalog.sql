-- ============================================================================
-- 7-install-reporting-catalog.sql
--
-- Single-file install for the Type 2 report catalog (`reporting.ReportCatalogue`).
-- Idempotent end-to-end: schema, table, and all 45 seed rows skip-if-exists.
-- Safe to re-run.
--
-- Structure:
--   Section 1: Schema + table (DDL)
--   Section 2: SP availability check (45 SPs)
--   Section 3: Seed (32 job-scoped + 13 cross-customer SU-only)
--   Section 4: Final state (per-row SP status)
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
-- Section 3: Seed rows (idempotent on Title)
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
-- Section 4: Final state (per-row SP existence status)
-- ============================================================================

SELECT
    c.Title,
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
