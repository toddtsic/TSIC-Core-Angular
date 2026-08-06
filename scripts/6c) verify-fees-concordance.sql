-- ============================================================
-- Concordance Test: fees.JobFees vs legacy fee resolution
--
-- Mimics legacy logic per Ann's rules, compares against seeded
-- fees.JobFees rows. Any mismatch = migration bug.
--
-- Rules:
--   Player-only (1,4,6): cascade Teams.PerRegistrantFee → Agegroups.RosterFee
--     Camp deposit outlier (type 4): RosterFee=Deposit, TeamFee-RosterFee=BalanceDue
--   Tournament (2): player fee = $0 UNLESS Teams.PerRegistrantFee > 0
--   League (3): player fee = Teams.PerRegistrantFee → Leagues.PlayerFeeOverride
--   Team fee (2,3): RosterFee=Deposit, TeamFee=BalanceDue when BOTH set;
--     one amount alone (either column) = single payment -> Deposit=NULL,
--     BalanceDue=the amount (6a §3 positional normalization)
--
-- Run AFTER seed-fees-from-legacy.sql
-- ============================================================

PRINT '============================================================';
PRINT 'TEST 1A: Camp deposit model (type 4, both RosterFee+TeamFee set)';
PRINT '  Legacy: Deposit=RosterFee, BalanceDue=TeamFee-RosterFee';
PRINT '============================================================';

SELECT
    j.JobPath,
    ag.AgegroupName,
    ag.RosterFee                AS [Legacy_RosterFee],
    ag.TeamFee                  AS [Legacy_TeamFee],
    jf.Deposit                  AS [New_Deposit],
    jf.BalanceDue               AS [New_BalanceDue],
    CASE
        WHEN ISNULL(ag.RosterFee, 0) = ISNULL(jf.Deposit, 0)
         AND ISNULL(ag.TeamFee - ag.RosterFee, 0) = ISNULL(jf.BalanceDue, 0)
        THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.AgegroupId = ag.AgegroupId
    AND jf.TeamId IS NULL
    AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'  -- Player
WHERE j.JobTypeId = 4
  AND j.Year IN ('2025', '2026')
  AND ag.RosterFee > 0 AND ag.TeamFee > 0
ORDER BY
    CASE WHEN ISNULL(ag.RosterFee, 0) != ISNULL(jf.Deposit, 0)
           OR ISNULL(ag.TeamFee - ag.RosterFee, 0) != ISNULL(jf.BalanceDue, 0)
    THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName;

PRINT '';
PRINT '============================================================';
PRINT 'TEST 1B: Player-only agegroup fees (types 1,4,6 — normal)';
PRINT '  Legacy: RosterFee → Player.BalanceDue (no deposit)';
PRINT '  Excludes camp deposit model';
PRINT '============================================================';

SELECT
    j.JobPath,
    ag.AgegroupName,
    ag.RosterFee                AS [Legacy_RosterFee],
    jf.BalanceDue               AS [New_BalanceDue],
    CASE
        WHEN ISNULL(ag.RosterFee, 0) = ISNULL(jf.BalanceDue, 0) THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.AgegroupId = ag.AgegroupId
    AND jf.TeamId IS NULL
    AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
WHERE j.JobTypeId IN (1, 4, 6)
  AND j.Year IN ('2025', '2026')
  AND ag.RosterFee > 0
  AND NOT (j.JobTypeId = 4 AND ISNULL(ag.TeamFee, 0) > 0)
ORDER BY
    CASE WHEN ISNULL(ag.RosterFee, 0) != ISNULL(jf.BalanceDue, 0) THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName;

PRINT '';
PRINT '============================================================';
PRINT 'TEST 2: Player-only team overrides (types 1,4,6)';
PRINT '  Legacy: Teams.PerRegistrantFee where != Agegroups.RosterFee';
PRINT '============================================================';

SELECT
    j.JobPath,
    ag.AgegroupName,
    t.TeamName,
    t.PerRegistrantFee          AS [Legacy_PerRegistrantFee],
    ag.RosterFee                AS [Agegroup_RosterFee],
    jf.BalanceDue               AS [New_BalanceDue],
    CASE
        WHEN ISNULL(t.PerRegistrantFee, 0) = ISNULL(jf.BalanceDue, 0) THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]
