/*
  28 - Remove the Device_Teams / Device_RegistrationIds rows written by the device/sync walk.

  Background: from 2026-08-23 (4a6157c86) until the 2026-10-05 deploy (fa028001b), POST device/sync
  filed a TSIC-Teams phone against EVERY active registration the login's family held on any live
  job, instead of the one registration the login named. Legacy only ever wrote the one. The fix is
  deployed; this script removes what the walk wrote. The new sync never re-creates these rows.

  Two rules, both scoped to the walk era (modified >= 2026-08-23):

    A. Rows on jobs that are not TSIC-Teams jobs (tournament, league, showcase, or a club/camp/sales
       job without bEnableTSICTeams). The app has no function on those jobs and nobody has logged in
       as a registration on one since usage logging began (0 such registrations in TSICLogs).
       Without this, these rows become live score/alert subscriptions the moment tournament sends
       reach the Teams pool.

    B. Rows on TSIC-Teams jobs whose registration has NEVER made a TSIC-Teams request (TSICLogs
       logs.AppUsage, AppClientId = 1, since 2026-09-04), on a phone that HAS made requests as some
       other registration. The phone was active, so a login as that registration would be in the
       log; its absence means the walk wrote the row. Phones with no logged request at all are left
       alone - nothing can be said about them, and their rows are harmless until they log in again,
       when sync files the registration they actually pick.

  Pre-walk rows (before 2026-08-23) are legacy login rows and are not touched.

  RUN THE DRY RUN FIRST. Then run the DELETE block inside the transaction and compare the counts.
*/

SET NOCOUNT ON;
DECLARE @walkStart datetime = '2026-08-23';

-- Registrations the TSIC-Teams app has actually sent a request as
IF OBJECT_ID('tempdb..#used') IS NOT NULL DROP TABLE #used;
SELECT DISTINCT RegId INTO #used
FROM TSICLogs.logs.AppUsage WHERE AppClientId = 1 AND RegId IS NOT NULL;

-- Phones that have made at least one request as some registration
IF OBJECT_ID('tempdb..#activeDevices') IS NOT NULL DROP TABLE #activeDevices;
SELECT DISTINCT dt.DeviceId INTO #activeDevices
FROM mobile.Device_Teams dt JOIN #used u ON u.RegId = dt.RegistrationID;

-- The rows to remove
IF OBJECT_ID('tempdb..#doomed') IS NOT NULL DROP TABLE #doomed;
SELECT dt.Id, dt.DeviceId, dt.RegistrationID, dt.TeamID,
       CASE WHEN NOT (j.JobTypeID NOT IN (0,2,3,6) AND ISNULL(j.bEnableTSICTeams,0) = 1) THEN 'A non-Teams job'
            ELSE 'B never used on an active phone' END AS rule_
INTO #doomed
FROM mobile.Device_Teams dt
JOIN Leagues.teams t ON t.teamID = dt.TeamID
JOIN Jobs.Jobs j ON j.jobID = t.jobID
LEFT JOIN #used u ON u.RegId = dt.RegistrationID
LEFT JOIN #activeDevices ad ON ad.DeviceId = dt.DeviceId
WHERE dt.RegistrationID IS NOT NULL
  AND dt.modified >= @walkStart
  AND (
        -- Rule A
        NOT (j.JobTypeID NOT IN (0,2,3,6) AND ISNULL(j.bEnableTSICTeams,0) = 1)
        -- Rule B
        OR (u.RegId IS NULL AND ad.DeviceId IS NOT NULL)
      );

-- Device_RegistrationIds rows for the same (device, registration) pairs, plus walk-era rows whose
-- registration sits on a non-Teams job (the walk wrote one of these per registration too).
IF OBJECT_ID('tempdb..#doomedReg') IS NOT NULL DROP TABLE #doomedReg;
SELECT dr.Id INTO #doomedReg
FROM mobile.Device_RegistrationIds dr
JOIN Jobs.Registrations r ON r.RegistrationID = dr.RegistrationID
JOIN Jobs.Jobs j ON j.jobID = r.jobID
LEFT JOIN #used u ON u.RegId = dr.RegistrationID
LEFT JOIN #activeDevices ad ON ad.DeviceId = dr.DeviceId
WHERE dr.modified >= @walkStart
  AND (
        NOT (j.JobTypeID NOT IN (0,2,3,6) AND ISNULL(j.bEnableTSICTeams,0) = 1)
        OR (u.RegId IS NULL AND ad.DeviceId IS NOT NULL)
      );

-- ===================== DRY RUN =====================
SELECT 'Device_Teams to delete' AS what, rule_, COUNT(*) AS rows_, COUNT(DISTINCT DeviceId) AS devices
FROM #doomed GROUP BY rule_;
SELECT 'Device_Teams to delete, total' AS what, COUNT(*) AS rows_, COUNT(DISTINCT DeviceId) AS devices FROM #doomed;
SELECT 'Device_RegistrationIds to delete' AS what, COUNT(*) AS rows_ FROM #doomedReg;
SELECT 'Device_Teams before' AS what, COUNT(*) AS rows_ FROM mobile.Device_Teams WHERE RegistrationID IS NOT NULL;

-- Devices that would be left with no Teams-pool row at all (expected: phones whose only rows were walk rows)
SELECT 'devices left with no Teams-pool row' AS what, COUNT(*) AS devices
FROM (SELECT DISTINCT DeviceId FROM #doomed) d
WHERE NOT EXISTS (SELECT 1 FROM mobile.Device_Teams k
                  WHERE k.DeviceId = d.DeviceId AND k.RegistrationID IS NOT NULL
                    AND k.Id NOT IN (SELECT Id FROM #doomed));

-- ===================== DELETE (run after reviewing the dry run) =====================
/*
BEGIN TRAN;

DELETE dt FROM mobile.Device_Teams dt JOIN #doomed d ON d.Id = dt.Id;
SELECT 'Device_Teams deleted' AS what, @@ROWCOUNT AS rows_;

DELETE dr FROM mobile.Device_RegistrationIds dr JOIN #doomedReg d ON d.Id = dr.Id;
SELECT 'Device_RegistrationIds deleted' AS what, @@ROWCOUNT AS rows_;

SELECT 'Device_Teams after' AS what, COUNT(*) AS rows_ FROM mobile.Device_Teams WHERE RegistrationID IS NOT NULL;

-- COMMIT TRAN;
-- ROLLBACK TRAN;
*/
