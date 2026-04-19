WITH ReportRows AS (
    SELECT
        jmiP.[Text] AS L1Section,
        jmiC.[Text] AS L2ReportTitle,
        jmiC.Controller,
        jmiC.[Action],
        j.JobId,
        jt.JobTypeId,
        jt.JobTypeDesc,
        roles.Name AS RoleName,
        j.[year] AS JobYear
    FROM Jobs.JobMenus jm
        INNER JOIN Jobs.Jobs j ON jm.JobId = j.JobId
        INNER JOIN reference.JobTypes jt ON j.JobTypeId = jt.JobTypeId
        INNER JOIN dbo.AspNetRoles roles ON jm.RoleID = roles.Id
        INNER JOIN Jobs.JobMenu_Items jmiP ON jm.MenuId = jmiP.menuID
            AND jmiP.parentMenuItemID IS NULL
        INNER JOIN Jobs.JobMenu_Items jmiC ON jmiC.menuId = jmiP.menuID
            AND jmiC.parentMenuItemID = jmiP.menuItemID
    WHERE jm.menuTypeId = 6
        AND j.[year] IN ('2025', '2026', '2027')
        AND roles.Name IN ('Director', 'SuperDirector', 'SuperUser')
        AND jmiC.Controller = 'Reporting'
        AND jmiP.[Text] <> 'new parent item'
        AND jmiC.[Text] <> 'new child item'
),
ReportDistinctTypes AS (
    SELECT DISTINCT L1Section, L2ReportTitle, Controller, [Action], JobTypeDesc
    FROM ReportRows
),
ReportDistinctRoles AS (
    SELECT DISTINCT L1Section, L2ReportTitle, Controller, [Action], RoleName
    FROM ReportRows
)
SELECT
    r.L1Section,
    r.L2ReportTitle,
    r.Controller,
    r.[Action],
    COUNT(DISTINCT r.JobId) AS JobCount,
    COUNT(DISTINCT r.JobTypeId) AS JobTypeCount,
    (SELECT STRING_AGG(JobTypeDesc, ', ') WITHIN GROUP (ORDER BY JobTypeDesc)
     FROM ReportDistinctTypes t
     WHERE t.L1Section = r.L1Section
       AND t.L2ReportTitle = r.L2ReportTitle
       AND t.Controller = r.Controller
       AND t.[Action] = r.[Action]) AS JobTypes,
    (SELECT STRING_AGG(RoleName, ', ') WITHIN GROUP (ORDER BY RoleName)
     FROM ReportDistinctRoles rr
     WHERE rr.L1Section = r.L1Section
       AND rr.L2ReportTitle = r.L2ReportTitle
       AND rr.Controller = r.Controller
       AND rr.[Action] = r.[Action]) AS Roles,
    MAX(r.JobYear) AS MostRecentYear
FROM ReportRows r
GROUP BY
    r.L1Section,
    r.L2ReportTitle,
    r.Controller,
    r.[Action]
ORDER BY
    JobCount DESC,
    L1Section,
    L2ReportTitle;
