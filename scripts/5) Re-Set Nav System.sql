-- ============================================================================
-- 5) Re-Set Nav System.sql
-- Generated: 2026-04-18 20:44:36 by 5) Re-Set Nav System.ps1
-- Role-scoped manifest; VisibilityRules seeded on L1 section parents where
-- the section is JobType/sport/customer-conditional (e.g. Scheduling).
-- Preserves: job-level overrides, reporting items, hand-authored L2 rules.
-- ============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- -- 1. Ensure schema + tables exist -------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')
    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'Nav')
BEGIN
    CREATE TABLE [nav].[Nav] (
        [NavId] INT IDENTITY(1,1) NOT NULL, [RoleId] NVARCHAR(450) NOT NULL,
        [JobId] UNIQUEIDENTIFIER NULL, [Active] BIT NOT NULL DEFAULT 1,
        [Modified] DATETIME2 NOT NULL DEFAULT GETDATE(), [ModifiedBy] NVARCHAR(450) NULL,
        CONSTRAINT [PK_nav_Nav] PRIMARY KEY CLUSTERED ([NavId]),
        CONSTRAINT [FK_nav_Nav_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles]([Id]),
        CONSTRAINT [FK_nav_Nav_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs]([JobId]),
        CONSTRAINT [FK_nav_Nav_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers]([Id])
    );
    CREATE UNIQUE INDEX [UQ_nav_Nav_Role_Job] ON [nav].[Nav]([RoleId],[JobId]) WHERE [JobId] IS NOT NULL;
    PRINT 'Created table: nav.Nav';
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'NavItem')
BEGIN
    CREATE TABLE [nav].[NavItem] (
        [NavItemId] INT IDENTITY(1,1) NOT NULL, [NavId] INT NOT NULL,
        [ParentNavItemId] INT NULL, [Active] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0, [Text] NVARCHAR(200) NOT NULL,
        [IconName] NVARCHAR(100) NULL, [RouterLink] NVARCHAR(500) NULL,
        [NavigateUrl] NVARCHAR(500) NULL, [Target] NVARCHAR(20) NULL,
        [VisibilityRules] NVARCHAR(MAX) NULL,
        [Modified] DATETIME2 NOT NULL DEFAULT GETDATE(), [ModifiedBy] NVARCHAR(450) NULL,
        CONSTRAINT [PK_nav_NavItem] PRIMARY KEY CLUSTERED ([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_NavId] FOREIGN KEY ([NavId]) REFERENCES [nav].[Nav]([NavId]) ON DELETE CASCADE,
        CONSTRAINT [FK_nav_NavItem_ParentNavItemId] FOREIGN KEY ([ParentNavItemId]) REFERENCES [nav].[NavItem]([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers]([Id])
    );
    PRINT 'Created table: nav.NavItem';
END

-- -- 2. Role GUIDs -------------------------------------------------------
DECLARE @Director NVARCHAR(450) = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06';
DECLARE @SuperDirector NVARCHAR(450) = '7B9EB503-53C9-44FA-94A0-17760C512440';
DECLARE @SuperUser NVARCHAR(450) = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';
DECLARE @RefAssignor NVARCHAR(450) = '122075A3-2C42-4092-97F1-9673DF5B6A2C';
DECLARE @StoreAdmin NVARCHAR(450) = '5B9B7055-4530-4E46-B403-1019FD8B8418';

-- -- 3. Preserve reporting items + visibility rules ----------------------
DECLARE @cnt INT;

IF OBJECT_ID('tempdb..#ReportingItems') IS NOT NULL DROP TABLE #ReportingItems;
SELECT n.RoleId,
       parent.[Text]      AS ParentText,
       parent.IconName    AS ParentIcon,
       parent.SortOrder   AS ParentSort,
       ni.Active, ni.SortOrder,
       ni.[Text], ni.IconName, ni.RouterLink, ni.NavigateUrl, ni.[Target]
INTO #ReportingItems
FROM nav.NavItem ni
JOIN nav.Nav n ON ni.NavId = n.NavId
LEFT JOIN nav.NavItem parent ON ni.ParentNavItemId = parent.NavItemId
WHERE n.JobId IS NULL AND ni.RouterLink LIKE 'reporting/%';
SELECT @cnt = COUNT(*) FROM #ReportingItems;
PRINT CONCAT('Preserved ', @cnt, ' reporting item(s)');

IF OBJECT_ID('tempdb..#VisRules') IS NOT NULL DROP TABLE #VisRules;
SELECT n.RoleId, ni.RouterLink, ni.VisibilityRules
INTO #VisRules
FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId
WHERE n.JobId IS NULL
  AND ni.RouterLink IS NOT NULL
  AND ni.VisibilityRules IS NOT NULL
  AND ni.VisibilityRules <> '';
SELECT @cnt = COUNT(*) FROM #VisRules;
PRINT CONCAT('Preserved ', @cnt, ' visibility rule(s)');

-- -- 4. Clear platform defaults (job overrides preserved) ----------------
DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);
DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;
PRINT 'Cleared platform defaults';

-- -- 5. Insert Nav records (one per role) -------------------------------
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@Director, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@SuperDirector, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@SuperUser, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@RefAssignor, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@StoreAdmin, NULL, 1, GETDATE());
PRINT 'Inserted 5 Nav records';

