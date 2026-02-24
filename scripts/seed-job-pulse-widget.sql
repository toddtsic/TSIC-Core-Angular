-- ============================================================================
-- Seed: Job Pulse Widget
-- Created: 2026-02-24
--
-- Adds the "Job Pulse" smart registration widget to the widget system.
-- Shows real-time availability cards (player reg, team reg, store, schedule).
-- Idempotent — safe to run multiple times.
-- ============================================================================

SET NOCOUNT ON;

-- Insert Widget definition (if not already present)
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'job-pulse')
BEGIN
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
    VALUES (
        N'Job Pulse',
        N'content',
        N'job-pulse',
        1,  -- Public Content category
        N'Smart registration availability cards — player/team reg, store, schedule',
        N'{"displayStyle":"pulse"}'
    );
    PRINT 'Inserted Widget: Job Pulse';
END
ELSE
    PRINT 'Widget: Job Pulse already exists — skipped';

-- Get the WidgetId for insertion of defaults
DECLARE @widgetId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'job-pulse');
DECLARE @roleId NVARCHAR(450) = 'CBF3F384-190F-4962-BF58-40B095628DC8'; -- Anonymous
DECLARE @categoryId INT = 1; -- Public Content
DECLARE @displayOrder INT = 4; -- After event-contact (3)

-- Insert WidgetDefault rows for Anonymous role × all 7 JobTypes (0–6)
-- Only inserts if not already present for that JobType+Role+Widget+Category combo
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, @roleId, @widgetId, @categoryId, @displayOrder, NULL
FROM (VALUES (0),(1),(2),(3),(4),(5),(6)) AS jt(JobTypeId)
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault wd
    WHERE wd.JobTypeId = jt.JobTypeId
      AND wd.RoleId = @roleId
      AND wd.WidgetId = @widgetId
      AND wd.CategoryId = @categoryId
);

PRINT CONCAT('Inserted ', @@ROWCOUNT, ' WidgetDefault rows for Job Pulse (Anonymous × 7 JobTypes)');

-- Verification
SELECT w.WidgetId, w.Name, w.ComponentKey, w.WidgetType, w.DefaultConfig
FROM widgets.Widget w WHERE w.ComponentKey = 'job-pulse';

SELECT wd.WidgetDefaultId, wd.JobTypeId, wd.RoleId, wd.DisplayOrder
FROM widgets.WidgetDefault wd
JOIN widgets.Widget w ON wd.WidgetId = w.WidgetId
WHERE w.ComponentKey = 'job-pulse'
ORDER BY wd.JobTypeId;

SET NOCOUNT OFF;
