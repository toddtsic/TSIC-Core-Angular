-- ============================================================================
-- Dashboard Evolution Migration
--
-- Simplifies the widget system by removing spoke navigation and link-tiles.
-- Navigation is now handled by the nav menu system; the dashboard focuses
-- on operational intelligence (charts, KPIs) and communication (bulletins).
--
-- Changes:
--   1. Deletes all link-tile widgets + their WidgetDefault/JobWidget rows
--   2. Deletes 3 dormant status-tile entries (type stays for future KPIs)
--   3. Deletes 6 spoke workspace categories (keeps public + dashboard only)
--   4. Updates CHECK constraints:
--        WidgetType   → ('content','chart-tile','status-tile')
--        Workspace    → ('public','dashboard')
--   5. Creates widgets.UserWidget table for per-user dashboard customization
--
-- Fully idempotent. Safe to run repeatedly.
-- ============================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- ════════════════════════════════════════════════════════════
-- STEP 1: Remove link-tile widget assignments
-- ════════════════════════════════════════════════════════════

DELETE wd FROM widgets.WidgetDefault wd
  JOIN widgets.Widget w ON wd.WidgetId = w.WidgetId
  WHERE w.WidgetType = 'link-tile';
PRINT CONCAT('Step 1a: Deleted ', @@ROWCOUNT, ' WidgetDefault rows for link-tile widgets');

DELETE jw FROM widgets.JobWidget jw
  JOIN widgets.Widget w ON jw.WidgetId = w.WidgetId
  WHERE w.WidgetType = 'link-tile';
PRINT CONCAT('Step 1b: Deleted ', @@ROWCOUNT, ' JobWidget rows for link-tile widgets');

-- ════════════════════════════════════════════════════════════
-- STEP 2: Remove dormant status-tile assignments
-- ════════════════════════════════════════════════════════════

DELETE wd FROM widgets.WidgetDefault wd
  JOIN widgets.Widget w ON wd.WidgetId = w.WidgetId
  WHERE w.ComponentKey IN ('registration-status', 'financial-status', 'scheduling-status');
PRINT CONCAT('Step 2a: Deleted ', @@ROWCOUNT, ' WidgetDefault rows for dormant status-tiles');

DELETE jw FROM widgets.JobWidget jw
  JOIN widgets.Widget w ON jw.WidgetId = w.WidgetId
  WHERE w.ComponentKey IN ('registration-status', 'financial-status', 'scheduling-status');
PRINT CONCAT('Step 2b: Deleted ', @@ROWCOUNT, ' JobWidget rows for dormant status-tiles');

-- ════════════════════════════════════════════════════════════
-- STEP 3: Delete widget definitions
-- ════════════════════════════════════════════════════════════

DELETE FROM widgets.Widget WHERE WidgetType = 'link-tile';
PRINT CONCAT('Step 3a: Deleted ', @@ROWCOUNT, ' link-tile widget definitions');

DELETE FROM widgets.Widget
  WHERE ComponentKey IN ('registration-status', 'financial-status', 'scheduling-status');
PRINT CONCAT('Step 3b: Deleted ', @@ROWCOUNT, ' dormant status-tile widget definitions');

-- ════════════════════════════════════════════════════════════
-- STEP 4: Remove spoke workspace categories + orphaned assignments
-- ════════════════════════════════════════════════════════════

-- Clear any remaining assignments in non-surviving workspaces
DELETE wd FROM widgets.WidgetDefault wd
  JOIN widgets.WidgetCategory c ON wd.CategoryId = c.CategoryId
  WHERE c.Workspace NOT IN ('public', 'dashboard');
PRINT CONCAT('Step 4a: Deleted ', @@ROWCOUNT, ' orphan WidgetDefault rows in removed workspaces');

DELETE jw FROM widgets.JobWidget jw
  JOIN widgets.WidgetCategory c ON jw.CategoryId = c.CategoryId
  WHERE c.Workspace NOT IN ('public', 'dashboard');
PRINT CONCAT('Step 4b: Deleted ', @@ROWCOUNT, ' orphan JobWidget rows in removed workspaces');

-- Delete categories for removed workspaces
DELETE FROM widgets.WidgetCategory WHERE Workspace NOT IN ('public', 'dashboard');
PRINT CONCAT('Step 4c: Deleted ', @@ROWCOUNT, ' WidgetCategory rows for removed workspaces');

