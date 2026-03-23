-- ============================================================
-- Concordance Test: fees.JobFees cascade vs Registrations.fee_base
--
-- For every active registration in 2025/2026, resolves the fee
-- via the fees.JobFees cascade (Team -> Agegroup -> Job) and
-- compares the result against the fee_base stamped on the
-- registration at time of signup.
--
-- NOTE: fee_base is a point-in-time snapshot. If fees were
-- changed after a player registered, mismatches are expected
-- and not necessarily bugs — they mean the fee schedule was
-- updated post-registration.
--
-- Run AFTER seed-fees-from-legacy.sql
-- ============================================================

-- ============================================================
-- TEST 1: Player registrations — fee_base vs fees cascade
-- ============================================================

PRINT '============================================================';
PRINT 'TEST 1: Player fee_base vs fees.JobFees cascade';
PRINT '  Cascade: Team -> Agegroup -> Job (most specific wins)';
PRINT '  fee_base should = resolved Deposit + resolved BalanceDue';
PRINT '============================================================';

SELECT
    j.JobPath,
    jt.JobTypeName,
    ag.AgegroupName,
    t.TeamName,
    u.FirstName + ' ' + u.LastName       AS [PlayerName],
    r.fee_base                            AS [Reg_FeeBase],

    -- Cascade components
    ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
                                          AS [Resolved_Deposit],
    ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
                                          AS [Resolved_BalanceDue],

    -- Resolved total = Deposit + BalanceDue
    ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
  + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
                                          AS [Resolved_FeeBase],

    -- Match?
    -- Camp deposit (type 4): fee_base = Deposit (deposit phase) or Deposit+BalanceDue (full payment)
    -- All others: fee_base = Deposit + BalanceDue
    CASE
        WHEN r.fee_base =
            ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
          + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        THEN 'MATCH'
        WHEN j.JobTypeId = 4
         AND r.fee_base = ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
        THEN 'MATCH (deposit phase)'
        ELSE '*** MISMATCH ***'
    END AS [Status],

    -- Source level that won the cascade
    CASE
        WHEN jf_team.JobFeeId IS NOT NULL THEN 'Team'
        WHEN jf_ag.JobFeeId IS NOT NULL   THEN 'Agegroup'
        WHEN jf_job.JobFeeId IS NOT NULL  THEN 'Job'
        ELSE 'NO FEE ROW'
    END AS [ResolvedFrom]

FROM Jobs.Registrations r
JOIN dbo.AspNetUsers u    ON r.UserId = u.Id
LEFT JOIN Leagues.teams t ON r.assigned_teamID = t.TeamId
LEFT JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j          ON t.jobID = j.JobId
JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId

-- fees cascade: team-level
LEFT JOIN fees.JobFees jf_team
    ON jf_team.JobId = t.jobID
    AND jf_team.AgegroupId = t.AgegroupId
    AND jf_team.TeamId = t.TeamId
    AND jf_team.RoleId = r.RoleId

-- fees cascade: agegroup-level
LEFT JOIN fees.JobFees jf_ag
    ON jf_ag.JobId = t.jobID
    AND jf_ag.AgegroupId = t.AgegroupId
    AND jf_ag.TeamId IS NULL
    AND jf_ag.RoleId = r.RoleId

-- fees cascade: job-level
LEFT JOIN fees.JobFees jf_job
    ON jf_job.JobId = t.jobID
    AND jf_job.AgegroupId IS NULL
    AND jf_job.TeamId IS NULL
    AND jf_job.RoleId = r.RoleId

WHERE j.Year IN ('2025', '2026')
    AND r.bActive = 1
    AND r.fee_base > 0
    AND r.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'  -- Player
ORDER BY
    -- Mismatches first
    CASE
        WHEN r.fee_base =
            ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
          + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        THEN 1
        WHEN j.JobTypeId = 4
         AND r.fee_base = ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
        THEN 1
        ELSE 0
    END,
    j.JobPath, ag.AgegroupName, t.TeamName, u.LastName;


-- ============================================================
-- TEST 1B: Camp mismatch diagnostic
--   Are mismatches explained by fee_base = BalanceDue only
--   (not Deposit + BalanceDue)?
-- ============================================================