FROM Leagues.teams t
JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j ON t.JobId = j.JobId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.AgegroupId = t.AgegroupId
    AND jf.TeamId = t.TeamId
    AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
WHERE j.JobTypeId IN (1, 4, 6)
  AND j.Year IN ('2025', '2026')
  AND t.PerRegistrantFee > 0
  AND t.PerRegistrantFee != ISNULL(ag.RosterFee, 0)
ORDER BY
    CASE WHEN ISNULL(t.PerRegistrantFee, 0) != ISNULL(jf.BalanceDue, 0) THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName, t.TeamName;

PRINT '';
PRINT '============================================================';
PRINT 'TEST 3: Team fees — ClubRep (types 2, 3)';
PRINT '  Both legacy amounts set: Deposit=RosterFee, BalanceDue=TeamFee';
PRINT '  One amount alone (either column): single payment -> Deposit=NULL,';
PRINT '  BalanceDue=the amount (6a s.3 positional normalization)';
PRINT '============================================================';

SELECT
    j.JobPath,
    ag.AgegroupName,
    ag.RosterFee                AS [Legacy_RosterFee],
    ag.TeamFee                  AS [Legacy_TeamFee],
    jf.Deposit                  AS [New_Deposit],
    jf.BalanceDue               AS [New_BalanceDue],
    CASE
        WHEN ISNULL(jf.Deposit, 0) =
             CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ISNULL(ag.RosterFee, 0) ELSE 0 END
         AND ISNULL(jf.BalanceDue, 0) =
             CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ag.TeamFee ELSE ISNULL(ag.RosterFee, 0) END
        THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.AgegroupId = ag.AgegroupId
    AND jf.TeamId IS NULL
    AND jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'  -- ClubRep
WHERE j.JobTypeId IN (2, 3)
  AND j.Year IN ('2025', '2026')
  AND (ag.RosterFee > 0 OR ag.TeamFee > 0 OR jf.JobFeeId IS NOT NULL)
ORDER BY
    CASE WHEN ISNULL(jf.Deposit, 0) !=
              CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ISNULL(ag.RosterFee, 0) ELSE 0 END
           OR ISNULL(jf.BalanceDue, 0) !=
              CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ag.TeamFee ELSE ISNULL(ag.RosterFee, 0) END
    THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName;

PRINT '';
PRINT '============================================================';
PRINT 'TEST 4: Tournament player fees (type 2)';
PRINT '  Legacy: $0 unless Teams.PerRegistrantFee > 0';
PRINT '============================================================';

SELECT
    j.JobPath,
    ag.AgegroupName,
    t.TeamName,
    t.PerRegistrantFee          AS [Legacy_PerRegistrantFee],
    jf.BalanceDue               AS [New_BalanceDue],
    CASE
        WHEN ISNULL(t.PerRegistrantFee, 0) = ISNULL(jf.BalanceDue, 0) THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]
FROM Leagues.teams t
JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j ON t.JobId = j.JobId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.AgegroupId = t.AgegroupId
    AND jf.TeamId = t.TeamId
    AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
WHERE j.JobTypeId = 2
  AND j.Year IN ('2025', '2026')
  AND (t.PerRegistrantFee > 0 OR jf.JobFeeId IS NOT NULL)
ORDER BY
    CASE WHEN ISNULL(t.PerRegistrantFee, 0) != ISNULL(jf.BalanceDue, 0) THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName, t.TeamName;

PRINT '';
PRINT '============================================================';
PRINT 'TEST 5: League player fees (type 3)';
PRINT '  Legacy: Teams.PerRegistrantFee → Leagues.PlayerFeeOverride';
PRINT '============================================================';

-- 5A: Team-level overrides
SELECT
    'TEAM' AS [Scope],
    j.JobPath,
    ag.AgegroupName,
    t.TeamName,
    t.PerRegistrantFee          AS [Legacy_PerRegistrantFee],
    jf.BalanceDue               AS [New_BalanceDue],
    CASE
        WHEN ISNULL(t.PerRegistrantFee, 0) = ISNULL(jf.BalanceDue, 0) THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]
