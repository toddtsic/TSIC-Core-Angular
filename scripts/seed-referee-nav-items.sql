-- ═══════════════════════════════════════════════════════════════
-- Seed Nav Items: Referee Assignment Module
-- Run AFTER seed-nav-defaults.sql has created the base nav tree
-- ═══════════════════════════════════════════════════════════════

-- Add "Assign Referees" under the Scheduling group for Director role
DECLARE @directorNavId INT = (
    SELECT TOP 1 n.NavId
    FROM [nav].[Nav] n
    WHERE n.RoleId = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06' -- Director
      AND n.JobId IS NULL AND n.Active = 1
);

DECLARE @schedulingParentId INT = (
    SELECT TOP 1 ni.NavItemId
    FROM [nav].[NavItem] ni
    WHERE ni.NavId = @directorNavId
      AND ni.ParentNavItemId IS NULL
      AND ni.Text = N'Scheduling'
      AND ni.Active = 1
);

-- Insert "Assign Referees" under Scheduling (if not exists)
IF NOT EXISTS (
    SELECT 1 FROM [nav].[NavItem]
    WHERE NavId = @directorNavId
      AND ParentNavItemId = @schedulingParentId
      AND RouterLink = N'scheduling/referee-assignment'
)
BEGIN
    INSERT INTO [nav].[NavItem] (NavId, ParentNavItemId, Text, IconName, RouterLink, NavigateUrl, SortOrder, Active, Modified)
    VALUES (@directorNavId, @schedulingParentId, N'Assign Referees', N'person-badge', N'scheduling/referee-assignment', NULL, 11, 1, GETUTCDATE());
    PRINT 'Inserted: Assign Referees (Scheduling)';
END

-- Insert "Referee Calendar" under Scheduling (if not exists)
IF NOT EXISTS (
    SELECT 1 FROM [nav].[NavItem]
    WHERE NavId = @directorNavId
      AND ParentNavItemId = @schedulingParentId
      AND RouterLink = N'scheduling/referee-calendar'
)
BEGIN
    INSERT INTO [nav].[NavItem] (NavId, ParentNavItemId, Text, IconName, RouterLink, NavigateUrl, SortOrder, Active, Modified)
    VALUES (@directorNavId, @schedulingParentId, N'Referee Calendar', N'calendar-event', N'scheduling/referee-calendar', NULL, 12, 1, GETUTCDATE());
    PRINT 'Inserted: Referee Calendar (Scheduling)';
END

-- ═══════════════════════════════════════════════════════════════
-- Verification
-- ═══════════════════════════════════════════════════════════════
SELECT
    CASE WHEN ni.ParentNavItemId IS NULL THEN N'► ' ELSE N'  └ ' END + ni.Text AS [Nav Tree],
    ni.RouterLink,
    ni.IconName,
    ni.SortOrder,
    CASE WHEN ni.Active = 1 THEN N'Active' ELSE N'Inactive' END AS [Status]
FROM [nav].[NavItem] ni
WHERE ni.NavId = @directorNavId
ORDER BY
    COALESCE(ni.ParentNavItemId, ni.NavItemId),
    CASE WHEN ni.ParentNavItemId IS NULL THEN 0 ELSE 1 END,
    ni.SortOrder;
