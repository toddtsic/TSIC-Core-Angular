/*
    Usage Analysis nav row — targeted, idempotent, additive.

    Same shape as add-stp-club-reps-nav.sql, for the same reason: 5) Re-Set Nav System.sql
    REBUILDS every role menu from its manifest, and one row does not justify that blast
    radius. The manifest in 5) Re-Set Nav System.ps1 carries the same row, so a future
    full re-seed reproduces this state exactly.

    WHAT: one leaf under "TSIC Admin" -> tools/usage (Usage Analysis, logs.AppUsage).

    WHO: Superuser ONLY, for now. The ROUTE admits Director and SuperDirector too, and
    the page already hands each role its own scope ceiling — but every tab is still an
    empty slot. Nothing to show a customer yet. Widening the audience is two flag flips
    in the manifest plus rows for the other two roles; do it when the first real tab
    lands, not before.

    Parent is matched by TEXT because section parents carry no RouterLink. Appended
    after the section's current last child so a hand-reordered menu is not disturbed.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SuperUser NVARCHAR(450) = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';
DECLARE @Route     NVARCHAR(500) = N'tools/usage';
DECLARE @Text      NVARCHAR(200) = N'Usage Analysis';
DECLARE @Icon      NVARCHAR(100) = N'activity';
DECLARE @Section   NVARCHAR(200) = N'TSIC Admin';

-- ---------------------------------------------------------------------------
-- BEFORE (expect 0 rows on a DB that has not had this applied)
-- ---------------------------------------------------------------------------
PRINT '--- existing tools/usage rows ---';
SELECT ni.NavItemId, n.RoleId, n.JobId, ni.Active, ni.SortOrder, ni.[Text], ni.RouterLink
FROM   nav.NavItem ni
JOIN   nav.Nav n ON n.NavId = ni.NavId
WHERE  ni.RouterLink = @Route;

BEGIN TRANSACTION;

DECLARE @navId INT = (SELECT NavId FROM nav.Nav WHERE RoleId = @SuperUser AND JobId IS NULL);
DECLARE @parentId INT = (SELECT TOP 1 ni.NavItemId
                         FROM   nav.NavItem ni
                         WHERE  ni.NavId = @navId
                           AND  ni.ParentNavItemId IS NULL
                           AND  ni.[Text] = @Section);
DECLARE @sort INT;

IF @navId IS NULL OR @parentId IS NULL
    PRINT CONCAT('SKIPPED - no Superuser default nav or no "', @Section, '" section');
ELSE IF EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND RouterLink = @Route)
    PRINT 'Superuser: Usage Analysis already present - skipped';
ELSE
BEGIN
    SET @sort = ISNULL((SELECT MAX(SortOrder) FROM nav.NavItem
                        WHERE NavId = @navId AND ParentNavItemId = @parentId), 0) + 1;

    INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified)
    VALUES (@navId, @parentId, 1, @sort, @Text, @Icon, @Route, GETDATE());

    PRINT CONCAT('Superuser: added Usage Analysis under ', @Section, ' at sort ', @sort);
END

COMMIT TRANSACTION;

-- ---------------------------------------------------------------------------
-- AFTER: expect exactly 1 row — Superuser default nav (JobId NULL), Active = 1.
-- ---------------------------------------------------------------------------
SELECT ni.NavItemId, n.RoleId, n.JobId, ni.Active, ni.SortOrder, ni.[Text], ni.IconName, ni.RouterLink
FROM   nav.NavItem ni
JOIN   nav.Nav n ON n.NavId = ni.NavId
WHERE  ni.RouterLink = @Route
ORDER  BY n.RoleId;
