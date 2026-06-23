-- ============================================================
-- Seed fees.JobFees from legacy Agegroup/Team fee columns
-- Idempotent: clears and repopulates on each run
-- Each section is its own batch (GO) to avoid variable conflicts
-- ============================================================

SET IMPLICIT_TRANSACTIONS OFF;
GO

DELETE FROM fees.FeeModifiers;
DELETE FROM fees.JobFees;
PRINT 'Cleared fees.JobFees + fees.FeeModifiers';
GO

-- 1A. Camp deposit model (type 4, both RosterFee AND TeamFee set)
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), j.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    ag.AgegroupId, NULL,
    ag.RosterFee,
    ag.TeamFee - ag.RosterFee,
    GETUTCDATE()
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
WHERE j.JobTypeId = 4
  AND j.Year IN ('2025', '2026', '2027')
  AND ag.RosterFee IS NOT NULL AND ag.RosterFee > 0
  AND ag.TeamFee IS NOT NULL AND ag.TeamFee > 0;
PRINT '1A Camp deposit rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 1B. Player-only agegroup fees (types 1, 4, 6 — normal)
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), j.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    ag.AgegroupId, NULL,
    NULL,
    ag.RosterFee,
    GETUTCDATE()
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
WHERE j.JobTypeId IN (1, 4, 6)
  AND j.Year IN ('2025', '2026', '2027')
  AND ag.RosterFee IS NOT NULL AND ag.RosterFee > 0
  AND NOT (j.JobTypeId = 4 AND ag.TeamFee IS NOT NULL AND ag.TeamFee > 0);
PRINT '1B Player-only agegroup rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 2. Player-only team overrides (types 1, 4, 6)
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), t.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    t.AgegroupId, t.TeamId,
    NULL,
    t.PerRegistrantFee,
    GETUTCDATE()
FROM Leagues.teams t
JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j ON t.JobId = j.JobId
WHERE j.JobTypeId IN (1, 4, 6)
  AND j.Year IN ('2025', '2026', '2027')
  AND t.PerRegistrantFee IS NOT NULL AND t.PerRegistrantFee > 0
  AND t.PerRegistrantFee != ISNULL(ag.RosterFee, 0)
  AND t.teamName NOT LIKE '%dropped%';   -- skip admin-dead teams (floating + CI; catches mid/trailing-space '(DROPPED 6/29/25) ')
PRINT '2  Player-only team override rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 3. Team fees — ClubRep (types 2, 3)
--    Director-managed leagues (type 3 with no club-rep flow) do NOT charge or even
--    present club-rep registration, so they must NOT get ClubRep-role rows. Mirror
--    §5B's director-managed test: a job is director-managed when no two of its teams
--    carry distinct clubrep_registrationid values (all NULL or all the same). Tournaments
--    (type 2) always get ClubRep rows. Without this guard, a League Scheduling job's
--    agegroup RosterFee/TeamFee leaked into a phantom ClubRep fee card.
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), j.JobId, '6A26171F-4D94-4928-94FA-2FEFD42C3C3E',
    ag.AgegroupId, NULL,
    ag.RosterFee,
    ag.TeamFee,
    GETUTCDATE()
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
WHERE j.JobTypeId IN (2, 3)
  AND j.Year IN ('2025', '2026', '2027')
  AND ((ag.RosterFee IS NOT NULL AND ag.RosterFee > 0)
    OR (ag.TeamFee IS NOT NULL AND ag.TeamFee > 0))
  -- Skip director-managed type-3 leagues (no club-rep flow); type-2 always seeds
  AND NOT (
      j.JobTypeId = 3
      AND NOT EXISTS (
          SELECT 1 FROM Leagues.teams t1
          JOIN Leagues.teams t2 ON t1.jobID = t2.jobID
              AND t1.TeamId != t2.TeamId
          WHERE t1.jobID = j.JobId
            AND ISNULL(t1.clubrep_registrationid, '00000000-0000-0000-0000-000000000000')
             != ISNULL(t2.clubrep_registrationid, '00000000-0000-0000-0000-000000000000')
      )
  );
