-- ============================================================================
-- Seed: Dashboard Chart Widgets
--
-- Creates 3 chart widgets for the dashboard:
--   1. Player Registration Trend  (player-trend-chart)
--   2. Team Registration Trend    (team-trend-chart)
--   3. Age Group Distribution     (agegroup-distribution)
--
-- Widgets are assigned to Superuser, SuperDirector, and Director roles
-- for ALL existing JobTypes. Each renders in the 'content' section
-- (full-width, inside a collapsible card).
--
-- Prerequisites:
--   - widgets schema + tables (run Section 11 creation script first)
--   - seed-anonymous-widgets.sql (creates 'content' section/category)
--
-- Per-job overrides: INSERT into widgets.JobWidget with IsEnabled = 0
-- to hide a chart for a specific job.
-- ============================================================================

SET NOCOUNT ON;

-- ============================================================
-- 0. Widen CHECK constraint to allow 'chart' widget type
-- ============================================================

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')
BEGIN
    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];
    ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]
        CHECK ([WidgetType] IN ('content', 'chart', 'status-card', 'quick-action', 'workflow-pipeline', 'link-group'));
    PRINT 'Widened CK_widgets_Widget_WidgetType to include ''chart''.';
END

-- ============================================================
-- 1. Charts category (in content section, renders after banner/bulletins)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = 'Dashboard Charts' AND Section = 'content')
BEGIN
    INSERT INTO widgets.WidgetCategory (Name, Section, Icon, DefaultOrder)
    VALUES ('Dashboard Charts', 'content', NULL, 1);
    PRINT 'Created WidgetCategory: Dashboard Charts';
END

DECLARE @chartsCatId INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = 'Dashboard Charts' AND Section = 'content');

-- ============================================================
-- 2. Widget definitions
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'player-trend-chart')
BEGIN
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Player Registration Trend', 'chart', 'player-trend-chart', @chartsCatId,
            'Daily player registration counts and cumulative revenue over time');
    PRINT 'Created Widget: Player Registration Trend';
END

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'team-trend-chart')
BEGIN
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Team Registration Trend', 'chart', 'team-trend-chart', @chartsCatId,
            'Daily team registration counts and cumulative revenue over time');
    PRINT 'Created Widget: Team Registration Trend';
END

IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'agegroup-distribution')
BEGIN
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
    VALUES ('Age Group Distribution', 'chart', 'agegroup-distribution', @chartsCatId,
            'Player and team counts broken down by age group');
    PRINT 'Created Widget: Age Group Distribution';
END

DECLARE @playerTrendId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'player-trend-chart');
DECLARE @teamTrendId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'team-trend-chart');
DECLARE @agDistId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'agegroup-distribution');

-- ============================================================
-- 3. Role IDs
-- ============================================================

DECLARE @superuserId NVARCHAR(450)      = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';
DECLARE @superDirectorId NVARCHAR(450)  = '7B9EB503-53C9-44FA-94A0-17760C512440';
DECLARE @directorId NVARCHAR(450)       = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06';

-- ============================================================
-- 4. WidgetDefault entries for ALL JobTypes x each role
--    DisplayOrder: Player=1, Team=2, AgeGroup=3
-- ============================================================

-- Helper table with role IDs
DECLARE @roles TABLE (RoleId NVARCHAR(450));
INSERT INTO @roles VALUES (@superuserId), (@superDirectorId), (@directorId);

-- Player Trend Chart
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @playerTrendId, @chartsCatId, 1, NULL
FROM reference.JobTypes jt
CROSS JOIN @roles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId
      AND wd.RoleId = r.RoleId
      AND wd.WidgetId = @playerTrendId
);

-- Team Trend Chart
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @teamTrendId, @chartsCatId, 2, NULL
FROM reference.JobTypes jt
CROSS JOIN @roles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId
      AND wd.RoleId = r.RoleId
      AND wd.WidgetId = @teamTrendId
);

-- Age Group Distribution
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @agDistId, @chartsCatId, 3, NULL
FROM reference.JobTypes jt
CROSS JOIN @roles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId
      AND wd.RoleId = r.RoleId
      AND wd.WidgetId = @agDistId
);

-- ============================================================
-- Summary
-- ============================================================
PRINT '';
PRINT 'Chart widget defaults seeded for Superuser, SuperDirector, and Director on all JobTypes.';
PRINT '';
PRINT 'Widget IDs:';
PRINT '  Player Trend   = ' + CAST(@playerTrendId AS VARCHAR);
PRINT '  Team Trend     = ' + CAST(@teamTrendId AS VARCHAR);
PRINT '  Age Group Dist = ' + CAST(@agDistId AS VARCHAR);
PRINT '  Charts Cat     = ' + CAST(@chartsCatId AS VARCHAR);
PRINT '';
PRINT 'To disable a chart for a specific job:';
PRINT '  INSERT INTO widgets.JobWidget (JobId, WidgetId, RoleId, CategoryId, DisplayOrder, IsEnabled, Config)';
PRINT '  VALUES (''<job-guid>'', <widgetId>, ''<roleId>'', ' + CAST(@chartsCatId AS VARCHAR) + ', 1, 0, NULL);';

SET NOCOUNT OFF;
