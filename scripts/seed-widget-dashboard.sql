-- ============================================================================
-- Widget Dashboard — Complete Setup Script
--
-- THE ONE SCRIPT TO RUN after restoring a prod backup to dev, or on a fresh DB.
-- Handles all three scenarios:
--   A) Fresh DB — no [widgets] schema at all
--   B) Prod restore — old schema with [Section] column
--   C) Already migrated — [Workspace] column exists
--
-- Fully idempotent. Safe to run repeatedly.
--
-- What it does:
--   1. Creates [widgets] schema + 4 tables (if not exists)
--   2. Migrates [Section] → [Workspace] column (if old schema)
--   3. Updates constraints (CHECK + UNIQUE KEY)
--   4. Seeds 11 workspace categories, 19 widget definitions
--   5. Seeds WidgetDefault entries per role-workspace matrix × ALL JobTypes
--
-- Supersedes: seed-anonymous-widgets.sql, seed-chart-widgets.sql,
--             seed-workspace-evolution.sql
--
-- Prerequisites:
--   - reference.JobTypes populated
--   - dbo.AspNetRoles populated
--
-- Workspaces: public, dashboard, job-config, player-reg, team-reg,
--             scheduling, fin-per-job, fin-per-customer
--
-- Roles seeded:
--   Anonymous        → public (banner + bulletins)
--   SuperUser        → all workspaces
--   SuperDirector    → all workspaces
--   Director         → all except fin-per-customer
--   Club Rep         → dashboard, player-reg, team-reg, scheduling (view)
--   Player           → dashboard, player-reg, scheduling (view)
--   Staff            → dashboard, player-reg (view), team-reg (view), scheduling (view)
--   Unassigned Adult → dashboard only
-- ============================================================================

SET NOCOUNT ON;

-- ════════════════════════════════════════════════════════════
-- BATCH 1: SCHEMA + TABLES + SCHEMA MIGRATION
-- (Separate batch so sp_rename takes effect before Batch 2)
-- ════════════════════════════════════════════════════════════

-- 1a. Create schema
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'widgets')
BEGIN
    EXEC('CREATE SCHEMA [widgets] AUTHORIZATION [dbo]');
    PRINT 'Created [widgets] schema';
END

-- 1b. Create WidgetCategory (with Workspace column — new schema)
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'WidgetCategory')
BEGIN
    CREATE TABLE [widgets].[WidgetCategory]
    (
        [CategoryId]    INT IDENTITY(1,1)   NOT NULL,
        [Name]          NVARCHAR(100)       NOT NULL,
        [Workspace]     NVARCHAR(20)        NOT NULL,
        [Icon]          NVARCHAR(50)        NULL,
        [DefaultOrder]  INT                 NOT NULL    DEFAULT 0,

        CONSTRAINT [PK_widgets_WidgetCategory]
            PRIMARY KEY CLUSTERED ([CategoryId]),
        CONSTRAINT [CK_widgets_WidgetCategory_Workspace]
            CHECK ([Workspace] IN ('public','dashboard','job-config','player-reg',
                   'team-reg','scheduling','fin-per-job','fin-per-customer'))
    );
    PRINT 'Created table: widgets.WidgetCategory';
END