-- Delete WidgetDefault/JobWidget rows referencing soon-to-be-deleted empty categories
-- (WidgetDefault.CategoryId can differ from Widget.CategoryId — it's a placement override)
DELETE wd FROM widgets.WidgetDefault wd
  WHERE wd.CategoryId IN (
    SELECT c.CategoryId FROM widgets.WidgetCategory c
    WHERE NOT EXISTS (SELECT 1 FROM widgets.Widget w WHERE w.CategoryId = c.CategoryId)
  );
PRINT CONCAT('Step 4d: Deleted ', @@ROWCOUNT, ' WidgetDefault rows referencing empty categories');

DELETE jw FROM widgets.JobWidget jw
  WHERE jw.CategoryId IN (
    SELECT c.CategoryId FROM widgets.WidgetCategory c
    WHERE NOT EXISTS (SELECT 1 FROM widgets.Widget w WHERE w.CategoryId = c.CategoryId)
  );
PRINT CONCAT('Step 4e: Deleted ', @@ROWCOUNT, ' JobWidget rows referencing empty categories');

-- Now safe to delete empty categories
DELETE FROM widgets.WidgetCategory
  WHERE NOT EXISTS (
    SELECT 1 FROM widgets.Widget w WHERE w.CategoryId = WidgetCategory.CategoryId
  );
PRINT CONCAT('Step 4f: Deleted ', @@ROWCOUNT, ' empty WidgetCategory rows');

-- ════════════════════════════════════════════════════════════
-- STEP 5: Update CHECK constraints
-- ════════════════════════════════════════════════════════════

-- WidgetType: remove 'link-tile'
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')
    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];

ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]
    CHECK ([WidgetType] IN ('content', 'chart-tile', 'status-tile'));

PRINT 'Step 5a: Updated Widget.WidgetType CHECK → (content, chart-tile, status-tile)';

-- Workspace: reduce to 2 values
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')
    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Workspace];

ALTER TABLE [widgets].[WidgetCategory] ADD CONSTRAINT [CK_widgets_WidgetCategory_Workspace]
    CHECK ([Workspace] IN ('public', 'dashboard'));

PRINT 'Step 5b: Updated WidgetCategory.Workspace CHECK → (public, dashboard)';

-- ════════════════════════════════════════════════════════════
-- STEP 6: Create UserWidget table (per-user dashboard delta)
-- ════════════════════════════════════════════════════════════

IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'UserWidget')
BEGIN
    CREATE TABLE [widgets].[UserWidget]
    (
        [UserWidgetId]      INT IDENTITY(1,1)       NOT NULL,
        [RegistrationId]    UNIQUEIDENTIFIER        NOT NULL,
        [WidgetId]          INT                     NOT NULL,
        [CategoryId]        INT                     NOT NULL,
        [DisplayOrder]      INT                     NOT NULL    DEFAULT 0,
        [IsHidden]          BIT                     NOT NULL    DEFAULT 0,
        [Config]            NVARCHAR(MAX)           NULL,

        CONSTRAINT [PK_widgets_UserWidget]
            PRIMARY KEY CLUSTERED ([UserWidgetId]),

        CONSTRAINT [FK_widgets_UserWidget_Widget]
            FOREIGN KEY ([WidgetId])
            REFERENCES [widgets].[Widget] ([WidgetId]),

        CONSTRAINT [FK_widgets_UserWidget_Category]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),

        CONSTRAINT [UQ_widgets_UserWidget_Reg_Widget]
            UNIQUE ([RegistrationId], [WidgetId])
    );

    PRINT 'Step 6: Created table widgets.UserWidget';
END
ELSE
    PRINT 'Step 6: widgets.UserWidget already exists — skipped';

COMMIT TRANSACTION;

-- ════════════════════════════════════════════════════════════
-- VERIFICATION
-- ════════════════════════════════════════════════════════════

PRINT '';
PRINT '================================================';
PRINT ' Dashboard Evolution Migration — Complete';
PRINT '================================================';
PRINT '';

PRINT '— Surviving widgets by type:';
SELECT WidgetType, COUNT(*) AS [Count]
FROM widgets.Widget
GROUP BY WidgetType
ORDER BY WidgetType;

PRINT '— Surviving categories by workspace:';
SELECT Workspace, COUNT(*) AS [Count]
FROM widgets.WidgetCategory
GROUP BY Workspace
ORDER BY Workspace;

PRINT '— Assignment counts:';
SELECT 'WidgetDefault' AS [Table], COUNT(*) AS [Count] FROM widgets.WidgetDefault
UNION ALL
SELECT 'JobWidget', COUNT(*) FROM widgets.JobWidget
UNION ALL
SELECT 'UserWidget', COUNT(*) FROM widgets.UserWidget;

PRINT '— Widget detail:';
SELECT w.WidgetId, w.Name, w.WidgetType, w.ComponentKey, c.Name AS Category, c.Workspace
FROM widgets.Widget w
JOIN widgets.WidgetCategory c ON w.CategoryId = c.CategoryId
ORDER BY c.Workspace, c.DefaultOrder, w.Name;

SET NOCOUNT OFF;
