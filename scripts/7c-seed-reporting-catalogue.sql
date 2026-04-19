-- ============================================================
-- Seed: reporting.ReportCatalogue
-- Idempotent: inserts only rows whose Title is not yet present.
--
-- Seeds 32 Type 2 (stored-proc -> multi-tab XLSX) report entries
-- discovered from the 2025-2027 legacy menu system.
--
-- VisibilityRules and ParametersJson are seeded as NULL. Both must
-- be populated per-row before a report can execute end-to-end:
--   - VisibilityRules: JSON matching the nav.NavItem shape; filters
--     the catalog per JobType / sport / feature flag.
--   - ParametersJson:  declares runtime binding between catalog and
--     stored-proc input params (e.g. @jobId).
--
-- The SuperUser editor UI (phase 3) will maintain these values in-app.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ============================================================
-- Step 1: Verify referenced SPs exist in this DB
-- ============================================================
DECLARE @SpCheck TABLE (SpName NVARCHAR(200) NOT NULL PRIMARY KEY);
INSERT INTO @SpCheck VALUES
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
    ('reporting.RefAssignmentQA');

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

-- ============================================================
-- Step 2: Seed catalog rows (idempotent on Title)
-- Ordered by legacy JobCount desc (most-used reports first).
-- ============================================================
DECLARE @Seed TABLE (
    Title           NVARCHAR(200)  NOT NULL PRIMARY KEY,
    Description     NVARCHAR(1000) NULL,
    IconName        NVARCHAR(50)   NULL,
    StoredProcName  NVARCHAR(200)  NOT NULL,
    SortOrder       INT            NOT NULL
);

