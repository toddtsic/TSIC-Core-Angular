-- ============================================================================
-- Widget Type Rename Migration
--
-- Renames 3 WidgetType values from muddled domain names to proper
-- rendering-pattern names per the Widget Component Taxonomy decision.
--
-- Old → New:
--   'quick-action'       → 'action-card'
--   'workflow-pipeline'  → 'pipeline-card'
--   'link-group'         → 'link-card'
--
-- Fully idempotent. Safe to run repeatedly.
-- Run BEFORE deploying the updated seed-widget-dashboard.sql.
-- ============================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- 1. Drop the CHECK constraint first (so UPDATEs can write new values)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')
    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];

PRINT 'Dropped CK_widgets_Widget_WidgetType constraint';

-- 2. Rename widget type values in the Widget table
UPDATE widgets.Widget SET WidgetType = 'action-card'   WHERE WidgetType = 'quick-action';
UPDATE widgets.Widget SET WidgetType = 'pipeline-card'  WHERE WidgetType = 'workflow-pipeline';
UPDATE widgets.Widget SET WidgetType = 'link-card'      WHERE WidgetType = 'link-group';

PRINT 'Updated WidgetType values in widgets.Widget';

-- 3. Re-add the CHECK constraint with new allowed values
ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]
    CHECK ([WidgetType] IN ('content','chart','status-card','action-card','pipeline-card','link-card'));

PRINT 'Re-created CK_widgets_Widget_WidgetType constraint with new values';

COMMIT TRANSACTION;

PRINT '';
PRINT '================================================';
PRINT ' Widget Type Rename Migration — Complete';
PRINT '================================================';

-- Verify
SELECT WidgetType, COUNT(*) AS [Count]
FROM widgets.Widget
GROUP BY WidgetType
ORDER BY WidgetType;

SET NOCOUNT OFF;
