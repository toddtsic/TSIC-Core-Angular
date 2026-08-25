-- ============================================================================
-- Seed: Financial Health widget (DIRECTOR ONLY)
-- Created: 2026-08-25
--
-- Registers the Financial Health panel in the widget system and creates the
-- dashboard category it lives in.
--
-- DELIBERATELY DOES **NOT** SEED widgets.WidgetDefault.
-- WidgetDefault rows are what auto-attach a widget to every job of a given
-- JobType. This panel is still being designed and must appear ONLY on jobs
-- chosen by hand — that control is the entire reason it was moved out of the
-- Smart Bulletins band, which renders unconditionally. Attaching is STEP 3
-- below, one job at a time.
--
-- Idempotent — safe to run multiple times.
-- Read-only verification at the end; run that FIRST if you want to look before
-- you write.
-- ============================================================================

SET NOCOUNT ON;

-- ── BEFORE: what exists today ───────────────────────────────────────────────
PRINT '--- BEFORE ---';
SELECT CategoryId, Name, Workspace, DefaultOrder FROM widgets.WidgetCategory ORDER BY Workspace, DefaultOrder;
SELECT WidgetId, Name, ComponentKey, WidgetType, CategoryId FROM widgets.Widget WHERE ComponentKey = 'financial-health';

-- ── STEP 1: category ────────────────────────────────────────────────────────
-- Existing categories are 1 = Public Content (public) and 3 = Dashboard Charts
-- (dashboard). This panel is 'content', not a chart tile, so it needs a content
-- category on the dashboard workspace. DefaultOrder 0 puts it above the charts.
IF NOT EXISTS (SELECT 1 FROM widgets.WidgetCategory WHERE Name = N'Financial')
BEGIN
    INSERT INTO widgets.WidgetCategory (Name, Workspace, Icon, DefaultOrder)
    VALUES (N'Financial', N'dashboard', N'bi-heart-pulse', 0);
    PRINT 'Inserted WidgetCategory: Financial';
END
ELSE
    PRINT 'WidgetCategory: Financial already exists - skipped';

DECLARE @categoryId INT = (SELECT CategoryId FROM widgets.WidgetCategory WHERE Name = N'Financial');

-- ── STEP 2: widget catalog row ──────────────────────────────────────────────
-- ComponentKey MUST match the WIDGET_MANIFEST key in widget-registry.ts
-- ('financial-health'); that string is how the dashboard resolves the Angular
-- component for NgComponentOutlet. A typo here renders nothing, silently.
IF NOT EXISTS (SELECT 1 FROM widgets.Widget WHERE ComponentKey = 'financial-health')
BEGIN
    INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
    VALUES (
        N'Financial Health',
        N'content',
        N'financial-health',
        @categoryId,
        N'DIRECTOR ONLY - expiring cards, subscription drift, and balances owed',
        N'{"displayStyle":"panel"}'
    );
    PRINT 'Inserted Widget: Financial Health';
END
ELSE
    PRINT 'Widget: Financial Health already exists - skipped';

DECLARE @widgetId INT = (SELECT WidgetId FROM widgets.Widget WHERE ComponentKey = 'financial-health');