FROM Leagues.teams t
JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Jobs.Jobs j ON t.JobId = j.JobId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.AgegroupId = t.AgegroupId
    AND jf.TeamId = t.TeamId
    AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
WHERE j.JobTypeId = 3
  AND j.Year IN ('2025', '2026')
  AND (t.PerRegistrantFee > 0 OR jf.JobFeeId IS NOT NULL)
ORDER BY
    CASE WHEN ISNULL(t.PerRegistrantFee, 0) != ISNULL(jf.BalanceDue, 0) THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName, t.TeamName;

-- 5B: Job-level default from Leagues.PlayerFeeOverride
SELECT
    'JOB' AS [Scope],
    j.JobPath,
    l.PlayerFeeOverride         AS [Legacy_PlayerFeeOverride],
    jf.BalanceDue               AS [New_BalanceDue],
    CASE
        WHEN ISNULL(l.PlayerFeeOverride, 0) = ISNULL(jf.BalanceDue, 0) THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]
FROM Leagues.leagues l
JOIN Jobs.Job_Leagues jl ON l.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.AgegroupId IS NULL
    AND jf.TeamId IS NULL
    AND jf.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
WHERE j.JobTypeId = 3
  AND j.Year IN ('2025', '2026')
  AND (l.PlayerFeeOverride > 0 OR jf.JobFeeId IS NOT NULL)
ORDER BY
    CASE WHEN ISNULL(l.PlayerFeeOverride, 0) != ISNULL(jf.BalanceDue, 0) THEN 0 ELSE 1 END,
    j.JobPath;

PRINT '';
PRINT '============================================================';
PRINT 'TEST 6: Full cascade — legacy effective player fee per active team';
PRINT '  Player-only (1,4,6): Teams.PerRegistrantFee ?? Agegroups.RosterFee';
PRINT '  Tournament (2): Teams.PerRegistrantFee ?? $0';
PRINT '  League (3): Teams.PerRegistrantFee ?? Leagues.PlayerFeeOverride ?? $0';
PRINT '  Compares against fees.JobFees cascade: Team → Agegroup → Job';
PRINT '============================================================';

SELECT
    j.JobPath,
    jt.JobTypeName,
    ag.AgegroupName,
    t.TeamName,

    -- Legacy effective player fee
    CASE
        -- Camp deposit: player pays TeamFee total (not just RosterFee)
        WHEN j.JobTypeId = 4 AND ISNULL(ag.TeamFee, 0) > 0 AND ISNULL(ag.RosterFee, 0) > 0 THEN
            ag.TeamFee
        -- Player-only normal: cascade team → agegroup
        WHEN j.JobTypeId IN (1, 4, 6) THEN
            COALESCE(NULLIF(t.PerRegistrantFee, 0), ag.RosterFee, 0)
        -- Tournament: team-level only, otherwise $0
        WHEN j.JobTypeId = 2 THEN
            ISNULL(NULLIF(t.PerRegistrantFee, 0), 0)
        -- League: team → league override
        WHEN j.JobTypeId = 3 THEN
            COALESCE(NULLIF(t.PerRegistrantFee, 0), NULLIF(l.PlayerFeeOverride, 0), 0)
        ELSE 0
    END AS [Legacy_EffectivePlayerFee],

    -- New resolved player fee (cascade: team → agegroup → job)
    -- For camp deposit model, total = Deposit + BalanceDue
    CASE
        WHEN ISNULL(jf_team.Deposit, jf_ag.Deposit) IS NOT NULL THEN
            ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
          + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        ELSE
            ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
    END AS [New_ResolvedPlayerFee],

    -- Match?
    CASE
        WHEN
            CASE
                WHEN j.JobTypeId = 4 AND ISNULL(ag.TeamFee, 0) > 0 AND ISNULL(ag.RosterFee, 0) > 0 THEN
                    ag.TeamFee
                WHEN j.JobTypeId IN (1, 4, 6) THEN
                    COALESCE(NULLIF(t.PerRegistrantFee, 0), ag.RosterFee, 0)
                WHEN j.JobTypeId = 2 THEN
                    ISNULL(NULLIF(t.PerRegistrantFee, 0), 0)
                WHEN j.JobTypeId = 3 THEN
                    COALESCE(NULLIF(t.PerRegistrantFee, 0), NULLIF(l.PlayerFeeOverride, 0), 0)
                ELSE 0
            END
            =
            CASE
                WHEN ISNULL(jf_team.Deposit, jf_ag.Deposit) IS NOT NULL THEN
                    ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
                  + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
                ELSE
                    ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
            END
        THEN 'MATCH'
        ELSE '*** MISMATCH ***'
    END AS [Status]

