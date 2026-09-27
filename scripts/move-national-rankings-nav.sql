/*
    National Rankings nav item: MOVE it from the "USA Lacrosse" section to "Teams & Rosters",
    and RELABEL it "USA Lacrosse Rankings" -> "National Rankings".

    Why: the rankings are scraped from US Club Lacrosse (usclublax.com), NOT USA Lacrosse.
    They exist to inform directors assigning teams to pools, so they belong beside Pool
    Assignment. The new label matches the page heading and Pool Assignment's own button.

    What changes, per nav (one per admin role; job-level override navs too, if any hold it):
      - ParentNavItemId -> that same nav's "Teams & Rosters" section
      - SortOrder       -> last in Teams & Rosters (MAX + 1 within that nav's section)
      - Text            -> 'National Rankings'
    Untouched: RouterLink, IconName, Active, VisibilityRules ({"sports":["Lacrosse"]} stays --
    still Lacrosse-only). The "USA Lacrosse" section keeps Test + Membership.

    Matches by section TEXT within the same NavId, never by hard-coded ids, so it runs the
    same on dev and prod. Idempotent: a second run finds no row under "USA Lacrosse".

    Matching source change: "5) Re-Set Nav System.ps1" (and its generated .sql) now seeds the
    item under Teams & Rosters as 'National Rankings', so a re-seed reproduces this state.
*/

SET NOCOUNT ON;

-- ---------------------------------------------------------------------------
-- BEFORE: on dev, 3 rows (NavId 112 Director, 113 SuperDirector, 114 Superuser),
--         all under "USA Lacrosse", Text 'USA Lacrosse Rankings'.
-- ---------------------------------------------------------------------------
SELECT ni.NavItemId, ni.NavId, n.JobId, r.Name AS ForRole, p.[Text] AS Section,
       ni.SortOrder, ni.[Text], ni.RouterLink, ni.VisibilityRules, ni.Active
FROM   nav.NavItem ni
JOIN   nav.Nav n         ON n.NavId = ni.NavId
JOIN   dbo.AspNetRoles r ON r.Id = CAST(n.RoleId AS NVARCHAR(450))
LEFT   JOIN nav.NavItem p ON p.NavItemId = ni.ParentNavItemId
WHERE  ni.RouterLink = N'tools/uslax-rankings'
ORDER  BY n.JobId, ni.NavId;

BEGIN TRANSACTION;

UPDATE ni
SET    ni.ParentNavItemId = tr.NavItemId,
       ni.SortOrder       = ISNULL((SELECT MAX(s.SortOrder)
                                    FROM   nav.NavItem s
                                    WHERE  s.ParentNavItemId = tr.NavItemId), 0) + 1,
       ni.[Text]          = N'National Rankings',
       ni.Modified        = GETDATE()
FROM   nav.NavItem ni
JOIN   nav.NavItem cur ON cur.NavItemId = ni.ParentNavItemId
                      AND cur.[Text]    = N'USA Lacrosse'
JOIN   nav.NavItem tr  ON tr.NavId      = ni.NavId
                      AND tr.ParentNavItemId IS NULL
                      AND tr.[Text]     = N'Teams & Rosters'
WHERE  ni.RouterLink = N'tools/uslax-rankings';

PRINT CONCAT('Moved ', @@ROWCOUNT, ' National Rankings row(s) into Teams & Rosters.');

-- A nav that holds the item under "USA Lacrosse" but has NO "Teams & Rosters" section is
-- left where it is (the join above skips it). Surface any such row rather than guess.
IF EXISTS (SELECT 1
           FROM   nav.NavItem ni
           JOIN   nav.NavItem cur ON cur.NavItemId = ni.ParentNavItemId AND cur.[Text] = N'USA Lacrosse'
           WHERE  ni.RouterLink = N'tools/uslax-rankings')
    PRINT 'WARNING: a rankings row is still under "USA Lacrosse" (its nav has no "Teams & Rosters" section). Review the AFTER result.';

COMMIT TRANSACTION;

-- ---------------------------------------------------------------------------
-- AFTER: expect the same rows, Section 'Teams & Rosters', Text 'National Rankings',
--        SortOrder last in that section, VisibilityRules unchanged.
-- ---------------------------------------------------------------------------
SELECT ni.NavItemId, ni.NavId, n.JobId, r.Name AS ForRole, p.[Text] AS Section,
       ni.SortOrder, ni.[Text], ni.RouterLink, ni.VisibilityRules, ni.Active
FROM   nav.NavItem ni
JOIN   nav.Nav n         ON n.NavId = ni.NavId
JOIN   dbo.AspNetRoles r ON r.Id = CAST(n.RoleId AS NVARCHAR(450))
LEFT   JOIN nav.NavItem p ON p.NavItemId = ni.ParentNavItemId
WHERE  ni.RouterLink = N'tools/uslax-rankings'
ORDER  BY n.JobId, ni.NavId;
