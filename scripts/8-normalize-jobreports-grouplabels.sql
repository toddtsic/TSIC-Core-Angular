/*
    Normalizes reporting.JobReports.GroupLabel onto the seven category codes the
    Reports Library groups by: Rosters, Schedules, Registrations, Financials, Camp,
    Recruiting, Administration.

    ONE-SHOT. Paste the whole thing into a query window connected to the target server
    and execute once. No transaction to remember -- it commits itself on success and
    rolls itself back on any error. It ABORTS BEFORE WRITING ANYTHING if either guard
    trips (an unmatched map key, or a unique-index collision).

    WHY: GroupLabel holds legacy MENU HEADINGS imported verbatim from the old site
    ('Reports', 'Scheduling', 'Docs', 'Player Stats', 'Search'). normalizeReportCategory()
    matches the code exactly, so every non-matching heading falls into the "Other" tab.
    That was invisible while Crystal-kind rows were hidden from Directors; now that
    reporting.JobReports is the sole entitlement (23e8463cb), it is the difference
    between a usable library and 13-of-23 tiles in a catch-all.

    Keyed on the REPORT, not the title: the same report carries different titles across
    jobs (clubrostersNoMedicalII is "Rosters for Coaches (pdf)" on 10 jobs and "Club
    Rosters for Coaches (pdf)" on 17). For stored-proc rows the key is the spName inside
    the Action; for named-endpoint rows it is the Action itself.

    Categories reviewed report-by-report with Todd 2026-09-08. His rulings against the
    proposed set: MaxExposure_CountsAtYear_ByTeam -> Administration (not Registrations),
    CathyCampCheckinWithVacAndMedform -> Registrations (not Camp), sibling_report ->
    Registrations (not Financials). TournamentRecruitingReportUSL stays Rosters despite
    its action name -- it is the LSN TV broadcast roster, not a recruiting report.

    Touches ONE column (plus Modified). No inserts, no deletes, no change to Active,
    Title, Action, SortOrder or RoleId. Rows already carrying a valid code are untouched.
    Idempotent -- re-running changes nothing further.

    Dev (TSIC-SEDONA) run 2026-09-08: 0 unmatched, 0 collisions, 1643 rows.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

USE TSICV5;

-- WHERE AM I? This script names no server -- USE TSICV5 resolves against whatever instance
-- the query window is connected to, and BOTH boxes have a TSICV5. Dev (SEDONA) is a RESTORED
-- BACKUP of prod; prod lives on TSIC-PHOENIX's own local .SS2016. CHECK THIS FIRST GRID.
SELECT @@SERVERNAME AS Server_, DB_NAME() AS Database_, SYSDATETIME() AS RunAt;

-- Report key -> category. One row per distinct report.
IF OBJECT_ID('tempdb..#map') IS NOT NULL DROP TABLE #map;
CREATE TABLE #map (ReportKey nvarchar(400) NOT NULL PRIMARY KEY, NewLabel nvarchar(50) NOT NULL);

INSERT INTO #map (ReportKey, NewLabel) VALUES
 ('[reporting].[Customer_RegistrationAccounting_Records]',                  'Financials')
,('[reporting].[Schedule_Export_Teams_ToTournyMachine]',                    'Schedules')
,('[reporting].[Schedule_Export_ToTournyMachine]',                          'Schedules')
,('Schedule_ByAgegroup',                                                    'Schedules')
,('[reporting].[JobClubRepContacts]',                                       'Rosters')
,('AmericanSelectEvaluation',                                               'Rosters')
,('AmericanSelectMainEventRosters',                                         'Rosters')
,('Job_Rosters_NoMedical',                                                  'Rosters')
-- PlayerStats_E120 DELIBERATELY OMITTED: obsolete report (Todd 2026-09-08), and it is
-- listed TWICE in 36 jobs for the same role -- 'Entry Form (pdf)' under 'Player Stats' and
-- 'Player Stats Entry Form (pdf)' under 'Reports'. Mapping both to Rosters would violate
-- UX_JobReports_JobRoleActionGroup (JobId, RoleId, Controller, Action, GroupLabel) and abort
-- the UPDATE. Left untouched: both copies stay exactly where the Director sees them today.
,('Job_Club_Rosters',                                                       'Rosters')
,('[reporting].[RefAssignmentQA]',                                          'Schedules')
,('clubrostersNoMedicalII',                                                 'Rosters')
,('[reporting].[ThePlayers_PlayerShowCaseExport_IncludeInactive]',          'Rosters')
,('[reporting].[ThePlayers_PlayerShowCaseExportII]',                        'Rosters')
,('[utility].[MaxExposure_ClubCoaches]',                                    'Rosters')
,('[utility].[MaxExposure_CountsAtYear_ByTeam]',                            'Administration')
,('[reporting].[PlayerParentMailingData]',                                  'Rosters')
,('[reporting].[CathyCampBreakoutPerJob_PreviousMonth]',                    'Financials')
,('[reporting].[CathyCampCheckinWithVacAndMedform]',                        'Registrations')
,('[reporting].[JobPushTeamResultsSummary]',                                'Administration')
,('[reporting].[StepsCoaches_Export]',                                      'Rosters')
,('[reporting].[ThePlayers_PlayerShowCaseExport]',                          'Rosters')
,('[utility].[Schedule_QA_Tourny]',                                         'Schedules')
,('[reporting].[CustomerEndUsersDump]',                                     'Administration')
,('[adn].[monthlycustomerrollups]',                                         'Financials')
,('[reporting].[GetJobTransactionRollup]',                                  'Financials')
,('[reporting].[RegsaverRegistrants_Charlie]',                              'Registrations')
,('Club_AllJobs_Rosters_NoMedical',                                         'Rosters')
,('TournamentRecruitingReportUSL',                                          'Rosters')
,('[reporting].[ExportReportsHistory]',                                     'Administration')
,('[reporting].[AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam]','Registrations')
,('[reporting].[StaffExport]',                                              'Administration')
,('[utility].[sibling_report]',                                             'Registrations')
,('FieldUtilizationWithNominations',                                        'Schedules')
,('[reporting].[TournyYearOverYearClubTeams]',                              'Registrations')
,('[utility].[AmericanSelect_CountsAtYear_ByTeam]',                         'Registrations')
,('ScheduleByClubAgTPerPage',                                               'Schedules')
,('TournamentRecruitingReportASL',                                          'Recruiting')
,('[utility].[AmericanSelect_CountsAtYear_ByTeamII]',                       'Registrations')
,('[utility].[JobCloneQA]',                                                 'Administration')
,('[reporting].[TLCContactsHx]',                                            'Rosters')
,('[reporting].[TournamentExportClubrepsAndCoachesEmail]',                  'Rosters')
,('camp_excelexport_summer_pdf',                                            'Camp')
,('[reporting].[CathyCampBreakoutPerJob_AmtPaid]',                          'Financials')
,('Schedule_Gamecards',                                                     'Schedules')
;

-- Every row with its report key.
IF OBJECT_ID('tempdb..#keyed') IS NOT NULL DROP TABLE #keyed;
SELECT r.JobReportId,
       CASE WHEN r.Action LIKE 'ExportStoredProcedureResults%'
            THEN SUBSTRING(r.Action, CHARINDEX('spName=', r.Action) + 7,
                 CASE WHEN CHARINDEX('&', r.Action, CHARINDEX('spName=', r.Action)) > 0
                      THEN CHARINDEX('&', r.Action, CHARINDEX('spName=', r.Action)) - CHARINDEX('spName=', r.Action) - 7
                      ELSE 8000 END)
            ELSE r.Action END AS ReportKey
INTO #keyed
FROM reporting.JobReports r;

DECLARE @unmatched int, @collisions int, @toChange int, @changed int;

-- GUARD 0: this window must have NO transaction already open. T-SQL transactions NEST:
-- an outer BEGIN TRAN left over from an earlier run makes this script's COMMIT merely
-- decrement @@TRANCOUNT instead of committing, so the UPDATE looks like it worked, reports
-- "COMMITTED", and is silently discarded when the window closes. Cost us two runs on dev.
-- Fix: run ROLLBACK; in this window (it unwinds ALL levels, unlike COMMIT), then re-run.
IF @@TRANCOUNT > 0
BEGIN
    SELECT '*** ABORTED -- a transaction is already open in this window ***' AS Result,
           @@TRANCOUNT AS OpenTranCount,
           'Run ROLLBACK; in THIS window, then re-run this script.' AS Fix;
    RETURN;
END

-- GUARD 1: any report in the map that matched nothing (typo / renamed action).
SELECT @unmatched = COUNT(*)
FROM   #map m
WHERE  NOT EXISTS (SELECT 1 FROM #keyed k WHERE k.ReportKey = m.ReportKey);

-- GUARD 2: unique-index collisions. UX_JobReports_JobRoleActionGroup is UNIQUE on
-- (JobId, RoleId, Controller, Action, GroupLabel) -- GroupLabel is PART OF THE KEY. If a job
-- lists the same report twice under two headings, normalizing both to one label collides and
-- the UPDATE aborts partway. Computes the post-update label for EVERY row, looks for dupes.
SELECT @collisions = COUNT(*) FROM (
    SELECT a.JobId, a.RoleId, a.Controller, a.Action, a.FinalLabel
    FROM (SELECT r.JobId, r.RoleId, r.Controller, r.Action,
                 ISNULL(m.NewLabel, r.GroupLabel) AS FinalLabel
          FROM reporting.JobReports r
          JOIN #keyed k ON k.JobReportId = r.JobReportId
          LEFT JOIN #map m ON m.ReportKey = k.ReportKey) a
    GROUP BY a.JobId, a.RoleId, a.Controller, a.Action, a.FinalLabel
    HAVING COUNT(*) > 1) x;

SELECT @toChange = COUNT(*)
FROM   reporting.JobReports r
JOIN   #keyed k ON k.JobReportId = r.JobReportId
JOIN   #map   m ON m.ReportKey   = k.ReportKey
WHERE  ISNULL(r.GroupLabel, '') <> m.NewLabel;

-- What is about to change, and from what.
SELECT m.NewLabel, ISNULL(r.GroupLabel, '<NULL>') AS CurrentLabel, COUNT(*) AS Rows_
FROM   reporting.JobReports r
JOIN   #keyed k ON k.JobReportId = r.JobReportId
JOIN   #map   m ON m.ReportKey   = k.ReportKey
WHERE  ISNULL(r.GroupLabel, '') <> m.NewLabel
GROUP BY m.NewLabel, r.GroupLabel
ORDER BY m.NewLabel, CurrentLabel;

IF @unmatched > 0 OR @collisions > 0
BEGIN
    SELECT '*** ABORTED -- NOTHING WAS CHANGED ***' AS Result,
           @unmatched  AS UnmatchedMapEntries,
           @collisions AS CollidingKeyGroups;
    -- Unmatched: an Action was renamed -- fix the map key.
    -- Collisions: a report is double-listed under two headings -- decide which copy survives.
    RETURN;
END

BEGIN TRY
    BEGIN TRAN;

    UPDATE r
    SET    r.GroupLabel = m.NewLabel,
           r.Modified   = GETDATE()
    FROM   reporting.JobReports r
    JOIN   #keyed k ON k.JobReportId = r.JobReportId
    JOIN   #map   m ON m.ReportKey   = k.ReportKey
    WHERE  ISNULL(r.GroupLabel, '') <> m.NewLabel;

    SET @changed = @@ROWCOUNT;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    SELECT '*** FAILED -- ROLLED BACK, NOTHING CHANGED ***' AS Result,
           ERROR_NUMBER() AS ErrNo, ERROR_MESSAGE() AS ErrMsg;
    THROW;   -- re-raise: ends the batch, so the "COMMITTED" line below cannot print
END CATCH

SELECT 'COMMITTED' AS Result, @toChange AS RowsExpected, @changed AS RowsChanged;

-- VERIFY: every remaining label across the ACTIVE catalogue, flagged valid or not.
SELECT ISNULL(r.GroupLabel, '<NULL>') AS GroupLabel,
       CASE WHEN r.GroupLabel IN ('Rosters','Schedules','Registrations','Financials',
                                  'Camp','Recruiting','Administration')
            THEN 'VALID' ELSE '*** OTHER TAB ***' END AS Status,
       COUNT(*) AS Rows_
FROM   reporting.JobReports r
WHERE  r.Active = 1
GROUP BY r.GroupLabel
ORDER BY Status, COUNT(*) DESC;
-- EXPECT EXACTLY TWO '*** OTHER TAB ***' rows, both PlayerStats_E120 (obsolete, deliberately
-- omitted above): 'Player Stats' and 'Reports'. Anything ELSE in that bucket is a miss.
-- To retire it later: DELETE the redundant copy in the 36 double-listed jobs, then map it.
