-- ============================================================================
-- Widget Type Consolidation Migration
--
-- Consolidates 6 WidgetType values down to 4 final types and seeds
-- displayStyle into Config JSON.
--
-- Old → New:
--   'content'        → 'content'       (unchanged)
--   'chart'          → 'chart-tile'    (rename)
--   'status-card'    → 'status-tile'   (rename)
--   'action-card'    → 'link-tile'     (absorb)
--   'pipeline-card'  → 'link-tile'     (absorb)
--   'link-card'      → 'link-tile'     (absorb)
--
-- Also handles legacy names from pre-rename DBs:
--   'quick-action'       → 'link-tile'
--   'workflow-pipeline'  → 'link-tile'
--   'link-group'         → 'link-tile'
--
-- Fully idempotent. Safe to run repeatedly.
-- Run AFTER migrate-widget-type-rename.sql (or standalone on any DB state).
-- ============================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- 1. Drop the CHECK constraint (so UPDATEs can write new values)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')
    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];

PRINT 'Dropped CK_widgets_Widget_WidgetType constraint';

-- 2. Rename and consolidate widget type values
--    Handle all possible prior states (original names + intermediate names)

-- chart → chart-tile
UPDATE widgets.Widget SET WidgetType = 'chart-tile' WHERE WidgetType = 'chart';

-- status-card → status-tile
UPDATE widgets.Widget SET WidgetType = 'status-tile' WHERE WidgetType = 'status-card';

-- All link-like types → link-tile
UPDATE widgets.Widget SET WidgetType = 'link-tile' WHERE WidgetType IN (
    'action-card', 'pipeline-card', 'link-card',        -- intermediate names
    'quick-action', 'workflow-pipeline', 'link-group'    -- original names
);

PRINT 'Consolidated WidgetType values → 4 final types';

-- 3. Re-add CHECK constraint with final 4 values
ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]
    CHECK ([WidgetType] IN ('content','chart-tile','status-tile','link-tile'));

PRINT 'Re-created CK_widgets_Widget_WidgetType with 4 values';

-- 4. Seed displayStyle into DefaultConfig JSON for content widgets
--    (banner vs feed distinction — only for widgets that have NULL config)

-- client-banner → displayStyle: banner
UPDATE widgets.Widget
SET DefaultConfig = '{"displayStyle":"banner"}'
WHERE ComponentKey = 'client-banner'
  AND (DefaultConfig IS NULL OR DefaultConfig = '');

-- bulletins → displayStyle: feed
UPDATE widgets.Widget
SET DefaultConfig = '{"displayStyle":"feed"}'
WHERE ComponentKey = 'bulletins'
  AND (DefaultConfig IS NULL OR DefaultConfig = '');

PRINT 'Seeded displayStyle for content widgets';

COMMIT TRANSACTION;

PRINT '';
PRINT '================================================';
PRINT ' Widget Type Consolidation — Complete';
PRINT '================================================';

-- Verify
SELECT WidgetType, COUNT(*) AS [Count]
FROM widgets.Widget
GROUP BY WidgetType
ORDER BY WidgetType;

-- Show content widgets with displayStyle
SELECT ComponentKey, WidgetType, DefaultConfig
FROM widgets.Widget
WHERE WidgetType = 'content';

SET NOCOUNT OFF;