-- 1c. Create Widget
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'Widget')
BEGIN
    CREATE TABLE [widgets].[Widget]
    (
        [WidgetId]      INT IDENTITY(1,1)   NOT NULL,
        [Name]          NVARCHAR(100)       NOT NULL,
        [WidgetType]    NVARCHAR(30)        NOT NULL,
        [ComponentKey]  NVARCHAR(100)       NOT NULL,
        [CategoryId]    INT                 NOT NULL,
        [Description]   NVARCHAR(500)       NULL,

        CONSTRAINT [PK_widgets_Widget]
            PRIMARY KEY CLUSTERED ([WidgetId]),
        CONSTRAINT [FK_widgets_Widget_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [CK_widgets_Widget_WidgetType]
            CHECK ([WidgetType] IN ('content','chart','status-card',
                   'quick-action','workflow-pipeline','link-group'))
    );
    PRINT 'Created table: widgets.Widget';
END

-- 1d. Create WidgetDefault
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'WidgetDefault')
BEGIN
    CREATE TABLE [widgets].[WidgetDefault]
    (
        [WidgetDefaultId]   INT IDENTITY(1,1)   NOT NULL,
        [JobTypeId]         INT                 NOT NULL,
        [RoleId]            NVARCHAR(450)       NOT NULL,
        [WidgetId]          INT                 NOT NULL,
        [CategoryId]        INT                 NOT NULL,
        [DisplayOrder]      INT                 NOT NULL    DEFAULT 0,
        [Config]            NVARCHAR(MAX)       NULL,

        CONSTRAINT [PK_widgets_WidgetDefault]
            PRIMARY KEY CLUSTERED ([WidgetDefaultId]),
        CONSTRAINT [FK_widgets_WidgetDefault_JobTypeId]
            FOREIGN KEY ([JobTypeId])
            REFERENCES [reference].[JobTypes] ([JobTypeId]),
        CONSTRAINT [FK_widgets_WidgetDefault_RoleId]
            FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_widgets_WidgetDefault_WidgetId]
            FOREIGN KEY ([WidgetId])
            REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_WidgetDefault_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category]
            UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId])
    );
    PRINT 'Created table: widgets.WidgetDefault';
END

-- 1e. Create JobWidget
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'JobWidget')
BEGIN
    CREATE TABLE [widgets].[JobWidget]
    (
        [JobWidgetId]   INT IDENTITY(1,1)       NOT NULL,
        [JobId]         UNIQUEIDENTIFIER        NOT NULL,
        [WidgetId]      INT                     NOT NULL,
        [RoleId]        NVARCHAR(450)           NOT NULL,
        [CategoryId]    INT                     NOT NULL,
        [DisplayOrder]  INT                     NOT NULL    DEFAULT 0,
        [IsEnabled]     BIT                     NOT NULL    DEFAULT 1,
        [Config]        NVARCHAR(MAX)           NULL,

        CONSTRAINT [PK_widgets_JobWidget]
            PRIMARY KEY CLUSTERED ([JobWidgetId]),
        CONSTRAINT [FK_widgets_JobWidget_JobId]
            FOREIGN KEY ([JobId])
            REFERENCES [Jobs].[Jobs] ([JobId]),
        CONSTRAINT [FK_widgets_JobWidget_WidgetId]
            FOREIGN KEY ([WidgetId])
            REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_JobWidget_RoleId]
            FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_widgets_JobWidget_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_JobWidget_Job_Widget_Role]
            UNIQUE ([JobId], [WidgetId], [RoleId])
    );
    PRINT 'Created table: widgets.JobWidget';
END

-- ──────────────────────────────────────────────────────────
-- 1f. SCHEMA MIGRATION (old prod backup with [Section] column)
-- ──────────────────────────────────────────────────────────

-- Drop old CHECK constraints (must happen before sp_rename)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Section')
BEGIN
    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Section];
    PRINT 'Dropped CK_widgets_WidgetCategory_Section';
END

-- Temporarily drop new CHECK too (will recreate in Batch 2 after data migration)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')
BEGIN
    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Workspace];
    PRINT 'Dropped CK_widgets_WidgetCategory_Workspace (will recreate after data migration)';
END

-- Rename column if still named 'Section'
IF COL_LENGTH('widgets.WidgetCategory', 'Section') IS NOT NULL
   AND COL_LENGTH('widgets.WidgetCategory', 'Workspace') IS NULL
BEGIN
    EXEC sp_rename 'widgets.WidgetCategory.Section', 'Workspace', 'COLUMN';
    PRINT 'Renamed WidgetCategory.Section -> Workspace';
END

-- Update UNIQUE KEY: old 3-column → new 4-column (includes CategoryId)
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_widgets_WidgetDefault_JobType_Role_Widget'
           AND object_id = OBJECT_ID('widgets.WidgetDefault'))