INSERT INTO @Seed (Title, Description, IconName, StoredProcName, SortOrder) VALUES
    ('All Customer Jobs Accounting Records',                                'Cross-customer accounting rollup',                         'cash-stack',        'reporting.Customer_RegistrationAccounting_Records',                     10),
    ('Export Schedule (TournyMachine)',                                     'Schedule export in TournyMachine format',                  'calendar-week',     'reporting.Schedule_Export_ToTournyMachine',                              20),
    ('Export Teams (TournyMachine)',                                        'Scheduled teams export in TournyMachine format',           'box-arrow-up',      'reporting.Schedule_Export_Teams_ToTournyMachine',                        30),
    ('Club Reps',                                                           'Club rep contacts for the job',                            'people',            'reporting.JobClubRepContacts',                                           40),
    ('Evaluation Report',                                                   'American Select player evaluation sheet',                  'clipboard-check',   'reporting.AmericanSelectEvaluationSheet',                                50),
    ('Active and Inactive Player Data',                                     'Player showcase export including inactive players',        'person-lines-fill', 'reporting.ThePlayers_PlayerShowCaseExport_IncludeInactive',              60),
    ('Rostered Players Data',                                               'Rostered players showcase export',                         'person-check',      'reporting.ThePlayers_PlayerShowCaseExportII',                            70),
    ('Custom Accounting Report',                                            'Cathy camp breakout for previous month',                   'calculator',        'reporting.CathyCampBreakoutPerJob_PreviousMonth',                        80),
    ('Counts Year over Year',                                               'Team counts year over year',                               'bar-chart',         'utility.MaxExposure_CountsAtYear_ByTeam',                                90),
    ('Parents Mailing Data',                                                'Mailing list for player parents',                          'envelope',          'reporting.PlayerParentMailingData',                                     100),
    ('Player Club Coaches',                                                 'Club coaches associated with players',                     'person-badge',      'utility.MaxExposure_ClubCoaches',                                       110),
    ('Check-In with VacCard and MedForm Status',                            'Check-in roster including vaccination + medical status',   'clipboard-data',    'reporting.CathyCampCheckinWithVacAndMedform',                           120),
    ('Club Coaches Details',                                                'Detailed export of STEPS club coaches',                    'person-vcard',      'reporting.StepsCoaches_Export',                                         130),
    ('Pushes Per Event',                                                    'Team push notification summary per event',                 'bell',              'reporting.JobPushTeamResultsSummary',                                   140),
    ('Last Month Cross-Customer Revenue',                                   'ADN monthly customer revenue rollup',                      'cash',              'adn.monthlycustomerrollups',                                            150),
    ('RegSaver Registrants',                                                'Registrants discovered via RegSaver flow',                 'person-plus',       'reporting.RegsaverRegistrants_Charlie',                                 160),
    ('Customer Contact Export',                                             'Customer end-user contact export',                         'person-rolodex',    'reporting.CustomerEndUsersDump',                                        170),
    ('Export History',                                                      'Audit log of prior report exports',                        'clock-history',     'reporting.ExportReportsHistory',                                        180),
    ('Rostered Players Data (Legacy)',                                      'Prior version of rostered players showcase export',        'archive',           'reporting.ThePlayers_PlayerShowCaseExport',                             190),
    ('Transaction Rollup',                                                  'Job-level transaction rollup',                             'receipt',           'reporting.GetJobTransactionRollup',                                     200),
    ('Customer Registrations by Job / Agegroup / Division / Team',          'American Select registration detail grid',                 'grid-3x3',          'reporting.AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam', 210),
    ('Staff Export',                                                        'Staff list export',                                        'people-fill',       'reporting.StaffExport',                                                 220),
    ('Job QA',                                                              'Job clone QA diagnostic export',                           'check2-square',     'utility.JobCloneQA',                                                    230),
    ('Players Amount Paid By Camp',                                         'Cathy camp payment breakout by player',                    'currency-dollar',   'reporting.CathyCampBreakoutPerJob_AmtPaid',                             240),
    ('Sibling Report',                                                      'Sibling relationship report',                              'diagram-2',         'utility.sibling_report',                                                250),
    ('Team Contact Emails',                                                 'Clubrep + coach email export for tournament teams',        'envelope-at',       'reporting.TournamentExportClubrepsAndCoachesEmail',                     260),
    ('Team Counts - All Regions YTD',                                       'Team counts across all regions, year to date',             'bar-chart-steps',   'utility.AmericanSelect_CountsAtYear_ByTeam',                            270),
    ('Schedule QA Results',                                                 'Tournament schedule QA diagnostics',                       'list-check',        'utility.Schedule_QA_Tourny',                                            280),
    ('Historical Tourney Participation by Club',                            'Multi-year tournament team counts by club',                'graph-up',          'reporting.TournyYearOverYearClubTeams',                                 290),
    ('Player Contact History',                                              'TLC contact history for a player',                         'journal-text',      'reporting.TLCContactsHx',                                               300),
    ('Team Counts - Summary Rollup',                                        'Summary-level year-over-year team counts',                 'pie-chart',         'utility.AmericanSelect_CountsAtYear_ByTeamII',                          310),
    ('Referee Assignment QA',                                               'Referee assignment QA diagnostic',                         'clipboard-check',   'reporting.RefAssignmentQA',                                             320);

DECLARE @InsertedCount INT;

INSERT INTO reporting.ReportCatalogue (Title, Description, IconName, StoredProcName, SortOrder, Active, Modified)
SELECT
    s.Title,
    s.Description,
    s.IconName,
    s.StoredProcName,
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
PRINT CONCAT('Inserted: ', @InsertedCount, ' row(s)');
PRINT CONCAT('Total rows in catalog: ', @TotalCount);

-- ============================================================
-- Verification: show seeded catalog with SP-existence status
-- ============================================================
SELECT
    c.Title,
    c.StoredProcName,
    c.SortOrder,
    c.Active,
    CASE WHEN OBJECT_ID(c.StoredProcName, 'P') IS NULL
         THEN 'MISSING'
         ELSE 'OK'
    END AS SpStatus
FROM reporting.ReportCatalogue c
ORDER BY c.SortOrder;

SET NOCOUNT OFF;
