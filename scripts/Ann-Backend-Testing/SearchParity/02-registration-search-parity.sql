-- ============================================================================
-- Registration Search Parity Audit — legacy vs new query semantics, real job
--
-- Legacy oracle : TSIC-Unify SearchController.LookUpQueryResults
--                 (reference/TSIC-Unify-2024/.../SearchController.cs:1365)
-- New side      : RegistrationRepository.SearchAsync / BuildFilteredQueryAsync
--                 (src/backend/TSIC.Infrastructure/Repositories/RegistrationRepository.cs:1276,1668)
--
-- Key structural difference to watch:
--   legacy base INNER JOINs AspNetRoles + AspNetUsers (drops regs with null/
--   dangling RoleId or UserId); new filters only on JobId and LEFT-joins
--   navigations in the projection.
--
-- Run:  sqlcmd -S .\SS2016 -d TSICV5 -E -W -s"|" -v JobId="<jobId>" -i 02-registration-search-parity.sql
-- ============================================================================
SET NOCOUNT ON;
DECLARE @job uniqueidentifier = '$(JobId)';

-- ── LEGACY semantics ────────────────────────────────────────────────────────
IF OBJECT_ID('tempdb..#legacy') IS NOT NULL DROP TABLE #legacy;
SELECT
    r.RegistrationID,
    r.RegistrationAI,
    FirstName  = u.FirstName,
    LastName   = u.LastName,
    Email      = u.Email,
    CellPhone  = u.cellphone,
    Dob        = u.dob,
    RoleName   = role_.Name,
    RoleId     = r.RoleId,
    IsActive   = r.bActive,                -- legacy passes bActive through (nullable)
    Position   = r.position,
    ClubName   = r.club_name,
    SchoolName = r.school_name,
    FeeTotal   = r.fee_total,
    AmtPaid    = r.paid_total,
    AmtDue     = r.owed_total,
    PayStatus  = CASE WHEN r.owed_total > 0 THEN 'UNDER PAID'
                      WHEN r.owed_total < 0 THEN 'OVER PAID'
                      ELSE 'PAID IN FULL' END,
    RegDate    = r.RegistrationTS,
    Assignment = r.assignment,             -- legacy shows the STORED string
    AssignedTeamId = r.assigned_teamID
INTO #legacy
FROM Jobs.Registrations r
JOIN dbo.AspNetRoles role_ ON role_.Id = r.RoleId    -- INNER
JOIN dbo.AspNetUsers u     ON u.Id = r.UserId        -- INNER
WHERE r.jobID = @job;

