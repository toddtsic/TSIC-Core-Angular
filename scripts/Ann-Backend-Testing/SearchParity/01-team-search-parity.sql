-- ============================================================================
-- Team Search Parity Audit — legacy vs new query semantics, real job data
--
-- Legacy oracle : TSIC-Unify SearchTeamsController.LookUpQueryResults
--                 (reference/TSIC-Unify-2024/.../SearchTeamsController.cs:586)
-- New side      : TeamRepository.SearchTeamsAsync
--                 (src/backend/TSIC.Infrastructure/Repositories/TeamRepository.cs:1015)
--
-- Both are hand-translated LINQ->SQL. Any diff found here is investigated
-- against the C# before being called a bug.
--
-- Run:  sqlcmd -S .\SS2016 -d TSICV5 -E -W -s"|" -v JobId="<jobId>" -i 01-team-search-parity.sql
-- ============================================================================
SET NOCOUNT ON;
DECLARE @job uniqueidentifier = '$(JobId)';

-- ── LEGACY semantics ────────────────────────────────────────────────────────
-- Base: ALL teams for job (active + inactive). Navigations in projection
-- (ClubrepRegistration, User, Agegroup, Div) => LEFT JOINs.
-- PayStatus: owed>0 UNDER PAID, owed<0 OVER PAID, else PAID IN FULL.
IF OBJECT_ID('tempdb..#legacy') IS NOT NULL DROP TABLE #legacy;
SELECT
    t.teamID,
    ClubName     = r.club_name,
    AgegroupName = ag.agegroupName,
    DivName      = d.divName,
    TeamName     = t.teamName,
    Lop          = t.level_of_play,
    IsActive     = CAST(ISNULL(t.active, 0) AS bit),
    AmtPaid      = t.paid_total,
    AmtDue       = t.owed_total,
    PayStatus    = CASE WHEN t.owed_total > 0 THEN 'UNDER PAID'
                        WHEN t.owed_total < 0 THEN 'OVER PAID'
                        ELSE 'PAID IN FULL' END,
    ClubrepName  = CASE WHEN t.clubrep_registrationid IS NOT NULL
                        THEN u.LastName + ', ' + u.FirstName END,
    ClubrepEmail = u.Email,
    ClubrepCell  = u.cellphone,
    TeamComment  = t.team_comments,
    CreateDate   = t.createdate,
    t.agegroupID, t.divID, t.leagueID
INTO #legacy
FROM Leagues.teams t
LEFT JOIN Jobs.Registrations r ON r.RegistrationID = t.clubrep_registrationid
LEFT JOIN dbo.AspNetUsers u    ON u.Id = r.UserId
LEFT JOIN Leagues.agegroups ag ON ag.agegroupID = t.agegroupID
LEFT JOIN Leagues.divisions d  ON d.divID = t.divID
WHERE t.jobID = @job;

-- ── NEW semantics ───────────────────────────────────────────────────────────
-- Base: Teams INNER JOIN Agegroups (explicit .Join), then LEFT (GroupJoin/
-- DefaultIfEmpty) Divisions, Registrations, AspNetUsers.
-- Display coalesces: Active??false, Paid/Owed??0, ClubRepName from user row.
IF OBJECT_ID('tempdb..#new') IS NOT NULL DROP TABLE #new;
SELECT
    t.teamID,
    ClubName     = r.club_name,
    AgegroupName = ISNULL(ag.agegroupName, ''),
    DivName      = d.divName,
    TeamName     = ISNULL(t.teamName, ''),
    Lop          = t.level_of_play,
    IsActive     = CAST(ISNULL(t.active, 0) AS bit),
    AmtPaid      = ISNULL(t.paid_total, 0),
    AmtDue       = ISNULL(t.owed_total, 0),
    PayStatus    = CASE WHEN t.owed_total > 0 THEN 'UNDER PAID'
                        WHEN t.owed_total < 0 THEN 'OVER PAID'
                        ELSE 'PAID IN FULL' END,
    ClubrepName  = CASE WHEN u.Id IS NOT NULL
                        THEN u.LastName + ', ' + u.FirstName END,
    ClubrepEmail = u.Email,
    ClubrepCell  = u.cellphone,
    TeamComment  = t.team_comments,
    CreateDate   = t.createdate,
    t.agegroupID, t.divID, t.leagueID
INTO #new
FROM Leagues.teams t
INNER JOIN Leagues.agegroups ag ON ag.agegroupID = t.agegroupID
LEFT JOIN Leagues.divisions d   ON d.divID = t.divID
LEFT JOIN Jobs.Registrations r  ON r.RegistrationID = t.clubrep_registrationid
LEFT JOIN dbo.AspNetUsers u     ON u.Id = r.UserId
WHERE t.jobID = @job;

