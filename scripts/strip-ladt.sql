/* ============================================================================
   Strip a job's LADT.
   ----------------------------------------------------------------------------
   Give it a job id. Deletes, in FK child -> parent order:
     bracket seeds -> schedule -> timeslots -> teams -> divisions -> agegroups
   Agegroups/divisions are league-scoped; the league is resolved from the job
   (its teams + schedule). Assumes the league is 1:1 with the job.
   One batch, wrapped in a transaction (XACT_ABORT rolls back the whole thing
   on any error).
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @JobId uniqueidentifier = 'D02BC5D1-0752-4005-890B-E4E2A2262140';

/* league(s) for this job */
DECLARE @lg TABLE (id uniqueidentifier PRIMARY KEY);
INSERT INTO @lg
SELECT leagueID FROM Leagues.teams    WHERE jobID = @JobId
UNION
SELECT leagueID FROM Leagues.schedule WHERE jobID = @JobId;

/* agegroups + divisions under those leagues */
DECLARE @ag TABLE (id uniqueidentifier PRIMARY KEY);
INSERT INTO @ag SELECT agegroupID FROM Leagues.agegroups WHERE leagueID IN (SELECT id FROM @lg);

DECLARE @dv TABLE (id uniqueidentifier PRIMARY KEY);
INSERT INTO @dv SELECT DivId FROM Leagues.divisions WHERE agegroupID IN (SELECT id FROM @ag);

BEGIN TRAN;

/* bracket seeds: children of schedule (gid) and divisions (t1/t2 seed div) */
DELETE FROM Leagues.BracketSeeds
 WHERE t1SeedDivId IN (SELECT id FROM @dv)
    OR t2SeedDivId IN (SELECT id FROM @dv)
    OR gid IN (SELECT gid FROM Leagues.schedule WHERE jobID = @JobId);

/* games */
DELETE FROM Leagues.schedule WHERE jobID = @JobId;

/* scheduling timeslots (reference agegroups/divisions; block the ag/div delete) */
DELETE FROM Leagues.Timeslots_LeagueSeason_Fields WHERE agegroupID IN (SELECT id FROM @ag);
DELETE FROM Leagues.Timeslots_LeagueSeason_Dates  WHERE agegroupID IN (SELECT id FROM @ag);

/* mobile: device-favorited teams */
DELETE FROM mobile.Device_Teams
 WHERE TeamID IN (SELECT teamID FROM Leagues.teams WHERE jobID = @JobId);

/* teams */
DELETE FROM Leagues.teams WHERE jobID = @JobId;

/* divisions */
DELETE FROM Leagues.divisions WHERE agegroupID IN (SELECT id FROM @ag);

/* agegroups */
DELETE FROM Leagues.agegroups WHERE agegroupID IN (SELECT id FROM @ag);

COMMIT TRAN;
