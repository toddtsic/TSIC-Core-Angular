/*
    25 — AmericanSelect 2027: "Docs" section on the Director + SuperDirector + Superuser nav
    Customer: AmericanSelect  (76586DE3-ACB3-42EE-91EB-48597CA06802)

    WHAT THIS DOES
    Legacy put six stored PDFs (statics.teamsportsinfo.com/docs/americanselect/) under a "Docs"
    parent on the Director and SuperDirector job menus (Jobs.JobMenus / Jobs.JobMenu_Items).
    The new app's admin chrome reads nav.Nav / nav.NavItem, so those links are unreachable.
    This adds a per-job nav override for each 2027 job: one new root section "Docs" with one
    External URL child per document.

    SEEDED FROM THE DATA, NOT A HARDCODED LIST
    Link text, URLs and order come from the customer's 2027 legacy Director "Docs" links (job
    clone carried them forward from 2026; identical on every job that has them). Aborts if
    that list is empty or one document carries two labels. Rulings (Todd, 2026-09-16):
        - EVERY 2027 job of this customer gets the section — including jobs whose own legacy
          menu had no Docs (americanselect-mainevent-2027)
        - roles: Director, SuperDirector, Superuser (legacy had no Superuser Docs section)
        - COVID Athlete Admittance Ticket is DROPPED
        - every link opens in a new tab (Target = _blank); legacy was mixed _self/_blank/NULL
        - section name "Docs", appended after the platform sections

    Expected on a fresh dev DB (09-16): 75 nav.Nav rows (25 jobs x 3 roles), 75 "Docs" roots,
    375 links. After the first run (24 jobs done), a re-run adds mainevent only: 3 / 3 / 15.

    RE-RUN SAFE
    A job/role whose override nav already has an active "Docs" root is skipped and reported.
    An override nav that already exists for other reasons is reused, never duplicated
    (UQ_nav_Nav_Role_Job).

    job clone (JobCloneResetRules.CloneNav) copies per-job overrides, so 2028 inherits these.

    RUN
    @Commit = 0 (default) previews inside a transaction and rolls back.
    @Commit = 1 writes.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @CustomerId uniqueidentifier = '76586DE3-ACB3-42EE-91EB-48597CA06802';
DECLARE @Year       varchar(4)       = '2027';
DECLARE @Commit     bit              = 0;

DECLARE @SectionText varchar(50) = 'Docs';
DECLARE @SectionIcon varchar(50) = 'folder2-open';
DECLARE @SectionSort int         = 100;   -- after every platform root (Director max 10, SuperDirector max 11, Superuser max 12)
DECLARE @LinkIcon    varchar(50) = 'file-earmark-pdf';
DECLARE @Now         datetime2(7) = SYSDATETIME();   -- matches nav.*.Modified

-- ── 1. Source: the customer's canonical Docs list x every job x every role ──────────
-- The document list comes from the Director Docs links across ALL of this customer's
-- @Year legacy menus (one row per URL, legacy order). Every @Year job then gets that list
-- for every role in @Roles — including a job whose own legacy menu never had Docs.
IF OBJECT_ID('tempdb..#docs') IS NOT NULL DROP TABLE #docs;
IF OBJECT_ID('tempdb..#src')  IS NOT NULL DROP TABLE #src;

DECLARE @Roles TABLE (Name nvarchar(256) NOT NULL PRIMARY KEY);
INSERT INTO @Roles VALUES ('Director'), ('SuperDirector'), ('Superuser');

WITH legacy AS (
    SELECT  LTRIM(RTRIM(mi.Text))        AS Text,
            LTRIM(RTRIM(mi.NavigateUrl)) AS Url,
            mi.[index]                   AS LegacyIndex
    FROM    Jobs.JobMenu_Items mi
    JOIN    Jobs.JobMenus      m ON m.menuID = mi.menuID
    JOIN    Jobs.Jobs          j ON j.JobId  = m.jobID
    JOIN    dbo.AspNetRoles    r ON r.Id     = m.RoleID
    JOIN    Jobs.JobMenu_Items p ON p.menuItemID = mi.parentMenuItemID
    WHERE   j.customerID = @CustomerId
      AND   j.year       = @Year
      AND   r.Name       = 'Director'
      AND   m.active  = 1
      AND   mi.active = 1
      AND   p.Text    = @SectionText
      AND   mi.NavigateUrl LIKE '%statics.teamsportsinfo.com/docs/americanselect/%'
      AND   mi.NavigateUrl NOT LIKE '%covidathleteadmittanceticket%'
)
SELECT  LOWER(Url)        AS UrlKey,
        MIN(Url)          AS Url,
        MIN(Text)         AS Text,
        COUNT(DISTINCT Text) AS TextVariants,
        MIN(LegacyIndex)  AS LegacyIndex
INTO    #docs
FROM    legacy
GROUP BY LOWER(Url);

IF NOT EXISTS (SELECT 1 FROM #docs) OR EXISTS (SELECT 1 FROM #docs WHERE TextVariants > 1)
BEGIN
    SELECT * FROM #docs ORDER BY LegacyIndex;
    RAISERROR('ABORT: legacy Docs list is empty or a document has more than one label. Nothing written.', 16, 1);
    RETURN;
END;

SELECT  j.JobId, j.JobPath, r.Id AS RoleId, r.Name AS RoleName, d.Text, d.Url,
        ROW_NUMBER() OVER (PARTITION BY j.JobId, r.Id ORDER BY d.LegacyIndex, d.Text) AS SortOrder
INTO    #src
FROM    Jobs.Jobs j
CROSS JOIN #docs d
JOIN    dbo.AspNetRoles r ON r.Name IN (SELECT Name FROM @Roles)
WHERE   j.customerID = @CustomerId
  AND   j.year       = @Year;

-- ── 2. Job/role pairs, minus any that already have a Docs section ───────────────────
IF OBJECT_ID('tempdb..#pairs') IS NOT NULL DROP TABLE #pairs;

SELECT  DISTINCT s.JobId, s.JobPath, s.RoleId, s.RoleName,
        CAST(NULL AS int) AS NavId,
        CAST(NULL AS int) AS RootNavItemId
INTO    #pairs
FROM    #src s;

SELECT  p.JobPath, p.RoleName, 'SKIPPED - Docs section already present' AS Note
FROM    #pairs p
JOIN    nav.Nav n      ON n.JobId = p.JobId AND n.RoleId = p.RoleId
JOIN    nav.NavItem ni ON ni.NavId = n.NavId
WHERE   ni.ParentNavItemId IS NULL AND ni.DefaultNavItemId IS NULL AND ni.DefaultParentNavItemId IS NULL
  AND   ni.Active = 1 AND ni.Text = @SectionText;

DELETE  p
FROM    #pairs p
WHERE   EXISTS (SELECT 1
                FROM nav.Nav n
                JOIN nav.NavItem ni ON ni.NavId = n.NavId
                WHERE n.JobId = p.JobId AND n.RoleId = p.RoleId
                  AND ni.ParentNavItemId IS NULL AND ni.DefaultNavItemId IS NULL AND ni.DefaultParentNavItemId IS NULL
                  AND ni.Active = 1 AND ni.Text = @SectionText);

BEGIN TRANSACTION;

-- ── 3. Override nav per job/role (reuse if one exists) ──────────────────────────────
INSERT INTO nav.Nav (RoleId, JobId, Active, Modified, ModifiedBy)
SELECT  p.RoleId, p.JobId, 1, @Now, NULL
FROM    #pairs p
WHERE   NOT EXISTS (SELECT 1 FROM nav.Nav n WHERE n.RoleId = p.RoleId AND n.JobId = p.JobId);

UPDATE  p SET NavId = n.NavId
FROM    #pairs p
JOIN    nav.Nav n ON n.RoleId = p.RoleId AND n.JobId = p.JobId;

-- ── 4. "Docs" root section ──────────────────────────────────────────────────────────
-- Capture the new ids with OUTPUT (one root per NavId). Do NOT match back on Modified:
-- a datetime variable compared to the datetime2(7) column is not equal for ~half of all
-- timestamps, which aborted the first run.
DECLARE @roots TABLE (NavId int NOT NULL PRIMARY KEY, NavItemId int NOT NULL);

INSERT INTO nav.NavItem (NavId, ParentNavItemId, DefaultNavItemId, DefaultParentNavItemId,
                         Active, SortOrder, Text, IconName, RouterLink, NavigateUrl, Target, Modified, ModifiedBy)
OUTPUT  inserted.NavId, inserted.NavItemId INTO @roots (NavId, NavItemId)
SELECT  p.NavId, NULL, NULL, NULL,
        1, @SectionSort, @SectionText, @SectionIcon, NULL, NULL, NULL, @Now, NULL
FROM    #pairs p;

UPDATE  p SET RootNavItemId = r.NavItemId
FROM    #pairs p
JOIN    @roots r ON r.NavId = p.NavId;

IF EXISTS (SELECT 1 FROM #pairs WHERE NavId IS NULL OR RootNavItemId IS NULL)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('ABORT: a job/role did not resolve to exactly one nav + Docs root. Nothing written.', 16, 1);
    RETURN;
END;

-- ── 5. One External URL link per document ───────────────────────────────────────────
INSERT INTO nav.NavItem (NavId, ParentNavItemId, DefaultNavItemId, DefaultParentNavItemId,
                         Active, SortOrder, Text, IconName, RouterLink, NavigateUrl, Target, Modified, ModifiedBy)
SELECT  p.NavId, p.RootNavItemId, NULL, NULL,
        1, s.SortOrder, s.Text, @LinkIcon, NULL, s.Url, '_blank', @Now, NULL
FROM    #pairs p
JOIN    #src   s ON s.JobId = p.JobId AND s.RoleId = p.RoleId;

-- ── 6. Preview / verify ─────────────────────────────────────────────────────────────
SELECT  COUNT(DISTINCT p.NavId)         AS navs,
        COUNT(DISTINCT p.RootNavItemId) AS docs_roots,
        (SELECT COUNT(*) FROM nav.NavItem c JOIN #pairs q ON q.RootNavItemId = c.ParentNavItemId) AS links,
        COUNT(DISTINCT p.JobId)         AS jobs
FROM    #pairs p;

SELECT  p.JobPath, p.RoleName, root.Text AS Section, c.SortOrder, c.Text, c.NavigateUrl, c.Target
FROM    #pairs p
JOIN    nav.NavItem root ON root.NavItemId = p.RootNavItemId
JOIN    nav.NavItem c    ON c.ParentNavItemId = root.NavItemId
ORDER BY p.JobPath, p.RoleName, c.SortOrder;

IF @Commit = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT 'COMMITTED.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'PREVIEW ONLY - rolled back. Set @Commit = 1 to write.';
END;
