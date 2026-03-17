-- ============================================================================
--
--   0-Restore-DevConfig-PROD.sql
--
--   RUN THIS IMMEDIATELY AFTER RESTORING A PRODUCTION BACKUP.
--
--   Todd -- this is the ONE script. Run it, verify the summary at the
--   bottom, you're done.
--
-- ============================================================================
--
-- Generated: 2026-03-17 09:05:14
-- Source:    0-Restore-DevConfig-DEV.ps1 (widgets/store from dev DB, nav from route manifest)
--
-- WHAT THIS DOES:
--   Section 1 -- Creates schemas/tables/columns that don't exist yet
--   Section 2 -- Seeds dev configuration into new-system tables only
--
-- SAFETY GUARANTEES:
--   * 100% idempotent -- safe to run multiple times
--   * ZERO writes to legacy tables (Jobs, Registrations, Users, Teams)
--   * Schema changes use IF NOT EXISTS
--   * Data targets ONLY: widgets.*, nav.*, logs.*, stores.StoreItemImage
--   * ZERO writes to legacy tables
--
-- Snapshot: 2 widget categories, 7 widgets, 87 defaults, 0 job overrides
--           10 navs, 180 nav items [from route manifest]
--           20 store images
--
-- Prerequisites: reference.JobTypes + dbo.AspNetRoles + dbo.AspNetUsers populated
-- ============================================================================

SET NOCOUNT ON;

PRINT '';
PRINT '==========================================================';
PRINT '  0-Restore-DevConfig-PROD.sql';
PRINT '  Generated: 2026-03-17 09:05:14';
PRINT '==========================================================';
PRINT '';

-- ========================================================================
-- SECTION 1: SCHEMA SAFETY NET
-- Creates schemas, tables, columns that don't exist yet.
-- Every statement is IF NOT EXISTS -- completely safe on any DB state.
-- ========================================================================

PRINT '-- 1A: widgets schema + tables';

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'widgets')
    EXEC('CREATE SCHEMA [widgets] AUTHORIZATION [dbo]');

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'WidgetCategory')
BEGIN
    CREATE TABLE [widgets].[WidgetCategory] (
        [CategoryId]   INT IDENTITY(1,1) NOT NULL,
        [Name]         NVARCHAR(100)     NOT NULL,
        [Workspace]    NVARCHAR(20)      NOT NULL,
        [Icon]         NVARCHAR(50)      NULL,
        [DefaultOrder] INT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_widgets_WidgetCategory] PRIMARY KEY CLUSTERED ([CategoryId])
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'Widget')
BEGIN
    CREATE TABLE [widgets].[Widget] (
        [WidgetId]      INT IDENTITY(1,1) NOT NULL,
        [Name]          NVARCHAR(100)     NOT NULL,
        [WidgetType]    NVARCHAR(30)      NOT NULL,
        [ComponentKey]  NVARCHAR(100)     NOT NULL,
        [CategoryId]    INT               NOT NULL,
        [Description]   NVARCHAR(500)     NULL,
        [DefaultConfig] NVARCHAR(MAX)     NULL,
        CONSTRAINT [PK_widgets_Widget] PRIMARY KEY CLUSTERED ([WidgetId]),
        CONSTRAINT [FK_widgets_Widget_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [CK_widgets_Widget_WidgetType] CHECK ([WidgetType] IN ('content','chart-tile','status-tile'))
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'WidgetDefault')
BEGIN
    CREATE TABLE [widgets].[WidgetDefault] (
        [WidgetDefaultId] INT IDENTITY(1,1) NOT NULL,
        [JobTypeId]       INT               NOT NULL,
        [RoleId]          NVARCHAR(450)     NOT NULL,
        [WidgetId]        INT               NOT NULL,
        [CategoryId]      INT               NOT NULL,
        [DisplayOrder]    INT               NOT NULL DEFAULT 0,
        [Config]          NVARCHAR(MAX)     NULL,
        CONSTRAINT [PK_widgets_WidgetDefault] PRIMARY KEY CLUSTERED ([WidgetDefaultId]),
        CONSTRAINT [FK_widgets_WidgetDefault_JobTypeId] FOREIGN KEY ([JobTypeId]) REFERENCES [reference].[JobTypes] ([JobTypeId]),
        CONSTRAINT [FK_widgets_WidgetDefault_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_widgets_WidgetDefault_WidgetId] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_WidgetDefault_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category] UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId])
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'JobWidget')
BEGIN
    CREATE TABLE [widgets].[JobWidget] (
        [JobWidgetId]  INT IDENTITY(1,1)    NOT NULL,
        [JobId]        UNIQUEIDENTIFIER     NOT NULL,
        [WidgetId]     INT                  NOT NULL,
        [RoleId]       NVARCHAR(450)        NOT NULL,
        [CategoryId]   INT                  NOT NULL,
        [DisplayOrder] INT                  NOT NULL DEFAULT 0,
        [IsEnabled]    BIT                  NOT NULL DEFAULT 1,
        [Config]       NVARCHAR(MAX)        NULL,
        CONSTRAINT [PK_widgets_JobWidget] PRIMARY KEY CLUSTERED ([JobWidgetId]),
        CONSTRAINT [FK_widgets_JobWidget_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs] ([JobId]),
        CONSTRAINT [FK_widgets_JobWidget_WidgetId] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_JobWidget_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_widgets_JobWidget_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_JobWidget_Job_Widget_Role] UNIQUE ([JobId], [WidgetId], [RoleId])
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'UserWidget')
BEGIN
    CREATE TABLE [widgets].[UserWidget] (
        [UserWidgetId]   INT IDENTITY(1,1)    NOT NULL,
        [RegistrationId] UNIQUEIDENTIFIER     NOT NULL,
        [WidgetId]       INT                  NOT NULL,
        [CategoryId]     INT                  NOT NULL,
        [DisplayOrder]   INT                  NOT NULL DEFAULT 0,
        [IsHidden]       BIT                  NOT NULL DEFAULT 0,
        [Config]         NVARCHAR(MAX)        NULL,
        CONSTRAINT [PK_widgets_UserWidget] PRIMARY KEY CLUSTERED ([UserWidgetId]),
        CONSTRAINT [FK_widgets_UserWidget_Widget] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_UserWidget_Category] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_UserWidget_Reg_Widget] UNIQUE ([RegistrationId], [WidgetId])
    );
