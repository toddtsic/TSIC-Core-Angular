-- ============================================================================
-- 27: TeamsAppUsage widget (TSIC-TEAMS app usage, Director and above)
-- Created: 2026-09-16
--
-- Registers the widget and places it on the dashboard for Director, SuperDirector and
-- Superuser on the job types that CAN run TSIC-TEAMS: 1 Club, 4 Camp, 5 Sales (the Teams
-- branch of PushAudienceResolver). Types 2/3 are TSIC-EVENTS jobs; 6 Showcase is ruled off
-- both apps.
--
-- A job of those types with bEnableTSICTeams = 0 shows the card with one line:
-- "TSIC-TEAMS is not enabled for this event."
--
-- ComponentKey MUST equal the WIDGET_MANIFEST key in widget-registry.ts
-- ('teams-app-usage'), or the dashboard renders nothing, silently.
-- WidgetType MUST be 'content' -- 'status-tile' is skipped and 'chart-tile' lands in the
-- small-chart grid.
--
-- Idempotent. Run BEFORE / AFTER selects first if you want to look before writing.
-- NOTE: __Restore-DevDb-From-Prod.ps1 wipes these rows on dev; re-run after a restore.
-- ============================================================================

SET NOCOUNT ON;

PRINT '--- BEFORE ---';
SELECT WidgetId, Name, ComponentKey, WidgetType, CategoryId FROM widgets.Widget WHERE ComponentKey = 'teams-app-usage';

-- ── Widget catalog row ──────────────────────────────────────────────────────
-- Category 3 = Dashboard Charts (dashboard workspace), same as UsageStatsPerJob.
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'teams-app-usage')
BEGIN
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
    VALUES (
        N'TeamsAppUsage',
        N'content',
        N'teams-app-usage',
        3,
        N'TSIC-TEAMS app use by rostered players and staff: people, teams and days',
        N'{"label":"TeamsAppUsage","icon":"bi-phone","displayStyle":"table"}'
    );
    PRINT 'Inserted Widget: TeamsAppUsage';
END
ELSE
    PRINT 'Widget: TeamsAppUsage already exists - skipped';

DECLARE @widgetId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'teams-app-usage');

-- ── Platform defaults: 3 job types x 3 admin roles ──────────────────────────
-- One row per role, never RoleId NULL (NULL matches every role on the dashboard).
INSERT INTO widgets.WidgetDefault (JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)
SELECT jt.JobTypeId, r.RoleId, @widgetId, 3, 8,
       N'{"label":"TeamsAppUsage","icon":"bi-phone","displayStyle":"table"}'
FROM (VALUES (1), (4), (5)) AS jt(JobTypeId)
CROSS JOIN (VALUES
        ('FF4D1C27-F6DA-4745-98CC-D7E8121A5D06'),   -- Director
        ('7B9EB503-53C9-44FA-94A0-17760C512440'),   -- SuperDirector
        ('CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9')    -- Superuser
     ) AS r(RoleId)
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.WidgetDefault d
    WHERE d.JobTypeId = jt.JobTypeId
      AND d.RoleId    = r.RoleId
      AND d.WidgetId  = @widgetId
);
PRINT CONCAT('Inserted ', @@ROWCOUNT, ' WidgetDefault row(s)');

PRINT '--- AFTER ---';
SELECT w.WidgetId, w.Name, w.ComponentKey, w.WidgetType, w.CategoryId
FROM widgets.Widget w WHERE w.ComponentKey = 'teams-app-usage';

SELECT d.JobTypeId, d.RoleId, d.CategoryId, d.DisplayOrder
FROM widgets.WidgetDefault d
JOIN widgets.Widget w ON w.WidgetId = d.WidgetId
WHERE w.ComponentKey = 'teams-app-usage'
ORDER BY d.JobTypeId, d.RoleId;