FROM Leagues.teams t
JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Leagues.leagues l ON ag.LeagueId = l.LeagueId
JOIN Jobs.Jobs j ON t.JobId = j.JobId
JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId

-- New fee: team-level Player row
LEFT JOIN fees.JobFees jf_team
    ON jf_team.JobId = j.JobId
    AND jf_team.AgegroupId = t.AgegroupId
    AND jf_team.TeamId = t.TeamId
    AND jf_team.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'

-- New fee: agegroup-level Player row
LEFT JOIN fees.JobFees jf_ag
    ON jf_ag.JobId = j.JobId
    AND jf_ag.AgegroupId = ag.AgegroupId
    AND jf_ag.TeamId IS NULL
    AND jf_ag.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'

-- New fee: job-level Player row (League.PlayerFeeOverride)
LEFT JOIN fees.JobFees jf_job
    ON jf_job.JobId = j.JobId
    AND jf_job.AgegroupId IS NULL
    AND jf_job.TeamId IS NULL
    AND jf_job.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'

WHERE j.JobTypeId IN (1, 2, 3, 4, 6)
  AND j.Year IN ('2025', '2026')
  AND t.Active = 1
ORDER BY
    CASE WHEN
        CASE
            WHEN j.JobTypeId = 4 AND ISNULL(ag.TeamFee, 0) > 0 AND ISNULL(ag.RosterFee, 0) > 0 THEN
                ag.TeamFee
            WHEN j.JobTypeId IN (1, 4, 6) THEN
                COALESCE(NULLIF(t.PerRegistrantFee, 0), ag.RosterFee, 0)
            WHEN j.JobTypeId = 2 THEN
                ISNULL(NULLIF(t.PerRegistrantFee, 0), 0)
            WHEN j.JobTypeId = 3 THEN
                COALESCE(NULLIF(t.PerRegistrantFee, 0), NULLIF(l.PlayerFeeOverride, 0), 0)
            ELSE 0
        END
        !=
        CASE
            WHEN ISNULL(jf_team.Deposit, jf_ag.Deposit) IS NOT NULL THEN
                ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
              + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
            ELSE
                ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        END
    THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName, t.TeamName;

PRINT '';
PRINT '============================================================';
PRINT 'TEST 7: ClubRep structural concordance (types 2, 3)';
PRINT '  Both legacy amounts set: Deposit=RosterFee, BalanceDue=TeamFee (agegroup level)';
PRINT '  One amount alone: single payment -> Deposit=NULL, BalanceDue=the amount';
PRINT '  Compares source agegroup columns against fees.JobFees ClubRep rows';
PRINT '============================================================';

SELECT
    j.JobPath,
    jt.JobTypeName,
    ag.AgegroupName,

    -- Legacy (positional source columns)
    ISNULL(ag.RosterFee, 0)     AS [Legacy_RosterFee],
    ISNULL(ag.TeamFee, 0)       AS [Legacy_TeamFee],

    -- Expected after 6a §3 normalization
    CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ISNULL(ag.RosterFee, 0) ELSE 0 END
                                AS [Expected_Deposit],
    CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ag.TeamFee ELSE ISNULL(ag.RosterFee, 0) END
                                AS [Expected_BalanceDue],

    -- New
    ISNULL(jf.Deposit, 0)       AS [New_Deposit],
    ISNULL(jf.BalanceDue, 0)    AS [New_BalanceDue],

    CASE
        WHEN ISNULL(jf.Deposit, 0) =
             CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ISNULL(ag.RosterFee, 0) ELSE 0 END
         AND ISNULL(jf.BalanceDue, 0) =
             CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ag.TeamFee ELSE ISNULL(ag.RosterFee, 0) END
        THEN 'MATCH'
        WHEN jf.JobFeeId IS NULL
         AND ISNULL(ag.RosterFee, 0) = 0
         AND ISNULL(ag.TeamFee, 0) = 0
        THEN 'MATCH (no fee)'
        ELSE '*** MISMATCH ***'
    END AS [Status]

FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.AgegroupId = ag.AgegroupId
    AND jf.TeamId IS NULL
    AND jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'  -- ClubRep

WHERE j.JobTypeId IN (2, 3)
  AND j.Year IN ('2025', '2026')
  AND (ag.RosterFee > 0 OR ag.TeamFee > 0 OR jf.JobFeeId IS NOT NULL)
ORDER BY
    CASE WHEN ISNULL(jf.Deposit, 0) !=
              CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ISNULL(ag.RosterFee, 0) ELSE 0 END
           OR ISNULL(jf.BalanceDue, 0) !=
              CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ag.TeamFee ELSE ISNULL(ag.RosterFee, 0) END
    THEN 0 ELSE 1 END,
    j.JobPath, ag.AgegroupName;

PRINT '';
PRINT '============================================================';
PRINT 'TEST 8: Payment phase concordance (types 2, 3)';
PRINT '  Legacy: Jobs.bTeamsFullPaymentRequired = 1 -> job in full-payment phase';
PRINT '  New: >=1 LEAGUE-scope ClubRep fees.JobFees row stamped bFullPaymentRequired = 1';
PRINT '  (6a s.8P; agegroups inherit — AG-scope stamps here would veto the league control)';
PRINT '  Known unstampable (INFO): blueridgebombers camp (type 1, flag inert),';
PRINT '  compass teamsignup demo (type 3, director-managed - no ClubRep rows)';
PRINT '============================================================';

-- 8A: every flagged job must carry the stamp (type 2 = MISMATCH if missing;
--     non-tournament = INFO, phase has no ClubRep flow to act on)
SELECT
    j.JobPath,
    j.JobTypeId,
    j.bTeamsFullPaymentRequired AS [Legacy_Flag],
    COUNT(jf.JobFeeId)          AS [Stamped_ClubRep_Rows],
    CASE
        WHEN COUNT(jf.JobFeeId) > 0 THEN 'MATCH'
        WHEN j.JobTypeId != 2 THEN 'INFO (no rows to stamp)'
        ELSE '*** MISMATCH ***'
    END AS [Status]
FROM Jobs.Jobs j
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'  -- ClubRep
    AND jf.bFullPaymentRequired = 1
    AND jf.LeagueId IS NOT NULL AND jf.AgegroupId IS NULL AND jf.TeamId IS NULL  -- league scope
WHERE j.bTeamsFullPaymentRequired = 1
  AND j.Year IN ('2025', '2026', '2027')
GROUP BY j.JobPath, j.JobTypeId, j.bTeamsFullPaymentRequired
ORDER BY
    CASE WHEN COUNT(jf.JobFeeId) = 0 AND j.JobTypeId = 2 THEN 0 ELSE 1 END,
    j.JobPath;

-- 8B: reverse direction — at seed time no UNflagged job may carry a stamp
--     (a hit here = bad join in 6a s.8P; post-seed admin edits via LADT are
--     legitimate, so this direction is only meaningful right after seeding)
SELECT
    j.JobPath,
    COUNT(jf.JobFeeId) AS [Stamped_Rows_On_Unflagged_Job],
    '*** MISMATCH ***' AS [Status]
FROM Jobs.Jobs j
JOIN fees.JobFees jf
    ON jf.JobId = j.JobId
    AND jf.bFullPaymentRequired = 1
WHERE ISNULL(j.bTeamsFullPaymentRequired, 0) = 0
GROUP BY j.JobPath
ORDER BY j.JobPath;

-- Final summary
PRINT '';
PRINT '============================================================';
PRINT 'MISMATCH COUNTS';
PRINT '============================================================';

