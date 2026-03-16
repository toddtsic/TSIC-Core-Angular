-- ============================================================================
-- Nav Platform Defaults — Route-Driven Menu Manifest
--
-- Menu structure derived from app.routes.ts Controller/Action pattern.
-- Guard data determines which roles see each item:
--   requireAdmin  → Director, SuperDirector, SuperUser, Staff, RefAssignor, StoreAdmin
--   requireSuperUser → SuperUser only
--
-- Non-admin roles (Family, Player, ClubRep, UnassignedAdult) get Nav records
-- but no items — dashboard-only experience.
--
-- Reporting items are NOT auto-generated. Add via nav editor (SuperUser).
--
-- Workflow: Run this → then run 0-Restore-DevConfig-DEV.ps1 to export.
-- ============================================================================

SET NOCOUNT ON;

-- ── 1. Create [nav] schema + tables if not exists ──

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')
BEGIN
    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');
    PRINT 'Created [nav] schema';
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'Nav')
BEGIN
    CREATE TABLE [nav].[Nav] (
        [NavId]      INT IDENTITY(1,1)  NOT NULL,
        [RoleId]     NVARCHAR(450)      NOT NULL,
        [JobId]      UNIQUEIDENTIFIER   NULL,
        [Active]     BIT                NOT NULL DEFAULT 1,
        [Modified]   DATETIME2          NOT NULL DEFAULT GETDATE(),
        [ModifiedBy] NVARCHAR(450)      NULL,
        CONSTRAINT [PK_nav_Nav] PRIMARY KEY CLUSTERED ([NavId]),
        CONSTRAINT [FK_nav_Nav_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_nav_Nav_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs] ([JobId]),
        CONSTRAINT [FK_nav_Nav_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])
    );
    CREATE UNIQUE INDEX [UQ_nav_Nav_Role_Job] ON [nav].[Nav] ([RoleId], [JobId]) WHERE [JobId] IS NOT NULL;
    PRINT 'Created table: nav.Nav';
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'NavItem')
BEGIN
    CREATE TABLE [nav].[NavItem] (
        [NavItemId]       INT IDENTITY(1,1) NOT NULL,
        [NavId]           INT               NOT NULL,
        [ParentNavItemId] INT               NULL,
        [Active]          BIT               NOT NULL DEFAULT 1,
        [SortOrder]       INT               NOT NULL DEFAULT 0,
        [Text]            NVARCHAR(200)     NOT NULL,
        [IconName]        NVARCHAR(100)     NULL,
        [RouterLink]      NVARCHAR(500)     NULL,
        [NavigateUrl]     NVARCHAR(500)     NULL,
        [Target]          NVARCHAR(20)      NULL,
        [Modified]        DATETIME2         NOT NULL DEFAULT GETDATE(),
        [ModifiedBy]      NVARCHAR(450)     NULL,
        CONSTRAINT [PK_nav_NavItem] PRIMARY KEY CLUSTERED ([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_NavId] FOREIGN KEY ([NavId]) REFERENCES [nav].[Nav] ([NavId]) ON DELETE CASCADE,
        CONSTRAINT [FK_nav_NavItem_ParentNavItemId] FOREIGN KEY ([ParentNavItemId]) REFERENCES [nav].[NavItem] ([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])
    );
    PRINT 'Created table: nav.NavItem';
END

-- ── 2. Role GUIDs ──

DECLARE @Director       NVARCHAR(450) = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06';
DECLARE @SuperDirector  NVARCHAR(450) = '7B9EB503-53C9-44FA-94A0-17760C512440';
DECLARE @SuperUser      NVARCHAR(450) = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';
DECLARE @Staff          NVARCHAR(450) = '1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA';
DECLARE @RefAssignor    NVARCHAR(450) = '122075A3-2C42-4092-97F1-9673DF5B6A2C';
DECLARE @StoreAdmin     NVARCHAR(450) = '5B9B7055-4530-4E46-B403-1019FD8B8418';
DECLARE @Family         NVARCHAR(450) = 'E0A8A5C3-A36C-417F-8312-E7083F1AA5A0';
DECLARE @Player         NVARCHAR(450) = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
DECLARE @ClubRep        NVARCHAR(450) = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
DECLARE @UnassignedAdult NVARCHAR(450) = 'CE2CB370-5880-4624-A43E-048379C64331';

-- ── 3. Preserve existing reporting items ──

IF OBJECT_ID('tempdb..#ReportingItems') IS NOT NULL DROP TABLE #ReportingItems;

SELECT ni.NavItemId, ni.NavId, ni.ParentNavItemId, ni.Active, ni.SortOrder,
       ni.[Text], ni.IconName, ni.RouterLink, ni.NavigateUrl, ni.[Target]
INTO #ReportingItems
FROM nav.NavItem ni
JOIN nav.Nav n ON ni.NavId = n.NavId
WHERE n.JobId IS NULL AND ni.RouterLink LIKE 'reporting/%';

DECLARE @reportCount INT;
SELECT @reportCount = COUNT(*) FROM #ReportingItems;
PRINT CONCAT('Preserved ', @reportCount, ' reporting item(s)');

-- ── 4. Clear platform defaults ──

DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);
DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;
PRINT 'Cleared existing platform defaults';