-- ── NEW semantics ───────────────────────────────────────────────────────────
-- Base: Registrations WHERE JobId only; User/Role/AssignedTeam are LEFT joins.
-- Assignment is COMPOSED live: [team's clubrep ClubName] [AgegroupName]
-- [TeamName], else fallback to registration's own ClubName.
IF OBJECT_ID('tempdb..#new') IS NOT NULL DROP TABLE #new;
SELECT
    r.RegistrationID,
    r.RegistrationAI,
    FirstName  = ISNULL(u.FirstName, ''),
    LastName   = ISNULL(u.LastName, ''),
    Email      = ISNULL(u.Email, ''),
    CellPhone  = u.cellphone,
    Dob        = u.dob,
    RoleName   = ISNULL(role_.Name, ''),
    RoleId     = r.RoleId,
    IsActive   = CAST(ISNULL(r.bActive, 0) AS bit),   -- new: Active = BActive ?? false
    Position   = r.position,
    ClubName   = r.club_name,
    SchoolName = r.school_name,
    FeeTotal   = r.fee_total,
    AmtPaid    = r.paid_total,
    AmtDue     = r.owed_total,
    PayStatus  = CASE WHEN r.owed_total > 0 THEN 'UNDER PAID'
                      WHEN r.owed_total < 0 THEN 'OVER PAID'
                      ELSE 'PAID IN FULL' END,
    RegDate    = r.RegistrationTS,
    Assignment = COALESCE(
                   NULLIF(LTRIM(RTRIM(
                       CONCAT(crr.club_name,
                              CASE WHEN ag.agegroupName IS NOT NULL THEN ' ' + ag.agegroupName ELSE '' END,
                              CASE WHEN at_.teamName    IS NOT NULL THEN ' ' + at_.teamName    ELSE '' END))), ''),
                   NULLIF(LTRIM(RTRIM(r.club_name)), '')),
    AssignedTeamId = r.assigned_teamID
INTO #new
FROM Jobs.Registrations r
LEFT JOIN dbo.AspNetRoles role_ ON role_.Id = r.RoleId
LEFT JOIN dbo.AspNetUsers u     ON u.Id = r.UserId
LEFT JOIN Leagues.teams at_     ON at_.teamID = r.assigned_teamID
LEFT JOIN Leagues.agegroups ag  ON ag.agegroupID = at_.agegroupID
LEFT JOIN Jobs.Registrations crr ON crr.RegistrationID = at_.clubrep_registrationid
WHERE r.jobID = @job;

PRINT '=== [R1] MEMBERSHIP: row counts ===';
SELECT LegacyRows = (SELECT COUNT(*) FROM #legacy),
       NewRows    = (SELECT COUNT(*) FROM #new);

PRINT '=== [R2] MEMBERSHIP: in NEW but not LEGACY (= null/dangling RoleId or UserId) ===';
SELECT n.RegistrationID, n.RegistrationAI, n.FirstName, n.LastName, n.RoleId
FROM #new n WHERE NOT EXISTS (SELECT 1 FROM #legacy l WHERE l.RegistrationID = n.RegistrationID);

PRINT '=== [R3] MEMBERSHIP: in LEGACY but not NEW (expect none) ===';
SELECT l.RegistrationID, l.FirstName, l.LastName
FROM #legacy l WHERE NOT EXISTS (SELECT 1 FROM #new n WHERE n.RegistrationID = l.RegistrationID);

PRINT '=== [R4] FIELD DIFFS on shared registrations, excluding Assignment (expect none) ===';
SELECT l.RegistrationID, l.LastName, l.FirstName,
    DiffCols =
        CASE WHEN ISNULL(l.FirstName,'')     <> n.FirstName                THEN 'FirstName; '  ELSE '' END
      + CASE WHEN ISNULL(l.LastName,'')      <> n.LastName                 THEN 'LastName; '   ELSE '' END
      + CASE WHEN ISNULL(l.Email,'')         <> n.Email                    THEN 'Email; '      ELSE '' END
      + CASE WHEN ISNULL(l.CellPhone,'~')    <> ISNULL(n.CellPhone,'~')    THEN 'Phone; '      ELSE '' END
      + CASE WHEN ISNULL(l.Dob,'19000101')   <> ISNULL(n.Dob,'19000101')   THEN 'Dob; '        ELSE '' END
      + CASE WHEN ISNULL(l.RoleName,'')      <> n.RoleName                 THEN 'RoleName; '   ELSE '' END
      + CASE WHEN CAST(ISNULL(l.IsActive,0) AS bit) <> n.IsActive          THEN 'Active; '     ELSE '' END
      + CASE WHEN ISNULL(l.Position,'~')     <> ISNULL(n.Position,'~')     THEN 'Position; '   ELSE '' END
      + CASE WHEN ISNULL(l.ClubName,'~')     <> ISNULL(n.ClubName,'~')     THEN 'ClubName; '   ELSE '' END
      + CASE WHEN ISNULL(l.SchoolName,'~')   <> ISNULL(n.SchoolName,'~')   THEN 'SchoolName; ' ELSE '' END
      + CASE WHEN ISNULL(l.FeeTotal,0)       <> ISNULL(n.FeeTotal,0)       THEN 'FeeTotal; '   ELSE '' END
      + CASE WHEN ISNULL(l.AmtPaid,0)        <> ISNULL(n.AmtPaid,0)        THEN 'Paid; '       ELSE '' END
      + CASE WHEN ISNULL(l.AmtDue,0)         <> ISNULL(n.AmtDue,0)         THEN 'Owed; '       ELSE '' END
      + CASE WHEN l.PayStatus                <> n.PayStatus                THEN 'PayStatus; '  ELSE '' END
FROM #legacy l
JOIN #new n ON n.RegistrationID = l.RegistrationID
WHERE ISNULL(l.FirstName,'')   <> n.FirstName
   OR ISNULL(l.LastName,'')    <> n.LastName
   OR ISNULL(l.Email,'')       <> n.Email
   OR ISNULL(l.CellPhone,'~')  <> ISNULL(n.CellPhone,'~')
   OR ISNULL(l.Dob,'19000101') <> ISNULL(n.Dob,'19000101')
   OR ISNULL(l.RoleName,'')    <> n.RoleName
   OR CAST(ISNULL(l.IsActive,0) AS bit) <> n.IsActive
   OR ISNULL(l.Position,'~')   <> ISNULL(n.Position,'~')
   OR ISNULL(l.ClubName,'~')   <> ISNULL(n.ClubName,'~')
   OR ISNULL(l.SchoolName,'~') <> ISNULL(n.SchoolName,'~')
   OR ISNULL(l.FeeTotal,0)     <> ISNULL(n.FeeTotal,0)
   OR ISNULL(l.AmtPaid,0)      <> ISNULL(n.AmtPaid,0)
   OR ISNULL(l.AmtDue,0)       <> ISNULL(n.AmtDue,0)
   OR l.PayStatus              <> n.PayStatus;

PRINT '=== [R5] ASSIGNMENT: stored (legacy) vs composed (new) — diffs are usually STALE stored strings; new is live ===';
-- Known formatting difference (NOT a bug): legacy stores "Agegroup:TeamName";
-- new composes "ClubRepClub Agegroup TeamName" with spaces. Normalize ':' to ' '
-- before comparing so only CONTENT diffs (stale stored strings) surface.
SELECT l.RegistrationID, l.LastName, l.FirstName,
       LegacyStored = l.Assignment, NewComposed = n.Assignment
FROM #legacy l
JOIN #new n ON n.RegistrationID = l.RegistrationID
WHERE ISNULL(NULLIF(LTRIM(RTRIM(REPLACE(l.Assignment, ':', ' '))),''),'~')
   <> ISNULL(REPLACE(n.Assignment, ':', ' '),'~');

PRINT '=== [R6] FILTER SWEEP: Active status counts ===';
PRINT '(legacy filter compares raw bActive; new requires bActive NOT NULL — null-bActive rows differ under multi-select)';
SELECT Side='legacy', IsActive = ISNULL(CAST(IsActive AS varchar(5)),'(null)'), Cnt=COUNT(*) FROM #legacy GROUP BY IsActive
UNION ALL
SELECT 'new', CAST(IsActive AS varchar(5)), COUNT(*) FROM #new GROUP BY IsActive
ORDER BY 2, 1;

PRINT '=== [R7] FILTER SWEEP: PayStatus counts ===';
SELECT Side='legacy', PayStatus, Cnt=COUNT(*) FROM #legacy GROUP BY PayStatus
UNION ALL
SELECT 'new', PayStatus, COUNT(*) FROM #new GROUP BY PayStatus
ORDER BY PayStatus, Side;

PRINT '=== [R8] FILTER SWEEP: per-Role counts (mismatches only) ===';
SELECT RoleName = ISNULL(c.RoleName,'(null)'),
       LegacyCnt = SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END),
       NewCnt    = SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
FROM (SELECT RoleName, Side='L' FROM #legacy
      UNION ALL SELECT RoleName, 'N' FROM #new) c
GROUP BY ISNULL(c.RoleName,'(null)')
HAVING SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END) <> SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
ORDER BY 1;
PRINT '(empty = parity)';

PRINT '=== [R9] FILTER SWEEP: per-Club counts (mismatches only) ===';
SELECT ClubName = ISNULL(c.ClubName,'(null)'),
       LegacyCnt = SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END),
       NewCnt    = SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
FROM (SELECT ClubName, Side='L' FROM #legacy
      UNION ALL SELECT ClubName, 'N' FROM #new) c
GROUP BY ISNULL(c.ClubName,'(null)')
HAVING SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END) <> SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
ORDER BY 1;
PRINT '(empty = parity)';

PRINT '=== [R10] FILTER SWEEP: per-Position counts (mismatches only) ===';
SELECT Position = ISNULL(c.Position,'(null)'),
       LegacyCnt = SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END),
       NewCnt    = SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
