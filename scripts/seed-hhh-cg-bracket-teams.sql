/* ============================================================================
   Build HHH "Committed Games Summer 2026" LADT: agegroups + divisions + teams
   ----------------------------------------------------------------------------
   Job d02bc5d1 / League 8cb27cde. Run as one batch (no GO). Re-runnable.

   STRUCTURE
     Platinum   -> div "Seed 1" -> 16 placeholders  P01..P16
     Gold       -> div "Seed 2" -> 16 placeholders  P01..P16
     Silver     -> div "Seed 3" -> 16 placeholders  P01..P16
     Bronze     -> div "Seed 4" -> 16 placeholders  P01..P16
     Round Robin -> 16 pool divs "Pool 01".."Pool 16" -> the 68 REAL teams
                   (4 per pool "Pool 01".."Pool 14", 6 per "Pool 15"/"Pool 16")

   Placeholders are dummy rows with stable teamIDs. Championship games are
   scheduled against them; "reseeding" after RR = renaming the scheduled
   placeholders to "{RR team name} ({source pool})" -- no re-wiring.

   RR pool fill = explicit team->pool assignment from source spreadsheet
   "UPDATED CG BRACKET INFO NEW.xlsx" (ranking no longer matters).

   NOTE: agegroups are UPSERTed, not deleted. A DELETE on Leagues.agegroups
   cannot compile on this DB (Msg 8624 -- the table's foreign-key graph blows
   up the optimizer, even for a single row; NOCHECK does not help). Divisions
   and teams delete cleanly, so those are stripped and rebuilt. Bracket
   agegroups keep their existing rows; Round-Robin is inserted if missing.
   team.year mirrors job.year. Fees all 0.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LeagueId    uniqueidentifier = '8CB27CDE-F0D7-4810-A0F6-93B9A4E781F1';
DECLARE @JobId       uniqueidentifier = 'D02BC5D1-0752-4005-890B-E4E2A2262140';
DECLARE @ClubRepReg  uniqueidentifier = '015578B6-3DCC-4FF4-8E17-1BC32CFFB5E9';
DECLARE @LebUserId   nvarchar(450)    = '71765055-647D-432E-AFB6-0F84218D0247';
DECLARE @Now         datetime         = GETDATE();
DECLARE @JobYear     nvarchar(50)     = (SELECT j.year FROM Jobs.Jobs j WHERE j.jobID = @JobId);

BEGIN TRAN;

/* ===========================================================================
   TEARDOWN  (FK child -> parent order, scoped to this league)
   agegroups are NOT deleted (see header) -- their divisions/teams are.
   =========================================================================== */
DECLARE @agIds TABLE (id uniqueidentifier PRIMARY KEY);
INSERT INTO @agIds SELECT agegroupID FROM Leagues.agegroups WHERE leagueID = @LeagueId;

DECLARE @dvIds TABLE (id uniqueidentifier PRIMARY KEY);
INSERT INTO @dvIds SELECT DivId FROM Leagues.divisions WHERE agegroupID IN (SELECT id FROM @agIds);

DECLARE @d_sched int, @d_seed int, @d_tsf int, @d_tsd int, @d_team int, @d_div int;

DELETE FROM Leagues.BracketSeeds                        -- child of schedule (gid FK) + divisions; delete first
 WHERE t1SeedDivId IN (SELECT id FROM @dvIds)
    OR t2SeedDivId IN (SELECT id FROM @dvIds)
    OR gid IN (SELECT gid FROM Leagues.schedule
               WHERE agegroupID IN (SELECT id FROM @agIds));   SET @d_seed = @@ROWCOUNT;

DELETE FROM Leagues.schedule
 WHERE agegroupID IN (SELECT id FROM @agIds);           SET @d_sched = @@ROWCOUNT;

DELETE FROM Leagues.Timeslots_LeagueSeason_Fields
 WHERE agegroupID IN (SELECT id FROM @agIds);           SET @d_tsf = @@ROWCOUNT;

DELETE FROM Leagues.Timeslots_LeagueSeason_Dates
 WHERE agegroupID IN (SELECT id FROM @agIds);           SET @d_tsd = @@ROWCOUNT;

