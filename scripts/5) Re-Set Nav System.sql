-- ============================================================================
-- 5) Re-Set Nav System.sql
-- Generated: 2026-04-06 08:11:51 by 5) Re-Set Nav System.ps1
-- Idempotent: safe to run multiple times on any target database
-- Preserves: job-level overrides, reporting items, visibility rules
-- ============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- ================================================================
-- 1. Ensure schema + tables exist
-- ================================================================
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

-- ================================================================
-- 2. Preserve reporting items + visibility rules
-- ================================================================
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

-- ================================================================
-- 3. Clear platform defaults (job overrides preserved)
-- ================================================================
DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);
DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;
PRINT 'Cleared platform defaults (job overrides preserved)';

-- ================================================================
-- 4. Insert Nav records (one per role)
-- ================================================================
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('5B9B7055-4530-4E46-B403-1019FD8B8418',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('DAC0C570-94AA-4A88-8D73-6034F1F72F3A',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('122075A3-2C42-4092-97F1-9673DF5B6A2C',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('FF4D1C27-F6DA-4745-98CC-D7E8121A5D06',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('CE2CB370-5880-4624-A43E-048379C64331',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('6A26171F-4D94-4928-94FA-2FEFD42C3C3E',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('E0A8A5C3-A36C-417F-8312-E7083F1AA5A0',NULL,1,GETDATE());
INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('7B9EB503-53C9-44FA-94A0-17760C512440',NULL,1,GETDATE());
PRINT 'Inserted 10 Nav records';

-- ================================================================
-- 5. Insert nav items per admin role
-- ================================================================
DECLARE @navId INT, @parentId INT;

-- --- Director ---
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06' AND JobId IS NULL;
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,6,N'ARB',N'credit-card',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Health Check',N'heart-pulse',N'arb/health',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,3,N'Communications',N'envelope',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bulletins',N'megaphone',N'communications/bulletins',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Email Log',N'envelope-open',N'communications/email-log',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Push Notification',N'bell',N'communications/push-notification',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,2,N'Configure',N'gear',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Age Ranges',N'sliders',N'configure/age-ranges',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'Discount Codes',N'tags',N'configure/discount-codes',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Job Settings',N'briefcase',N'configure/job',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,4,N'LADT',N'diagram-3',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Editor',N'pencil',N'ladt/editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Pool Assignment',N'people',N'ladt/pool-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Roster Swapper',N'arrow-left-right',N'ladt/roster-swapper',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,5,N'Scheduling',N'calendar',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bracket Seeds',N'trophy',N'scheduling/bracket-seeds',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Fields',N'geo-alt',N'scheduling/fields',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Master Schedule',N'table',N'scheduling/master-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,4,N'Mobile Scorers',N'phone',N'scheduling/mobile-scorers',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Pairings',N'arrow-left-right',N'scheduling/pairings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'QA Results',N'clipboard-check',N'scheduling/qa-results',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Referee Assignment',N'person-check',N'scheduling/referee-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,8,N'Referee Calendar',N'calendar-check',N'scheduling/referee-calendar',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,9,N'Rescheduler',N'arrow-repeat',N'scheduling/rescheduler',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,10,N'Schedule Hub',N'grid',N'scheduling/schedule-hub',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,11,N'Timeslots',N'clock',N'scheduling/timeslots',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,12,N'Tournament Parking',N'p-circle',N'scheduling/tournament-parking',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,13,N'View Schedule',N'eye',N'scheduling/view-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,1,N'Search',N'search',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Registrations',N'people',N'search/registrations',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Teams',N'shield',N'search/teams',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,8,N'Store',N'cart',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Store Admin',N'shop',N'store/admin',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,7,N'Tools',N'tools',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Uniform Upload',N'upload',N'tools/uniform-upload',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'US Lax Rankings',N'trophy',N'tools/uslax-rankings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'US Lax Tester',N'check-circle',N'tools/uslax-test',GETDATE());

-- --- SuperDirector ---
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = '7B9EB503-53C9-44FA-94A0-17760C512440' AND JobId IS NULL;
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,6,N'ARB',N'credit-card',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Health Check',N'heart-pulse',N'arb/health',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,3,N'Communications',N'envelope',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bulletins',N'megaphone',N'communications/bulletins',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Email Log',N'envelope-open',N'communications/email-log',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Push Notification',N'bell',N'communications/push-notification',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,2,N'Configure',N'gear',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Age Ranges',N'sliders',N'configure/age-ranges',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'Discount Codes',N'tags',N'configure/discount-codes',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Job Settings',N'briefcase',N'configure/job',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,4,N'LADT',N'diagram-3',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Editor',N'pencil',N'ladt/editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Pool Assignment',N'people',N'ladt/pool-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Roster Swapper',N'arrow-left-right',N'ladt/roster-swapper',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,5,N'Scheduling',N'calendar',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bracket Seeds',N'trophy',N'scheduling/bracket-seeds',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Fields',N'geo-alt',N'scheduling/fields',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Master Schedule',N'table',N'scheduling/master-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,4,N'Mobile Scorers',N'phone',N'scheduling/mobile-scorers',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Pairings',N'arrow-left-right',N'scheduling/pairings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'QA Results',N'clipboard-check',N'scheduling/qa-results',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Referee Assignment',N'person-check',N'scheduling/referee-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,8,N'Referee Calendar',N'calendar-check',N'scheduling/referee-calendar',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,9,N'Rescheduler',N'arrow-repeat',N'scheduling/rescheduler',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,10,N'Schedule Hub',N'grid',N'scheduling/schedule-hub',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,11,N'Timeslots',N'clock',N'scheduling/timeslots',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,12,N'Tournament Parking',N'p-circle',N'scheduling/tournament-parking',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,13,N'View Schedule',N'eye',N'scheduling/view-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,1,N'Search',N'search',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Registrations',N'people',N'search/registrations',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Teams',N'shield',N'search/teams',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,8,N'Store',N'cart',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Store Admin',N'shop',N'store/admin',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,7,N'Tools',N'tools',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Uniform Upload',N'upload',N'tools/uniform-upload',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'US Lax Rankings',N'trophy',N'tools/uslax-rankings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'US Lax Tester',N'check-circle',N'tools/uslax-test',GETDATE());

-- --- SuperUser ---
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9' AND JobId IS NULL;
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,6,N'ARB',N'credit-card',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Health Check',N'heart-pulse',N'arb/health',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,3,N'Communications',N'envelope',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bulletins',N'megaphone',N'communications/bulletins',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Email Log',N'envelope-open',N'communications/email-log',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Push Notification',N'bell',N'communications/push-notification',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,2,N'Configure',N'gear',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Administrators',N'person-badge',N'configure/administrators',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Age Ranges',N'sliders',N'configure/age-ranges',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Customer Groups',N'people',N'configure/customer-groups',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,4,N'Customers',N'building',N'configure/customers',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Dropdown Options',N'list',N'configure/ddl-options',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'Discount Codes',N'tags',N'configure/discount-codes',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Job Settings',N'briefcase',N'configure/job',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,8,N'Job Clone',N'copy',N'configure/job-clone',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,9,N'Menus',N'list',N'configure/nav-editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,10,N'Theme',N'palette',N'configure/theme',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,11,N'Widgets',N'grid',N'configure/widget-editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,4,N'LADT',N'diagram-3',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Editor',N'pencil',N'ladt/editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Pool Assignment',N'people',N'ladt/pool-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Roster Swapper',N'arrow-left-right',N'ladt/roster-swapper',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,5,N'Scheduling',N'calendar',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bracket Seeds',N'trophy',N'scheduling/bracket-seeds',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Fields',N'geo-alt',N'scheduling/fields',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Master Schedule',N'table',N'scheduling/master-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,4,N'Mobile Scorers',N'phone',N'scheduling/mobile-scorers',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Pairings',N'arrow-left-right',N'scheduling/pairings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'QA Results',N'clipboard-check',N'scheduling/qa-results',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Referee Assignment',N'person-check',N'scheduling/referee-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,8,N'Referee Calendar',N'calendar-check',N'scheduling/referee-calendar',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,9,N'Rescheduler',N'arrow-repeat',N'scheduling/rescheduler',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,10,N'Schedule Hub',N'grid',N'scheduling/schedule-hub',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,11,N'Timeslots',N'clock',N'scheduling/timeslots',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,12,N'Tournament Parking',N'p-circle',N'scheduling/tournament-parking',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,13,N'View Schedule',N'eye',N'scheduling/view-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,1,N'Search',N'search',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Registrations',N'people',N'search/registrations',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Teams',N'shield',N'search/teams',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,8,N'Store',N'cart',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Store Admin',N'shop',N'store/admin',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,7,N'Tools',N'tools',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Change Password',N'key',N'tools/change-password',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Job Revenue',N'cash-stack',N'tools/customer-job-revenue',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Profile Editor',N'pencil-square',N'tools/profile-editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,4,N'Profile Migration',N'arrow-right',N'tools/profile-migration',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Uniform Upload',N'upload',N'tools/uniform-upload',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'US Lax Rankings',N'trophy',N'tools/uslax-rankings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'US Lax Tester',N'check-circle',N'tools/uslax-test',GETDATE());

-- --- Staff ---
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = '1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA' AND JobId IS NULL;
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,6,N'ARB',N'credit-card',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Health Check',N'heart-pulse',N'arb/health',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,3,N'Communications',N'envelope',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bulletins',N'megaphone',N'communications/bulletins',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Email Log',N'envelope-open',N'communications/email-log',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Push Notification',N'bell',N'communications/push-notification',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,2,N'Configure',N'gear',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Age Ranges',N'sliders',N'configure/age-ranges',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'Discount Codes',N'tags',N'configure/discount-codes',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Job Settings',N'briefcase',N'configure/job',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,4,N'LADT',N'diagram-3',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Editor',N'pencil',N'ladt/editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Pool Assignment',N'people',N'ladt/pool-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Roster Swapper',N'arrow-left-right',N'ladt/roster-swapper',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,5,N'Scheduling',N'calendar',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bracket Seeds',N'trophy',N'scheduling/bracket-seeds',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Fields',N'geo-alt',N'scheduling/fields',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Master Schedule',N'table',N'scheduling/master-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,4,N'Mobile Scorers',N'phone',N'scheduling/mobile-scorers',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Pairings',N'arrow-left-right',N'scheduling/pairings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'QA Results',N'clipboard-check',N'scheduling/qa-results',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Referee Assignment',N'person-check',N'scheduling/referee-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,8,N'Referee Calendar',N'calendar-check',N'scheduling/referee-calendar',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,9,N'Rescheduler',N'arrow-repeat',N'scheduling/rescheduler',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,10,N'Schedule Hub',N'grid',N'scheduling/schedule-hub',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,11,N'Timeslots',N'clock',N'scheduling/timeslots',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,12,N'Tournament Parking',N'p-circle',N'scheduling/tournament-parking',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,13,N'View Schedule',N'eye',N'scheduling/view-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,1,N'Search',N'search',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Registrations',N'people',N'search/registrations',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Teams',N'shield',N'search/teams',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,8,N'Store',N'cart',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Store Admin',N'shop',N'store/admin',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,7,N'Tools',N'tools',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Uniform Upload',N'upload',N'tools/uniform-upload',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'US Lax Rankings',N'trophy',N'tools/uslax-rankings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'US Lax Tester',N'check-circle',N'tools/uslax-test',GETDATE());

-- --- RefAssignor ---
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = '122075A3-2C42-4092-97F1-9673DF5B6A2C' AND JobId IS NULL;
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,6,N'ARB',N'credit-card',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Health Check',N'heart-pulse',N'arb/health',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,3,N'Communications',N'envelope',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bulletins',N'megaphone',N'communications/bulletins',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Email Log',N'envelope-open',N'communications/email-log',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Push Notification',N'bell',N'communications/push-notification',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,2,N'Configure',N'gear',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Age Ranges',N'sliders',N'configure/age-ranges',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'Discount Codes',N'tags',N'configure/discount-codes',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Job Settings',N'briefcase',N'configure/job',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,4,N'LADT',N'diagram-3',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Editor',N'pencil',N'ladt/editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Pool Assignment',N'people',N'ladt/pool-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Roster Swapper',N'arrow-left-right',N'ladt/roster-swapper',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,5,N'Scheduling',N'calendar',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bracket Seeds',N'trophy',N'scheduling/bracket-seeds',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Fields',N'geo-alt',N'scheduling/fields',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Master Schedule',N'table',N'scheduling/master-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,4,N'Mobile Scorers',N'phone',N'scheduling/mobile-scorers',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Pairings',N'arrow-left-right',N'scheduling/pairings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'QA Results',N'clipboard-check',N'scheduling/qa-results',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Referee Assignment',N'person-check',N'scheduling/referee-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,8,N'Referee Calendar',N'calendar-check',N'scheduling/referee-calendar',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,9,N'Rescheduler',N'arrow-repeat',N'scheduling/rescheduler',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,10,N'Schedule Hub',N'grid',N'scheduling/schedule-hub',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,11,N'Timeslots',N'clock',N'scheduling/timeslots',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,12,N'Tournament Parking',N'p-circle',N'scheduling/tournament-parking',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,13,N'View Schedule',N'eye',N'scheduling/view-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,1,N'Search',N'search',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Registrations',N'people',N'search/registrations',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Teams',N'shield',N'search/teams',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,8,N'Store',N'cart',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Store Admin',N'shop',N'store/admin',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,7,N'Tools',N'tools',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Uniform Upload',N'upload',N'tools/uniform-upload',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'US Lax Rankings',N'trophy',N'tools/uslax-rankings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'US Lax Tester',N'check-circle',N'tools/uslax-test',GETDATE());

-- --- StoreAdmin ---
SELECT @navId = NavId FROM nav.Nav WHERE RoleId = '5B9B7055-4530-4E46-B403-1019FD8B8418' AND JobId IS NULL;
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,6,N'ARB',N'credit-card',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Health Check',N'heart-pulse',N'arb/health',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,3,N'Communications',N'envelope',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bulletins',N'megaphone',N'communications/bulletins',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Email Log',N'envelope-open',N'communications/email-log',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Push Notification',N'bell',N'communications/push-notification',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,2,N'Configure',N'gear',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Age Ranges',N'sliders',N'configure/age-ranges',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'Discount Codes',N'tags',N'configure/discount-codes',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Job Settings',N'briefcase',N'configure/job',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,4,N'LADT',N'diagram-3',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Editor',N'pencil',N'ladt/editor',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Pool Assignment',N'people',N'ladt/pool-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Roster Swapper',N'arrow-left-right',N'ladt/roster-swapper',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,5,N'Scheduling',N'calendar',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Bracket Seeds',N'trophy',N'scheduling/bracket-seeds',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Fields',N'geo-alt',N'scheduling/fields',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,3,N'Master Schedule',N'table',N'scheduling/master-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,4,N'Mobile Scorers',N'phone',N'scheduling/mobile-scorers',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Pairings',N'arrow-left-right',N'scheduling/pairings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'QA Results',N'clipboard-check',N'scheduling/qa-results',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'Referee Assignment',N'person-check',N'scheduling/referee-assignment',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,8,N'Referee Calendar',N'calendar-check',N'scheduling/referee-calendar',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,9,N'Rescheduler',N'arrow-repeat',N'scheduling/rescheduler',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,10,N'Schedule Hub',N'grid',N'scheduling/schedule-hub',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,11,N'Timeslots',N'clock',N'scheduling/timeslots',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,12,N'Tournament Parking',N'p-circle',N'scheduling/tournament-parking',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,13,N'View Schedule',N'eye',N'scheduling/view-schedule',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,1,N'Search',N'search',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Registrations',N'people',N'search/registrations',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,2,N'Teams',N'shield',N'search/teams',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,8,N'Store',N'cart',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,1,N'Store Admin',N'shop',N'store/admin',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,7,N'Tools',N'tools',GETDATE());
SET @parentId = SCOPE_IDENTITY();
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,5,N'Uniform Upload',N'upload',N'tools/uniform-upload',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,6,N'US Lax Rankings',N'trophy',N'tools/uslax-rankings',GETDATE());
INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,7,N'US Lax Tester',N'check-circle',N'tools/uslax-test',GETDATE());

-- ================================================================
-- 6. Restore reporting items
-- ================================================================
SELECT @cnt = COUNT(*) FROM #ReportingItems;
IF @cnt > 0
BEGIN
    DECLARE @suNavId INT;
    SELECT @suNavId = NavId FROM nav.Nav WHERE RoleId = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9' AND JobId IS NULL;

    DECLARE @apId INT;
    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL)
    BEGIN
        INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified)
        VALUES(@suNavId,NULL,1,9,N'Analyze',N'bar-chart',GETDATE());
        SET @apId = SCOPE_IDENTITY();
    END
    ELSE
        SELECT @apId = NavItemId FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL;

    INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,NavigateUrl,[Target],Modified)
    SELECT @suNavId, @apId, r.Active, r.SortOrder, r.[Text], r.IconName, r.RouterLink, r.NavigateUrl, r.[Target], GETDATE()
    FROM #ReportingItems r;
    PRINT CONCAT('Restored ', @cnt, ' reporting item(s) under Analyze section');
END
DROP TABLE #ReportingItems;

-- ================================================================
-- 7. Restore visibility rules
-- ================================================================
UPDATE ni
SET ni.VisibilityRules = vr.VisibilityRules
FROM nav.NavItem ni
JOIN nav.Nav n ON ni.NavId = n.NavId
JOIN #VisRules vr ON vr.RoleId = n.RoleId AND vr.RouterLink = ni.RouterLink
WHERE n.JobId IS NULL;
PRINT CONCAT('Restored ', @@ROWCOUNT, ' visibility rule(s)');
DROP TABLE #VisRules;

-- ================================================================
-- 8. Summary
-- ================================================================
COMMIT TRANSACTION;

SELECT
    (SELECT COUNT(*) FROM nav.Nav WHERE JobId IS NULL) AS [Platform Navs],
    (SELECT COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId=n.NavId WHERE n.JobId IS NULL) AS [Platform Items],
    (SELECT COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId=n.NavId WHERE n.JobId IS NULL AND ni.ParentNavItemId IS NULL) AS [Sections],
    (SELECT COUNT(*) FROM nav.Nav WHERE JobId IS NOT NULL) AS [Job Overrides (preserved)];

PRINT 'Nav system reset complete.';

