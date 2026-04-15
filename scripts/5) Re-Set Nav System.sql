-- ============================================================================
-- 5) Re-Set Nav System.sql
-- Generated: 2026-04-15 15:11:04 by 5) Re-Set Nav System.ps1
-- Role-scoped manifest; no ladder, no VisibilityRules emitted.
-- Preserves: job-level overrides, reporting items, existing visibility rules.
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
DECLARE @Staff NVARCHAR(450) = '1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA';
DECLARE @RefAssignor NVARCHAR(450) = '122075A3-2C42-4092-97F1-9673DF5B6A2C';
DECLARE @StoreAdmin NVARCHAR(450) = '5B9B7055-4530-4E46-B403-1019FD8B8418';
DECLARE @Family NVARCHAR(450) = 'E0A8A5C3-A36C-417F-8312-E7083F1AA5A0';
DECLARE @Player NVARCHAR(450) = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
DECLARE @ClubRep NVARCHAR(450) = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
DECLARE @UnassignedAdult NVARCHAR(450) = 'C92D71A9-464D-40C5-BA35-DFD9111CC7EA';

-- -- 3. Preserve reporting items + visibility rules ----------------------
DECLARE @cnt INT;

IF OBJECT_ID('tempdb..#ReportingItems') IS NOT NULL DROP TABLE #ReportingItems;
SELECT ni.NavItemId, ni.NavId, ni.ParentNavItemId, ni.Active, ni.SortOrder,
       ni.[Text], ni.IconName, ni.RouterLink, ni.NavigateUrl, ni.[Target]
INTO #ReportingItems
FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId
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
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@Staff, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@RefAssignor, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@StoreAdmin, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@Family, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@Player, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@ClubRep, NULL, 1, GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@UnassignedAdult, NULL, 1, GETDATE());
PRINT 'Inserted 10 Nav records';

-- -- 6. Admin manifest (Director / SuperDirector / SuperUser) -----------
IF OBJECT_ID('tempdb..#AdminManifest') IS NOT NULL DROP TABLE #AdminManifest;
CREATE TABLE #AdminManifest (
    Controller   NVARCHAR(50)  NOT NULL,
    Icon         NVARCHAR(50)  NULL,
    CtrlSort     INT           NOT NULL,
    [Action]     NVARCHAR(100) NOT NULL,
    ActionIcon   NVARCHAR(50)  NULL,
    RouterLink   NVARCHAR(200) NOT NULL,
    ActionSort   INT           NOT NULL,
    ForDirector  BIT           NOT NULL,
    ForSuperDir  BIT           NOT NULL,
    ForSuperUser BIT           NOT NULL
);
INSERT INTO #AdminManifest VALUES (N'Search', N'search', 1, N'Registrations', N'people', N'search/registrations', 1, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Search', N'search', 1, N'Teams', N'shield', N'search/teams', 2, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Job', N'briefcase', N'configure/job', 1, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Age Ranges', N'sliders', N'configure/age-ranges', 2, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Discount Codes', N'tags', N'configure/discount-codes', 3, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Uniform Upload', N'upload', N'configure/uniform-upload', 4, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Administrators', N'person-badge', N'configure/administrators', 10, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Customer Groups', N'people', N'configure/customer-groups', 11, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Dropdown Options', N'list', N'configure/ddl-options', 12, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Customers', N'building', N'configure/customers', 13, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Theme', N'palette', N'configure/theme', 14, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Menus', N'list', N'configure/nav-editor', 15, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Widgets', N'grid', N'configure/widget-editor', 16, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Configure', N'gear', 2, N'Job Clone', N'copy', N'configure/job-clone', 17, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Communications', N'envelope', 3, N'Bulletins', N'megaphone', N'communications/bulletins', 1, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Communications', N'envelope', 3, N'Email Log', N'envelope-open', N'communications/email-log', 2, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Communications', N'envelope', 3, N'Push Notification', N'bell', N'communications/push-notification', 3, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'LADT', N'diagram-3', 4, N'Editor', N'pencil', N'ladt/editor', 1, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'LADT', N'diagram-3', 4, N'Roster Swapper', N'arrow-left-right', N'ladt/roster-swapper', 2, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'LADT', N'diagram-3', 4, N'Pool Assignment', N'people', N'ladt/pool-assignment', 3, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Schedule Hub', N'grid', N'scheduling/schedule-hub', 1, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'View Schedule', N'eye', N'scheduling/view-schedule', 2, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Rescheduler', N'arrow-repeat', N'scheduling/rescheduler', 3, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Scheduling', N'calendar', 5, N'Mobile Scorers', N'phone', N'scheduling/mobile-scorers', 4, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'ARB', N'credit-card', 6, N'Health Check', N'heart-pulse', N'arb/health', 1, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 7, N'US Lax Tester', N'check-circle', N'tools/uslax-test', 1, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 7, N'US Lax Rankings', N'trophy', N'tools/uslax-rankings', 2, 1, 1, 1);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 7, N'Profile Migration', N'arrow-right', N'tools/profile-migration', 10, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 7, N'Profile Editor', N'pencil-square', N'tools/profile-editor', 11, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 7, N'Change Password', N'key', N'tools/change-password', 12, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Tools', N'tools', 7, N'Job Revenue', N'cash-stack', N'tools/customer-job-revenue', 13, 0, 0, 1);
INSERT INTO #AdminManifest VALUES (N'Store', N'cart', 8, N'Store Admin', N'shop', N'store/admin', 1, 1, 1, 1);

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

    DECLARE @ctrl NVARCHAR(50), @ctrlIcon NVARCHAR(50), @ctrlSort INT;
    DECLARE ctrl_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT Controller, Icon, CtrlSort
        FROM #AdminManifest
        WHERE CASE @roleId
                 WHEN @Director      THEN ForDirector
                 WHEN @SuperDirector THEN ForSuperDir
                 WHEN @SuperUser     THEN ForSuperUser
                 ELSE 0
             END = 1
        ORDER BY CtrlSort;

    OPEN ctrl_cursor;
    FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified)
        VALUES (@navId, NULL, 1, @ctrlSort, @ctrl, @ctrlIcon, GETDATE());
        SET @parentId = SCOPE_IDENTITY();

        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified)
        SELECT @navId, @parentId, 1, ActionSort, [Action], ActionIcon, RouterLink, GETDATE()
        FROM #AdminManifest
        WHERE Controller = @ctrl
          AND CASE @roleId
                 WHEN @Director      THEN ForDirector
                 WHEN @SuperDirector THEN ForSuperDir
                 WHEN @SuperUser     THEN ForSuperUser
                 ELSE 0
             END = 1
        ORDER BY ActionSort;

        FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort;
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