BEGIN
    ALTER TABLE [widgets].[WidgetDefault] DROP CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget];
    PRINT 'Dropped old UQ_widgets_WidgetDefault_JobType_Role_Widget';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_widgets_WidgetDefault_JobType_Role_Widget_Category'
               AND object_id = OBJECT_ID('widgets.WidgetDefault'))
BEGIN
    ALTER TABLE [widgets].[WidgetDefault] ADD CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category]
        UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId]);
    PRINT 'Created UQ_widgets_WidgetDefault_JobType_Role_Widget_Category';
END

-- Ensure WidgetType CHECK allows all needed types
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')
    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];

ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]
    CHECK ([WidgetType] IN ('content','chart','status-card','quick-action','workflow-pipeline','link-group'));

PRINT 'Batch 1 complete: schema + tables + migration';
GO

-- ════════════════════════════════════════════════════════════
-- BATCH 2: DATA MIGRATION + SEEDING
-- (Compiles fresh — sees the renamed Workspace column)
-- ════════════════════════════════════════════════════════════

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- ──────────────────────────────────────────────────────────
-- 2a. Migrate old workspace values (prod restore scenario)
-- ──────────────────────────────────────────────────────────

UPDATE widgets.WidgetCategory SET Workspace = 'public'
WHERE Name = 'Content' AND Workspace IN ('content', 'public');

UPDATE widgets.WidgetCategory SET Workspace = 'dashboard'
WHERE Name = 'Dashboard Charts' AND Workspace IN ('content', 'dashboard');

UPDATE widgets.WidgetCategory SET Workspace = 'dashboard'
WHERE Workspace IN ('health', 'action', 'insight');

-- Now add the CHECK constraint (data is clean)
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')
BEGIN
    ALTER TABLE [widgets].[WidgetCategory] ADD CONSTRAINT [CK_widgets_WidgetCategory_Workspace]
        CHECK ([Workspace] IN (
            'public','dashboard','job-config','player-reg',
            'team-reg','scheduling','fin-per-job','fin-per-customer'
        ));
    PRINT 'Created CK_widgets_WidgetCategory_Workspace';
END

PRINT 'Data migration complete';

-- ──────────────────────────────────────────────────────────
-- 2b. Workspace categories (MERGE — idempotent)
-- ──────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Content' AND Workspace = 'public')
   AND NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Public Content' AND Workspace = 'public')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Public Content', 'public', NULL, 0);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Dashboard Bulletins' AND Workspace = 'dashboard')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Dashboard Bulletins', 'dashboard', NULL, -1);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Dashboard Charts' AND Workspace = 'dashboard')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Dashboard Charts', 'dashboard', NULL, 1);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Dashboard Status' AND Workspace = 'dashboard')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Dashboard Status', 'dashboard', 'bi-activity', 0);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Job Configuration' AND Workspace = 'job-config')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Job Configuration', 'job-config', 'bi-gear-fill', 0);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Player Management' AND Workspace = 'player-reg')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Player Management', 'player-reg', 'bi-person-lines-fill', 0);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Team Management' AND Workspace = 'team-reg')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Team Management', 'team-reg', 'bi-people-fill', 0);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Scheduling Tools' AND Workspace = 'scheduling')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Scheduling Tools', 'scheduling', 'bi-calendar-range', 0);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Schedule View' AND Workspace = 'scheduling')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Schedule View', 'scheduling', 'bi-calendar-check', 1);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Organization Finances' AND Workspace = 'fin-per-job')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('Organization Finances', 'fin-per-job', 'bi-cash-stack', 0);

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'My Finances' AND Workspace = 'fin-per-customer')
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES ('My Finances', 'fin-per-customer', 'bi-wallet2', 0);

PRINT 'Workspace categories ensured';

-- ──────────────────────────────────────────────────────────
-- 2c. Resolve category IDs
-- ──────────────────────────────────────────────────────────