PRINT '3  Team-only agegroup rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 4. Tournament player fees (type 2) — team level
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), t.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    t.AgegroupId, t.TeamId,
    NULL,
    t.PerRegistrantFee,
    GETUTCDATE()
FROM Leagues.teams t
JOIN Jobs.Jobs j ON t.JobId = j.JobId
WHERE j.JobTypeId = 2
  AND j.Year IN ('2025', '2026', '2027')
  AND t.PerRegistrantFee IS NOT NULL AND t.PerRegistrantFee > 0
  AND t.teamName NOT LIKE '%dropped%';   -- skip admin-dead teams (floating + CI; catches mid/trailing-space '(DROPPED 6/29/25) ')
PRINT '4  Tournament player fee team rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 5. League player fees (type 3) — team level
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), t.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    t.AgegroupId, t.TeamId,
    NULL,
    t.PerRegistrantFee,
    GETUTCDATE()
FROM Leagues.teams t
JOIN Jobs.Jobs j ON t.JobId = j.JobId
WHERE j.JobTypeId = 3
  AND j.Year IN ('2025', '2026', '2027')
  AND t.PerRegistrantFee IS NOT NULL AND t.PerRegistrantFee > 0
  AND t.teamName NOT LIKE '%dropped%';   -- skip admin-dead teams (floating + CI; catches mid/trailing-space '(DROPPED 6/29/25) ')
PRINT '5  League player fee team rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 5B. Director-managed league agegroup fees (type 3, no ClubRep flow)
--     Same cascade as player-only: RosterFee → Player.BalanceDue
--     Director-managed = all teams in the job share same clubrep_registrationid (or NULL)
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), j.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    ag.AgegroupId, NULL,
    NULL,
    ag.RosterFee,
    GETUTCDATE()
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
WHERE j.JobTypeId = 3
  AND j.Year IN ('2025', '2026', '2027')
  AND ag.RosterFee IS NOT NULL AND ag.RosterFee > 0
  -- Director-managed: no distinct ClubRepRegistrationId pairs exist
  AND NOT EXISTS (
      SELECT 1 FROM Leagues.teams t1
      JOIN Leagues.teams t2 ON t1.jobID = t2.jobID
          AND t1.TeamId != t2.TeamId
      WHERE t1.jobID = j.JobId
        AND ISNULL(t1.clubrep_registrationid, '00000000-0000-0000-0000-000000000000')
         != ISNULL(t2.clubrep_registrationid, '00000000-0000-0000-0000-000000000000')
  )
  -- Avoid duplicates if step 6 would also seed this job
  AND NOT EXISTS (
      SELECT 1 FROM fees.JobFees jf
      WHERE jf.JobId = j.JobId
        AND jf.AgegroupId = ag.AgegroupId
        AND jf.TeamId IS NULL
        AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
  );
PRINT '5B Director-managed league agegroup rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 6. League player fee fallback — LEAGUE level
--    PlayerFeeOverride lives per-league on Leagues.leagues, so it seeds as a
--    league-scoped row (LeagueId set, Agegroup/Team NULL) — the top tier of the
--    Deposit/BalanceDue cascade (Team -> Agegroup -> League). Previously this was
--    flattened to a job-level row (all keys NULL); job is no longer a fee scope
--    for Player/ClubRep.
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, LeagueId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), j.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    NULL, NULL, l.LeagueId,
    NULL,
    l.PlayerFeeOverride,
    GETUTCDATE()
FROM Leagues.leagues l
JOIN Jobs.Job_Leagues jl ON l.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
WHERE j.JobTypeId = 3
  AND j.Year IN ('2025', '2026', '2027')
  AND l.PlayerFeeOverride IS NOT NULL AND l.PlayerFeeOverride > 0;
