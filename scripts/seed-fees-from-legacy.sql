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
  AND j.Year IN ('2025', '2026')
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
  AND j.Year IN ('2025', '2026')
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
  AND j.Year IN ('2025', '2026')
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
  AND j.Year IN ('2025', '2026')
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
  AND j.Year IN ('2025', '2026')
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
  AND j.Year IN ('2025', '2026')
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
  AND j.Year IN ('2025', '2026')
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

-- 6. League player fee fallback — job level
INSERT INTO fees.JobFees (JobFeeId, JobId, RoleId, AgegroupId, TeamId, Deposit, BalanceDue, Modified)
SELECT
    NEWID(), j.JobId, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    NULL, NULL,
    NULL,
    l.PlayerFeeOverride,
    GETUTCDATE()
FROM Leagues.leagues l
JOIN Jobs.Job_Leagues jl ON l.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
WHERE j.JobTypeId = 3
  AND j.Year IN ('2025', '2026')
  AND l.PlayerFeeOverride IS NOT NULL AND l.PlayerFeeOverride > 0;
PRINT '6  League player fee job-level rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
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
      AND j.Year IN ('2025', '2026')
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