DECLARE @publicContentCatId  INT = (SELECT TOP 1 CategoryId FROM widgets.WidgetCategory WHERE Workspace = 'public' ORDER BY DefaultOrder);
DECLARE @dashBulletinsCatId  INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Dashboard Bulletins');
DECLARE @dashChartsCatId     INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Dashboard Charts');
DECLARE @dashStatusCatId     INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Dashboard Status');
DECLARE @jobConfigCatId      INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Job Configuration');
DECLARE @playerMgmtCatId     INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Player Management');
DECLARE @teamMgmtCatId       INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Team Management');
DECLARE @schedToolsCatId     INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Scheduling Tools');
DECLARE @schedViewCatId      INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Schedule View');
DECLARE @orgFinCatId         INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Organization Finances');
DECLARE @myFinCatId          INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'My Finances');

-- ──────────────────────────────────────────────────────────
-- 2d. Widget definitions (idempotent)
-- ──────────────────────────────────────────────────────────

-- Content widgets
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'client-banner')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Client Banner', 'content', 'client-banner', @publicContentCatId,
            'Job banner with logo and images');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'bulletins')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Bulletins', 'content', 'bulletins', @publicContentCatId,
            'Active job bulletins and announcements');

-- Chart widgets
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'player-trend-chart')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Player Registration Trend', 'chart', 'player-trend-chart', @dashChartsCatId,
            'Daily player registration counts and cumulative revenue over time');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'team-trend-chart')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Team Registration Trend', 'chart', 'team-trend-chart', @dashChartsCatId,
            'Daily team registration counts and cumulative revenue over time');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'agegroup-distribution')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Age Group Distribution', 'chart', 'agegroup-distribution', @dashChartsCatId,
            'Player and team counts broken down by age group');

-- Status card widgets (dashboard KPIs)
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'registration-status')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Registration Status', 'status-card', 'registration-status', @dashStatusCatId,
            'Active registration count and trend indicator');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'financial-status')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Financial Status', 'status-card', 'financial-status', @dashStatusCatId,
            'Revenue collected vs outstanding balance summary');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'scheduling-status')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Scheduling Status', 'status-card', 'scheduling-status', @dashStatusCatId,
            'Schedule completion percentage and game count');

-- Job Configuration workspace
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'ladt-editor')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('LADT Editor', 'quick-action', 'ladt-editor', @jobConfigCatId,
            'Configure leagues, age groups, divisions, and teams');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'fee-config')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Fee Configuration', 'quick-action', 'fee-config', @jobConfigCatId,
            'Configure registration fees and payment options');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'job-settings')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Job Settings', 'quick-action', 'job-settings', @jobConfigCatId,
            'General event configuration and settings');

-- Player Registration workspace
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'search-registrations')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Search Registrations', 'quick-action', 'search-registrations', @playerMgmtCatId,
            'Search and manage player registrations');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'roster-swapper')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Roster Swapper', 'quick-action', 'roster-swapper', @playerMgmtCatId,
            'Move players between team rosters');

-- Team Registration workspace
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'view-by-club')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('View by Club', 'quick-action', 'view-by-club', @teamMgmtCatId,
            'Browse registrations grouped by club');

-- Scheduling workspace
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'scheduling-pipeline')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Scheduling Pipeline', 'quick-action', 'scheduling-pipeline', @schedToolsCatId,
            'Step-by-step scheduling workflow');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'pool-assignment')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Pool Assignment', 'quick-action', 'pool-assignment', @schedToolsCatId,
            'Assign teams to pools');

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'view-schedule')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('View Schedule', 'quick-action', 'view-schedule', @schedViewCatId,
            'View published game schedule');

-- Organization Finances workspace
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'financial-overview')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Financial Overview', 'quick-action', 'financial-overview', @orgFinCatId,
            'Payment status and outstanding balance reports');

-- My Finances workspace
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'my-payments')
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('My Payments', 'quick-action', 'my-payments', @myFinCatId,
            'View your payment history and outstanding balances');

PRINT 'Widget definitions ensured';

-- ──────────────────────────────────────────────────────────
-- 2e. Resolve widget IDs
-- ──────────────────────────────────────────────────────────