FROM (SELECT Position, Side='L' FROM #legacy
      UNION ALL SELECT Position, 'N' FROM #new) c
GROUP BY ISNULL(c.Position,'(null)')
HAVING SUM(CASE WHEN c.Side='L' THEN 1 ELSE 0 END) <> SUM(CASE WHEN c.Side='N' THEN 1 ELSE 0 END)
ORDER BY 1;
PRINT '(empty = parity)';

PRINT '=== [R11] AGGREGATES: Count / TotalFees / TotalPaid / TotalOwed (new computes over full match set) ===';
SELECT Side='legacy', Cnt=COUNT(*), TotalFees=SUM(ISNULL(FeeTotal,0)), TotalPaid=SUM(ISNULL(AmtPaid,0)), TotalOwed=SUM(ISNULL(AmtDue,0)) FROM #legacy
UNION ALL
SELECT 'new', COUNT(*), SUM(ISNULL(FeeTotal,0)), SUM(ISNULL(AmtPaid,0)), SUM(ISNULL(AmtDue,0)) FROM #new;

PRINT '=== [R12] SORT: default order first 15 (both: LastName, FirstName; new adds RegistrationId tiebreaker) ===';
SELECT TOP 15 teamKey=RegistrationID, LastName, FirstName FROM #legacy ORDER BY LastName, FirstName;
SELECT TOP 15 teamKey=RegistrationID, LastName, FirstName FROM #new ORDER BY LastName, FirstName, RegistrationID;

PRINT '=== [R13] LADT membership sanity: regs with assigned team pointing outside this job (would confuse tree filters) ===';
SELECT r.RegistrationID, u.LastName, u.FirstName, t.teamID, TeamJob = t.jobID
FROM Jobs.Registrations r
JOIN Leagues.teams t ON t.teamID = r.assigned_teamID
LEFT JOIN dbo.AspNetUsers u ON u.Id = r.UserId
WHERE r.jobID = @job AND t.jobID <> @job;