DELETE FROM mobile.Device_Teams                        -- device-favorited teams
 WHERE TeamID IN (SELECT teamID FROM Leagues.teams WHERE jobID = @JobId);

DELETE FROM Leagues.teams
 WHERE jobID = @JobId;                                  SET @d_team = @@ROWCOUNT;

DELETE FROM Leagues.divisions
 WHERE agegroupID IN (SELECT id FROM @agIds);           SET @d_div = @@ROWCOUNT;

/* small helper: 1..16 */
DECLARE @N16 TABLE (n int PRIMARY KEY);
INSERT INTO @N16 (n) VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),(16);

/* ===========================================================================
   BUILD 1. Agegroups -- UPSERT (4 brackets + Round-Robin), matched by name
   =========================================================================== */
DECLARE @AgDef TABLE (
    Name    nvarchar(255) NOT NULL PRIMARY KEY,
    Color   nvarchar(50)  NOT NULL,
    SortAge tinyint       NOT NULL,
    IsRR    bit           NOT NULL
);
INSERT INTO @AgDef (Name, Color, SortAge, IsRR) VALUES
 ('Platinum'   ,'#B0C4DE',1,0),
 ('Gold'       ,'#FFD700',2,0),
 ('Silver'     ,'#C0C0C0',3,0),
 ('Bronze'     ,'#B8860B',4,0),
 ('Round Robin','#708090',5,1);

DECLARE @up_ag int, @in_ag int;

/* rename legacy "Round-Robin" -> "Round Robin" in place (agegroups can't be
   deleted here; without this the upsert would orphan the old-named row) */
UPDATE Leagues.agegroups SET agegroupName = 'Round Robin'
WHERE leagueID = @LeagueId AND agegroupName = 'Round-Robin';

UPDATE a
SET a.color = d.Color, a.gender = 'F', a.lebUserID = @LebUserId,
    a.maxTeams = 100, a.maxTeamsPerClub = 100, a.modified = @Now,
    a.season = 'Summer', a.sortAge = d.SortAge,
    a.teamFee = 0, a.rosterFee = 0, a.lateFee = 0, a.discountFee = 0,
    a.bAllowSelfRostering = 1, a.bChampionsByDivision = 0,
    a.bHideStandings = 0, a.bAllowApiRosterAccess = 0
FROM Leagues.agegroups a
JOIN @AgDef d ON d.Name = a.agegroupName
WHERE a.leagueID = @LeagueId;
SET @up_ag = @@ROWCOUNT;

INSERT INTO Leagues.agegroups (
    agegroupID, agegroupName, color, gender, leagueID, lebUserID,
    maxTeams, maxTeamsPerClub, modified, season, sortAge,
    teamFee, rosterFee, lateFee, discountFee,
    bAllowSelfRostering, bChampionsByDivision, bHideStandings, bAllowApiRosterAccess
)
SELECT
    NEWID(), d.Name, d.Color, 'F', @LeagueId, @LebUserId,
    100, 100, @Now, 'Summer', d.SortAge,
    0, 0, 0, 0,
    1, 0, 0, 0
FROM @AgDef d
WHERE NOT EXISTS (
    SELECT 1 FROM Leagues.agegroups a
    WHERE a.leagueID = @LeagueId AND a.agegroupName = d.Name
);
SET @in_ag = @@ROWCOUNT;

/* resolve the live agegroup ids (updated + inserted) for the rest of the build */
DECLARE @Ag TABLE (
    AgegroupId uniqueidentifier NOT NULL PRIMARY KEY,
    Name       nvarchar(255)    NOT NULL,
    SortAge    tinyint          NOT NULL,
    IsRR       bit              NOT NULL
);
INSERT INTO @Ag (AgegroupId, Name, SortAge, IsRR)
SELECT a.agegroupID, a.agegroupName, d.SortAge, d.IsRR
FROM Leagues.agegroups a
JOIN @AgDef d ON d.Name = a.agegroupName
WHERE a.leagueID = @LeagueId;

/* ===========================================================================
   BUILD 2. Divisions
     brackets    : one "Seed N" division each
     Round Robin : 16 pool divisions "Pool 01".."Pool 16"
   =========================================================================== */
