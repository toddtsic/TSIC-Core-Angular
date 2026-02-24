-- ============================================================================
-- Nav Platform Defaults — Export Script
-- Generated: 2026-02-24 03:58:33 UTC
-- Idempotent: creates schema/tables if needed, clears and reseeds data.
-- Target: naive production system (no prior nav schema required).
-- ============================================================================

SET NOCOUNT ON;

-- ── 1. Create [nav] schema if not exists ──

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')
BEGIN
    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');
    PRINT 'Created [nav] schema';
END

-- ── 2. Create tables if not exists ──

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'Nav')
BEGIN
    CREATE TABLE [nav].[Nav]
    (
        [NavId]         INT IDENTITY(1,1)       NOT NULL,
        [RoleId]        NVARCHAR(450)           NOT NULL,
        [JobId]         UNIQUEIDENTIFIER        NULL,
        [Active]        BIT                     NOT NULL    DEFAULT 1,
        [Modified]      DATETIME2               NOT NULL    DEFAULT GETDATE(),
        [ModifiedBy]    NVARCHAR(450)           NULL,

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
    CREATE TABLE [nav].[NavItem]
    (
        [NavItemId]         INT IDENTITY(1,1)   NOT NULL,
        [NavId]             INT                 NOT NULL,
        [ParentNavItemId]   INT                 NULL,
        [Active]            BIT                 NOT NULL    DEFAULT 1,
        [SortOrder]         INT                 NOT NULL    DEFAULT 0,
        [Text]              NVARCHAR(200)       NOT NULL,
        [IconName]          NVARCHAR(100)       NULL,
        [RouterLink]        NVARCHAR(500)       NULL,
        [NavigateUrl]       NVARCHAR(500)       NULL,
        [Target]            NVARCHAR(20)        NULL,
        [Modified]          DATETIME2           NOT NULL    DEFAULT GETDATE(),
        [ModifiedBy]        NVARCHAR(450)       NULL,

        CONSTRAINT [PK_nav_NavItem] PRIMARY KEY CLUSTERED ([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_NavId] FOREIGN KEY ([NavId]) REFERENCES [nav].[Nav] ([NavId]) ON DELETE CASCADE,
        CONSTRAINT [FK_nav_NavItem_ParentNavItemId] FOREIGN KEY ([ParentNavItemId]) REFERENCES [nav].[NavItem] ([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])
    );
    PRINT 'Created table: nav.NavItem';
END

-- ── 3. Clear existing platform default data ──

DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);
DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;
PRINT 'Cleared existing platform default navs and items';

-- ── 4. Insert Nav rows ──

SET IDENTITY_INSERT [nav].[Nav] ON;

INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (6, '6A26171F-4D94-4928-94FA-2FEFD42C3C3E', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (1, 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (4, 'E0A8A5C3-A36C-417F-8312-E7083F1AA5A0', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (5, 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (7, '122075A3-2C42-4092-97F1-9673DF5B6A2C', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (8, '1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (9, '5B9B7055-4530-4E46-B403-1019FD8B8418', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (10, 'CE2CB370-5880-4624-A43E-048379C64331', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (2, '7B9EB503-53C9-44FA-94A0-17760C512440', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (3, 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', NULL, 1, GETDATE());

SET IDENTITY_INSERT [nav].[Nav] OFF;
PRINT 'Inserted 10 platform default nav(s)';

-- ── 5. Insert NavItem rows (parents first, then children) ──

SET IDENTITY_INSERT [nav].[NavItem] ON;

INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (1, 1, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (4, 1, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (36, 1, NULL, 1, 3, N'Scheduling', N'receipt', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (8, 2, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (11, 2, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (15, 3, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (18, 3, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (23, 3, NULL, 1, 3, N'Analyze', N'bar-chart', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (63, 3, NULL, 1, 4, N'LADT', NULL, NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (26, 3, NULL, 1, 5, N'Scheduling', N'receipt', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (51, 3, NULL, 1, 6, N'ARB', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (46, 3, NULL, 1, 7, N'Tools', N'tools', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (54, 3, NULL, 1, 8, N'X-Job', NULL, NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (60, 3, NULL, 1, 9, N'Merch', N'cart', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (2, 1, 1, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (3, 1, 1, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (5, 1, 4, 1, 1, N'Job', N'briefcase', N'admin/job-config', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (6, 1, 4, 1, 2, N'Administrators', N'person-badge', N'configure/administrators', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (7, 1, 4, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (37, 1, 36, 1, 1, N'1) Pool Assignment', N'receipt', N'admin/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (38, 1, 36, 1, 2, N'2) Manage Fields', N'map', N'scheduling/fields', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (39, 1, 36, 1, 3, N'3) Manage Pairings', N'list', N'scheduling/pairings', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (40, 1, 36, 1, 4, N'4) Manage Timeslots', N'clock', N'scheduling/timeslots', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (41, 1, 36, 1, 5, N'5) Schedule Games', N'grid', N'scheduling/schedule-division', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (42, 1, 36, 1, 6, N'View Schedule', N'list', N'scheduling/view-schedule', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (43, 1, 36, 1, 7, N'Rescheduler', N'grid', N'scheduling/rescheduler', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (44, 1, 36, 1, 8, N'QA Schedule', N'receipt', N'scheduling/qa-results', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (9, 2, 8, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (10, 2, 8, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (12, 2, 11, 1, 1, N'Job', N'briefcase', N'admin/job-config', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (13, 2, 11, 1, 2, N'Administrators', N'person-badge', N'configure/administrators', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (14, 2, 11, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (16, 3, 15, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (17, 3, 15, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (45, 3, 15, 1, 3, N'Email Log', N'envelope', N'admin/email-log', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (19, 3, 18, 1, 1, N'Job', N'briefcase', N'admin/job-config', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (20, 3, 18, 1, 2, N'Administrators', N'person-badge', N'configure/administrators', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (21, 3, 18, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (22, 3, 18, 1, 4, N'Menus', N'list', N'admin/nav-editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (67, 3, 18, 1, 5, N'Widgets', N'tools', N'admin/widget-editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (68, 3, 18, 1, 6, N'Job Clone', N'tools', N'admin/job-clone', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (69, 3, 18, 1, 7, N'Theme', N'tools', N'admin/theme', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (24, 3, 23, 0, 1, N'new child', NULL, NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (25, 3, 23, 1, 2, N'Logs', N'tools', N'admin/log-viewer', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (65, 3, 63, 1, 2, N'Roster Swapper', N'people', N'admin/roster-swapper', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (66, 3, 63, 1, 2, N'Pool Assignment', N'tools', N'admin/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (28, 3, 26, 1, 2, N'1) Pool Assignment', N'receipt', N'admin/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (29, 3, 26, 1, 2, N'2) Manage Fields', N'map', N'scheduling/fields', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (30, 3, 26, 1, 3, N'3) Manage Pairings', N'list', N'scheduling/pairings', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (31, 3, 26, 1, 4, N'4) Manage Timeslots', N'clock', N'scheduling/timeslots', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (32, 3, 26, 1, 5, N'5) Schedule Games', N'grid', N'scheduling/schedule-division', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (33, 3, 26, 1, 6, N'View Schedule', N'list', N'scheduling/view-schedule', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (34, 3, 26, 1, 7, N'Rescheduler', N'grid', N'scheduling/rescheduler', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (35, 3, 26, 1, 8, N'QA Schedule', N'receipt', N'scheduling/qa-results', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (49, 3, 26, 1, 9, N'Mobile Scorers', N'pencil', N'admin/mobile-scorers', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (70, 3, 26, 1, 10, N'Auto-Schedule', N'tools', N'scheduling/auto-build', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (53, 3, 51, 1, 2, N'Check Status', N'gear', N'admin/arb-health', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (47, 3, 46, 0, 1, N'new child', NULL, NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (48, 3, 46, 1, 2, N'US Lax Number Tester', N'tools', N'admin/uslax-test', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (56, 3, 54, 1, 2, N'Configure Customers', N'gear', N'admin/customer-configure', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (57, 3, 54, 1, 2, N'Configure Customer Groups', N'gear', N'configure/customer-groups', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (58, 3, 54, 1, 3, N'Reg Form Profile Migration', N'tools', N'admin/profile-migration', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (59, 3, 54, 1, 4, N'Reg Form Profile Editor', N'tools', N'admin/profile-editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (61, 3, 60, 0, 1, N'new child', NULL, NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (62, 3, 60, 1, 2, N'Store', NULL, N'admin/store', NULL, NULL, GETDATE());

SET IDENTITY_INSERT [nav].[NavItem] OFF;
PRINT 'Inserted 65 nav item(s) (14 parents, 51 children)';

-- ── 6. Verification ──

PRINT '';
PRINT '════════════════════════════════════════════';
PRINT ' Nav Defaults Export — Complete';
PRINT '════════════════════════════════════════════';
PRINT '';

SELECT
    r.Name AS [Role],
    n.NavId,
    n.Active AS [NavActive],
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

SET NOCOUNT OFF;
