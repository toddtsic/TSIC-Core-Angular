/*
    Normalizes reporting.JobReports.GroupLabel onto the seven category codes the
    Reports Library groups by: Rosters, Schedules, Registrations, Financials, Camp,
    Recruiting, Administration.

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

    Rows already carrying a valid code are untouched. Safe to re-run.
*/

USE TSICV5;
GO

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
,('PlayerStats_E120',                                                       'Rosters')
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

-- 1. PRE-CHECK -------------------------------------------------------------
-- How many rows this will change, and from what.
SELECT m.NewLabel, ISNULL(r.GroupLabel, '<NULL>') AS CurrentLabel, COUNT(*) AS Rows_
FROM   reporting.JobReports r
JOIN   #keyed k ON k.JobReportId = r.JobReportId
JOIN   #map   m ON m.ReportKey   = k.ReportKey
WHERE  ISNULL(r.GroupLabel, '') <> m.NewLabel
GROUP BY m.NewLabel, r.GroupLabel
ORDER BY m.NewLabel, CurrentLabel;

-- Any report in the map that matched nothing (typo / renamed action).
SELECT m.ReportKey AS UnmatchedMapEntry
FROM   #map m
WHERE  NOT EXISTS (SELECT 1 FROM #keyed k WHERE k.ReportKey = m.ReportKey);
-- expect ZERO rows. Anything listed means the key is wrong -- stop.

BEGIN TRAN;

-- 2. UPDATE ----------------------------------------------------------------
UPDATE r
SET    r.GroupLabel = m.NewLabel,
       r.Modified   = GETDATE()
FROM   reporting.JobReports r
JOIN   #keyed k ON k.JobReportId = r.JobReportId
JOIN   #map   m ON m.ReportKey   = k.ReportKey
WHERE  ISNULL(r.GroupLabel, '') <> m.NewLabel;

-- 3. VERIFY ----------------------------------------------------------------
-- Every remaining label across the ACTIVE catalogue, flagged valid or not.
SELECT ISNULL(r.GroupLabel, '<NULL>') AS GroupLabel,
       CASE WHEN r.GroupLabel IN ('Rosters','Schedules','Registrations','Financials',
                                  'Camp','Recruiting','Administration')
            THEN 'VALID' ELSE '*** OTHER TAB ***' END AS Status,
       COUNT(*) AS Rows_
FROM   reporting.JobReports r
WHERE  r.Active = 1
GROUP BY r.GroupLabel
ORDER BY Status, COUNT(*) DESC;
-- expect NO '*** OTHER TAB ***' rows.

-- COMMIT;   -- or ROLLBACK;