PRINT '';
PRINT '============================================================';
PRINT 'TEST 1B: Camp mismatch diagnostic (type 4 only)';
PRINT '  Checking if fee_base matches BalanceDue alone';
PRINT '============================================================';

SELECT
    j.JobPath,
    ag.AgegroupName,
    t.TeamName,
    u.FirstName + ' ' + u.LastName       AS [PlayerName],
    r.fee_base                            AS [Reg_FeeBase],
    ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
                                          AS [Resolved_Deposit],
    ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
                                          AS [Resolved_BalanceDue],
    CASE
        WHEN r.fee_base = ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        THEN 'FeeBase=BalanceDue'
        WHEN r.fee_base = ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
        THEN 'FeeBase=Deposit'
        WHEN r.fee_base =
            ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
          + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        THEN 'FeeBase=Deposit+BalanceDue'
        ELSE 'NO MATCH'
    END AS [WhichMatches]

FROM Jobs.Registrations r
JOIN dbo.AspNetUsers u    ON r.UserId = u.Id
LEFT JOIN Leagues.teams t ON r.assigned_teamID = t.TeamId
LEFT JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j          ON t.jobID = j.JobId
JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId

LEFT JOIN fees.JobFees jf_team
    ON jf_team.JobId = t.jobID AND jf_team.AgegroupId = t.AgegroupId
    AND jf_team.TeamId = t.TeamId AND jf_team.RoleId = r.RoleId
LEFT JOIN fees.JobFees jf_ag
    ON jf_ag.JobId = t.jobID AND jf_ag.AgegroupId = t.AgegroupId
    AND jf_ag.TeamId IS NULL AND jf_ag.RoleId = r.RoleId
LEFT JOIN fees.JobFees jf_job
    ON jf_job.JobId = t.jobID AND jf_job.AgegroupId IS NULL
    AND jf_job.TeamId IS NULL AND jf_job.RoleId = r.RoleId

WHERE j.Year IN ('2025', '2026')
    AND j.JobTypeId = 4
    AND r.bActive = 1
    AND r.fee_base > 0
    AND r.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
    AND r.fee_base !=
        ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
      + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
ORDER BY j.JobPath, ag.AgegroupName, t.TeamName, u.LastName;


-- ============================================================
-- TEST 2: ClubRep registrations with fee_base > 0 (if any)
-- ============================================================

PRINT '';
PRINT '============================================================';
PRINT 'TEST 2: ClubRep fee_base vs fees.JobFees cascade (if any)';
PRINT '============================================================';

SELECT
    j.JobPath,
    jt.JobTypeName,
    ag.AgegroupName,
    t.TeamName,
    u.FirstName + ' ' + u.LastName       AS [ClubRepName],
    r.fee_base                            AS [Reg_FeeBase],
    ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
  + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
                                          AS [Resolved_FeeBase],
    CASE
        WHEN r.fee_base =
            ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
          + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]

FROM Jobs.Registrations r
JOIN dbo.AspNetUsers u    ON r.UserId = u.Id
LEFT JOIN Leagues.teams t ON r.assigned_teamID = t.TeamId
LEFT JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j          ON t.jobID = j.JobId
JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId

LEFT JOIN fees.JobFees jf_team
    ON jf_team.JobId = t.jobID
    AND jf_team.AgegroupId = t.AgegroupId
    AND jf_team.TeamId = t.TeamId
    AND jf_team.RoleId = r.RoleId
LEFT JOIN fees.JobFees jf_ag
    ON jf_ag.JobId = t.jobID
    AND jf_ag.AgegroupId = t.AgegroupId
    AND jf_ag.TeamId IS NULL
    AND jf_ag.RoleId = r.RoleId
LEFT JOIN fees.JobFees jf_job
    ON jf_job.JobId = t.jobID
    AND jf_job.AgegroupId IS NULL
    AND jf_job.TeamId IS NULL
    AND jf_job.RoleId = r.RoleId

WHERE j.Year IN ('2025', '2026')
    AND r.bActive = 1
    AND r.fee_base > 0
    AND r.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'  -- ClubRep
ORDER BY
    CASE WHEN r.fee_base !=
        ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
      + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
    THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName;


-- ============================================================
-- SUMMARY: Mismatch counts by role and job type
-- ============================================================