-- -- 6. Admin manifest (Director / SuperDirector / SuperUser) -----------
IF OBJECT_ID('tempdb..#AdminManifest') IS NOT NULL DROP TABLE #AdminManifest;
CREATE TABLE #AdminManifest (
    Controller      NVARCHAR(50)  NOT NULL,
    Icon            NVARCHAR(50)  NULL,
    CtrlSort        INT           NOT NULL,
    [Action]        NVARCHAR(100) NOT NULL,
    ActionIcon      NVARCHAR(50)  NULL,
    RouterLink      NVARCHAR(200) NOT NULL,
    ActionSort      INT           NOT NULL,
    ForDirector     BIT           NOT NULL,
    ForSuperDir     BIT           NOT NULL,
    ForSuperUser    BIT           NOT NULL,
    VisibilityRules NVARCHAR(MAX) NULL    -- JSON, applied to L1 section; NULL = always visible
);
INSERT INTO #AdminManifest VALUES (N'Search', N'search', 1, N'Registrations', N'people', N'search/registrations', 1, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Search', N'search', 1, N'Teams', N'shield', N'search/teams', 2, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Job Settings', N'briefcase', N'configure/job', 1, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Discount Codes', N'tags', N'configure/discount-codes', 2, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Age Ranges', N'sliders', N'configure/age-ranges', 3, 1, 1, 1, N'{"requiresFlags":["teamEligibilityByAge"]}');
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Administrators', N'person-badge', N'configure/administrators', 4, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Customer Groups', N'people', N'configure/customer-groups', 5, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Dropdown Options', N'list', N'configure/ddl-options', 6, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Customers', N'building', N'configure/customers', 7, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Theme', N'palette', N'configure/theme', 8, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Nav Editor', N'list', N'configure/nav-editor', 9, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Widget Editor', N'grid', N'configure/widget-editor', 10, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Job Clone', N'copy', N'configure/job-clone', 11, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Communications', N'megaphone', 3, N'Bulletins', N'megaphone', N'communications/bulletins', 1, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Communications', N'megaphone', 3, N'Email Log', N'envelope-open', N'communications/email-log', 2, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Communications', N'megaphone', 3, N'Push Notification', N'bell', N'communications/push-notification', 3, 1, 1, 1, N'{"requiresFlags":["mobileEnabled"]}');
INSERT INTO #AdminManifest VALUES (N'LADT', N'diagram-3', 4, N'Editor', N'pencil-square', N'ladt/editor', 1, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'LADT', N'diagram-3', 4, N'Roster Swapper', N'arrow-left-right', N'ladt/roster-swapper', 2, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'LADT', N'diagram-3', 4, N'Pool Assignment', N'people', N'ladt/pool-assignment', 3, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'View Schedule', N'eye', N'scheduling/view-schedule', 1, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Bracket Seeds', N'trophy', N'scheduling/bracket-seeds', 2, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Master Schedule', N'calendar-week', N'scheduling/master-schedule', 3, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Rescheduler', N'arrow-repeat', N'scheduling/rescheduler', 4, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Tournament Parking', N'car-front', N'scheduling/tournament-parking', 5, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Referee Assignment', N'clipboard-check', N'scheduling/referee-assignment', 6, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Referee Calendar', N'calendar-week', N'scheduling/referee-calendar', 7, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Mobile Scorers', N'phone', N'scheduling/mobile-scorers', 8, 1, 1, 1, N'{"requiresFlags":["mobileEnabled"]}');
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Fields', N'geo-alt', N'scheduling/fields', 9, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Pairings', N'arrows-collapse', N'scheduling/pairings', 10, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Timeslots', N'clock', N'scheduling/timeslots', 11, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Schedule Hub', N'grid', N'scheduling/schedule-hub', 12, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'QA Results', N'check2-square', N'scheduling/qa-results', 13, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Reports', N'file-earmark-bar-graph', 6, N'Report Library', N'collection', N'reports', 1, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'ARB', N'credit-card', 7, N'Health Check', N'heart-pulse', N'arb/health', 1, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Store', N'shop', 8, N'Store Admin', N'speedometer2', N'store/admin', 1, 1, 1, 1, N'{"requiresFlags":["storeEnabled"]}');
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 9, N'US Lax Test', N'check-circle', N'tools/uslax-test', 1, 1, 1, 1, N'{"sports":["Lacrosse"]}');
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 9, N'US Lax Rankings', N'trophy', N'tools/uslax-rankings', 2, 1, 1, 1, N'{"sports":["Lacrosse"]}');
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 9, N'Uniform Upload', N'upload', N'tools/uniform-upload', 3, 1, 1, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 9, N'Profile Migration', N'arrow-right', N'tools/profile-migration', 4, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 9, N'Profile Editor', N'pencil-square', N'tools/profile-editor', 5, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 9, N'Change Password', N'key', N'tools/change-password', 6, 0, 0, 1, NULL);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 9, N'Customer Job Revenue', N'cash-stack', N'tools/customer-job-revenue', 7, 0, 0, 1, NULL);

