/*
    28 — "Year over Year - All Events" dashboard widget
    Customer: AmericanSelect  (76586DE3-ACB3-42EE-91EB-48597CA06802)

    WHAT THIS DOES
    Registers the feeder-pace widget in the widget catalog and attaches it to the job the
    SuperDirector actually watches the season from.
        1. one widgets.Widget row, ComponentKey 'feeder-pace'
        2. widgets.JobWidget rows for SuperDirector and Superuser on the target job(s)
        3. relabels the sibling 'year-over-year' catalog row so the two read as a pair
           (@RenameSibling, ON by default — see below)

    THE TWO WIDGETS ARE SPLIT BY SCOPE, NOT BY PERIOD
    Both compare seasons; both are year-over-year. Naming one of them "Year to Date" implied the
    other was not, which is false and was the confusion (Todd, 2026-09-20). The real and only
    difference is how much of the customer's season each one reads:

        Year over Year - This Event   ComponentKey 'year-over-year' — THIS job against its own
                                      prior seasons, full-season cumulative curves on real
                                      calendar dates, up to 4 seasons, no as-of cut.
        Year over Year - All Events   ComponentKey 'feeder-pace' — EVERY job the customer runs
                                      in the season, rolled up and per site, each season cut
                                      year-to-date at the same calendar date, up to 6 seasons.

    WHY IT IS ITS OWN WIDGET, NOT AN EDIT TO 'year-over-year'
    The single-event report is the right one for a customer running one event a season — 221 of
    them — and it stays exactly as it is (Todd, 2026-09-20). It cannot answer this customer's
    question: AmericanSelect runs ~24 regional tryout sites plus a Main Event every season, and
    the SuperDirector wants the pace of ALL of them against prior years. On
    americanselect-mainevent-2027 the existing widget finds only two comparable jobs, both
    nearly empty, because the Main Event does not fill until the tryouts have run.

    ENCODING NOTE
    Widget names below use an ASCII hyphen, not an em dash. SSMS opens a BOM-less file as ANSI
    and would mojibake a non-ASCII character straight into widgets.Widget.Name.

    ROLES — SuperDirector AND Superuser ONLY, deliberately.
    The endpoint is gated CanCrossCustomerJobs, the same policy as job-reg-counts-dollars,
    because it reads every job of the customer. Attaching it for Director would put a widget on
    their dashboard that answers 403 and renders an error — worse than not offering it.

    TARGET JOB
    @AllJobsInSeason = 0 (default): americanselect-mainevent-2027 alone — the job the report
    was asked for. The report is customer-scoped, so it reads identically from any job in the
    season; set @AllJobsInSeason = 1 to put it on every AmericanSelect 2027 job instead.

    DEPLOY ORDER
    Deploy the build carrying the widget FIRST. ComponentKey must match WIDGET_MANIFEST in
    widgets/widget-registry.ts; before the deploy the key resolves to nothing and the
    dashboard renders an empty slot with only a dev-mode warning.

    RE-RUN SAFE
    Catalog row: reused if ComponentKey already exists (aborts if its ComponentKey is taken by
    a different WidgetType/Category). Attachment rows: UQ_widgets_JobWidget_Job_Widget_Role
    makes a duplicate an error, so existing rows are skipped and reported.

    RUN
    @Commit = 0 (default) previews inside a transaction and rolls back.
    @Commit = 1 writes.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @CustomerId       uniqueidentifier = '76586DE3-ACB3-42EE-91EB-48597CA06802';
DECLARE @Year             varchar(4)       = '2027';
DECLARE @MainEventPath    nvarchar(200)    = N'americanselect-mainevent-2027';
DECLARE @AllJobsInSeason  bit              = 0;
DECLARE @Commit           bit              = 0;

DECLARE @ComponentKey nvarchar(200) = N'feeder-pace';
DECLARE @WidgetName   nvarchar(200) = N'Year over Year - All Events';
DECLARE @WidgetType   nvarchar(50)  = N'content';
DECLARE @CategoryId   int           = 3;             -- Dashboard Charts
DECLARE @Description  nvarchar(500) = N'Registrations and money collected across EVERY event this customer runs in the season - rolled up and per site - each season cut year-to-date at the same calendar date. For a single event against its own prior seasons, use Year over Year - This Event.';
DECLARE @DefaultConfig nvarchar(max) = N'{"label":"Year over Year - All Events","icon":"bi-bar-chart-line"}';

-- RoleConstants.cs — SuperDirector and Superuser are the CanCrossCustomerJobs pair that can
-- also hold a dashboard. ApiAuthorized, RefAssignor, StoreAdmin and StpAdmin are in the policy
-- but are vendor/utility logins with no dashboard to put this on.
DECLARE @SuperDirector nvarchar(900) = N'7B9EB503-53C9-44FA-94A0-17760C512440';
DECLARE @Superuser     nvarchar(900) = N'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';

BEGIN TRANSACTION;

-- ── 1. Catalog row ─────────────────────────────────────────────────────────────
DECLARE @WidgetId int;

SELECT @WidgetId = WidgetId FROM widgets.Widget WHERE ComponentKey = @ComponentKey;

IF @WidgetId IS NULL
BEGIN
    INSERT widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
    VALUES (@WidgetName, @WidgetType, @ComponentKey, @CategoryId, @Description, @DefaultConfig);

    SET @WidgetId = SCOPE_IDENTITY();
    PRINT CONCAT('Catalog: created WidgetId ', @WidgetId, ' for ComponentKey ''', @ComponentKey, '''.');
END
ELSE
BEGIN
    PRINT CONCAT('Catalog: WidgetId ', @WidgetId, ' already holds ComponentKey ''', @ComponentKey, ''' — reused.');

    IF EXISTS (SELECT 1 FROM widgets.Widget
               WHERE WidgetId = @WidgetId AND (WidgetType <> @WidgetType OR CategoryId <> @CategoryId))
    BEGIN
        -- widgetType decides rendering: 'status-tile' categories are skipped outright and
        -- 'chart-tile' crushes a full-width table into the small-chart grid. Either way the
        -- widget renders nowhere with no error, so a mismatch is a stop, not a warning.
        ROLLBACK TRANSACTION;
        RAISERROR('ComponentKey ''%s'' exists with a different WidgetType/CategoryId. Resolve by hand.', 16, 1, @ComponentKey);
        RETURN;
    END
END

-- ── 1b. Sibling rename (ON by default) ─────────────────────────────────────────
-- Widget 23 is "Year-over-Year Comparison", and its stored Description reads "Registration
-- comparison between current and prior year" — which is wrong on its own terms: it draws up to
-- FOUR seasons, not two. Left alone, the widget editor lists "Year-over-Year Comparison" beside
-- "Year over Year - All Events" and the two are near-indistinguishable.
--
-- This changes the sibling's LABEL and DESCRIPTION only. ComponentKey, component, endpoint,
-- query and behaviour are all untouched, and widget-registry.ts already carries the matching
-- label, so leaving this at 0 puts the manifest and the catalog out of step.
--
-- Scope of the blast: widgets.Widget is the shared catalog, so this row's label changes for
-- EVERY customer that has the widget attached — which is the point. Set to 0 to skip it.
DECLARE @RenameSibling bit = 1;

DECLARE @SiblingName nvarchar(200) = N'Year over Year - This Event';
DECLARE @SiblingDesc nvarchar(500) = N'Registration pace for this event against its own prior seasons, as full-season cumulative curves. For every event the customer runs, use Year over Year - All Events.';

IF @RenameSibling = 1
BEGIN
    SELECT Name AS [Sibling name BEFORE], Description AS [Sibling description BEFORE]
    FROM widgets.Widget WHERE ComponentKey = N'year-over-year';

    UPDATE widgets.Widget
    SET Name        = @SiblingName,
        Description = @SiblingDesc
    WHERE ComponentKey = N'year-over-year'
      AND (Name <> @SiblingName OR ISNULL(Description, N'') <> @SiblingDesc);

    PRINT CONCAT('Sibling rename: ', @@ROWCOUNT, ' row(s).');
END
ELSE
    PRINT 'Sibling rename: SKIPPED — catalog label will not match widget-registry.ts.';

-- ── 2. Target jobs ─────────────────────────────────────────────────────────────
DECLARE @Targets TABLE (JobId uniqueidentifier PRIMARY KEY, JobName nvarchar(400));

INSERT @Targets (JobId, JobName)
SELECT j.JobId, j.JobName
FROM Jobs.Jobs j
WHERE j.CustomerId = @CustomerId
  AND j.year = @Year
  AND (@AllJobsInSeason = 1 OR j.JobPath = @MainEventPath);

IF NOT EXISTS (SELECT 1 FROM @Targets)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('No target job found. Check @MainEventPath / @Year against Jobs.Jobs.', 16, 1);
    RETURN;
END

SELECT JobName AS [Target job] FROM @Targets ORDER BY JobName;

-- ── 3. Attach ──────────────────────────────────────────────────────────────────
DECLARE @Roles TABLE (RoleId nvarchar(900) PRIMARY KEY);
INSERT @Roles (RoleId) VALUES (@SuperDirector), (@Superuser);

-- DisplayOrder 5: after the registration/financial tiles a dashboard already carries. The
-- widget editor is where this gets tuned; it only needs to not collide here.
INSERT widgets.JobWidget (JobId, WidgetId, RoleId, CategoryId, DisplayOrder, IsEnabled, Config)
SELECT t.JobId, @WidgetId, r.RoleId, @CategoryId, 5, 1, @DefaultConfig
FROM @Targets t
CROSS JOIN @Roles r
WHERE NOT EXISTS (
    SELECT 1 FROM widgets.JobWidget jw
    WHERE jw.JobId = t.JobId AND jw.WidgetId = @WidgetId AND jw.RoleId = r.RoleId);

PRINT CONCAT('Attached: ', @@ROWCOUNT, ' JobWidget row(s).');

SELECT j.JobName, jw.RoleId, jw.CategoryId, jw.DisplayOrder, jw.IsEnabled
FROM widgets.JobWidget jw
JOIN Jobs.Jobs j ON j.JobId = jw.JobId
WHERE jw.WidgetId = @WidgetId
ORDER BY j.JobName, jw.RoleId;

IF @Commit = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT 'COMMITTED.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'PREVIEW ONLY — rolled back. Set @Commit = 1 to write.';
END
