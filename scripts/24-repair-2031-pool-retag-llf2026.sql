/*
    24 — Repair the 2031 Knowledge/Love pool retag
    Job: Live Love Lax:Girls Fall Festival 2026  (B0397924-419D-4181-B422-F3531D14E989)

    WHAT HAPPENED
    NXT LC:2031 Black and STEPS Elite NJ:2031 White were swapped between the Knowledge and
    Love pools after the schedule was built. The pool-transfer path then retagged the game's
    OWNING DIVISION to the mover's new division — but only on rows where the mover sat in the
    T1 slot. Three games ended up filed under a pool whose pairing matrix they did not come
    from, leaving Knowledge holding seven games and Love five:

        GID 291363  15:30 DE-Turf 08   Knowledge matrix, tagged Love       (NXT was T1)
        GID 291364  13:15 DE-Turf 08   Love matrix,      tagged Knowledge  (STEPS was T1)
        GID 291367  14:45 DE-Turf 09   Love matrix,      tagged Knowledge  (STEPS was T1)

    A game belongs to the division whose matrix built it. T1_No/T2_No on these rows are ranks
    in the ORIGINAL division, so re-seating a row while it is mis-tagged would resolve those
    ranks against the WRONG pool and drop a stranger into the game. That is why this script
    runs BEFORE any re-seat, not after.

    agegroupID/agegroupName need no repair: Knowledge and Love are both 2031, so the retag
    could not move them.

    THE RANKS ARE ALSO WRONG — PART 2 BELOW
    The swap put STEPS in Knowledge and NXT in Love, which is right. But it seated each of
    them one slot off: the swap should hand each mover the OTHER one's rank, so that the two
    teams trade schedules and nobody else in either pool is disturbed. NXT held Knowledge 3
    and STEPS held Love 2, so the intended landing is STEPS -> Knowledge 3 and NXT -> Love 2.
    Instead STEPS landed on Knowledge 2 and NXT on Love 3, which shoved NEMS and Monster off
    their own slots. Re-seating this as it stands would hand NEMS and Monster each other's
    opponents — teams that were never part of the swap.

        NOW                              INTENDED
        Knowledge  1 Gold Coast          Knowledge  1 Gold Coast
                   2 STEPS      <- wrong            2 NEMS
                   3 NEMS       <- wrong            3 STEPS
                   4 Team 91                        4 Team 91
        Love       1 Tradition           Love       1 Tradition
                   2 Monster    <- wrong            2 NXT
                   3 NXT        <- wrong            3 Monster
                   4 HHH Philly                     4 HHH Philly

    Part 2 restores the intended ranks. Gold Coast, Team 91, Tradition and HHH are untouched
    and keep every opponent, time and field they already have.

    ORDER OF OPERATIONS
      1. This script (both parts — the retag and the ranks).
      2. Re-seat both divisions — open the pairings grid for 2031 Knowledge and 2031 Love and
         save once in each. (Requires the ITeamSeatingService build; the old code re-seated
         nothing.) That changes exactly 6 games: every NXT appearance in Knowledge becomes
         STEPS, and every STEPS appearance in Love becomes NXT.
      3. Schedule QA → "Teams Not Matching Ranks" must read 0.

    NOTE ON THE TRIGGER: Leagues.teams carries Team_AfterEdit_UpdateTeamAssignments, which
    would re-seat T1_ID/T2_ID off a divRank change. It is DISABLED (is_disabled = 1), as all
    triggers on this database are, so it will not fire for Part 2 and did not fire for Taylor.
    It is named here only so the next reader does not have to rediscover that it is inert.

    SAFE TO RE-RUN: every UPDATE is guarded on the wrong-state value, so a second run changes
    0 rows. Read-only preview first; the writes are inside an explicit transaction that is
    left UNCOMMITTED — inspect the AFTER result, then COMMIT or ROLLBACK by hand.
*/

SET NOCOUNT ON;

DECLARE @JobId       uniqueidentifier = 'B0397924-419D-4181-B422-F3531D14E989';
DECLARE @KnowledgeId uniqueidentifier = '16D2AECA-5460-4226-B19E-E349F3650BFF';
DECLARE @LoveId      uniqueidentifier = '845758B4-A801-4625-858F-0F127F85FB67';
DECLARE @UserId      nvarchar(450)    = 'b3007894-ad36-4bec-ad9d-a4c4858a3a52';  -- the director who made the change