-- Section-level rules applied to L1 independent of per-item aggregation
IF OBJECT_ID('tempdb..#SectionRules') IS NOT NULL DROP TABLE #SectionRules;
CREATE TABLE #SectionRules (
    Controller      NVARCHAR(50)  NOT NULL PRIMARY KEY,
    VisibilityRules NVARCHAR(MAX) NOT NULL
);
INSERT INTO #SectionRules VALUES (N'Scheduling', N'{"jobTypes":["Tournament Scheduling","League Scheduling"]}');
INSERT INTO #SectionRules VALUES (N'ARB', N'{"requiresFlags":["adnArb"]}');

-- Fan out admin manifest per admin role
DECLARE @navId INT, @parentId INT, @roleId NVARCHAR(450);

DECLARE role_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT RoleId FROM nav.Nav
    WHERE JobId IS NULL AND RoleId IN (@Director, @SuperDirector, @SuperUser);

OPEN role_cursor;
FETCH NEXT FROM role_cursor INTO @roleId;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @roleId AND JobId IS NULL;

    -- Per-L1 cursor. L1 rules are inherited ONLY when every visible item under the
    -- controller carries the same non-null rule (treated as a section-wide rule).
    -- Mixed / partial rules stay on their individual L2 rows so the L1 remains visible
    -- while per-item gating applies.
    DECLARE @ctrl NVARCHAR(50), @ctrlIcon NVARCHAR(50), @ctrlSort INT, @ctrlVisRules NVARCHAR(MAX);
    DECLARE ctrl_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT am.Controller,
               MIN(am.Icon)     AS Icon,
               MIN(am.CtrlSort) AS CtrlSort,
               COALESCE(
                    MIN(sr.VisibilityRules),
                    CASE
                        WHEN COUNT(*) = COUNT(am.VisibilityRules)
                         AND COUNT(DISTINCT am.VisibilityRules) = 1
                        THEN MIN(am.VisibilityRules)
                        ELSE NULL
                    END
               ) AS VisibilityRules
        FROM #AdminManifest am
        LEFT JOIN #SectionRules sr ON sr.Controller = am.Controller
        WHERE CASE @roleId
                 WHEN @Director      THEN ForDirector
                 WHEN @SuperDirector THEN ForSuperDir
                 WHEN @SuperUser     THEN ForSuperUser
                 ELSE 0
             END = 1
        GROUP BY am.Controller
        ORDER BY MIN(am.CtrlSort);

    OPEN ctrl_cursor;
    FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort, @ctrlVisRules;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- L1 (section parent) — gets rules only when every child shares them.
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, VisibilityRules, Modified)
        VALUES (@navId, NULL, 1, @ctrlSort, @ctrl, @ctrlIcon, @ctrlVisRules, GETDATE());
        SET @parentId = SCOPE_IDENTITY();

        -- L2 leaves — carry their own rules for per-item gating.
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, VisibilityRules, Modified)
        SELECT @navId, @parentId, 1, ActionSort, [Action], ActionIcon, RouterLink, VisibilityRules, GETDATE()
        FROM #AdminManifest
        WHERE Controller = @ctrl
          AND CASE @roleId
                 WHEN @Director      THEN ForDirector
                 WHEN @SuperDirector THEN ForSuperDir
                 WHEN @SuperUser     THEN ForSuperUser
                 ELSE 0
             END = 1
        ORDER BY ActionSort;

        FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort, @ctrlVisRules;
    END
    CLOSE ctrl_cursor;
    DEALLOCATE ctrl_cursor;

    FETCH NEXT FROM role_cursor INTO @roleId;
