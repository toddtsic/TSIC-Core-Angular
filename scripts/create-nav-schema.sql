-- ============================================================================
-- Nav Schema — Setup Script (v2)
--
-- DROPS and recreates the [nav] schema tables cleanly.
-- Run this, then re-scaffold EF entities.
--
-- Design:
--   nav.Nav     — One menu per role (JobId NULL = platform default)
--                  or per job+role (JobId set = override)
--   nav.NavItem — Two-level tree (parent → child) of menu items
--
-- Note: "One default per role" is enforced in the application service layer
--       (PlatformDefaultExistsAsync), NOT via a filtered unique index on
--       RoleId alone — that pattern causes EF scaffold to incorrectly infer
--       a 1:1 relationship between Nav and AspNetRoles.
--
-- Prerequisites:
--   - dbo.AspNetRoles populated
--   - dbo.AspNetUsers populated
--   - Jobs.Jobs populated
-- ============================================================================

SET NOCOUNT ON;

-- ── 1. Drop existing tables (child first) ──

IF EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'nav' AND t.name = 'NavItem')
BEGIN
    DROP TABLE [nav].[NavItem];
    PRINT 'Dropped table: nav.NavItem';
END

IF EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'nav' AND t.name = 'Nav')
BEGIN
    DROP TABLE [nav].[Nav];
    PRINT 'Dropped table: nav.Nav';
END

-- ── 2. Create schema (if first run) ──

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')
BEGIN
    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');
    PRINT 'Created [nav] schema';
END

-- ── 3. Create Nav table ──

CREATE TABLE [nav].[Nav]
(
    [NavId]         INT IDENTITY(1,1)       NOT NULL,
    [RoleId]        NVARCHAR(450)           NOT NULL,
    [JobId]         UNIQUEIDENTIFIER        NULL,
    [Active]        BIT                     NOT NULL    DEFAULT 1,
    [Modified]      DATETIME2               NOT NULL    DEFAULT GETDATE(),
    [ModifiedBy]    NVARCHAR(450)           NULL,

    CONSTRAINT [PK_nav_Nav]
        PRIMARY KEY CLUSTERED ([NavId]),
    CONSTRAINT [FK_nav_Nav_RoleId]
        FOREIGN KEY ([RoleId])
        REFERENCES [dbo].[AspNetRoles] ([Id]),
    CONSTRAINT [FK_nav_Nav_JobId]
        FOREIGN KEY ([JobId])
        REFERENCES [Jobs].[Jobs] ([JobId]),
    CONSTRAINT [FK_nav_Nav_ModifiedBy]
        FOREIGN KEY ([ModifiedBy])
        REFERENCES [dbo].[AspNetUsers] ([Id])
);

-- One override per role+job (only applies when JobId is set)
CREATE UNIQUE INDEX [UQ_nav_Nav_Role_Job]
    ON [nav].[Nav] ([RoleId], [JobId])
    WHERE [JobId] IS NOT NULL;

PRINT 'Created table: nav.Nav';

-- ── 4. Create NavItem table ──

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

    CONSTRAINT [PK_nav_NavItem]
        PRIMARY KEY CLUSTERED ([NavItemId]),
    CONSTRAINT [FK_nav_NavItem_NavId]
        FOREIGN KEY ([NavId])
        REFERENCES [nav].[Nav] ([NavId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_nav_NavItem_ParentNavItemId]
        FOREIGN KEY ([ParentNavItemId])
        REFERENCES [nav].[NavItem] ([NavItemId]),
    CONSTRAINT [FK_nav_NavItem_ModifiedBy]
        FOREIGN KEY ([ModifiedBy])
        REFERENCES [dbo].[AspNetUsers] ([Id])
);

PRINT 'Created table: nav.NavItem';

PRINT '';
PRINT '================================================';
PRINT ' Nav Schema Setup (v2) — Complete';
PRINT '================================================';

-- Verify
SELECT
    s.name AS [Schema],
    t.name AS [Table],
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS [Columns],
    (SELECT COUNT(*) FROM sys.indexes i WHERE i.object_id = t.object_id AND i.is_primary_key = 0 AND i.type > 0) AS [Indexes]
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = 'nav'
ORDER BY t.name;

SET NOCOUNT OFF;