-- ─────────────────────────────────────────────────────────────────────────────
-- BEFORE
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== BEFORE: games per pool tag (expect Knowledge 7, Love 5) ===';
SELECT s.divName, COUNT(*) AS games
FROM Leagues.schedule s
WHERE s.jobID = @JobId AND s.divID IN (@KnowledgeId, @LoveId)
GROUP BY s.divName;

PRINT '=== BEFORE: the three rows to repair ===';
SELECT s.GID, CONVERT(varchar(16), s.G_Date, 120) AS gdate, s.fName,
       s.divName AS tagged_as, s.T1_No, s.T1_Name, s.T2_No, s.T2_Name
FROM Leagues.schedule s
WHERE s.GID IN (291363, 291364, 291367)
ORDER BY s.GID;

PRINT '=== BEFORE: ranks (expect Knowledge 2=STEPS 3=NEMS, Love 2=Monster 3=NXT) ===';
SELECT d.divName, t.divRank, r.club_name + ':' + t.teamName AS team
FROM Leagues.teams t
JOIN Leagues.divisions d ON d.divID = t.divID
LEFT JOIN Jobs.Registrations r ON r.RegistrationID = t.clubrep_registrationid
WHERE t.jobID = @JobId AND t.divID IN (@KnowledgeId, @LoveId)
ORDER BY d.divName, t.divRank;

-- ─────────────────────────────────────────────────────────────────────────────
-- PART 1 — REPAIR THE POOL TAGS
-- ─────────────────────────────────────────────────────────────────────────────
BEGIN TRANSACTION;

-- 291363 is a Knowledge game currently filed under Love.
UPDATE Leagues.schedule
SET    divID = @KnowledgeId,
       divName = 'Knowledge',
       modified = GETDATE(),
       lebUserId = @UserId
WHERE  GID = 291363
  AND  jobID = @JobId
  AND  divID = @LoveId;          -- guard: no-op if already repaired

PRINT CONCAT('291363 -> Knowledge: ', @@ROWCOUNT, ' row(s)');

-- 291364 and 291367 are Love games currently filed under Knowledge.
UPDATE Leagues.schedule
SET    divID = @LoveId,
       divName = 'Love',
       modified = GETDATE(),
       lebUserId = @UserId
WHERE  GID IN (291364, 291367)
  AND  jobID = @JobId
  AND  divID = @KnowledgeId;     -- guard: no-op if already repaired

PRINT CONCAT('291364/291367 -> Love: ', @@ROWCOUNT, ' row(s)');

-- ─────────────────────────────────────────────────────────────────────────────
-- PART 2 — RESTORE THE INTENDED RANKS
--
-- Keyed on teamID so a name edit cannot make these miss, and guarded on the
-- wrong-state rank so a second run changes 0 rows. Ranks 2 and 3 trade inside
-- each pool; ranks 1 and 4 are not touched. No unique index covers divRank
-- (only PK_Leagues.teams, on teamID), so the two-statement trade is safe.
-- ─────────────────────────────────────────────────────────────────────────────
DECLARE @Steps   uniqueidentifier = 'E4119601-7A05-4CE8-9E44-D4D7B6E8D438';  -- STEPS Elite NJ:2031 White
DECLARE @Nems    uniqueidentifier = 'FD57A3B1-DF62-47BF-9E7E-D2E7F0ED2535';  -- NEMS Lacrosse :2031 YELLOW
DECLARE @Nxt     uniqueidentifier = '9995C341-AFD0-4A6D-BBC5-59854334B9DC';  -- NXT LC:2031 Black
DECLARE @Monster uniqueidentifier = '7D532FF8-FC13-4FD4-9663-F7EB3E5B4DC4';  -- Monster:purple

-- Knowledge: STEPS 2 -> 3 (the slot NXT vacated), NEMS 3 -> 2 (back to its own).
UPDATE Leagues.teams SET divRank = 3, modified = GETDATE(), lebUserID = @UserId
WHERE  teamID = @Steps AND jobID = @JobId AND divID = @KnowledgeId AND divRank = 2;
PRINT CONCAT('STEPS -> Knowledge rank 3: ', @@ROWCOUNT, ' row(s)');