END
CLOSE role_cursor;
DEALLOCATE role_cursor;

PRINT 'Fanned admin manifest to Director / SuperDirector / SuperUser';

-- -- 7. RefAssignor -----------------------------------------------------
-- RefAssignor: Referee Assignment + Referee Calendar
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @RefAssignor AND JobId IS NULL;
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 1, N'RefAssignor', N'person-check', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 1, N'Referee Assignment', N'clipboard-check', N'scheduling/referee-assignment', GETDATE());
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 2, N'Referee Calendar', N'calendar-week', N'scheduling/referee-calendar', GETDATE());
PRINT 'RefAssignor: Referee Assignment + Referee Calendar';

-- -- 8. StoreAdmin ------------------------------------------------------
-- StoreAdmin: Store Admin
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @StoreAdmin AND JobId IS NULL;
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 1, N'StoreAdmin', N'shop', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 1, N'Store Admin', N'speedometer2', N'store/admin', GETDATE());
PRINT 'StoreAdmin: Store Admin';

-- -- 13. Restore preserved reporting items ------------------------------
SELECT @cnt = COUNT(*) FROM #ReportingItems;
IF @cnt > 0
BEGIN
    DECLARE @rptRoleId NVARCHAR(450), @rptParentText NVARCHAR(200),
            @rptParentIcon NVARCHAR(100), @rptParentSort INT;
    DECLARE @rptNavId INT, @rptParentId INT;

    -- Restore grouped reporting items (with parent hierarchy, per role)
    DECLARE rpt_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT RoleId, ParentText, ParentIcon, ParentSort
        FROM #ReportingItems
        WHERE ParentText IS NOT NULL;

    OPEN rpt_cursor;
    FETCH NEXT FROM rpt_cursor INTO @rptRoleId, @rptParentText, @rptParentIcon, @rptParentSort;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SELECT @rptNavId = NavId FROM nav.Nav WHERE RoleId = @rptRoleId AND JobId IS NULL;

        -- Find or create the parent controller item
        IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @rptNavId AND [Text] = @rptParentText AND ParentNavItemId IS NULL)
        BEGIN
            INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified)
            VALUES (@rptNavId, NULL, 1, @rptParentSort, @rptParentText, @rptParentIcon, GETDATE());
            SET @rptParentId = SCOPE_IDENTITY();
        END
        ELSE
            SELECT @rptParentId = NavItemId FROM nav.NavItem WHERE NavId = @rptNavId AND [Text] = @rptParentText AND ParentNavItemId IS NULL;

        INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, NavigateUrl, [Target], Modified)
        SELECT @rptNavId, @rptParentId, ri.Active, ri.SortOrder, ri.[Text], ri.IconName, ri.RouterLink, ri.NavigateUrl, ri.[Target], GETDATE()
        FROM #ReportingItems ri
        WHERE ri.RoleId = @rptRoleId AND ri.ParentText = @rptParentText;

        FETCH NEXT FROM rpt_cursor INTO @rptRoleId, @rptParentText, @rptParentIcon, @rptParentSort;
    END
    CLOSE rpt_cursor;
    DEALLOCATE rpt_cursor;

    -- Restore orphaned reporting items (no parent — top-level leaves)
    INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, NavigateUrl, [Target], Modified)
    SELECT n.NavId, NULL, ri.Active, ri.SortOrder, ri.[Text], ri.IconName, ri.RouterLink, ri.NavigateUrl, ri.[Target], GETDATE()
    FROM #ReportingItems ri
    JOIN nav.Nav n ON n.RoleId = ri.RoleId AND n.JobId IS NULL
    WHERE ri.ParentText IS NULL;

    PRINT CONCAT('Restored ', @cnt, ' reporting item(s) with original role + parent grouping');