-- ── STEP 3: attach to ONE job, for the THREE admin roles ────────────────────
-- This is the rollout control. Nothing renders until a JobWidget row exists.
-- Pre-set to jobId EE511CAA-37FE-49D1-A2B7-1B9660F75F4F (STEPS Lacrosse: Girls Elite
-- Players 2026-2027 - 408 registrations, 371 ACTIVE ARB subscriptions). Uncomment
-- Runs as-is. Repeat per job as you widen (change @jobId). Idempotent.
--
-- !! THIS ROW IS NOT ENVIRONMENT-SCOPED IF RUN AGAINST THE REAL PROD DB. Run locally
-- !! (TSIC-SEDONA\SS2016) it is dev-only — that instance is a restored COPY of a prod
-- !! backup. Going live in production is a SEPARATE, deliberate run on that box. While
-- !! the panel shows placeholder figures, the envName guard in
-- !! financial-health.component.ts is what keeps invented money numbers out of a
-- !! production build. Do not delete that guard until the panel shows real data.
-- !!
-- !! NOTE: the next __Restore-DevDb-From-Prod.ps1 WIPES these rows. Re-run this script.
--
-- ONE ROW PER ROLE. GetJobWidgetsAsync filters `jw.RoleId == null || jw.RoleId == roleId`
-- against the CALLER's own role (resolved from their JWT role name by
-- WidgetDashboardService.RoleNameToIdMap), so a role with no row simply does not see the
-- panel. NEVER set RoleId = NULL here: null matches EVERY role on the dashboard, which
-- makes the panel's audience depend on who happens to have dashboard access rather than
-- on an explicit rule.
--
-- Todd 2026-08-25: all three admin roles during development, so the panel can be opened
-- as Superuser without impersonating a Director. TIGHTEN TO DIRECTOR-ONLY once it
-- carries real numbers — delete the SuperDirector and Superuser rows then.

DECLARE @jobId   UNIQUEIDENTIFIER = 'EE511CAA-37FE-49D1-A2B7-1B9660F75F4F';  -- stepsgirls-players-2026-2027
DECLARE @jobPath NVARCHAR(200) = (SELECT JobPath FROM Jobs.Jobs WHERE JobId = @jobId);

IF @jobId IS NULL
    PRINT 'NO SUCH JOB - nothing attached';
ELSE
BEGIN
    INSERT INTO widgets.JobWidget (JobId, WidgetId, RoleId, CategoryId, DisplayOrder, IsEnabled, Config)
    SELECT @jobId, @widgetId, r.RoleId, @categoryId, 0, 1, NULL
    FROM (VALUES
            ('FF4D1C27-F6DA-4745-98CC-D7E8121A5D06'),   -- Director
            ('7B9EB503-53C9-44FA-94A0-17760C512440'),   -- SuperDirector
            ('CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9')    -- Superuser
         ) AS r(RoleId)
    WHERE NOT EXISTS (
        SELECT 1 FROM widgets.JobWidget jw
        WHERE jw.JobId    = @jobId
          AND jw.WidgetId = @widgetId
          AND jw.RoleId   = r.RoleId
    );

    PRINT CONCAT('Attached Financial Health to ', @jobPath, ' for ', @@ROWCOUNT, ' role(s)');
END
-- ── AFTER: verification ─────────────────────────────────────────────────────
PRINT '--- AFTER ---';
SELECT c.CategoryId, c.Name, c.Workspace, c.DefaultOrder
FROM widgets.WidgetCategory c WHERE c.Name = N'Financial';

SELECT w.WidgetId, w.Name, w.ComponentKey, w.WidgetType, w.CategoryId, w.DefaultConfig
FROM widgets.Widget w WHERE w.ComponentKey = 'financial-health';

-- Should be EMPTY until you run step 3. Any row here is a job that will show it.
SELECT j.JobPath, j.JobName, jw.IsEnabled, jw.DisplayOrder,
       CASE
            WHEN jw.RoleId IS NULL THEN '!! NULL = EVERY ROLE !!'
            WHEN jw.RoleId = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06' THEN 'Director'
            WHEN jw.RoleId = '7B9EB503-53C9-44FA-94A0-17760C512440' THEN 'SuperDirector'
            WHEN jw.RoleId = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9' THEN 'Superuser'
            ELSE jw.RoleId END AS RoleName
FROM widgets.JobWidget jw
JOIN widgets.Widget w ON w.WidgetId = jw.WidgetId
JOIN Jobs.Jobs j       ON j.JobId    = jw.JobId
WHERE w.ComponentKey = 'financial-health'
ORDER BY j.JobName;

-- Confirms nothing auto-attaches to new jobs. Should be EMPTY, permanently.
SELECT wd.WidgetDefaultId, wd.JobTypeId, wd.RoleId
FROM widgets.WidgetDefault wd
JOIN widgets.Widget w ON w.WidgetId = wd.WidgetId
WHERE w.ComponentKey = 'financial-health';

SET NOCOUNT OFF;