PRINT '=== [T1] MEMBERSHIP: row counts ===';
SELECT LegacyRows = (SELECT COUNT(*) FROM #legacy),
       NewRows    = (SELECT COUNT(*) FROM #new);

PRINT '=== [T2] MEMBERSHIP: teams in LEGACY but not NEW (expect none; would mean dangling agegroupID) ===';
SELECT l.teamID, l.TeamName, l.agegroupID
FROM #legacy l WHERE NOT EXISTS (SELECT 1 FROM #new n WHERE n.teamID = l.teamID);

PRINT '=== [T3] MEMBERSHIP: teams in NEW but not LEGACY (expect none) ===';
SELECT n.teamID, n.TeamName
FROM #new n WHERE NOT EXISTS (SELECT 1 FROM #legacy l WHERE l.teamID = n.teamID);

PRINT '=== [T4] FIELD DIFFS on shared teams (expect none) ===';
SELECT l.teamID, l.TeamName,
    DiffCols =
        CASE WHEN ISNULL(l.ClubName,'~')      <> ISNULL(n.ClubName,'~')      THEN 'ClubName; '     ELSE '' END
      + CASE WHEN ISNULL(l.AgegroupName,'')   <> n.AgegroupName              THEN 'AgegroupName; ' ELSE '' END
      + CASE WHEN ISNULL(l.DivName,'~')       <> ISNULL(n.DivName,'~')       THEN 'DivName; '      ELSE '' END
      + CASE WHEN ISNULL(l.Lop,'~')           <> ISNULL(n.Lop,'~')           THEN 'Lop; '          ELSE '' END
      + CASE WHEN l.IsActive                  <> n.IsActive                  THEN 'Active; '       ELSE '' END
      + CASE WHEN ISNULL(l.AmtPaid,0)         <> n.AmtPaid                   THEN 'Paid; '         ELSE '' END
      + CASE WHEN ISNULL(l.AmtDue,0)          <> n.AmtDue                    THEN 'Owed; '         ELSE '' END
      + CASE WHEN l.PayStatus                 <> n.PayStatus                 THEN 'PayStatus; '    ELSE '' END
      + CASE WHEN ISNULL(l.ClubrepName,'~')   <> ISNULL(n.ClubrepName,'~')   THEN 'ClubrepName; '  ELSE '' END
      + CASE WHEN ISNULL(l.ClubrepEmail,'~')  <> ISNULL(n.ClubrepEmail,'~')  THEN 'ClubrepEmail; ' ELSE '' END
      + CASE WHEN ISNULL(l.ClubrepCell,'~')   <> ISNULL(n.ClubrepCell,'~')   THEN 'ClubrepCell; '  ELSE '' END
      + CASE WHEN ISNULL(l.TeamComment,'~')   <> ISNULL(n.TeamComment,'~')   THEN 'TeamComment; '  ELSE '' END
FROM #legacy l
JOIN #new n ON n.teamID = l.teamID
WHERE ISNULL(l.ClubName,'~')     <> ISNULL(n.ClubName,'~')
   OR ISNULL(l.AgegroupName,'')  <> n.AgegroupName
   OR ISNULL(l.DivName,'~')      <> ISNULL(n.DivName,'~')
   OR ISNULL(l.Lop,'~')          <> ISNULL(n.Lop,'~')
   OR l.IsActive                 <> n.IsActive
   OR ISNULL(l.AmtPaid,0)        <> n.AmtPaid
   OR ISNULL(l.AmtDue,0)         <> n.AmtDue
   OR l.PayStatus                <> n.PayStatus
   OR ISNULL(l.ClubrepName,'~')  <> ISNULL(n.ClubrepName,'~')
   OR ISNULL(l.ClubrepEmail,'~') <> ISNULL(n.ClubrepEmail,'~')
   OR ISNULL(l.ClubrepCell,'~')  <> ISNULL(n.ClubrepCell,'~')
   OR ISNULL(l.TeamComment,'~')  <> ISNULL(n.TeamComment,'~');

PRINT '=== [T5] FILTER SWEEP: Active status counts (filter = equality on coalesced Active) ===';
SELECT Side='legacy', IsActive, Cnt=COUNT(*) FROM #legacy GROUP BY IsActive
UNION ALL
SELECT 'new', IsActive, COUNT(*) FROM #new GROUP BY IsActive
ORDER BY IsActive, Side;

PRINT '=== [T6] FILTER SWEEP: PayStatus counts ===';
SELECT Side='legacy', PayStatus, Cnt=COUNT(*) FROM #legacy GROUP BY PayStatus
UNION ALL
SELECT 'new', PayStatus, COUNT(*) FROM #new GROUP BY PayStatus
ORDER BY PayStatus, Side;

PRINT '=== [T7] FILTER SWEEP: per-Club counts ===';
SELECT ClubName = ISNULL(c.ClubName,'(null)'),
       LegacyCnt = SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END),
       NewCnt    = SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
FROM (SELECT ClubName, Side='L' FROM #legacy
      UNION ALL SELECT ClubName, 'N' FROM #new) c
GROUP BY ISNULL(c.ClubName,'(null)')
HAVING SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END) <> SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
ORDER BY 1;
PRINT '(rows above = clubs whose counts differ; empty = parity)';

PRINT '=== [T8] FILTER SWEEP: per-LevelOfPlay counts (mismatches only) ===';
SELECT Lop = ISNULL(c.Lop,'(null)'),
       LegacyCnt = SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END),
       NewCnt    = SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