END
DROP TABLE #ReportingItems;

-- -- 14. Restore preserved visibility rules -----------------------------
UPDATE ni
SET ni.VisibilityRules = vr.VisibilityRules
FROM nav.NavItem ni
JOIN nav.Nav n ON ni.NavId = n.NavId
JOIN #VisRules vr ON vr.RoleId = n.RoleId AND vr.RouterLink = ni.RouterLink
WHERE n.JobId IS NULL;
PRINT CONCAT('Restored ', @@ROWCOUNT, ' visibility rule(s)');
DROP TABLE #VisRules;
DROP TABLE #AdminManifest;
DROP TABLE #SectionRules;

-- -- 15. Commit + summary -----------------------------------------------
COMMIT TRANSACTION;

SELECT
    (SELECT COUNT(*) FROM nav.Nav WHERE JobId IS NULL)                                                                      AS [Platform Navs],
    (SELECT COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId WHERE n.JobId IS NULL)                        AS [Platform Items],
    (SELECT COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId WHERE n.JobId IS NULL AND ni.ParentNavItemId IS NULL) AS [Sections or Top-level Leaves],
    (SELECT COUNT(*) FROM nav.Nav WHERE JobId IS NOT NULL)                                                                  AS [Job Overrides (preserved)];

SELECT
    r.Name AS [Role],
    parent.Text AS [Section],
    parent.SortOrder AS [SectionOrder],
    parent.RouterLink AS [SectionRoute],
    child.Text AS [Item],
    child.SortOrder AS [ItemOrder],
    child.RouterLink AS [ItemRoute]
FROM [nav].[Nav] n
JOIN [dbo].[AspNetRoles] r ON n.RoleId = r.Id
LEFT JOIN [nav].[NavItem] parent ON parent.NavId = n.NavId AND parent.ParentNavItemId IS NULL
LEFT JOIN [nav].[NavItem] child  ON child.ParentNavItemId = parent.NavItemId
WHERE n.JobId IS NULL
ORDER BY r.Name, parent.SortOrder, child.SortOrder;

PRINT 'Nav system reset complete.';
SET NOCOUNT OFF;