-- -- 9. Family ----------------------------------------------------------
-- Family: Registration + Store
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @Family AND JobId IS NULL;
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 1, N'Registration', N'pencil-square', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 1, N'Register Player', N'person-plus', N'registration/entry', GETDATE());
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 2, N'Pay Balance Due', N'credit-card', N'registration/player?step=payment', GETDATE());
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 3, N'Store', N'cart', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 1, N'Event Store', N'shop', N'store', GETDATE());
PRINT 'Family: Registration + Store';

-- -- 10. ClubRep --------------------------------------------------------
-- ClubRep: Registration + Accounting + Rosters
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @ClubRep AND JobId IS NULL;
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 1, N'Registration', N'pencil-square', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 3, N'Register Teams', N'shield-plus', N'registration/entry', GETDATE());
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 2, N'Accounting', N'cash-stack', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 1, N'Team Accounting', N'receipt', N'registration/team?step=payment', GETDATE());
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 4, N'Rosters', N'people', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 1, N'Club Rosters', N'people', N'rosters/club', GETDATE());
PRINT 'ClubRep: Registration + Accounting + Rosters';

-- -- 11. Player ---------------------------------------------------------
-- Player: View Rosters
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @Player AND JobId IS NULL;
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 4, N'Rosters', N'people', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 2, N'View Rosters', N'people', N'rosters/view-rosters', GETDATE());
PRINT 'Player: View Rosters';

-- -- 12. Staff ----------------------------------------------------------
-- Staff: View Rosters
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @Staff AND JobId IS NULL;
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) VALUES (@navId, NULL, 1, 4, N'Rosters', N'people', GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) VALUES (@navId, @parentId, 1, 2, N'View Rosters', N'people', N'rosters/view-rosters', GETDATE());
PRINT 'Staff: View Rosters';

-- UnassignedAdult: Nav row from section 5; no items emitted (intentional).
PRINT 'UnassignedAdult: no menu items (intentional)';

-- -- 13. Restore preserved reporting items ------------------------------
SELECT @cnt = COUNT(*) FROM #ReportingItems;
IF @cnt > 0
BEGIN
    DECLARE @suNavId INT;
    SELECT @suNavId = NavId FROM nav.Nav WHERE RoleId = @SuperUser AND JobId IS NULL;

    DECLARE @apId INT;
    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL)
    BEGIN
        INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified)
        VALUES (@suNavId, NULL, 1, 9, N'Analyze', N'bar-chart', GETDATE());
        SET @apId = SCOPE_IDENTITY();
    END
    ELSE
        SELECT @apId = NavItemId FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL;

    INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, NavigateUrl, [Target], Modified)
    SELECT @suNavId, @apId, r.Active, r.SortOrder, r.[Text], r.IconName, r.RouterLink, r.NavigateUrl, r.[Target], GETDATE()
    FROM #ReportingItems r;
    PRINT CONCAT('Restored ', @cnt, ' reporting item(s) under Analyze');
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

