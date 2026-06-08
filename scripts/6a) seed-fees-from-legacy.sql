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
  AND t.PerRegistrantFee != ISNULL(ag.RosterFee, 0);
PRINT '2  Player-only team override rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 3. Team fees — ClubRep (types 2, 3)
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
    OR (ag.TeamFee IS NOT NULL AND ag.TeamFee > 0));
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
  AND t.PerRegistrantFee IS NOT NULL AND t.PerRegistrantFee > 0;
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
  AND t.PerRegistrantFee IS NOT NULL AND t.PerRegistrantFee > 0;
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

-- 7. Waitlist mirror teams — explicit $0 CONFIGURED row (free, but FeeConfigured=true).
--    Sections 1-6 only seed POSITIVE legacy fees, so the free waitlist mirrors get no
--    row; and the DELETE at the top of this script wipes any $0 stamp the runtime mint
--    (TeamPlacementService.EnsureWaitlistTeamFeeAsync) wrote. Without a row the resolver
--    returns FeeConfigured=false and the player wizard fail-loud blocks registration
--    ("Fee not set") — no Registration row is written, so the player is invisible in
--    search and the confirmation renders raw !TOKENS. Stamp a team-scoped (0,0) Player
--    row for every team under a 'WAITLIST - %' agegroup so the mirror resolves as
--    genuinely-free-but-configured, matching the runtime mint. NOT EXISTS preserves any
--    row sections 1-6 already wrote (a mirror that somehow carries a positive legacy fee).
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), t.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    t.AgegroupId, t.TeamId,
    0, 0,
    GETUTCDATE()
FROM Leagues.teams t
JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j ON t.JobId = j.JobId
WHERE ag.AgegroupName LIKE 'WAITLIST - %'
  AND j.Year IN ('2025', '2026', '2027')
  AND NOT EXISTS (
      SELECT 1 FROM fees.JobFees jf
      WHERE jf.JobId = t.JobId
        AND jf.TeamId = t.TeamId
        AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
  );
PRINT '7  Waitlist mirror $0 Player rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
GO

-- 8. Self-rostering free players — explicit $0 CONFIGURED Player row.
--    Covers a genuinely-free CAC (Camps & Clinics) registration AND free
--    tournament/league self-rostering: the player self-rosters onto an available team
--    and is NOT charged (the club rep, not the player, pays any team/agegroup fee).
--    Same hole as the waitlist mirror (section 7) — a free team matches none of
--    sections 1-6 so it has no row, "no row" reads as FeeConfigured=false, the player
--    wizard fail-loud blocks registration ("Fee not set"), no Registration row is
--    written, the player is invisible in search and the confirmation renders raw
--    !TOKENS. Self-rosterable = team OR agegroup bAllowSelfRostering set (matches the
--    picker, TeamRepository.GetAvailableTeamsQueryResultsAsync).
--
--    Two money guards so this NEVER silently zeroes a real fee:
--    (a) Type-correct free test. In tournament/league (2,3) RosterFee/TeamFee belong to
--        the CLUB REP (section 3), NOT the player, so the player free-test there is
--        PerRegistrantFee (+ PlayerFeeOverride for league type 3 only). In camps
--        (1,4,6) the player fee is RosterFee + PerRegistrantFee. Only a team whose
--        legacy PLAYER fee is genuinely 0 is zeroed.
--    (b) Orphan-only NOT EXISTS. A team that already resolves a Player fee at any tier
--        (team / agegroup incl. 5B director-managed / league section 6) is left alone;
--        a team with a positive legacy player fee but no row stays orphan -> fail-loud,
--        so a coverage gap surfaces instead of registering someone free.
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), t.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    t.AgegroupId, t.TeamId,
    0, 0,
    GETUTCDATE()
FROM Leagues.teams t
JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
LEFT JOIN Leagues.leagues l ON ag.LeagueId = l.LeagueId
JOIN Jobs.Jobs j ON t.JobId = j.JobId
WHERE j.JobTypeId IN (1, 2, 3, 4, 6)
  AND j.Year IN ('2025', '2026', '2027')
  AND (ISNULL(t.bAllowSelfRostering, 0) = 1 OR ISNULL(ag.bAllowSelfRostering, 0) = 1)
  AND ag.AgegroupName NOT LIKE 'WAITLIST%'
  AND ag.AgegroupName NOT LIKE 'Dropped%'
  -- (a) genuinely free per legacy, type-correctly (don't test ClubRep RosterFee in 2,3)
  AND ISNULL(t.PerRegistrantFee, 0) = 0
  AND (   (j.JobTypeId IN (1, 4, 6) AND ISNULL(ag.RosterFee, 0) = 0)
       OR (j.JobTypeId = 2)
       OR (j.JobTypeId = 3 AND ISNULL(l.PlayerFeeOverride, 0) = 0) )
  -- (b) orphan-only: don't override any Player row sections 1-6 already wrote
  AND NOT EXISTS (
      SELECT 1 FROM fees.JobFees jf
      WHERE jf.JobId = t.JobId
        AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
        AND (   (jf.AgegroupId = t.AgegroupId AND jf.TeamId = t.TeamId)
             OR (jf.AgegroupId = t.AgegroupId AND jf.TeamId IS NULL)
             OR (jf.AgegroupId IS NULL AND jf.TeamId IS NULL AND jf.LeagueId = ag.LeagueId) ) );
PRINT '8  Self-rostering free $0 Player rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
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
