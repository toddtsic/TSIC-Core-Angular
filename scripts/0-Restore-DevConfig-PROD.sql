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
-- Generated: 2026-03-08 17:05:43
-- Source:    0-Restore-DevConfig-DEV.ps1 (queried dev DB)
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
-- Snapshot: 0 widget categories, 0 widgets, 0 defaults, 0 job overrides
--           0 navs, 0 nav items
--           20 store images
--
-- Prerequisites: reference.JobTypes + dbo.AspNetRoles + dbo.AspNetUsers populated
-- ============================================================================

SET NOCOUNT ON;

PRINT '';
PRINT '==========================================================';
PRINT '  0-Restore-DevConfig-PROD.sql';
PRINT '  Generated: 2026-03-08 17:05:43';
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
SET IDENTITY_INSERT widgets.WidgetCategory OFF;
PRINT '  Loaded 0 categories';

SET IDENTITY_INSERT widgets.Widget ON;
SET IDENTITY_INSERT widgets.Widget OFF;
PRINT '  Loaded 0 widgets';

SET IDENTITY_INSERT widgets.WidgetDefault ON;
SET IDENTITY_INSERT widgets.WidgetDefault OFF;
PRINT '  Loaded 0 defaults';

PRINT '  Loaded 0 job overrides';
PRINT '  2A complete';
GO

SET NOCOUNT ON;
PRINT '-- 2B: Nav data (platform defaults only)';

-- Clear platform defaults only (job-specific overrides survive)
DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);
DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;

SET IDENTITY_INSERT [nav].[Nav] ON;
SET IDENTITY_INSERT [nav].[Nav] OFF;
PRINT '  Loaded 0 platform default navs';

SET IDENTITY_INSERT [nav].[NavItem] ON;

-- Parents

-- Children

SET IDENTITY_INSERT [nav].[NavItem] OFF;
PRINT '  Loaded 0 nav items (0 parents, 0 children)';
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

