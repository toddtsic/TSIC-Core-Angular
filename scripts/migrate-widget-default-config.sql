-- ============================================================================
-- Migration: Add DefaultConfig column to widgets.Widget
--
-- Adds a DefaultConfig column to the Widget table so the widget editor
-- can store routing/display JSON on the widget definition itself.
-- New WidgetDefault entries created through the editor will inherit this
-- config automatically.
--
-- Also backfills DefaultConfig from existing WidgetDefault entries.
--
-- Idempotent — safe to run repeatedly.
-- ============================================================================

SET NOCOUNT ON;

-- ──────────────────────────────────────────────────────────
-- BATCH 1: Add DefaultConfig column (if not exists)
-- ──────────────────────────────────────────────────────────

IF COL_LENGTH('widgets.Widget', 'DefaultConfig') IS NULL
BEGIN
    ALTER TABLE [widgets].[Widget]
        ADD [DefaultConfig] NVARCHAR(MAX) NULL;
    PRINT 'Added column: widgets.Widget.DefaultConfig';
END
ELSE
BEGIN
    PRINT 'Column widgets.Widget.DefaultConfig already exists — skipping';
END

GO

-- ──────────────────────────────────────────────────────────
-- BATCH 2: Backfill + fix (separate batch so column is visible)
-- ──────────────────────────────────────────────────────────

SET NOCOUNT ON;

-- 2a. Backfill DefaultConfig from existing WidgetDefault entries
--     Takes the first non-null Config per widget.

;WITH FirstConfig AS (
    SELECT
        wd.WidgetId,
        wd.Config,
        ROW_NUMBER() OVER (PARTITION BY wd.WidgetId ORDER BY wd.WidgetDefaultId) AS rn
    FROM widgets.WidgetDefault wd
    WHERE wd.Config IS NOT NULL
)
UPDATE w
SET w.DefaultConfig = fc.Config
FROM widgets.Widget w
INNER JOIN FirstConfig fc ON w.WidgetId = fc.WidgetId AND fc.rn = 1
WHERE w.DefaultConfig IS NULL;

PRINT 'Backfilled DefaultConfig from existing WidgetDefault entries';

-- 2b. Set Config on any NULL WidgetDefault entries
--     that belong to a widget with a known DefaultConfig.
--     This fixes entries created before DefaultConfig existed.

UPDATE wd
SET wd.Config = w.DefaultConfig
FROM widgets.WidgetDefault wd
INNER JOIN widgets.Widget w ON wd.WidgetId = w.WidgetId
WHERE wd.Config IS NULL
  AND w.DefaultConfig IS NOT NULL;

DECLARE @fixedCount INT = @@ROWCOUNT;
PRINT CONCAT('Fixed ', @fixedCount, ' WidgetDefault entries with NULL Config');

-- ──────────────────────────────────────────────────────────
-- Done
-- ──────────────────────────────────────────────────────────

PRINT '';
PRINT 'Migration complete: DefaultConfig on widgets.Widget';

SELECT w.WidgetId, w.Name, w.ComponentKey, w.DefaultConfig
FROM widgets.Widget w
ORDER BY w.Name;

SET NOCOUNT OFF;