-- ── 5. Insert Nav records (one per role) ──

INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@Director,        NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@SuperDirector,   NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@SuperUser,       NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@Staff,           NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@RefAssignor,     NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@StoreAdmin,      NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@Family,          NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@Player,          NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@ClubRep,         NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([RoleId], [JobId], [Active], [Modified]) VALUES (@UnassignedAdult, NULL, 1, GETDATE());
PRINT 'Inserted 10 Nav records';

-- ── 6. Menu manifest ──
-- Uses a temp table approach: define items once, then fan out to roles.

IF OBJECT_ID('tempdb..#MenuManifest') IS NOT NULL DROP TABLE #MenuManifest;

CREATE TABLE #MenuManifest (
    Controller   NVARCHAR(50)  NOT NULL,  -- L1 header text
    Icon         NVARCHAR(50)  NULL,       -- L1 icon (Bootstrap icon name)
    CtrlSort     INT           NOT NULL,   -- L1 sort order
    [Action]     NVARCHAR(100) NOT NULL,   -- L2 item text
    ActionIcon   NVARCHAR(50)  NULL,       -- L2 icon
    RouterLink   NVARCHAR(200) NOT NULL,   -- route path
    ActionSort   INT           NOT NULL,   -- L2 sort order
    Guard        NVARCHAR(20)  NOT NULL    -- 'admin' or 'superuser'
);

-- Search (requireAdmin)
INSERT INTO #MenuManifest VALUES ('Search', 'search', 1, 'Players', 'people', 'search/players', 1, 'admin');
INSERT INTO #MenuManifest VALUES ('Search', 'search', 1, 'Teams', 'shield', 'search/teams', 2, 'admin');

-- Configure (mixed guards)
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Job', 'briefcase', 'configure/job', 1, 'admin');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Age Ranges', 'sliders', 'configure/age-ranges', 2, 'admin');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Discount Codes', 'tags', 'configure/discount-codes', 3, 'admin');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Uniform Upload', 'upload', 'configure/uniform-upload', 4, 'admin');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Administrators', 'person-badge', 'configure/administrators', 10, 'superuser');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Customer Groups', 'people', 'configure/customer-groups', 11, 'superuser');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Dropdown Options', 'list', 'configure/ddl-options', 12, 'superuser');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Customers', 'building', 'configure/customers', 13, 'superuser');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Theme', 'palette', 'configure/theme', 14, 'superuser');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Menus', 'list', 'configure/nav-editor', 15, 'superuser');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Widgets', 'grid', 'configure/widget-editor', 16, 'superuser');
INSERT INTO #MenuManifest VALUES ('Configure', 'gear', 2, 'Job Clone', 'copy', 'configure/job-clone', 17, 'superuser');

-- Communications (requireAdmin)
INSERT INTO #MenuManifest VALUES ('Communications', 'envelope', 3, 'Bulletins', 'megaphone', 'communications/bulletins', 1, 'admin');
INSERT INTO #MenuManifest VALUES ('Communications', 'envelope', 3, 'Email Log', 'envelope-open', 'communications/email-log', 2, 'admin');
INSERT INTO #MenuManifest VALUES ('Communications', 'envelope', 3, 'Push Notification', 'bell', 'communications/push-notification', 3, 'admin');