PRINT '6  League player fee LEAGUE-level rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 7N. Agegroup-level $0 Player base — faithful encoding of legacy "free".
--     REPLACES the old per-team §7 (waitlist mirrors) + §8 (self-roster-free). Legacy's
--     player fee resolver bottoms out at 0 (IAccountingService.UpdatePlayerFeesRecord_
--     TeamAgBasis: team.PerRegistrantFee ?? ag.RosterFee ?? leagues.PlayerFeeOverride ?? 0;
--     tournament/type-2 skips the player charge entirely unless a league override exists).
--     The new resolver instead reads "no JobFees row at any tier" as FeeConfigured=false
--     -> "Fee not set" -> blocks the self-rostering player. So legacy "free" must be an
--     EXPLICIT $0 row. Write it ONCE per agegroup, not per team:
--       * Agegroup sits ABOVE team in the Team->Agegroup->League cascade, so one row
--         covers every team in the agegroup — including teams a club rep creates AFTER
--         seed-time (team creation writes no Player JobFees row), closing the hole the
--         per-team rows could not.
--       * Waitlist mirrors fold in via the 'WAITLIST - %' branches: one agegroup row
--         covers all mirror teams (the runtime mint TeamPlacementService.
--         EnsureWaitlistTeamFeeAsync still stamps per-team $0 for waitlist teams created
--         after seed-time — harmless overlap, both resolve $0; kept as belt-and-suspenders).
--
--     Two money guards (lifted from old §8, team tier -> agegroup tier) so this NEVER
--     zeroes a fee legacy charged:
--     (a) Type-correct free test — refuse the $0 base only where legacy carries a player
--         charge at THIS (agegroup) tier, so a missing positive row fails loud instead of
--         being masked.
--           * Camps (1,4,6): agegroup base = ag.RosterFee -> free only when RosterFee=0
--             (positive bases already written by §1A/§1B).
--           * Tournament (2): legacy has NO agegroup/league player base. The player charge
--             is purely team.PerRegistrantFee, seeded at TEAM tier by §4, which overrides
--             this $0 base (team > agegroup) — even when a league PlayerFeeOverride flags
--             the tournament as charging: the override is a FLAG, the amount is
--             team.PerRegistrantFee (IAccountingService.UpdatePlayerFeesRecord_TeamAgBasis
--             line 167). So every type-2 agegroup gets the $0 base; paid teams keep §4.
--           * League (3): RosterFee/TeamFee belong to the CLUB REP (§3), not the player, so
--             we cannot test them; gate on the PlayerFeeOverride flag instead (matches the
--             prior shipped §8). Waitlist always free.
--     (b) Orphan-only NOT EXISTS at the agegroup/league tier. An agegroup that already
--         resolves a positive Player base (§1A/§1B/§5B) or whose league carries an override
--         row (§6, type-3) is left alone; a paid team simply keeps its §2/§4/§5 team override
--         on top of this $0 base (team > agegroup in the cascade) — the mixed-agegroup case.
--         A genuine seeding gap (positive base missing) is NOT covered here, so it still
--         fails loud rather than registering someone free.
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), j.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    ag.AgegroupId, NULL,
    0, 0,
    GETUTCDATE()
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
LEFT JOIN Leagues.leagues l ON ag.LeagueId = l.LeagueId
WHERE j.JobTypeId IN (1, 2, 3, 4, 6)
  AND j.Year IN ('2025', '2026', '2027')
  AND ag.AgegroupName NOT LIKE 'Dropped%'
  -- registerable: agegroup enabled OR has an active self-rosterable team OR is a waitlist mirror
  AND ( ISNULL(ag.bAllowSelfRostering, 0) = 1
        OR EXISTS ( SELECT 1 FROM Leagues.teams t
                    WHERE t.AgegroupId = ag.AgegroupId
                      AND ISNULL(t.bAllowSelfRostering, 0) = 1
                      AND ISNULL(t.Active, 1) = 1
                      AND t.teamName NOT LIKE '%dropped%' )   -- a dead team alone doesn't make an agegroup registerable
        OR ag.AgegroupName LIKE 'WAITLIST - %' )
  -- (a) type-correct legacy-free test
  AND ( (j.JobTypeId IN (1, 4, 6) AND ISNULL(ag.RosterFee, 0) = 0)
        OR (j.JobTypeId = 2)   -- type-2 player charge is purely team.PerRegistrantFee (§4, team tier); $0 agegroup base is safe
        OR (j.JobTypeId = 3 AND ISNULL(l.PlayerFeeOverride, 0) = 0)
        OR ag.AgegroupName LIKE 'WAITLIST - %' )
  -- (b) orphan-only: never override a positive base (§1A/§1B/§5B) or league override (§6)
  AND NOT EXISTS (
      SELECT 1 FROM fees.JobFees jf
      WHERE jf.JobId = j.JobId
        AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
        AND jf.TeamId IS NULL
        AND ( jf.AgegroupId = ag.AgegroupId
              OR (jf.AgegroupId IS NULL AND jf.LeagueId = ag.LeagueId) ) );