UPDATE Leagues.teams SET divRank = 2, modified = GETDATE(), lebUserID = @UserId
WHERE  teamID = @Nems AND jobID = @JobId AND divID = @KnowledgeId AND divRank = 3;
PRINT CONCAT('NEMS -> Knowledge rank 2: ', @@ROWCOUNT, ' row(s)');

-- Love: NXT 3 -> 2 (the slot STEPS vacated), Monster 2 -> 3 (back to its own).
UPDATE Leagues.teams SET divRank = 2, modified = GETDATE(), lebUserID = @UserId
WHERE  teamID = @Nxt AND jobID = @JobId AND divID = @LoveId AND divRank = 3;
PRINT CONCAT('NXT -> Love rank 2: ', @@ROWCOUNT, ' row(s)');

UPDATE Leagues.teams SET divRank = 3, modified = GETDATE(), lebUserID = @UserId
WHERE  teamID = @Monster AND jobID = @JobId AND divID = @LoveId AND divRank = 2;
PRINT CONCAT('Monster -> Love rank 3: ', @@ROWCOUNT, ' row(s)');

-- ─────────────────────────────────────────────────────────────────────────────
-- AFTER — verify BEFORE committing
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== AFTER: games per pool tag (both must read 6) ===';
SELECT s.divName, COUNT(*) AS games
FROM Leagues.schedule s
WHERE s.jobID = @JobId AND s.divID IN (@KnowledgeId, @LoveId)
GROUP BY s.divName;

PRINT '=== AFTER: every 2031 game whose tag disagrees with BOTH its teams (expect 0 rows) ===';
SELECT s.GID, s.divName AS tagged_as, s.T1_Name, d1.divName AS t1_div, s.T2_Name, d2.divName AS t2_div
FROM Leagues.schedule s
LEFT JOIN Leagues.teams t1 ON t1.teamID = s.T1_ID
LEFT JOIN Leagues.divisions d1 ON d1.divID = t1.divID
LEFT JOIN Leagues.teams t2 ON t2.teamID = s.T2_ID
LEFT JOIN Leagues.divisions d2 ON d2.divID = t2.divID
WHERE s.jobID = @JobId
  AND s.divID IN (@KnowledgeId, @LoveId)
  AND s.divID <> t1.divID
  AND s.divID <> t2.divID;

PRINT '=== AFTER: ranks (Knowledge 1 Gold / 2 NEMS / 3 STEPS / 4 Team 91, Love 1 Tradition / 2 NXT / 3 Monster / 4 HHH) ===';
SELECT d.divName, t.divRank, r.club_name + ':' + t.teamName AS team
FROM Leagues.teams t
JOIN Leagues.divisions d ON d.divID = t.divID
LEFT JOIN Jobs.Registrations r ON r.RegistrationID = t.clubrep_registrationid
WHERE t.jobID = @JobId AND t.divID IN (@KnowledgeId, @LoveId)
ORDER BY d.divName, t.divRank;

PRINT '=== AFTER: ranks must be 1-4, once each, in both pools (expect 0 rows) ===';
SELECT d.divName, t.divRank, COUNT(*) AS teams_at_this_rank
FROM Leagues.teams t
JOIN Leagues.divisions d ON d.divID = t.divID
WHERE t.jobID = @JobId AND t.divID IN (@KnowledgeId, @LoveId)
GROUP BY d.divName, t.divRank
HAVING COUNT(*) <> 1 OR t.divRank NOT BETWEEN 1 AND 4;

/*
    Seat mismatches (T*_No vs T*_ID) will STILL be reported at this point — that is expected.
    This script fixes which pool each game belongs to and which rank each team holds; it does
    not touch T1_ID/T1_Name. Those are cleared by step 2, the re-seat.

        COMMIT TRANSACTION;     -- both game counts read 6, and both "expect 0 rows" queries
                                -- returned 0 rows, and the AFTER ranks read as listed above
        ROLLBACK TRANSACTION;   -- anything else
*/