-- LADT (requireAdmin)
INSERT INTO #MenuManifest VALUES ('LADT', 'diagram-3', 4, 'Editor', 'pencil', 'ladt/editor', 1, 'admin');
INSERT INTO #MenuManifest VALUES ('LADT', 'diagram-3', 4, 'Roster Swapper', 'arrow-left-right', 'ladt/roster-swapper', 2, 'admin');
INSERT INTO #MenuManifest VALUES ('LADT', 'diagram-3', 4, 'Pool Assignment', 'people', 'ladt/pool-assignment', 3, 'admin');

-- Scheduling (requireAdmin)
INSERT INTO #MenuManifest VALUES ('Scheduling', 'calendar', 5, 'Schedule Hub', 'grid', 'scheduling/schedule-hub', 1, 'admin');
INSERT INTO #MenuManifest VALUES ('Scheduling', 'calendar', 5, 'View Schedule', 'eye', 'scheduling/view-schedule', 2, 'admin');
INSERT INTO #MenuManifest VALUES ('Scheduling', 'calendar', 5, 'Rescheduler', 'arrow-repeat', 'scheduling/rescheduler', 3, 'admin');
INSERT INTO #MenuManifest VALUES ('Scheduling', 'calendar', 5, 'Mobile Scorers', 'phone', 'scheduling/mobile-scorers', 4, 'admin');

-- ARB (requireAdmin)
INSERT INTO #MenuManifest VALUES ('ARB', 'credit-card', 6, 'Health Check', 'heart-pulse', 'arb/health', 1, 'admin');

-- Tools (mixed guards)
INSERT INTO #MenuManifest VALUES ('Tools', 'tools', 7, 'US Lax Tester', 'check-circle', 'tools/uslax-test', 1, 'admin');
INSERT INTO #MenuManifest VALUES ('Tools', 'tools', 7, 'US Lax Rankings', 'trophy', 'tools/uslax-rankings', 2, 'admin');
INSERT INTO #MenuManifest VALUES ('Tools', 'tools', 7, 'Profile Migration', 'arrow-right', 'tools/profile-migration', 10, 'superuser');
INSERT INTO #MenuManifest VALUES ('Tools', 'tools', 7, 'Profile Editor', 'pencil-square', 'tools/profile-editor', 11, 'superuser');
INSERT INTO #MenuManifest VALUES ('Tools', 'tools', 7, 'Change Password', 'key', 'tools/change-password', 12, 'superuser');
INSERT INTO #MenuManifest VALUES ('Tools', 'tools', 7, 'Job Revenue', 'cash-stack', 'tools/customer-job-revenue', 13, 'superuser');

-- Store (requireAdmin)
INSERT INTO #MenuManifest VALUES ('Store', 'cart', 8, 'Store Admin', 'shop', 'store/admin', 1, 'admin');

DECLARE @manifestCount INT;
SELECT @manifestCount = COUNT(*) FROM #MenuManifest;
PRINT CONCAT('Menu manifest: ', @manifestCount, ' items');

-- ── 7. Fan out manifest to role-specific Nav records ──
-- Admin-level roles get 'admin' items. SuperUser gets 'admin' + 'superuser' items.

DECLARE @navId INT;
DECLARE @parentId INT;

-- Helper: generates menu for a given role
-- We iterate controllers, create parent, then insert children
DECLARE @roleId NVARCHAR(450);
DECLARE @guardLevel NVARCHAR(20);

-- Cursor over admin-level roles
DECLARE role_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT RoleId,
           CASE WHEN RoleId = @SuperUser THEN 'superuser' ELSE 'admin' END AS GuardLevel
    FROM nav.Nav
    WHERE JobId IS NULL
      AND RoleId IN (@Director, @SuperDirector, @SuperUser, @Staff, @RefAssignor, @StoreAdmin);