DECLARE @bannerWidgetId    INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'client-banner');
DECLARE @bulletinsWidgetId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'bulletins');
DECLARE @playerTrendId     INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'player-trend-chart');
DECLARE @teamTrendId       INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'team-trend-chart');
DECLARE @agDistId          INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'agegroup-distribution');
DECLARE @regStatusId       INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'registration-status');
DECLARE @finStatusId       INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'financial-status');
DECLARE @schedStatusId     INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'scheduling-status');
DECLARE @ladtEditorId      INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'ladt-editor');
DECLARE @feeConfigId       INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'fee-config');
DECLARE @jobSettingsId     INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'job-settings');
DECLARE @searchRegsId      INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'search-registrations');
DECLARE @rosterSwapperId   INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'roster-swapper');
DECLARE @viewByClubId      INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'view-by-club');
DECLARE @schedPipelineId   INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'scheduling-pipeline');
DECLARE @poolAssignId      INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'pool-assignment');
DECLARE @viewScheduleId    INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'view-schedule');
DECLARE @finOverviewId     INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'financial-overview');
DECLARE @myPaymentsId      INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'my-payments');

-- ──────────────────────────────────────────────────────────
-- 2f. Role GUIDs
-- ──────────────────────────────────────────────────────────

DECLARE @anonymousId     NVARCHAR(450) = 'CBF3F384-190F-4962-BF58-40B095628DC8';
DECLARE @superuserId     NVARCHAR(450) = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';
DECLARE @superDirectorId NVARCHAR(450) = '7B9EB503-53C9-44FA-94A0-17760C512440';
DECLARE @directorId      NVARCHAR(450) = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06';
DECLARE @clubRepId       NVARCHAR(450) = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
DECLARE @playerId        NVARCHAR(450) = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
DECLARE @staffId         NVARCHAR(450) = '1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA';
DECLARE @guestId         NVARCHAR(450) = 'E956616B-DF48-4225-8A10-424229105711'; -- Unassigned Adult

-- ──────────────────────────────────────────────────────────
-- 2g. Role sets for batch inserts
-- ──────────────────────────────────────────────────────────

DECLARE @adminRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @adminRoles VALUES (@superuserId), (@superDirectorId), (@directorId);

DECLARE @dashboardOnlyRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @dashboardOnlyRoles VALUES (@clubRepId), (@playerId), (@staffId), (@guestId);

DECLARE @jobConfigRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @jobConfigRoles VALUES (@superuserId), (@superDirectorId), (@directorId);

DECLARE @playerRegFullRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @playerRegFullRoles VALUES (@superuserId), (@superDirectorId), (@directorId), (@clubRepId), (@playerId);

DECLARE @playerRegViewRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @playerRegViewRoles VALUES (@staffId);

DECLARE @teamRegFullRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @teamRegFullRoles VALUES (@superuserId), (@superDirectorId), (@directorId), (@clubRepId);

DECLARE @teamRegViewRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @teamRegViewRoles VALUES (@staffId);

DECLARE @schedFullRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @schedFullRoles VALUES (@superuserId), (@superDirectorId), (@directorId);

DECLARE @schedViewRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @schedViewRoles VALUES (@clubRepId), (@playerId), (@staffId);

DECLARE @finPerJobRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @finPerJobRoles VALUES (@superuserId), (@superDirectorId), (@directorId);

DECLARE @finPerCustomerRoles TABLE (RoleId NVARCHAR(450));
INSERT INTO @finPerCustomerRoles VALUES (@superuserId), (@superDirectorId);

-- ──────────────────────────────────────────────────────────
-- 2h. Pre-seed cleanup: admin roles do NOT get bulletins on the dashboard hub.
--     They see charts + status cards + spoke navigation cards instead.
-- ──────────────────────────────────────────────────────────

DELETE wd
FROM widgets.WidgetDefault wd
WHERE wd.WidgetId = @bulletinsWidgetId
  AND wd.CategoryId = @dashBulletinsCatId
  AND wd.RoleId IN (SELECT RoleId FROM @adminRoles);

PRINT 'Cleaned: removed admin-role bulletins from dashboard workspace';