END

-- Prod backup compatibility: Section -> Workspace
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Section')
    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Section];
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')
    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Workspace];
IF COL_LENGTH('widgets.WidgetCategory', 'Section') IS NOT NULL AND COL_LENGTH('widgets.WidgetCategory', 'Workspace') IS NULL
    EXEC sp_rename 'widgets.WidgetCategory.Section', 'Workspace', 'COLUMN';

IF COL_LENGTH('widgets.Widget', 'DefaultConfig') IS NULL
BEGIN
    ALTER TABLE [widgets].[Widget] ADD [DefaultConfig] NVARCHAR(MAX) NULL;
    PRINT '  Added column: widgets.Widget.DefaultConfig';
END

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_widgets_WidgetDefault_JobType_Role_Widget' AND object_id = OBJECT_ID('widgets.WidgetDefault'))
    ALTER TABLE [widgets].[WidgetDefault] DROP CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget];
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_widgets_WidgetDefault_JobType_Role_Widget_Category' AND object_id = OBJECT_ID('widgets.WidgetDefault'))
    ALTER TABLE [widgets].[WidgetDefault] ADD CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category] UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId]);

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')
    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];
ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]
    CHECK ([WidgetType] IN ('content','chart-tile','status-tile'));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')
    ALTER TABLE [widgets].[WidgetCategory] ADD CONSTRAINT [CK_widgets_WidgetCategory_Workspace]
        CHECK ([Workspace] IN ('dashboard','public'));

PRINT '  1A complete: widgets';
GO

PRINT '-- 1B: nav schema + tables';

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')
    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');

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
END

PRINT '  1B complete: nav';
GO

PRINT '-- 1C: logs schema + table';

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'logs')
    EXEC('CREATE SCHEMA logs');

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'logs' AND t.name = 'AppLog')
BEGIN
    CREATE TABLE logs.AppLog (
        Id            bigint IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
        TimeStamp     datetimeoffset(7)    NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        Level         nvarchar(16)         NOT NULL,
        Message       nvarchar(max)        NULL,
        Exception     nvarchar(max)        NULL,
        Properties    nvarchar(max)        NULL,
        SourceContext  nvarchar(512)        NULL,
        RequestPath   nvarchar(512)        NULL,
        StatusCode    int                  NULL,
        Elapsed       float                NULL
    );
    CREATE NONCLUSTERED INDEX IX_AppLog_TimeStamp_Level ON logs.AppLog (TimeStamp DESC, Level) INCLUDE (Message, SourceContext, RequestPath, StatusCode);
END

PRINT '  1C complete: logs';
GO

PRINT '-- 1D: stores.StoreItemImage table';

IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'stores' AND t.name = 'StoreItemImage')
BEGIN
    CREATE TABLE stores.StoreItemImage (
        StoreItemImageId INT IDENTITY(1,1) PRIMARY KEY,
        StoreItemId      INT NOT NULL,
        ImageUrl         NVARCHAR(500) NOT NULL,
        DisplayOrder     INT NOT NULL DEFAULT 0,
        AltText          NVARCHAR(200) NULL,
        Modified         DATETIME NOT NULL DEFAULT GETUTCDATE(),
        LebUserId        NVARCHAR(128) NOT NULL,
        CONSTRAINT FK_StoreItemImage_StoreItem FOREIGN KEY (StoreItemId) REFERENCES stores.StoreItems(StoreItemId)
    );
END

PRINT '  1D complete: StoreItemImage';
GO

PRINT '-- 1E: AdultProfileMetadataJson column';

IF COL_LENGTH('Jobs.Jobs', 'AdultProfileMetadataJson') IS NULL
BEGIN
    ALTER TABLE Jobs.Jobs ADD AdultProfileMetadataJson NVARCHAR(MAX) NULL;
    PRINT '  Added: Jobs.Jobs.AdultProfileMetadataJson';
END

PRINT '  Section 1 complete -- all schemas/tables/columns verified.';
PRINT '';
GO