OPEN role_cursor;
FETCH NEXT FROM role_cursor INTO @roleId, @guardLevel;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @roleId AND JobId IS NULL;

    -- Get distinct controllers for this role's guard level
    DECLARE @ctrl NVARCHAR(50), @ctrlIcon NVARCHAR(50), @ctrlSort INT;

    DECLARE ctrl_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT Controller, Icon, CtrlSort
        FROM #MenuManifest
        WHERE Guard = 'admin' OR Guard <= @guardLevel
        ORDER BY CtrlSort;

    OPEN ctrl_cursor;
    FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Only create the L1 header if this role has visible children under it
        IF EXISTS (
            SELECT 1 FROM #MenuManifest
            WHERE Controller = @ctrl
              AND (Guard = 'admin' OR (@guardLevel = 'superuser' AND Guard = 'superuser'))
        )
        BEGIN
            -- Insert L1 parent
            INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified)
            VALUES (@navId, NULL, 1, @ctrlSort, @ctrl, @ctrlIcon, GETDATE());

            SET @parentId = SCOPE_IDENTITY();

            -- Insert L2 children
            INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified)
            SELECT @navId, @parentId, 1, ActionSort, [Action], ActionIcon, RouterLink, GETDATE()
            FROM #MenuManifest
            WHERE Controller = @ctrl
              AND (Guard = 'admin' OR (@guardLevel = 'superuser' AND Guard = 'superuser'))
            ORDER BY ActionSort;
        END

        FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort;
    END

    CLOSE ctrl_cursor;
    DEALLOCATE ctrl_cursor;

    FETCH NEXT FROM role_cursor INTO @roleId, @guardLevel;
END

CLOSE role_cursor;
DEALLOCATE role_cursor;

-- Non-admin roles: Nav records exist but no items (dashboard-only)
PRINT 'Non-admin roles (Family, Player, ClubRep, UnassignedAdult): no menu items';

-- ── 8. Restore preserved reporting items ──

IF @reportCount > 0
BEGIN
    -- Re-insert reporting items into the SuperUser nav (they were manually added)
    DECLARE @suNavId INT;
    SELECT @suNavId = NavId FROM nav.Nav WHERE RoleId = @SuperUser AND JobId IS NULL;

    -- Find or create an "Analyze" parent for reporting items
    DECLARE @analyzeParentId INT;

    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL)
    BEGIN
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified)
        VALUES (@suNavId, NULL, 1, 9, N'Analyze', N'bar-chart', GETDATE());
        SET @analyzeParentId = SCOPE_IDENTITY();
    END
    ELSE
        SELECT @analyzeParentId = NavItemId FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL;

    INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, NavigateUrl, [Target], Modified)
    SELECT @suNavId, @analyzeParentId, r.Active, r.SortOrder, r.[Text], r.IconName, r.RouterLink, r.NavigateUrl, r.[Target], GETDATE()
    FROM #ReportingItems r;

    PRINT CONCAT('Restored ', @reportCount, ' reporting item(s) under Analyze section');
END

-- ── 9. Summary ──

DECLARE @totalNavs INT;
DECLARE @totalItems INT;
DECLARE @parentItems INT;
SELECT @totalNavs = COUNT(*) FROM nav.Nav WHERE JobId IS NULL;
SELECT @totalItems = COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId WHERE n.JobId IS NULL;
SELECT @parentItems = COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId WHERE n.JobId IS NULL AND ni.ParentNavItemId IS NULL;

PRINT '';
PRINT '════════════════════════════════════════════';
PRINT CONCAT(' Nav Defaults Seeded: ', @totalNavs, ' navs, ', @totalItems, ' items (', @parentItems, ' parents, ', @totalItems - @parentItems, ' children)');
PRINT '════════════════════════════════════════════';
PRINT '';

-- Verification query
SELECT
    r.Name AS [Role],
    n.NavId,
    parent.Text AS [Section],
    parent.SortOrder AS [SectionOrder],
    child.Text AS [Item],
    child.SortOrder AS [ItemOrder],
    child.IconName AS [Icon],
    child.RouterLink AS [Route]
FROM [nav].[Nav] n
JOIN [dbo].[AspNetRoles] r ON n.RoleId = r.Id
LEFT JOIN [nav].[NavItem] parent ON parent.NavId = n.NavId AND parent.ParentNavItemId IS NULL
LEFT JOIN [nav].[NavItem] child  ON child.ParentNavItemId = parent.NavItemId
WHERE n.JobId IS NULL
ORDER BY r.Name, parent.SortOrder, child.SortOrder;

DROP TABLE #MenuManifest;
DROP TABLE #ReportingItems;

SET NOCOUNT OFF;