-- ──────────────────────────────────────────────────────────
-- 2i. Seed WidgetDefault entries
--     Pattern: INSERT...SELECT FROM JobTypes CROSS JOIN @roles WHERE NOT EXISTS
-- ──────────────────────────────────────────────────────────

-- ── PUBLIC workspace: Anonymous banner + bulletins ──

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, @anonymousId, @bannerWidgetId, @publicContentCatId, 1, NULL
FROM reference.JobTypes jt
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = @anonymousId AND wd.WidgetId = @bannerWidgetId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, @anonymousId, @bulletinsWidgetId, @publicContentCatId, 2, NULL
FROM reference.JobTypes jt
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = @anonymousId AND wd.WidgetId = @bulletinsWidgetId
);

PRINT 'Seeded: public workspace (Anonymous)';

-- ── DASHBOARD workspace: Admin roles — charts + status (NO bulletins) ──
-- NOTE: Admin roles do NOT get bulletins in the dashboard workspace.
-- The hub shows charts + status cards + spoke navigation. Bulletins live on the public landing.
-- Non-admin roles (below) DO keep bulletins as their primary dashboard content.

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @playerTrendId, @dashChartsCatId, 1, NULL
FROM reference.JobTypes jt CROSS JOIN @adminRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @playerTrendId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @teamTrendId, @dashChartsCatId, 2, NULL
FROM reference.JobTypes jt CROSS JOIN @adminRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @teamTrendId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @agDistId, @dashChartsCatId, 3, NULL
FROM reference.JobTypes jt CROSS JOIN @adminRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @agDistId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @regStatusId, @dashStatusCatId, 1,
    '{"label":"Registrations","icon":"bi-people-fill","route":"registrations"}'
FROM reference.JobTypes jt CROSS JOIN @adminRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @regStatusId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @finStatusId, @dashStatusCatId, 2,
    '{"label":"Financials","icon":"bi-currency-dollar","route":"financials"}'
FROM reference.JobTypes jt CROSS JOIN @adminRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @finStatusId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @schedStatusId, @dashStatusCatId, 3,
    '{"label":"Schedule","icon":"bi-calendar-check","route":"scheduling"}'
FROM reference.JobTypes jt CROSS JOIN @adminRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @schedStatusId
);

PRINT 'Seeded: dashboard workspace (admin roles)';

-- Dashboard-only roles (ClubRep, Player, Staff, Guest): bulletins only
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @bulletinsWidgetId, @dashBulletinsCatId, 1, NULL
FROM reference.JobTypes jt CROSS JOIN @dashboardOnlyRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @bulletinsWidgetId
);

PRINT 'Seeded: dashboard workspace (non-admin roles)';

-- ── JOB-CONFIG workspace ──

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @ladtEditorId, @jobConfigCatId, 1,
    '{"label":"LADT Editor","icon":"bi-diagram-3","route":"ladt"}'
FROM reference.JobTypes jt CROSS JOIN @jobConfigRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @ladtEditorId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @feeConfigId, @jobConfigCatId, 2,
    '{"label":"Fee Configuration","icon":"bi-tags","route":"fees"}'
FROM reference.JobTypes jt CROSS JOIN @jobConfigRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @feeConfigId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @jobSettingsId, @jobConfigCatId, 3,
    '{"label":"Job Settings","icon":"bi-sliders","route":"settings"}'
FROM reference.JobTypes jt CROSS JOIN @jobConfigRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @jobSettingsId
);

PRINT 'Seeded: job-config workspace';

-- ── PLAYER-REG workspace (full access) ──

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @searchRegsId, @playerMgmtCatId, 1,
    '{"label":"Search Registrations","icon":"bi-search","route":"registrations/search"}'
FROM reference.JobTypes jt CROSS JOIN @playerRegFullRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @searchRegsId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @rosterSwapperId, @playerMgmtCatId, 2,
    '{"label":"Roster Swapper","icon":"bi-arrow-left-right","route":"roster-swapper"}'
FROM reference.JobTypes jt CROSS JOIN @playerRegFullRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @rosterSwapperId
);

PRINT 'Seeded: player-reg workspace (full access)';

