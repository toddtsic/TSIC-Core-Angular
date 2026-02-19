-- ============================================================================
-- Nav Defaults — Seed Script
--
-- Idempotent upsert script for platform-default nav menus (JobId = NULL).
-- Safe to re-run against dev, prod, or restored DBs.
--
-- Structure: Add shared sections via @sharedRoles loop, then role-specific
-- sections in individual blocks below.
--
-- Prerequisites: nav schema created (scripts/create-nav-schema.sql)
-- ============================================================================

SET NOCOUNT ON;

-- ── Role IDs (from RoleConstants.cs) ──
DECLARE @Director       NVARCHAR(450) = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06';
DECLARE @SuperDirector  NVARCHAR(450) = '7B9EB503-53C9-44FA-94A0-17760C512440';
DECLARE @Superuser      NVARCHAR(450) = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';

DECLARE @navId INT, @parentId INT;

-- ════════════════════════════════════════════════════════════════════════════
-- SHARED: Director, SuperDirector, Superuser
-- ════════════════════════════════════════════════════════════════════════════

DECLARE @sharedRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @sharedRoles VALUES (@Director), (@SuperDirector), (@Superuser);

DECLARE @roleId NVARCHAR(450);
DECLARE role_cur CURSOR LOCAL FAST_FORWARD FOR SELECT RoleId FROM @sharedRoles;
OPEN role_cur;
FETCH NEXT FROM role_cur INTO @roleId;

WHILE @@FETCH_STATUS = 0
BEGIN

    -- ── Ensure Nav row exists (platform default = JobId NULL) ──
    IF NOT EXISTS (SELECT 1 FROM nav.Nav WHERE RoleId = @roleId AND JobId IS NULL)
        INSERT INTO nav.Nav (RoleId, JobId, Active) VALUES (@roleId, NULL, 1);

    SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @roleId AND JobId IS NULL;

    -- ────────────────────────────────────────────────────────
    -- Section 1: Search
    -- ────────────────────────────────────────────────────────

    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Search')
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, Active)
        VALUES (@navId, NULL, 1, 'Search', 'search', 1);
    ELSE
        UPDATE nav.NavItem SET SortOrder = 1, IconName = 'search', Active = 1
        WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Search';

    SELECT @parentId = NavItemId FROM nav.NavItem
    WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Search';

    -- Players
    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Players')
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
        VALUES (@navId, @parentId, 1, 'Players', 'people', 'search/players', 1);
    ELSE
        UPDATE nav.NavItem SET SortOrder = 1, IconName = 'people', RouterLink = 'search/players', Active = 1
        WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Players';

    -- Teams
    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Teams')
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
        VALUES (@navId, @parentId, 2, 'Teams', 'shield', 'search/teams', 1);
    ELSE
        UPDATE nav.NavItem SET SortOrder = 2, IconName = 'shield', RouterLink = 'search/teams', Active = 1
        WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Teams';

    -- ────────────────────────────────────────────────────────
    -- Section 2: Configure
    -- ────────────────────────────────────────────────────────

    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Configure')
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, Active)
        VALUES (@navId, NULL, 2, 'Configure', 'gear', 1);
    ELSE
        UPDATE nav.NavItem SET SortOrder = 2, IconName = 'gear', Active = 1
        WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Configure';

    SELECT @parentId = NavItemId FROM nav.NavItem
    WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Configure';

    -- Administrators  (legacy: jobadministrator/admin)
    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Administrators')
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
        VALUES (@navId, @parentId, 1, 'Administrators', 'person-badge', 'configure/administrators', 1);
    ELSE
        UPDATE nav.NavItem SET SortOrder = 1, IconName = 'person-badge', RouterLink = 'configure/administrators', Active = 1
        WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Administrators';

    -- Discount Codes  (legacy: jobdiscountcodes/admin)
    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Discount Codes')
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
        VALUES (@navId, @parentId, 2, 'Discount Codes', 'tags', 'configure/discount-codes', 1);
    ELSE
        UPDATE nav.NavItem SET SortOrder = 2, IconName = 'tags', RouterLink = 'configure/discount-codes', Active = 1
        WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Discount Codes';

    -- (Add more shared sections here as needed)

    FETCH NEXT FROM role_cur INTO @roleId;
END

CLOSE role_cur;
DEALLOCATE role_cur;

-- ════════════════════════════════════════════════════════════════════════════
-- SUPERUSER-ONLY: Platform section
-- ════════════════════════════════════════════════════════════════════════════

SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @Superuser AND JobId IS NULL;