DECLARE @Div TABLE (
    DivId      uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
    AgegroupId uniqueidentifier NOT NULL,
    DivName    nvarchar(255)    NOT NULL
);
INSERT INTO @Div (AgegroupId, DivName)                       -- bracket seed divisions
SELECT a.AgegroupId, CONCAT('Seed ', a.SortAge)
FROM @Ag a WHERE a.IsRR = 0;

INSERT INTO @Div (AgegroupId, DivName)                       -- RR pools "Pool 01".."Pool 16"
SELECT a.AgegroupId, CONCAT('Pool ', RIGHT('0' + CAST(n.n AS varchar(2)), 2))
FROM @Ag a CROSS JOIN @N16 n WHERE a.IsRR = 1;

INSERT INTO Leagues.divisions (divID, agegroupID, divName, lebUserId, modified, maxRoundNumberToShow)
SELECT d.DivId, d.AgegroupId, d.DivName, @LebUserId, @Now, NULL
FROM @Div d;

/* ===========================================================================
   BUILD 3a. Placeholder teams  P01..P16  in each bracket's "Seed N" division
   =========================================================================== */
INSERT INTO Leagues.teams (
    teamID, active, agegroupID, clubrep_registrationid, createdate, divID,
    gender, jobID, leagueID, lebUserID, modified, season,
    teamName, teamFullName, effectiveasofdate, expireondate,
    fee_base, fee_discount, fee_processing, fee_total,
    perRegistrantFee, owed_total, paid_total,
    bHideRoster, team_comments, year
)
SELECT
    NEWID(), 1, a.AgegroupId, @ClubRepReg, @Now, d.DivId,
    'F', @JobId, @LeagueId, @LebUserId, @Now, 'Summer',
    p.pName,
    CONCAT(a.Name, ':', p.pName),
    @Now, DATEADD(year, 2, @Now),
    0, 0, 0, 0,
    0, 0, 0,
    0, CAST(n.n AS nvarchar(10)), @JobYear
FROM @Ag a
JOIN @Div d ON d.AgegroupId = a.AgegroupId          -- brackets have exactly one division
CROSS JOIN @N16 n
CROSS APPLY (SELECT pName = 'P' + RIGHT('0' + CAST(n.n AS varchar(2)), 2)) p
WHERE a.IsRR = 0;

/* ===========================================================================
   BUILD 3b. The 68 REAL teams -> Round-Robin pools
   Explicit team->pool assignment from "UPDATED CG BRACKET INFO NEW.xlsx".
   Team names are verbatim from the sheet.
   =========================================================================== */
DECLARE @Teams TABLE (
    TeamName nvarchar(255) NOT NULL PRIMARY KEY,
    PoolName nvarchar(20)  NOT NULL
);
INSERT INTO @Teams (TeamName, PoolName) VALUES
 ('Boston College','Pool 1'),('USC','Pool 1'),('Columbia','Pool 1'),('W&M','Pool 1'),
 ('UNC','Pool 2'),('Oregon','Pool 2'),('Florida','Pool 2'),('Stony Brook','Pool 2'),
 ('Duke','Pool 3'),('Georgetown','Pool 3'),('JMU','Pool 3'),('Colorado','Pool 3'),
 ('Syracuse','Pool 4'),('Denver','Pool 4'),('Temple','Pool 4'),('ASU','Pool 4'),
 ('UVA','Pool 5'),('Villanova','Pool 5'),('Vandy','Pool 5'),('Cincinnati','Pool 5'),
 ('Virginia Tech','Pool 6'),('Army','Pool 6'),('South Florida','Pool 6'),('FSU','Pool 6'),
 ('Louisville','Pool 7'),('Navy','Pool 7'),('UMass','Pool 7'),('Bryant','Pool 7'),
 ('ND/Clemson','Pool 8'),('Lehigh','Pool 8'),('Richmond','Pool 8'),('Jacksonville','Pool 8'),
 ('Pitt','Pool 9'),('BU','Pool 9'),('SJU','Pool 9'),('OSU','Pool 9'),
 ('Cal','Pool 10'),('Colgate','Pool 10'),('GW','Pool 10'),('Niagara/Union','Pool 10'),
 ('Stanford','Pool 11'),('Holy Cross','Pool 11'),('VCU','Pool 11'),('Furman','Pool 11'),
 ('Northwestern','Pool 12'),('Loyola','Pool 12'),('Princeton','Pool 12'),('Davidson','Pool 12'),
 ('Maryland','Pool 13'),('Harvard','Pool 13'),('URI','Pool 13'),('Coastal Carolina','Pool 13'),
 ('Michigan','Pool 14'),('Yale','Pool 14'),('Delaware','Pool 14'),('UCONN','Pool 14'),
 ('PSU','Pool 15'),('Penn','Pool 15'),('Drexel','Pool 15'),('Hopkins','Pool 15'),('Rider/Ursinus','Pool 15'),('Monmouth','Pool 15'),
 ('Rutgers','Pool 16'),('Brown','Pool 16'),('Towson','Pool 16'),('Elon','Pool 16'),('Dartmouth','Pool 16'),('Howard','Pool 16');