PRINT '';
PRINT '============================================================';
PRINT 'SUMMARY: Mismatch counts by JobType + Role';
PRINT '============================================================';

SELECT
    jt.JobTypeName,
    rol.Name                              AS [Role],
    SUM(CASE
        WHEN r.fee_base =
            ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
          + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        THEN 0
        WHEN j.JobTypeId = 4
         AND r.fee_base = ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
        THEN 0
        ELSE 1
    END)                                  AS [Mismatches],
    COUNT(*)                              AS [Total],
    CAST(
        100.0 * SUM(CASE
            WHEN r.fee_base =
                ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
              + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
            THEN 1
            WHEN j.JobTypeId = 4
             AND r.fee_base = ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
            THEN 1
            ELSE 0
        END) / NULLIF(COUNT(*), 0)
    AS decimal(5,1))                      AS [MatchPct]

FROM Jobs.Registrations r
JOIN dbo.AspNetRoles rol   ON r.RoleId = rol.Id
LEFT JOIN Leagues.teams t  ON r.assigned_teamID = t.TeamId
LEFT JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j           ON t.jobID = j.JobId
JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId

LEFT JOIN fees.JobFees jf_team
    ON jf_team.JobId = t.jobID
    AND jf_team.AgegroupId = t.AgegroupId
    AND jf_team.TeamId = t.TeamId
    AND jf_team.RoleId = r.RoleId
LEFT JOIN fees.JobFees jf_ag
    ON jf_ag.JobId = t.jobID
    AND jf_ag.AgegroupId = t.AgegroupId
    AND jf_ag.TeamId IS NULL
    AND jf_ag.RoleId = r.RoleId
LEFT JOIN fees.JobFees jf_job
    ON jf_job.JobId = t.jobID
    AND jf_job.AgegroupId IS NULL
    AND jf_job.TeamId IS NULL
    AND jf_job.RoleId = r.RoleId

WHERE j.Year IN ('2025', '2026')
    AND r.bActive = 1
    AND r.fee_base > 0
GROUP BY jt.JobTypeName, rol.Name
ORDER BY [Mismatches] DESC, jt.JobTypeName, rol.Name;


-- ============================================================
-- DIAGNOSTIC: Registrations with fee_base > 0 but NO fee row
-- ============================================================

PRINT '';
PRINT '============================================================';
PRINT 'DIAGNOSTIC: Registrations with fee_base > 0 but no fee row';
PRINT '  (no team, agegroup, or job-level fee exists)';
PRINT '============================================================';

SELECT
    j.JobPath,
    jt.JobTypeName,
    rol.Name                              AS [Role],
    ag.AgegroupName,
    t.TeamName,
    r.fee_base                            AS [Reg_FeeBase],
    COUNT(*) OVER (PARTITION BY j.JobPath) AS [RegsInJob]

FROM Jobs.Registrations r
JOIN dbo.AspNetRoles rol   ON r.RoleId = rol.Id
LEFT JOIN Leagues.teams t  ON r.assigned_teamID = t.TeamId
LEFT JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j           ON t.jobID = j.JobId
JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId

LEFT JOIN fees.JobFees jf_team
    ON jf_team.JobId = t.jobID
    AND jf_team.AgegroupId = t.AgegroupId
    AND jf_team.TeamId = t.TeamId
    AND jf_team.RoleId = r.RoleId
LEFT JOIN fees.JobFees jf_ag
    ON jf_ag.JobId = t.jobID
    AND jf_ag.AgegroupId = t.AgegroupId
    AND jf_ag.TeamId IS NULL
    AND jf_ag.RoleId = r.RoleId
LEFT JOIN fees.JobFees jf_job
    ON jf_job.JobId = t.jobID
    AND jf_job.AgegroupId IS NULL
    AND jf_job.TeamId IS NULL
    AND jf_job.RoleId = r.RoleId

WHERE j.Year IN ('2025', '2026')
    AND r.bActive = 1
    AND r.fee_base > 0
    AND jf_team.JobFeeId IS NULL
    AND jf_ag.JobFeeId IS NULL
    AND jf_job.JobFeeId IS NULL
ORDER BY j.JobPath, rol.Name, ag.AgegroupName;
GO