-- ────────────────────────────────────────────────────────
-- Section 3: Platform (SuperUser only)
-- ────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Platform')
    INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, Active)
    VALUES (@navId, NULL, 3, 'Platform', 'wrench-adjustable', 1);
ELSE
    UPDATE nav.NavItem SET SortOrder = 3, IconName = 'wrench-adjustable', Active = 1
    WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Platform';

SELECT @parentId = NavItemId FROM nav.NavItem
WHERE NavId = @navId AND ParentNavItemId IS NULL AND Text = 'Platform';

-- Job Configuration
IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Job Configuration')
    INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
    VALUES (@navId, @parentId, 1, 'Job Configuration', 'briefcase', 'admin/job-config', 1);
ELSE
    UPDATE nav.NavItem SET SortOrder = 1, IconName = 'briefcase', RouterLink = 'admin/job-config', Active = 1
    WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Job Configuration';

-- Widget Editor
IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Widget Editor')
    INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
    VALUES (@navId, @parentId, 2, 'Widget Editor', 'grid-1x2', 'admin/widget-editor', 1);
ELSE
    UPDATE nav.NavItem SET SortOrder = 2, IconName = 'grid-1x2', RouterLink = 'admin/widget-editor', Active = 1
    WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Widget Editor';

-- Nav Editor
IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Nav Editor')
    INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
    VALUES (@navId, @parentId, 3, 'Nav Editor', 'diagram-3', 'admin/nav-editor', 1);
ELSE
    UPDATE nav.NavItem SET SortOrder = 3, IconName = 'diagram-3', RouterLink = 'admin/nav-editor', Active = 1
    WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Nav Editor';

-- DDL Options
IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'DDL Options')
    INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
    VALUES (@navId, @parentId, 4, 'DDL Options', 'list-ul', 'admin/ddl-options', 1);
ELSE
    UPDATE nav.NavItem SET SortOrder = 4, IconName = 'list-ul', RouterLink = 'admin/ddl-options', Active = 1
    WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'DDL Options';

-- Job Clone
IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Job Clone')
    INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
    VALUES (@navId, @parentId, 5, 'Job Clone', 'copy', 'admin/job-clone', 1);
ELSE
    UPDATE nav.NavItem SET SortOrder = 5, IconName = 'copy', RouterLink = 'admin/job-clone', Active = 1
    WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Job Clone';

-- Theme Editor
IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Theme Editor')
    INSERT INTO nav.NavItem (NavId, ParentNavItemId, SortOrder, Text, IconName, RouterLink, Active)
    VALUES (@navId, @parentId, 6, 'Theme Editor', 'palette', 'admin/theme', 1);
ELSE
    UPDATE nav.NavItem SET SortOrder = 6, IconName = 'palette', RouterLink = 'admin/theme', Active = 1
    WHERE NavId = @navId AND ParentNavItemId = @parentId AND Text = 'Theme Editor';

-- ════════════════════════════════════════════════════════════════════════════
-- CLEANUP: Remove stale items from prior seed versions
-- ════════════════════════════════════════════════════════════════════════════

-- Remove old 'Configure > Job' items (moved to SuperUser Platform section)
DELETE ni FROM nav.NavItem ni
INNER JOIN nav.NavItem parent ON ni.ParentNavItemId = parent.NavItemId
WHERE ni.Text = 'Job' AND ni.RouterLink = 'configure/job'
  AND parent.Text = 'Configure';

-- ════════════════════════════════════════════════════════════════════════════
-- VERIFICATION
-- ════════════════════════════════════════════════════════════════════════════

PRINT '';
PRINT '════════════════════════════════════════════';
PRINT ' Nav Defaults Seed — Complete';
PRINT '════════════════════════════════════════════';
PRINT '';

SELECT
    r.Name AS [Role],
    n.NavId,
    parent.Text AS [Section],
    parent.SortOrder AS [SectionOrder],
    child.Text AS [Item],
    child.SortOrder AS [ItemOrder],
    child.IconName AS [Icon],
    child.RouterLink AS [Route]
FROM nav.Nav n
JOIN dbo.AspNetRoles r ON n.RoleId = r.Id
LEFT JOIN nav.NavItem parent ON parent.NavId = n.NavId AND parent.ParentNavItemId IS NULL
LEFT JOIN nav.NavItem child  ON child.ParentNavItemId = parent.NavItemId
WHERE n.JobId IS NULL
ORDER BY r.Name, parent.SortOrder, child.SortOrder;

SET NOCOUNT OFF;
