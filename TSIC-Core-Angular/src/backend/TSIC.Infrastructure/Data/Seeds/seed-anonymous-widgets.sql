-- ============================================================================
-- Seed: Anonymous (Public) Widget Dashboard
--
-- Creates content widgets (banner + bulletins) for the public landing page.
-- Uses the existing Anonymous role (CBF3F384-190F-4962-BF58-40B095628DC8).
-- WidgetDefault entries are created for ALL existing JobTypes.
--
-- Prerequisites:
--   - widgets schema + tables (run Section 11 creation script first)
--   - This script ALTERs the CHECK constraints to allow 'content' values
--
-- Per-job overrides: INSERT into widgets.JobWidget with IsEnabled = 0
-- to hide a widget for a specific job.
-- ============================================================================

SET NOCOUNT ON;

-- ============================================================
-- 0. Widen CHECK constraints to allow 'content' section + type
-- ============================================================

-- WidgetCategory.Section: add 'content'
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Section')
BEGIN
    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Section];
    ALTER TABLE [widgets].[WidgetCategory] ADD CONSTRAINT [CK_widgets_WidgetCategory_Section]
        CHECK ([Section] IN ('content', 'health', 'action', 'insight'));
    PRINT 'Widened CK_widgets_WidgetCategory_Section to include ''content''.';
END

-- Widget.WidgetType: add 'content'
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')
BEGIN
    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];
    ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]
        CHECK ([WidgetType] IN ('content', 'status-card', 'quick-action', 'workflow-pipeline', 'link-group'));
    PRINT 'Widened CK_widgets_Widget_WidgetType to include ''content''.';
END

-- ============================================================
-- 1. Content category
-- ============================================================

DECLARE @anonymousRoleId NVARCHAR(450) = 'CBF3F384-190F-4962-BF58-40B095628DC8';

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Content' AND Section = 'content')
BEGIN
    INSERT INTO widgets.WidgetCategory (Name, Section, Icon, DefaultOrder)
    VALUES ('Content', 'content', NULL, 0);
    PRINT 'Created WidgetCategory: Content';
END

DECLARE @catId INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Content' AND Section = 'content');

-- ============================================================
-- 2. Widget definitions
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'client-banner')
BEGIN
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Client Banner', 'content', 'client-banner', @catId, 'Job banner with logo and images');
    PRINT 'Created Widget: Client Banner';
END

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'bulletins')
BEGIN
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Bulletins', 'content', 'bulletins', @catId, 'Active job bulletins and announcements');
    PRINT 'Created Widget: Bulletins';
END

DECLARE @bannerWidgetId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'client-banner');
DECLARE @bulletinsWidgetId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'bulletins');

-- ============================================================
-- 3. WidgetDefault entries for ALL JobTypes x Anonymous role
--    Banner at DisplayOrder 1, Bulletins at DisplayOrder 2
-- ============================================================

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, @anonymousRoleId, @bannerWidgetId, @catId, 1, NULL
FROM reference.JobTypes jt
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId
      AND wd.RoleId = @anonymousRoleId
      AND wd.WidgetId = @bannerWidgetId
);

INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, @anonymousRoleId, @bulletinsWidgetId, @catId, 2, NULL
FROM reference.JobTypes jt
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId
      AND wd.RoleId = @anonymousRoleId
      AND wd.WidgetId = @bulletinsWidgetId
);

-- ============================================================
-- Summary
-- ============================================================
PRINT 'Anonymous widget defaults seeded for all JobTypes.';
PRINT '';
PRINT 'Widget IDs:';
PRINT '  Client Banner = ' + CAST(@bannerWidgetId AS VARCHAR);
PRINT '  Bulletins     = ' + CAST(@bulletinsWidgetId AS VARCHAR);
PRINT '  Category      = ' + CAST(@catId AS VARCHAR);
PRINT '';
PRINT 'To disable banner for a specific job:';
PRINT '  INSERT INTO widgets.JobWidget (JobId, WidgetId, RoleId, CategoryId, DisplayOrder, IsEnabled, Config)';
PRINT '  VALUES (''<job-guid>'', ' + CAST(@bannerWidgetId AS VARCHAR) + ', ''' + @anonymousRoleId + ''', ' + CAST(@catId AS VARCHAR) + ', 1, 0, NULL);';

SET NOCOUNT OFF;
