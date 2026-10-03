-- Stress test post-run checks (SELECT only). Run in SSMS or via clubrep-reopen-stampede.mjs.
-- Target: LFTC Fall Showcase 2027 on the local dev DB.
SET NOCOUNT ON;
DECLARE @tgt uniqueidentifier = '86806e88-d482-409f-acff-9c0852fa3519';

-- 1. Capacity: every team in the age group counts (same count TeamPlacementService uses).
SELECT  a.agegroupName,
        a.maxTeams,
        COUNT(t.TeamId)                                                         AS teams,
        COUNT(t.TeamId) - a.maxTeams                                            AS overCap,
        SUM(CASE WHEN t.clubrep_registrationid IS NOT NULL THEN 1 ELSE 0 END)   AS clubRepTeams
FROM    Leagues.agegroups a
JOIN    Jobs.Job_Leagues jl ON jl.leagueID = a.leagueID AND jl.jobID = @tgt
LEFT JOIN Leagues.teams t   ON t.agegroupID = a.agegroupID AND t.jobID = @tgt
GROUP BY a.agegroupName, a.maxTeams
ORDER BY a.agegroupName;

-- 2. Duplicate WAITLIST age groups (find-then-insert under concurrency).
SELECT  a.agegroupName, COUNT(*) AS copies
FROM    Leagues.agegroups a
JOIN    Jobs.Job_Leagues jl ON jl.leagueID = a.leagueID AND jl.jobID = @tgt
GROUP BY a.agegroupName
HAVING  COUNT(*) > 1;

-- 3. Duplicate 'Unassigned' divisions per age group.
SELECT  a.agegroupName, COUNT(*) AS unassignedDivs
FROM    Leagues.divisions d
JOIN    Leagues.agegroups a ON a.agegroupID = d.agegroupID
JOIN    Jobs.Job_Leagues jl ON jl.leagueID = a.leagueID AND jl.jobID = @tgt
WHERE   d.divName = 'Unassigned'
GROUP BY a.agegroupName
HAVING  COUNT(*) > 1;

-- 4. Duplicate Club Rep registrations per user, and more than one rep per club.
SELECT  'dup clubrep reg per user' AS chk, u.UserName AS k, COUNT(*) AS n
FROM    Jobs.Registrations r
JOIN    dbo.AspNetRoles ro ON ro.Id = r.RoleId AND ro.Name = 'Club Rep'
JOIN    dbo.AspNetUsers u  ON u.Id = r.UserId
WHERE   r.jobID = @tgt
GROUP BY u.UserName HAVING COUNT(*) > 1
UNION ALL
SELECT  'reps per club', r.club_name, COUNT(DISTINCT r.RegistrationId)
FROM    Jobs.Registrations r
JOIN    dbo.AspNetRoles ro ON ro.Id = r.RoleId AND ro.Name = 'Club Rep'
WHERE   r.jobID = @tgt AND EXISTS (SELECT 1 FROM Leagues.teams t WHERE t.clubrep_registrationid = r.RegistrationId)
GROUP BY r.club_name HAVING COUNT(DISTINCT r.RegistrationId) > 1;

-- 5. Money totals: teams vs ledger.
SELECT  COUNT(*)                                             AS clubRepTeams,
        SUM(CASE WHEN t.paid_total > 0 THEN 1 ELSE 0 END)    AS teamsPaid,
        SUM(t.paid_total)                                    AS sumTeamPaid,
        SUM(t.owed_total)                                    AS sumTeamOwed,
        (SELECT COUNT(*)    FROM Jobs.Registration_Accounting ra JOIN Leagues.teams t2 ON t2.TeamId = ra.teamID WHERE t2.jobID = @tgt AND ra.active = 1) AS ledgerRows,
        (SELECT SUM(payamt) FROM Jobs.Registration_Accounting ra JOIN Leagues.teams t2 ON t2.TeamId = ra.teamID WHERE t2.jobID = @tgt AND ra.active = 1) AS sumLedgerPaid
FROM    Leagues.teams t
WHERE   t.jobID = @tgt AND t.clubrep_registrationid IS NOT NULL;

-- 6. Money exceptions: team paid_total disagrees with its ledger, more than one charge per team,
--    charge without an ADN transaction id, ADN transaction id used twice, ledger row on the wrong rep.
SELECT  'paid_total <> ledger' AS chk, t.teamName AS k, t.paid_total AS a, ISNULL(l.amt, 0) AS b
FROM    Leagues.teams t
OUTER APPLY (SELECT SUM(payamt) amt FROM Jobs.Registration_Accounting ra WHERE ra.teamID = t.TeamId AND ra.active = 1) l
WHERE   t.jobID = @tgt AND t.clubrep_registrationid IS NOT NULL AND t.paid_total <> ISNULL(l.amt, 0)
UNION ALL
SELECT  'charges per team > 1', t.teamName, COUNT(*), SUM(ra.payamt)
FROM    Jobs.Registration_Accounting ra JOIN Leagues.teams t ON t.TeamId = ra.teamID
WHERE   t.jobID = @tgt AND ra.active = 1
GROUP BY t.teamName, t.TeamId HAVING COUNT(*) > 1
UNION ALL
SELECT  'ledger row, no ADN txn', t.teamName, ra.payamt, NULL
FROM    Jobs.Registration_Accounting ra JOIN Leagues.teams t ON t.TeamId = ra.teamID
WHERE   t.jobID = @tgt AND ra.active = 1 AND ISNULL(ra.adnTransactionID, '') = ''
UNION ALL
SELECT  'ADN txn used twice', ra.adnTransactionID, COUNT(*), SUM(ra.payamt)
FROM    Jobs.Registration_Accounting ra JOIN Leagues.teams t ON t.TeamId = ra.teamID
WHERE   t.jobID = @tgt AND ra.active = 1 AND ISNULL(ra.adnTransactionID, '') <> ''
GROUP BY ra.adnTransactionID HAVING COUNT(*) > 1
UNION ALL
SELECT  'ledger reg <> team rep', t.teamName, ra.payamt, NULL
FROM    Jobs.Registration_Accounting ra JOIN Leagues.teams t ON t.TeamId = ra.teamID
WHERE   t.jobID = @tgt AND ra.active = 1 AND ra.RegistrationID <> t.clubrep_registrationid;