-- Player-reg (view-only — Staff)
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @searchRegsId, @playerMgmtCatId, 1,
    '{"label":"Search Registrations","icon":"bi-search","route":"registrations/search","readOnly":true}'
FROM reference.JobTypes jt CROSS JOIN @playerRegViewRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @searchRegsId
);

PRINT 'Seeded: player-reg workspace (view only)';

-- ── TEAM-REG workspace (full access) ──

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @viewByClubId, @teamMgmtCatId, 1,
    '{"label":"View by Club","icon":"bi-building","route":"registrations/by-club"}'
FROM reference.JobTypes jt CROSS JOIN @teamRegFullRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @viewByClubId
);

PRINT 'Seeded: team-reg workspace (full access)';

-- Team-reg (view-only — Staff)
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @viewByClubId, @teamMgmtCatId, 1,
    '{"label":"View by Club","icon":"bi-building","route":"registrations/by-club","readOnly":true}'
FROM reference.JobTypes jt CROSS JOIN @teamRegViewRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @viewByClubId
);

PRINT 'Seeded: team-reg workspace (view only)';

-- ── SCHEDULING workspace (full tools) ──

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @schedPipelineId, @schedToolsCatId, 1,
    '{"label":"Scheduling Pipeline","icon":"bi-kanban","route":"scheduling/pipeline"}'
FROM reference.JobTypes jt CROSS JOIN @schedFullRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @schedPipelineId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @poolAssignId, @schedToolsCatId, 2,
    '{"label":"Pool Assignment","icon":"bi-people-fill","route":"scheduling/pools"}'
FROM reference.JobTypes jt CROSS JOIN @schedFullRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @poolAssignId
);

-- LADT also in scheduling workspace (cross-workspace widget)
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @ladtEditorId, @schedToolsCatId, 3,
    '{"label":"LADT Editor","icon":"bi-diagram-3","route":"ladt"}'
FROM reference.JobTypes jt CROSS JOIN @schedFullRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @ladtEditorId
      AND wd.CategoryId = @schedToolsCatId
);

PRINT 'Seeded: scheduling workspace (full tools)';

-- Scheduling (view-only — ClubRep, Player, Staff)
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @viewScheduleId, @schedViewCatId, 1,
    '{"label":"View Schedule","icon":"bi-calendar-check","route":"schedule"}'
FROM reference.JobTypes jt CROSS JOIN @schedViewRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @viewScheduleId
);

PRINT 'Seeded: scheduling workspace (view only)';

-- ── FIN-PER-JOB workspace ──

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @finOverviewId, @orgFinCatId, 1,
    '{"label":"Financial Overview","icon":"bi-cash-stack","route":"financials"}'
FROM reference.JobTypes jt CROSS JOIN @finPerJobRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @finOverviewId
);

PRINT 'Seeded: fin-per-job workspace';

-- ── FIN-PER-CUSTOMER workspace ──

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @myPaymentsId, @myFinCatId, 1,
    '{"label":"My Payments","icon":"bi-wallet2","route":"my-payments"}'
FROM reference.JobTypes jt CROSS JOIN @finPerCustomerRoles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId AND wd.RoleId = r.RoleId AND wd.WidgetId = @myPaymentsId
);

PRINT 'Seeded: fin-per-customer workspace';

-- ════════════════════════════════════════════════════════════
-- DONE
-- ════════════════════════════════════════════════════════════

COMMIT TRANSACTION;

PRINT '';
PRINT '================================================';
PRINT ' Widget Dashboard Setup — Complete';
PRINT '================================================';
PRINT '';

SELECT 'Categories' AS [Table], COUNT(*) AS [Count] FROM widgets.WidgetCategory
UNION ALL SELECT 'Widgets', COUNT(*) FROM widgets.Widget
UNION ALL SELECT 'Defaults', COUNT(*) FROM widgets.WidgetDefault
UNION ALL SELECT 'JobWidgets', COUNT(*) FROM widgets.JobWidget;

SELECT Workspace, COUNT(*) AS Categories
FROM widgets.WidgetCategory
GROUP BY Workspace
ORDER BY Workspace;

SET NOCOUNT OFF;
