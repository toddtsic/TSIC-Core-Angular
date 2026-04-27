-- ============================================================
-- Seed: reporting.ReportCatalogue — SuperUser cross-customer reports
-- Idempotent: inserts only rows whose Title is not yet present.
--
-- Seeds 13 Type 2 (stored-proc -> XLSX) report entries for the
-- TSIC home / monthly-close workflow. Distinct from 7c (the 32
-- job-scoped reports) because:
--   - bUseJobId=false (no @jobId param to the SP — cross-customer rollups)
--   - VisibilityRules requires the "Superuser" role
--
-- Together those two columns let the reports library render these only
-- to SuperUser AND make the FE call /api/reporting/export-sp with
-- bUseJobId=false. The BE additionally enforces the Superuser gate when
-- bUseJobId=false (ReportingController.cs) so a Director with the URL
-- can't pull cross-customer data.
--
-- Sort order: 1000+ to keep them visually grouped at the end of the
-- library when SuperUser views it.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ============================================================
-- Step 1: Verify referenced SPs exist in this DB
-- ============================================================
DECLARE @SpCheck TABLE (SpName NVARCHAR(200) NOT NULL PRIMARY KEY);
INSERT INTO @SpCheck VALUES
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

-- ============================================================
-- Step 2: Seed catalog rows (idempotent on Title)
-- All rows: ParametersJson '{"bUseJobId":false}', VisibilityRules requires Superuser.
-- ============================================================
DECLARE @ParamsCrossCustomer NVARCHAR(200) = N'{"bUseJobId":false}';
DECLARE @RulesSuperUser      NVARCHAR(200) = N'{"requiresRoles":["Superuser"]}';

DECLARE @Seed TABLE (
    Title           NVARCHAR(200)  NOT NULL PRIMARY KEY,
    Description     NVARCHAR(1000) NULL,
    IconName        NVARCHAR(50)   NULL,
    StoredProcName  NVARCHAR(200)  NOT NULL,
    SortOrder       INT            NOT NULL
);

-- Legacy "Reports" section (9 SP runners — utility / cross-customer rollups)
INSERT INTO @Seed (Title, Description, IconName, StoredProcName, SortOrder) VALUES
    ('ClubRep Contacts (All Customers)',          'Club rep contact dump across all customers',                         'people',              'reporting.ClubRepContacts-All',                  1000),
    ('Job Key Attributes (All Customers)',        'Per-job key attributes across all customers',                        'list-columns',        'reporting.JobKeyAttributes-ALL',                 1010),
    ('Tournament Keys (All Customers)',           'Per-tournament key attributes across all customers',                 'trophy',              'reporting.TournamentKeyAttributes-ALL',          1020),
    ('RegSaver Registrants (All)',                'Cross-customer registrants discovered via RegSaver flow',            'person-plus',         'reporting.RegsaverRegistrants_ALL',              1030),
    ('RegSaver Purchases - Raw',                  'Raw RegSaver purchases data dump across all customers',              'currency-dollar',     'reporting.RegsaverPurchases_ALL_Rawdata',        1040),
    ('Suspicious ARBs',                           'List of ARB subscriptions that look suspicious',                     'exclamation-triangle','utility.GetSuspiciousArbs',                       1050),
    ('Expiring Bulletins (3 months)',             'Job bulletins whose expiry date is within 3 months',                 'clock-history',       'utility.ExpiringBulletins',                       1060),
    ('Expired Player Registration Bulletins',     'Active sites whose player-reg bulletins have already expired',       'person-x',            'utility.PlayerRegistrationBulletinsQA',           1070),
    ('Expired Team Registration Bulletins',       'Active sites whose team-reg bulletins have already expired',         'shield-x',            'utility.TeamRegistrationBulletinsQA',             1080);

-- Legacy "Accounting" section (4 SP runners; the rest of Accounting is workflow screens)
INSERT INTO @Seed (Title, Description, IconName, StoredProcName, SortOrder) VALUES
    ('New Jobs Last Month (with txs)',            'Jobs created in the prior month that already have transactions',     'plus-square',         'reporting.NewTsicJobsWithTxs',                    1100),
    ('Job Admin Fees Summary',                    'Cross-customer summary of admin fees collected',                     'cash-coin',           'reporting.JobAdminFeesAll',                       1110),
    ('Last Month Grand Totals',                   'ADN grand totals for the prior month',                               'calculator',          'adn.GetLastMonthsGrandTotals',                    1120),
    ('ADN-Nuvei Reconcile',                       'Side-by-side reconciliation of ADN vs Nuvei prior month batches',    'arrow-left-right',    'adn.ReconcileNuvei',                              1130);

DECLARE @InsertedCount INT;

INSERT INTO reporting.ReportCatalogue (Title, Description, IconName, StoredProcName, ParametersJson, VisibilityRules, SortOrder, Active, Modified)
SELECT
    s.Title,
    s.Description,
    s.IconName,
    s.StoredProcName,
    @ParamsCrossCustomer,
    @RulesSuperUser,
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
-- Verification: show the SuperUser cross-customer rows and SP status
-- ============================================================
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
WHERE c.SortOrder >= 1000
ORDER BY c.SortOrder;

SET NOCOUNT OFF;