PRINT '7N Agegroup-level $0 Player base rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- Verification
SELECT
    CASE
        WHEN jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A' THEN 'Player'
        WHEN jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E' THEN 'ClubRep'
        ELSE jf.RoleId
    END AS [Role],
    CASE
        WHEN jf.TeamId IS NOT NULL THEN 'Team'
        WHEN jf.AgegroupId IS NOT NULL THEN 'Agegroup'
        WHEN jf.LeagueId IS NOT NULL THEN 'League'
        ELSE 'Job'
    END AS [Scope],
    COUNT(*) AS [Rows],
    SUM(CASE WHEN jf.Deposit IS NOT NULL AND jf.Deposit > 0 THEN 1 ELSE 0 END) AS [WithDeposit],
    SUM(CASE WHEN jf.BalanceDue IS NOT NULL AND jf.BalanceDue > 0 THEN 1 ELSE 0 END) AS [WithBalanceDue]
FROM fees.JobFees jf
GROUP BY
    CASE
        WHEN jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A' THEN 'Player'
        WHEN jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E' THEN 'ClubRep'
        ELSE jf.RoleId
    END,
    CASE
        WHEN jf.TeamId IS NOT NULL THEN 'Team'
        WHEN jf.AgegroupId IS NOT NULL THEN 'Agegroup'
        WHEN jf.LeagueId IS NOT NULL THEN 'League'
        ELSE 'Job'
    END
ORDER BY 1, 2;
GO

-- Spot-check: most recent job per job type
;WITH RecentJobs AS (
    SELECT j.JobId, j.JobPath, j.Year, j.JobTypeId, jt.JobTypeName,
           ROW_NUMBER() OVER (PARTITION BY j.JobTypeId ORDER BY j.Year DESC, j.JobPath DESC) AS rn
    FROM Jobs.Jobs j
    JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId
    WHERE j.JobTypeId IN (1, 2, 3, 4, 6)
      AND j.Year IN ('2025', '2026', '2027')
      AND EXISTS (SELECT 1 FROM fees.JobFees jf WHERE jf.JobId = j.JobId)
)
SELECT
    rj.JobTypeName, rj.JobPath, rj.Year,
    CASE WHEN jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A' THEN 'Player'
         WHEN jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E' THEN 'ClubRep'
         ELSE jf.RoleId END AS [Role],
    CASE WHEN jf.TeamId IS NOT NULL THEN 'Team' ELSE 'Agegroup' END AS [Scope],
    ag.AgegroupName, t.TeamName, jf.Deposit, jf.BalanceDue
FROM RecentJobs rj
JOIN fees.JobFees jf ON jf.JobId = rj.JobId
LEFT JOIN Leagues.agegroups ag ON jf.AgegroupId = ag.AgegroupId
LEFT JOIN Leagues.teams t ON jf.TeamId = t.TeamId
WHERE rj.rn = 1
ORDER BY rj.JobTypeId,
    CASE WHEN jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A' THEN 'Player'
         WHEN jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E' THEN 'ClubRep'
         ELSE jf.RoleId END,
    CASE WHEN jf.TeamId IS NOT NULL THEN 'Team' ELSE 'Agegroup' END,
    ag.AgegroupName, t.TeamName;
GO

DECLARE @total INT;
SELECT @total = COUNT(*) FROM fees.JobFees;
PRINT 'Seed complete — ' + CAST(@total AS VARCHAR) + ' total rows';
GO