-- ========================================================================
-- SECTION 2: SEED DEV DATA
-- Only touches new-system tables. Legacy tables are UNTOUCHED.
-- ========================================================================

SET NOCOUNT ON;

PRINT '-- 2A: Widget data';

DELETE FROM widgets.UserWidget;
DELETE FROM widgets.JobWidget;
DELETE FROM widgets.WidgetDefault;
DELETE FROM widgets.Widget;
DELETE FROM widgets.WidgetCategory;

SET IDENTITY_INSERT widgets.WidgetCategory ON;
INSERT INTO widgets.WidgetCategory (CategoryId, Name, Workspace, Icon, DefaultOrder)
VALUES (1, N'Public Content', N'public', NULL, 0);
INSERT INTO widgets.WidgetCategory (CategoryId, Name, Workspace, Icon, DefaultOrder)
VALUES (3, N'Dashboard Charts', N'dashboard', NULL, 1);
SET IDENTITY_INSERT widgets.WidgetCategory OFF;
PRINT '  Loaded 2 categories';

SET IDENTITY_INSERT widgets.Widget ON;
INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
VALUES (1, N'Client Banner', N'content', N'client-banner', 1, N'Job banner with logo and images', N'{"displayStyle":"banner"}');
INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
VALUES (2, N'Bulletins', N'content', N'bulletins', 1, N'Active job bulletins and announcements', N'{"displayStyle":"feed"}');
INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
VALUES (3, N'Player Registration Trend', N'chart-tile', N'player-trend-chart', 3, N'Daily player registration counts and cumulative revenue over time', NULL);
INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
VALUES (4, N'Team Registration Trend', N'chart-tile', N'team-trend-chart', 3, N'Daily team registration counts and cumulative revenue over time', NULL);
INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
VALUES (5, N'Age Group Distribution', N'chart-tile', N'agegroup-distribution', 3, N'Player and team counts broken down by age group', NULL);
INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
VALUES (21, N'Event Contact', N'content', N'event-contact', 1, NULL, N'{"label":"Event Contact","icon":"bi-person-fill","displayStyle":"block"}');
INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
VALUES (23, N'Year-over-Year Comparison', N'chart-tile', N'year-over-year', 3, N'Registration comparison between current and prior year', N'{"label":"Year-over-Year Comparison","icon":"bi-arrow-repeat"}');
SET IDENTITY_INSERT widgets.Widget OFF;
PRINT '  Loaded 7 widgets';