FROM (SELECT Lop, Side='L' FROM #legacy
      UNION ALL SELECT Lop, 'N' FROM #new) c
GROUP BY ISNULL(c.Lop,'(null)')
HAVING SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END) <> SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
ORDER BY 1;
PRINT '(empty = parity)';

PRINT '=== [T9] FILTER SWEEP: per-Agegroup counts (mismatches only) ===';
SELECT agegroupID,
       LegacyCnt = SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END),
       NewCnt    = SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
FROM (SELECT agegroupID, Side='L' FROM #legacy
      UNION ALL SELECT agegroupID, 'N' FROM #new) c
GROUP BY agegroupID
HAVING SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END) <> SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END);
PRINT '(empty = parity)';

PRINT '=== [T10] AGGREGATES: grid totals ===';
SELECT Side='legacy', TotalPaid=SUM(ISNULL(AmtPaid,0)), TotalOwed=SUM(ISNULL(AmtDue,0)) FROM #legacy
UNION ALL
SELECT 'new', SUM(AmtPaid), SUM(AmtDue) FROM #new;

PRINT '=== [T11] SORT ORDER: first 15 rows, both orders (legacy: ClubName,Agegroup,Div,TeamName / new: same with '''' coalesce) ===';
SELECT TOP 15 Ord=ROW_NUMBER() OVER (ORDER BY ClubName, AgegroupName, DivName, TeamName),
       teamID, ClubName, AgegroupName, DivName, TeamName
FROM #legacy ORDER BY ClubName, AgegroupName, DivName, TeamName;
SELECT TOP 15 Ord=ROW_NUMBER() OVER (ORDER BY ISNULL(ClubName,''), AgegroupName, ISNULL(DivName,''), TeamName),
       teamID, ClubName, AgegroupName, DivName, TeamName
FROM #new ORDER BY ISNULL(ClubName,''), AgegroupName, ISNULL(DivName,''), TeamName;

-- ── NEW-ONLY filters: internal-consistency counts (no legacy oracle) ────────
PRINT '=== [T12] NEW-ONLY: Waitlist / Scheduled counts ===';
SELECT Waitlisted        = SUM(CASE WHEN AgegroupName LIKE '%WAITLIST%' THEN 1 ELSE 0 END),
       NotWaitlisted     = SUM(CASE WHEN AgegroupName NOT LIKE '%WAITLIST%' THEN 1 ELSE 0 END)
FROM #new;
SELECT ScheduledTeams = COUNT(DISTINCT x.tid)
FROM (SELECT s.T1_ID AS tid FROM Leagues.schedule s WHERE s.jobID=@job
      UNION SELECT s.T2_ID FROM Leagues.schedule s WHERE s.jobID=@job) x
JOIN #new n ON n.teamID = x.tid;

PRINT '=== [T13] NEW-ONLY: AUTOPAY FAILED candidates (sub exists, owed>0, status not active OR NSF echeck row) ===';
SELECT t.teamID, t.teamName, t.owed_total, t.AdnSubscriptionStatus,
       HasNsf = CASE WHEN EXISTS (SELECT 1 FROM Jobs.Registration_Accounting ra
                                  JOIN reference.Accounting_PaymentMethods pm ON pm.paymentMethodID = ra.paymentMethodID
                                  WHERE ra.teamID = t.teamID AND ra.active = 1
                                    AND pm.paymentMethod LIKE '%eCheck%Failed%') THEN 1 ELSE 0 END
FROM Leagues.teams t
WHERE t.jobID=@job AND t.AdnSubscriptionId IS NOT NULL AND ISNULL(t.owed_total,0) > 0;

PRINT '=== [T14] NEW-ONLY: payment-method filter — active accounting rows by method for this job''s teams ===';
SELECT pm.paymentMethod, Teams=COUNT(DISTINCT ra.teamID), Rows_=COUNT(*)
FROM Jobs.Registration_Accounting ra
JOIN reference.Accounting_PaymentMethods pm ON pm.paymentMethodID = ra.paymentMethodID
JOIN Leagues.teams t ON t.teamID = ra.teamID
WHERE t.jobID=@job AND ra.active=1
GROUP BY pm.paymentMethod ORDER BY 1;