/* normalize sheet's "Pool 9" -> padded "Pool 09" to match division names */
UPDATE @Teams SET PoolName = 'Pool ' + RIGHT('0' + CAST(CAST(REPLACE(PoolName, 'Pool ', '') AS int) AS varchar(2)), 2);

INSERT INTO Leagues.teams (
    teamID, active, agegroupID, clubrep_registrationid, createdate, divID,
    gender, jobID, leagueID, lebUserID, modified, season,
    teamName, teamFullName, effectiveasofdate, expireondate,
    fee_base, fee_discount, fee_processing, fee_total,
    perRegistrantFee, owed_total, paid_total,
    bHideRoster, team_comments, year
)
SELECT
    NEWID(), 1, a.AgegroupId, @ClubRepReg, @Now, d.DivId,
    'F', @JobId, @LeagueId, @LebUserId, @Now, 'Summer',
    t.TeamName,
    CONCAT(a.Name, ':', t.TeamName),
    @Now, DATEADD(year, 2, @Now),
    0, 0, 0, 0,
    0, 0, 0,
    0, NULL, @JobYear
FROM @Teams t
CROSS JOIN @Ag a
JOIN @Div d ON d.AgegroupId = a.AgegroupId AND d.DivName = t.PoolName
WHERE a.IsRR = 1;

/* ===========================================================================
   REPORT + COMMIT
   =========================================================================== */
SELECT
    @d_sched AS DelSchedule, @d_seed AS DelBracketSeeds,
    @d_tsf   AS DelTimeslotFields, @d_tsd AS DelTimeslotDates,
    @d_team  AS DelTeams, @d_div AS DelDivisions,
    @up_ag   AS AgegroupsUpdated, @in_ag AS AgegroupsInserted;

SELECT
    (SELECT COUNT(*) FROM Leagues.agegroups WHERE leagueID = @LeagueId)                       AS AgegroupsTotal,
    (SELECT COUNT(*) FROM Leagues.divisions WHERE agegroupID IN (SELECT AgegroupId FROM @Ag)) AS DivisionsBuilt,
    (SELECT COUNT(*) FROM Leagues.teams WHERE jobID = @JobId AND teamName LIKE 'P[0-9][0-9]') AS PlaceholderTeams,
    (SELECT COUNT(*) FROM Leagues.teams WHERE jobID = @JobId AND teamName NOT LIKE 'P[0-9][0-9]') AS RealTeams,
    (SELECT COUNT(*) FROM Leagues.teams WHERE jobID = @JobId)                                 AS TotalTeams;

/* per-pool RR headcount sanity (expect 4 x Pool 1..14, 6 x Pool 15..16) */
SELECT d.divName AS Pool, COUNT(t.teamID) AS Teams
FROM Leagues.divisions d
JOIN @Ag a ON a.AgegroupId = d.agegroupID AND a.IsRR = 1
LEFT JOIN Leagues.teams t ON t.divID = d.DivId
GROUP BY d.divName
ORDER BY CAST(REPLACE(d.divName, 'Pool ', '') AS int);

COMMIT TRAN;