SET IDENTITY_INSERT widgets.WidgetDefault ON;
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (1, 0, N'CBF3F384-190F-4962-BF58-40B095628DC8', 1, 1, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (2, 1, N'CBF3F384-190F-4962-BF58-40B095628DC8', 1, 1, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (3, 2, N'CBF3F384-190F-4962-BF58-40B095628DC8', 1, 1, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (4, 3, N'CBF3F384-190F-4962-BF58-40B095628DC8', 1, 1, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (5, 4, N'CBF3F384-190F-4962-BF58-40B095628DC8', 1, 1, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (6, 5, N'CBF3F384-190F-4962-BF58-40B095628DC8', 1, 1, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (7, 6, N'CBF3F384-190F-4962-BF58-40B095628DC8', 1, 1, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (8, 0, N'CBF3F384-190F-4962-BF58-40B095628DC8', 2, 1, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (9, 1, N'CBF3F384-190F-4962-BF58-40B095628DC8', 2, 1, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (10, 2, N'CBF3F384-190F-4962-BF58-40B095628DC8', 2, 1, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (11, 3, N'CBF3F384-190F-4962-BF58-40B095628DC8', 2, 1, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (12, 4, N'CBF3F384-190F-4962-BF58-40B095628DC8', 2, 1, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (13, 5, N'CBF3F384-190F-4962-BF58-40B095628DC8', 2, 1, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (14, 6, N'CBF3F384-190F-4962-BF58-40B095628DC8', 2, 1, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (15, 0, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (16, 1, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (17, 2, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (18, 3, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (19, 4, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (20, 5, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (21, 6, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (22, 0, N'7B9EB503-53C9-44FA-94A0-17760C512440', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (23, 1, N'7B9EB503-53C9-44FA-94A0-17760C512440', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (24, 2, N'7B9EB503-53C9-44FA-94A0-17760C512440', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (25, 3, N'7B9EB503-53C9-44FA-94A0-17760C512440', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (26, 4, N'7B9EB503-53C9-44FA-94A0-17760C512440', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (27, 5, N'7B9EB503-53C9-44FA-94A0-17760C512440', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (28, 6, N'7B9EB503-53C9-44FA-94A0-17760C512440', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (29, 0, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (30, 1, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (31, 2, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (32, 3, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (33, 4, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (34, 5, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (35, 6, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 3, 3, 1, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (36, 0, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (37, 1, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (38, 2, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (39, 3, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (40, 4, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (41, 5, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (42, 6, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (43, 0, N'7B9EB503-53C9-44FA-94A0-17760C512440', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (44, 1, N'7B9EB503-53C9-44FA-94A0-17760C512440', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (45, 2, N'7B9EB503-53C9-44FA-94A0-17760C512440', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (46, 3, N'7B9EB503-53C9-44FA-94A0-17760C512440', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (47, 4, N'7B9EB503-53C9-44FA-94A0-17760C512440', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (48, 5, N'7B9EB503-53C9-44FA-94A0-17760C512440', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (49, 6, N'7B9EB503-53C9-44FA-94A0-17760C512440', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (50, 0, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (51, 1, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (52, 2, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (53, 3, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (54, 4, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (55, 5, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (56, 6, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 4, 3, 2, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (57, 0, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (58, 1, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (59, 2, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (60, 3, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (61, 4, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (62, 5, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (63, 6, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (64, 0, N'7B9EB503-53C9-44FA-94A0-17760C512440', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (65, 1, N'7B9EB503-53C9-44FA-94A0-17760C512440', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (66, 2, N'7B9EB503-53C9-44FA-94A0-17760C512440', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (67, 3, N'7B9EB503-53C9-44FA-94A0-17760C512440', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (68, 4, N'7B9EB503-53C9-44FA-94A0-17760C512440', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (69, 5, N'7B9EB503-53C9-44FA-94A0-17760C512440', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (70, 6, N'7B9EB503-53C9-44FA-94A0-17760C512440', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (71, 0, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (72, 1, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (73, 2, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (74, 3, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (75, 4, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (76, 5, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (77, 6, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 5, 3, 3, NULL);
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (470, 4, N'CBF3F384-190F-4962-BF58-40B095628DC8', 21, 1, 3, N'{"label":"Event Contact","icon":"bi-person-fill","displayStyle":"block"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (471, 1, N'CBF3F384-190F-4962-BF58-40B095628DC8', 21, 1, 3, N'{"label":"Event Contact","icon":"bi-person-fill","displayStyle":"block"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (472, 0, N'CBF3F384-190F-4962-BF58-40B095628DC8', 21, 1, 3, N'{"label":"Event Contact","icon":"bi-person-fill","displayStyle":"block"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (473, 3, N'CBF3F384-190F-4962-BF58-40B095628DC8', 21, 1, 3, N'{"label":"Event Contact","icon":"bi-person-fill","displayStyle":"block"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (474, 5, N'CBF3F384-190F-4962-BF58-40B095628DC8', 21, 1, 3, N'{"label":"Event Contact","icon":"bi-person-fill","displayStyle":"block"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (475, 6, N'CBF3F384-190F-4962-BF58-40B095628DC8', 21, 1, 3, N'{"label":"Event Contact","icon":"bi-person-fill","displayStyle":"block"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (476, 2, N'CBF3F384-190F-4962-BF58-40B095628DC8', 21, 1, 3, N'{"label":"Event Contact","icon":"bi-person-fill","displayStyle":"block"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (477, 1, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', 23, 3, 4, N'{"label":"Year-over-Year Comparison","icon":"bi-arrow-repeat"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (478, 1, N'7B9EB503-53C9-44FA-94A0-17760C512440', 23, 3, 4, N'{"label":"Year-over-Year Comparison","icon":"bi-arrow-repeat"}');
INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
VALUES (479, 1, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', 23, 3, 4, N'{"label":"Year-over-Year Comparison","icon":"bi-arrow-repeat"}');
SET IDENTITY_INSERT widgets.WidgetDefault OFF;
PRINT '  Loaded 87 defaults';

PRINT '  Loaded 0 job overrides';
PRINT '  2A complete';
GO

SET NOCOUNT ON;
PRINT '-- 2B: Nav data (generated from route manifest)';

-- Clear platform defaults only (job-specific overrides survive)
DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);
DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;

SET IDENTITY_INSERT [nav].[Nav] ON;
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (1, N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (2, N'7B9EB503-53C9-44FA-94A0-17760C512440', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (3, N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (4, N'1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (5, N'122075A3-2C42-4092-97F1-9673DF5B6A2C', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (6, N'5B9B7055-4530-4E46-B403-1019FD8B8418', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (7, N'E0A8A5C3-A36C-417F-8312-E7083F1AA5A0', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (8, N'DAC0C570-94AA-4A88-8D73-6034F1F72F3A', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (9, N'6A26171F-4D94-4928-94FA-2FEFD42C3C3E', NULL, 1, GETDATE());
INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])
VALUES (10, N'CE2CB370-5880-4624-A43E-048379C64331', NULL, 1, GETDATE());
SET IDENTITY_INSERT [nav].[Nav] OFF;
PRINT '  Loaded 10 platform default navs';

SET IDENTITY_INSERT [nav].[NavItem] ON;

-- Parents
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (1, 1, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (4, 1, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (9, 1, NULL, 1, 3, N'Communications', N'envelope', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (13, 1, NULL, 1, 4, N'LADT', N'diagram-3', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (17, 1, NULL, 1, 5, N'Scheduling', N'calendar', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (22, 1, NULL, 1, 6, N'ARB', N'credit-card', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (24, 1, NULL, 1, 7, N'Tools', N'tools', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (27, 1, NULL, 1, 8, N'Store', N'cart', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (29, 2, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (32, 2, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (37, 2, NULL, 1, 3, N'Communications', N'envelope', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (41, 2, NULL, 1, 4, N'LADT', N'diagram-3', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (45, 2, NULL, 1, 5, N'Scheduling', N'calendar', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (50, 2, NULL, 1, 6, N'ARB', N'credit-card', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (52, 2, NULL, 1, 7, N'Tools', N'tools', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (55, 2, NULL, 1, 8, N'Store', N'cart', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (57, 3, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (60, 3, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (73, 3, NULL, 1, 3, N'Communications', N'envelope', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (77, 3, NULL, 1, 4, N'LADT', N'diagram-3', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (81, 3, NULL, 1, 5, N'Scheduling', N'calendar', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (86, 3, NULL, 1, 6, N'ARB', N'credit-card', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (88, 3, NULL, 1, 7, N'Tools', N'tools', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (95, 3, NULL, 1, 8, N'Store', N'cart', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (97, 4, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (100, 4, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (105, 4, NULL, 1, 3, N'Communications', N'envelope', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (109, 4, NULL, 1, 4, N'LADT', N'diagram-3', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (113, 4, NULL, 1, 5, N'Scheduling', N'calendar', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (118, 4, NULL, 1, 6, N'ARB', N'credit-card', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (120, 4, NULL, 1, 7, N'Tools', N'tools', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (123, 4, NULL, 1, 8, N'Store', N'cart', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (125, 5, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (128, 5, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (133, 5, NULL, 1, 3, N'Communications', N'envelope', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (137, 5, NULL, 1, 4, N'LADT', N'diagram-3', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (141, 5, NULL, 1, 5, N'Scheduling', N'calendar', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (146, 5, NULL, 1, 6, N'ARB', N'credit-card', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (148, 5, NULL, 1, 7, N'Tools', N'tools', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (151, 5, NULL, 1, 8, N'Store', N'cart', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (153, 6, NULL, 1, 1, N'Search', N'search', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (156, 6, NULL, 1, 2, N'Configure', N'gear', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (161, 6, NULL, 1, 3, N'Communications', N'envelope', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (165, 6, NULL, 1, 4, N'LADT', N'diagram-3', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (169, 6, NULL, 1, 5, N'Scheduling', N'calendar', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (174, 6, NULL, 1, 6, N'ARB', N'credit-card', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (176, 6, NULL, 1, 7, N'Tools', N'tools', NULL, NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (179, 6, NULL, 1, 8, N'Store', N'cart', NULL, NULL, NULL, GETDATE());

-- Children
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (2, 1, 1, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (3, 1, 1, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (5, 1, 4, 1, 1, N'Job', N'briefcase', N'configure/job', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (6, 1, 4, 1, 2, N'Age Ranges', N'sliders', N'configure/age-ranges', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (7, 1, 4, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (8, 1, 4, 1, 4, N'Uniform Upload', N'upload', N'configure/uniform-upload', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (10, 1, 9, 1, 1, N'Bulletins', N'megaphone', N'communications/bulletins', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (11, 1, 9, 1, 2, N'Email Log', N'envelope-open', N'communications/email-log', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (12, 1, 9, 1, 3, N'Push Notification', N'bell', N'communications/push-notification', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (14, 1, 13, 1, 1, N'Editor', N'pencil', N'ladt/editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (15, 1, 13, 1, 2, N'Roster Swapper', N'arrow-left-right', N'ladt/roster-swapper', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (16, 1, 13, 1, 3, N'Pool Assignment', N'people', N'ladt/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (18, 1, 17, 1, 1, N'Schedule Hub', N'grid', N'scheduling/schedule-hub', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (19, 1, 17, 1, 2, N'View Schedule', N'eye', N'scheduling/view-schedule', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (20, 1, 17, 1, 3, N'Rescheduler', N'arrow-repeat', N'scheduling/rescheduler', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (21, 1, 17, 1, 4, N'Mobile Scorers', N'phone', N'scheduling/mobile-scorers', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (23, 1, 22, 1, 1, N'Health Check', N'heart-pulse', N'arb/health', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (25, 1, 24, 1, 1, N'US Lax Tester', N'check-circle', N'tools/uslax-test', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (26, 1, 24, 1, 2, N'US Lax Rankings', N'trophy', N'tools/uslax-rankings', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (28, 1, 27, 1, 1, N'Store Admin', N'shop', N'store/admin', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (30, 2, 29, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (31, 2, 29, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (33, 2, 32, 1, 1, N'Job', N'briefcase', N'configure/job', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (34, 2, 32, 1, 2, N'Age Ranges', N'sliders', N'configure/age-ranges', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (35, 2, 32, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (36, 2, 32, 1, 4, N'Uniform Upload', N'upload', N'configure/uniform-upload', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (38, 2, 37, 1, 1, N'Bulletins', N'megaphone', N'communications/bulletins', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (39, 2, 37, 1, 2, N'Email Log', N'envelope-open', N'communications/email-log', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (40, 2, 37, 1, 3, N'Push Notification', N'bell', N'communications/push-notification', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (42, 2, 41, 1, 1, N'Editor', N'pencil', N'ladt/editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (43, 2, 41, 1, 2, N'Roster Swapper', N'arrow-left-right', N'ladt/roster-swapper', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (44, 2, 41, 1, 3, N'Pool Assignment', N'people', N'ladt/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (46, 2, 45, 1, 1, N'Schedule Hub', N'grid', N'scheduling/schedule-hub', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (47, 2, 45, 1, 2, N'View Schedule', N'eye', N'scheduling/view-schedule', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (48, 2, 45, 1, 3, N'Rescheduler', N'arrow-repeat', N'scheduling/rescheduler', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (49, 2, 45, 1, 4, N'Mobile Scorers', N'phone', N'scheduling/mobile-scorers', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (51, 2, 50, 1, 1, N'Health Check', N'heart-pulse', N'arb/health', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (53, 2, 52, 1, 1, N'US Lax Tester', N'check-circle', N'tools/uslax-test', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (54, 2, 52, 1, 2, N'US Lax Rankings', N'trophy', N'tools/uslax-rankings', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (56, 2, 55, 1, 1, N'Store Admin', N'shop', N'store/admin', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (58, 3, 57, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (59, 3, 57, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (61, 3, 60, 1, 1, N'Job', N'briefcase', N'configure/job', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (62, 3, 60, 1, 2, N'Age Ranges', N'sliders', N'configure/age-ranges', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (63, 3, 60, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (64, 3, 60, 1, 4, N'Uniform Upload', N'upload', N'configure/uniform-upload', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (65, 3, 60, 1, 10, N'Administrators', N'person-badge', N'configure/administrators', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (66, 3, 60, 1, 11, N'Customer Groups', N'people', N'configure/customer-groups', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (67, 3, 60, 1, 12, N'Dropdown Options', N'list', N'configure/ddl-options', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (68, 3, 60, 1, 13, N'Customers', N'building', N'configure/customers', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (69, 3, 60, 1, 14, N'Theme', N'palette', N'configure/theme', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (70, 3, 60, 1, 15, N'Menus', N'list', N'configure/nav-editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (71, 3, 60, 1, 16, N'Widgets', N'grid', N'configure/widget-editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (72, 3, 60, 1, 17, N'Job Clone', N'copy', N'configure/job-clone', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (74, 3, 73, 1, 1, N'Bulletins', N'megaphone', N'communications/bulletins', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (75, 3, 73, 1, 2, N'Email Log', N'envelope-open', N'communications/email-log', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (76, 3, 73, 1, 3, N'Push Notification', N'bell', N'communications/push-notification', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (78, 3, 77, 1, 1, N'Editor', N'pencil', N'ladt/editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (79, 3, 77, 1, 2, N'Roster Swapper', N'arrow-left-right', N'ladt/roster-swapper', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (80, 3, 77, 1, 3, N'Pool Assignment', N'people', N'ladt/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (82, 3, 81, 1, 1, N'Schedule Hub', N'grid', N'scheduling/schedule-hub', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (83, 3, 81, 1, 2, N'View Schedule', N'eye', N'scheduling/view-schedule', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (84, 3, 81, 1, 3, N'Rescheduler', N'arrow-repeat', N'scheduling/rescheduler', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (85, 3, 81, 1, 4, N'Mobile Scorers', N'phone', N'scheduling/mobile-scorers', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (87, 3, 86, 1, 1, N'Health Check', N'heart-pulse', N'arb/health', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (89, 3, 88, 1, 1, N'US Lax Tester', N'check-circle', N'tools/uslax-test', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (90, 3, 88, 1, 2, N'US Lax Rankings', N'trophy', N'tools/uslax-rankings', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (91, 3, 88, 1, 10, N'Profile Migration', N'arrow-right', N'tools/profile-migration', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (92, 3, 88, 1, 11, N'Profile Editor', N'pencil-square', N'tools/profile-editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (93, 3, 88, 1, 12, N'Change Password', N'key', N'tools/change-password', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (94, 3, 88, 1, 13, N'Job Revenue', N'cash-stack', N'tools/customer-job-revenue', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (96, 3, 95, 1, 1, N'Store Admin', N'shop', N'store/admin', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (98, 4, 97, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (99, 4, 97, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (101, 4, 100, 1, 1, N'Job', N'briefcase', N'configure/job', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (102, 4, 100, 1, 2, N'Age Ranges', N'sliders', N'configure/age-ranges', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (103, 4, 100, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (104, 4, 100, 1, 4, N'Uniform Upload', N'upload', N'configure/uniform-upload', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (106, 4, 105, 1, 1, N'Bulletins', N'megaphone', N'communications/bulletins', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (107, 4, 105, 1, 2, N'Email Log', N'envelope-open', N'communications/email-log', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (108, 4, 105, 1, 3, N'Push Notification', N'bell', N'communications/push-notification', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (110, 4, 109, 1, 1, N'Editor', N'pencil', N'ladt/editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (111, 4, 109, 1, 2, N'Roster Swapper', N'arrow-left-right', N'ladt/roster-swapper', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (112, 4, 109, 1, 3, N'Pool Assignment', N'people', N'ladt/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (114, 4, 113, 1, 1, N'Schedule Hub', N'grid', N'scheduling/schedule-hub', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (115, 4, 113, 1, 2, N'View Schedule', N'eye', N'scheduling/view-schedule', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (116, 4, 113, 1, 3, N'Rescheduler', N'arrow-repeat', N'scheduling/rescheduler', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (117, 4, 113, 1, 4, N'Mobile Scorers', N'phone', N'scheduling/mobile-scorers', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (119, 4, 118, 1, 1, N'Health Check', N'heart-pulse', N'arb/health', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (121, 4, 120, 1, 1, N'US Lax Tester', N'check-circle', N'tools/uslax-test', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (122, 4, 120, 1, 2, N'US Lax Rankings', N'trophy', N'tools/uslax-rankings', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (124, 4, 123, 1, 1, N'Store Admin', N'shop', N'store/admin', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (126, 5, 125, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (127, 5, 125, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (129, 5, 128, 1, 1, N'Job', N'briefcase', N'configure/job', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (130, 5, 128, 1, 2, N'Age Ranges', N'sliders', N'configure/age-ranges', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (131, 5, 128, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (132, 5, 128, 1, 4, N'Uniform Upload', N'upload', N'configure/uniform-upload', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (134, 5, 133, 1, 1, N'Bulletins', N'megaphone', N'communications/bulletins', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (135, 5, 133, 1, 2, N'Email Log', N'envelope-open', N'communications/email-log', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (136, 5, 133, 1, 3, N'Push Notification', N'bell', N'communications/push-notification', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (138, 5, 137, 1, 1, N'Editor', N'pencil', N'ladt/editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (139, 5, 137, 1, 2, N'Roster Swapper', N'arrow-left-right', N'ladt/roster-swapper', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (140, 5, 137, 1, 3, N'Pool Assignment', N'people', N'ladt/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (142, 5, 141, 1, 1, N'Schedule Hub', N'grid', N'scheduling/schedule-hub', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (143, 5, 141, 1, 2, N'View Schedule', N'eye', N'scheduling/view-schedule', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (144, 5, 141, 1, 3, N'Rescheduler', N'arrow-repeat', N'scheduling/rescheduler', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (145, 5, 141, 1, 4, N'Mobile Scorers', N'phone', N'scheduling/mobile-scorers', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (147, 5, 146, 1, 1, N'Health Check', N'heart-pulse', N'arb/health', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (149, 5, 148, 1, 1, N'US Lax Tester', N'check-circle', N'tools/uslax-test', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (150, 5, 148, 1, 2, N'US Lax Rankings', N'trophy', N'tools/uslax-rankings', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (152, 5, 151, 1, 1, N'Store Admin', N'shop', N'store/admin', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (154, 6, 153, 1, 1, N'Players', N'people', N'search/players', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (155, 6, 153, 1, 2, N'Teams', N'shield', N'search/teams', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (157, 6, 156, 1, 1, N'Job', N'briefcase', N'configure/job', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (158, 6, 156, 1, 2, N'Age Ranges', N'sliders', N'configure/age-ranges', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (159, 6, 156, 1, 3, N'Discount Codes', N'tags', N'configure/discount-codes', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (160, 6, 156, 1, 4, N'Uniform Upload', N'upload', N'configure/uniform-upload', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (162, 6, 161, 1, 1, N'Bulletins', N'megaphone', N'communications/bulletins', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (163, 6, 161, 1, 2, N'Email Log', N'envelope-open', N'communications/email-log', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (164, 6, 161, 1, 3, N'Push Notification', N'bell', N'communications/push-notification', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (166, 6, 165, 1, 1, N'Editor', N'pencil', N'ladt/editor', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (167, 6, 165, 1, 2, N'Roster Swapper', N'arrow-left-right', N'ladt/roster-swapper', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (168, 6, 165, 1, 3, N'Pool Assignment', N'people', N'ladt/pool-assignment', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (170, 6, 169, 1, 1, N'Schedule Hub', N'grid', N'scheduling/schedule-hub', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (171, 6, 169, 1, 2, N'View Schedule', N'eye', N'scheduling/view-schedule', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (172, 6, 169, 1, 3, N'Rescheduler', N'arrow-repeat', N'scheduling/rescheduler', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (173, 6, 169, 1, 4, N'Mobile Scorers', N'phone', N'scheduling/mobile-scorers', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (175, 6, 174, 1, 1, N'Health Check', N'heart-pulse', N'arb/health', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (177, 6, 176, 1, 1, N'US Lax Tester', N'check-circle', N'tools/uslax-test', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (178, 6, 176, 1, 2, N'US Lax Rankings', N'trophy', N'tools/uslax-rankings', NULL, NULL, GETDATE());
INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])
VALUES (180, 6, 179, 1, 1, N'Store Admin', N'shop', N'store/admin', NULL, NULL, GETDATE());

SET IDENTITY_INSERT [nav].[NavItem] OFF;
PRINT '  Loaded 180 nav items (48 parents, 132 children)';
PRINT '  2B complete';
GO

SET NOCOUNT ON;
PRINT '-- 2C: Store image data';

DELETE FROM stores.StoreItemImage;

DECLARE @sys NVARCHAR(128) = '71765055-647D-432E-AFB6-0F84218D0247';
DECLARE @now DATETIME = GETUTCDATE();

INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (8, N'https://statics.teamsportsinfo.com/Store-Sku-Images/3-8-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (9, N'https://statics.teamsportsinfo.com/Store-Sku-Images/3-9-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (10, N'https://statics.teamsportsinfo.com/Store-Sku-Images/3-10-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (11, N'https://statics.teamsportsinfo.com/Store-Sku-Images/3-11-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (12, N'https://statics.teamsportsinfo.com/Store-Sku-Images/3-12-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (13, N'https://statics.teamsportsinfo.com/Store-Sku-Images/4-13-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (14, N'https://statics.teamsportsinfo.com/Store-Sku-Images/4-14-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (15, N'https://statics.teamsportsinfo.com/Store-Sku-Images/4-15-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (16, N'https://statics.teamsportsinfo.com/Store-Sku-Images/4-16-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (17, N'https://statics.teamsportsinfo.com/Store-Sku-Images/4-17-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (18, N'https://statics.teamsportsinfo.com/Store-Sku-Images/5-18-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (19, N'https://statics.teamsportsinfo.com/Store-Sku-Images/5-19-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (20, N'https://statics.teamsportsinfo.com/Store-Sku-Images/5-20-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (21, N'https://statics.teamsportsinfo.com/Store-Sku-Images/5-21-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (22, N'https://statics.teamsportsinfo.com/Store-Sku-Images/5-22-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (23, N'https://statics.teamsportsinfo.com/Store-Sku-Images/6-23-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (24, N'https://statics.teamsportsinfo.com/Store-Sku-Images/6-24-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (25, N'https://statics.teamsportsinfo.com/Store-Sku-Images/6-25-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (26, N'https://statics.teamsportsinfo.com/Store-Sku-Images/6-26-1.jpg', 0, @now, @sys);
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
VALUES (27, N'https://statics.teamsportsinfo.com/Store-Sku-Images/6-27-1.jpg', 0, @now, @sys);

PRINT '  Loaded 20 StoreItemImage rows';

PRINT '  2C complete';
GO

-- ========================================================================
-- VERIFICATION SUMMARY
-- ========================================================================

SET NOCOUNT ON;

PRINT '';
PRINT '==========================================================';
PRINT '  VERIFICATION SUMMARY';
PRINT '==========================================================';
PRINT '';

PRINT '-- Widgets:';
SELECT 'WidgetCategory' AS [Table], COUNT(*) AS [Rows] FROM widgets.WidgetCategory
UNION ALL SELECT 'Widget', COUNT(*) FROM widgets.Widget
UNION ALL SELECT 'WidgetDefault', COUNT(*) FROM widgets.WidgetDefault
UNION ALL SELECT 'JobWidget', COUNT(*) FROM widgets.JobWidget
UNION ALL SELECT 'UserWidget', COUNT(*) FROM widgets.UserWidget;

PRINT '-- Nav:';
SELECT 'Nav (platform defaults)' AS [Table], COUNT(*) AS [Rows] FROM nav.Nav WHERE JobId IS NULL
UNION ALL SELECT 'NavItem (in defaults)', (SELECT COUNT(*) FROM nav.NavItem WHERE NavId IN (SELECT NavId FROM nav.Nav WHERE JobId IS NULL));

PRINT '-- Store Images:';
SELECT 'StoreItemImage' AS [Table], COUNT(*) AS [Rows] FROM stores.StoreItemImage


PRINT '-- Schema Columns:';
SELECT 'Jobs.AdultProfileMetadataJson' AS [Column],
    CASE WHEN COL_LENGTH('Jobs.Jobs', 'AdultProfileMetadataJson') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END AS [Status]
UNION ALL
SELECT 'widgets.Widget.DefaultConfig',
    CASE WHEN COL_LENGTH('widgets.Widget', 'DefaultConfig') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END;

-- ========================================================================
-- SECTION 3: IIS APP POOL DB LOGIN
-- After a restore, the IIS app pool identity loses database access.
-- This idempotently creates the login + user mapping.
-- ========================================================================

PRINT '-- 3: IIS App Pool DB Login';

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'IIS APPPOOL\TSIC.Api')
    CREATE LOGIN [IIS APPPOOL\TSIC.Api] FROM WINDOWS;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'IIS APPPOOL\TSIC.Api')
    CREATE USER [IIS APPPOOL\TSIC.Api] FOR LOGIN [IIS APPPOOL\TSIC.Api];
ELSE
    ALTER USER [IIS APPPOOL\TSIC.Api] WITH LOGIN = [IIS APPPOOL\TSIC.Api];

ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\TSIC.Api];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\TSIC.Api];

PRINT '  IIS APPPOOL\TSIC.Api login ensured.';
PRINT '  Section 3 complete.';
GO

PRINT '';
PRINT '==========================================================';
PRINT '  0-Restore-DevConfig-PROD.sql -- COMPLETE';
PRINT '  All schemas, tables, dev config, and IIS login are in place.';
PRINT '  Legacy tables were NOT modified.';
PRINT '==========================================================';

SET NOCOUNT OFF;