SELECT 'Test 6 - Player Fee by Team' AS [Test],
    SUM(CASE WHEN
        CASE
            WHEN j.JobTypeId = 4 AND ISNULL(ag.TeamFee, 0) > 0 AND ISNULL(ag.RosterFee, 0) > 0 THEN
                ag.TeamFee
            WHEN j.JobTypeId IN (1, 4, 6) THEN
                COALESCE(NULLIF(t.PerRegistrantFee, 0), ag.RosterFee, 0)
            WHEN j.JobTypeId = 2 THEN
                ISNULL(NULLIF(t.PerRegistrantFee, 0), 0)
            WHEN j.JobTypeId = 3 THEN
                COALESCE(NULLIF(t.PerRegistrantFee, 0), NULLIF(l.PlayerFeeOverride, 0), 0)
            ELSE 0
        END
        !=
        CASE
            WHEN ISNULL(jf_team.Deposit, jf_ag.Deposit) IS NOT NULL THEN
                ISNULL(COALESCE(jf_team.Deposit, jf_ag.Deposit, jf_job.Deposit), 0)
              + ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
            ELSE
                ISNULL(COALESCE(jf_team.BalanceDue, jf_ag.BalanceDue, jf_job.BalanceDue), 0)
        END
    THEN 1 ELSE 0 END) AS [Mismatches],
    COUNT(*) AS [Total]
FROM Leagues.teams t
JOIN Leagues.agegroups ag ON t.AgegroupId = ag.AgegroupId
JOIN Leagues.leagues l ON ag.LeagueId = l.LeagueId
JOIN Jobs.Jobs j ON t.JobId = j.JobId
LEFT JOIN fees.JobFees jf_team
    ON jf_team.JobId = j.JobId AND jf_team.AgegroupId = t.AgegroupId
    AND jf_team.TeamId = t.TeamId AND jf_team.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
LEFT JOIN fees.JobFees jf_ag
    ON jf_ag.JobId = j.JobId AND jf_ag.AgegroupId = ag.AgegroupId
    AND jf_ag.TeamId IS NULL AND jf_ag.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
LEFT JOIN fees.JobFees jf_job
    ON jf_job.JobId = j.JobId AND jf_job.AgegroupId IS NULL
    AND jf_job.TeamId IS NULL AND jf_job.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
WHERE j.JobTypeId IN (1, 2, 3, 4, 6) AND j.Year IN ('2025', '2026') AND t.Active = 1

UNION ALL

SELECT 'Test 7 - ClubRep Fee by Agegroup' AS [Test],
    SUM(CASE
        WHEN ISNULL(jf.Deposit, 0) !=
             CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ISNULL(ag.RosterFee, 0) ELSE 0 END
          OR ISNULL(jf.BalanceDue, 0) !=
             CASE WHEN ISNULL(ag.TeamFee, 0) > 0 THEN ag.TeamFee ELSE ISNULL(ag.RosterFee, 0) END
        THEN 1 ELSE 0
    END) AS [Mismatches],
    COUNT(*) AS [Total]
FROM Leagues.agegroups ag
JOIN Jobs.Job_Leagues jl ON ag.LeagueId = jl.LeagueId
JOIN Jobs.Jobs j ON jl.JobId = j.JobId
LEFT JOIN fees.JobFees jf
    ON jf.JobId = j.JobId AND jf.AgegroupId = ag.AgegroupId
    AND jf.TeamId IS NULL AND jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'
WHERE j.JobTypeId IN (2, 3) AND j.Year IN ('2025', '2026')
  AND (ag.RosterFee > 0 OR ag.TeamFee > 0 OR jf.JobFeeId IS NOT NULL)

UNION ALL

SELECT 'Test 8 - Phase concordance' AS [Test],
    SUM(CASE WHEN stamped = 0 AND JobTypeId = 2 THEN 1 ELSE 0 END) AS [Mismatches],
    COUNT(*) AS [Total]
FROM (
    SELECT j.JobTypeId,
        (SELECT COUNT(*) FROM fees.JobFees jf
         WHERE jf.JobId = j.JobId
           AND jf.RoleId = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'
           AND jf.bFullPaymentRequired = 1
           AND jf.LeagueId IS NOT NULL AND jf.AgegroupId IS NULL AND jf.TeamId IS NULL) AS stamped
    FROM Jobs.Jobs j
    WHERE j.bTeamsFullPaymentRequired = 1
      AND j.Year IN ('2025', '2026', '2027')
) flagged;
GO
