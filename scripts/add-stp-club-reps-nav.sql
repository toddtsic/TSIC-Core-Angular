/*
    Stay-to-Play Club Reps nav — targeted, idempotent, additive.

    Why this exists and not "just re-run 5) Re-Set Nav System.sql": that script REBUILDS
    the whole nav tree from its manifest. It preserves job-level overrides, reporting items
    and hand-authored L2 rules, but it is still a teardown-and-rebuild of every role menu
    on a shared prod/staging database. Adding three rows does not justify that blast radius.
    5x carries the same rows, so a future full re-seed reproduces this state exactly.

    Three independent parts. Read all three before running.

    (1) VENDOR MENU.  The StpAdmin role gets its one-item menu (stp/club-reps). This is the
        only screen an STPAdmin login is ever served.

    (2) ADMIN MENU.  Director / Superuser get the SAME screen under Teams & Rosters, so
        the admin who authorised the sharing can see exactly what the vendor sees. Gated
        {"requiresFlags":["stayToPlayEnabled"]} — the leaf is hidden while Jobs.BEnableSTP
        is off, because nothing is being shared and there is nothing to review. The switch
        itself is on Configure > Job Settings > Teams/ClubReps.

        NOT SuperDirector (Todd 2026-08-23): sharing club-rep data with a vendor is the
        event's own director's call, so the review screen stays with them. Same split as
        Push Notification. The API policy CanViewStpClubReps matches, so hiding the row is
        not the only barrier.

    (3) FLAG RENAME.  VisibilityRulesEvaluator used to derive the flag name "mobileEnabled"
        from Jobs.BEnableSTP. That was simply wrong — BEnableSTP is Stay-To-Play and has
        never had anything to do with mobile. The mislabel hid the Push Notification screen
        on 197 of 204 jobs with registered devices (scripts/ungate-push-notification-nav.sql).
        The code now emits "stayToPlayEnabled". Any row still holding the OLD name would
        silently stop matching, so part 3 renames it in place.

        The seed manifest uses zero mobileEnabled rules, so part 3 is expected to touch
        NOTHING. It exists because step 14 of the re-seed RESTORES hand-authored rules off
        live rows — a rule typed into the nav editor in prod survives a re-seed and would
        not be visible in source. RUN THE BEFORE SELECT AND LOOK.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Director      NVARCHAR(450) = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06';

DECLARE @SuperUser     NVARCHAR(450) = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';
DECLARE @StpAdmin      NVARCHAR(450) = 'CE2CB370-5880-4624-A43E-048379C64331';
DECLARE @Rule          NVARCHAR(MAX) = N'{"requiresFlags":["stayToPlayEnabled"]}';

-- ---------------------------------------------------------------------------
-- BEFORE
-- ---------------------------------------------------------------------------
PRINT '--- existing stp/club-reps rows (expect 0 on a DB that has not had this applied) ---';
SELECT ni.NavItemId, n.RoleId, ni.Active, ni.[Text], ni.RouterLink, ni.VisibilityRules
FROM   nav.NavItem ni
JOIN   nav.Nav n ON n.NavId = ni.NavId
WHERE  ni.RouterLink = N'stp/club-reps';

PRINT '--- rows still using the old mobileEnabled flag (expect 0; investigate if not) ---';
SELECT ni.NavItemId, n.RoleId, ni.[Text], ni.RouterLink, ni.VisibilityRules
FROM   nav.NavItem ni
JOIN   nav.Nav n ON n.NavId = ni.NavId
WHERE  ni.VisibilityRules LIKE '%mobileEnabled%';

BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- (1) Vendor menu — StpAdmin
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM nav.Nav WHERE RoleId = @StpAdmin AND JobId IS NULL)
BEGIN
    INSERT INTO nav.Nav (RoleId, JobId, Active, Modified) VALUES (@StpAdmin, NULL, 1, GETDATE());
    PRINT 'Created nav.Nav container for StpAdmin';
END

DECLARE @navId INT = (SELECT NavId FROM nav.Nav WHERE RoleId = @StpAdmin AND JobId IS NULL);

IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND RouterLink = N'stp/club-reps')
BEGIN
    INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified)
    VALUES (@navId, NULL, 1, 1, N'Stay-to-Play Club Reps', N'buildings', N'stp/club-reps', GETDATE());
    PRINT 'StpAdmin: added Stay-to-Play Club Reps';
END
ELSE PRINT 'StpAdmin: Stay-to-Play Club Reps already present - skipped';

-- ---------------------------------------------------------------------------
-- (2) Admin menu — Director / Superuser, under Teams & Rosters. NOT SuperDirector.
--     Parent is matched by TEXT because the section parent carries no RouterLink.
-- ---------------------------------------------------------------------------
DECLARE @roleId NVARCHAR(450), @parentId INT, @sort INT;
DECLARE roles CURSOR LOCAL FAST_FORWARD FOR
    SELECT @Director UNION ALL SELECT @SuperUser;

OPEN roles;
FETCH NEXT FROM roles INTO @roleId;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @navId = (SELECT NavId FROM nav.Nav WHERE RoleId = @roleId AND JobId IS NULL);

    SET @parentId = (SELECT TOP 1 ni.NavItemId
                     FROM   nav.NavItem ni
                     WHERE  ni.NavId = @navId
                       AND  ni.ParentNavItemId IS NULL
                       AND  ni.[Text] = N'Teams & Rosters');

    IF @navId IS NULL OR @parentId IS NULL
        PRINT CONCAT('SKIPPED role ', @roleId, ' - no nav or no "Teams & Rosters" section');
    ELSE IF EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @navId AND RouterLink = N'stp/club-reps')
        PRINT CONCAT('Role ', @roleId, ': Stay-to-Play Club Reps already present - skipped');
    ELSE
    BEGIN
        -- Append after the section's current last child rather than assuming sort 8,
        -- so a hand-reordered menu is not disturbed.
        SET @sort = ISNULL((SELECT MAX(SortOrder) FROM nav.NavItem
                            WHERE NavId = @navId AND ParentNavItemId = @parentId), 0) + 1;

        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, VisibilityRules, Modified)
        VALUES (@navId, @parentId, 1, @sort, N'Stay-to-Play Club Reps', N'buildings', N'stp/club-reps', @Rule, GETDATE());
        PRINT CONCAT('Role ', @roleId, ': added Stay-to-Play Club Reps at sort ', @sort);
    END

    FETCH NEXT FROM roles INTO @roleId;
END
CLOSE roles;
DEALLOCATE roles;

-- ---------------------------------------------------------------------------
-- (3) Flag rename. Expected to affect 0 rows — see header.
-- ---------------------------------------------------------------------------
UPDATE nav.NavItem
SET    VisibilityRules = REPLACE(VisibilityRules, 'mobileEnabled', 'stayToPlayEnabled'),
       Modified = GETDATE()
WHERE  VisibilityRules LIKE '%mobileEnabled%';
PRINT CONCAT('Renamed mobileEnabled -> stayToPlayEnabled on ', @@ROWCOUNT, ' row(s)');

COMMIT TRANSACTION;

-- ---------------------------------------------------------------------------
-- AFTER: expect 3 rows — StpAdmin (no rule) + Director and Superuser
--        (stayToPlayEnabled). No SuperDirector row.
-- ---------------------------------------------------------------------------
SELECT ni.NavItemId, n.RoleId, ni.Active, ni.SortOrder, ni.[Text], ni.RouterLink, ni.VisibilityRules
FROM   nav.NavItem ni
JOIN   nav.Nav n ON n.NavId = ni.NavId
WHERE  ni.RouterLink = N'stp/club-reps'
ORDER  BY n.RoleId;
