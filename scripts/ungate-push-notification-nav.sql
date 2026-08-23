/*
    Push Notification nav item: SHOW it for Director + Superuser, REMOVE it for SuperDirector.

    Two independent changes below. Read both before running.

    (1) SHOW IT.  The item is currently invisible because its VisibilityRules column
        holds {"requiresFlags":["mobileEnabled"]}, and mobileEnabled derives solely from
        Jobs.BEnableSTP -- the Stay-To-Play flag, unrelated to mobile. That hid the screen
        on 197 of the 204 jobs that actually have registered devices.

        Setting VisibilityRules = NULL REMOVES THE GATE -- it does not disable the row.
        VisibilityRulesEvaluator.Passes() returns true immediately when the JSON is null
        or empty, so NULL == always visible. The on/off switch is nav.NavItem.Active,
        which stays 1 and is not touched here.

        The screen now reports its own unmet delivery conditions instead.

    (2) HIDE IT FROM SUPERDIRECTOR.  Push is an unrecallable blast to every registered
        device on the event, so it stays with the event's own director. NavId 113 is the
        SuperDirector nav. The API is tightened to match: PushNotificationController moved
        off "AdminOnly" (which includes SuperDirector) onto "CanSendPushNotifications"
        (Superuser + Director only), so hiding the row is not the only barrier.

    Matching source change: "5) Re-Set Nav System.sql" line 148 now seeds
    VisibilityRules = NULL and ForSuperDir = 0, so a re-seed reproduces this state.
*/

-- ---------------------------------------------------------------------------
-- BEFORE: expect 3 rows (NavId 112 Director, 113 SuperDirector, 114 Superuser),
--         all Active = 1, all with the mobileEnabled rule.
-- ---------------------------------------------------------------------------
SELECT ni.NavItemId, ni.NavId, r.Name AS ForRole, ni.Active, ni.[Text], ni.VisibilityRules
FROM   nav.NavItem ni
JOIN   nav.Nav n        ON n.NavId = ni.NavId
JOIN   dbo.AspNetRoles r ON r.Id = CAST(n.RoleId AS NVARCHAR(450))
WHERE  ni.RouterLink = N'communications/push-notification'
ORDER  BY ni.NavId;

-- ---------------------------------------------------------------------------
-- (1) Drop the gate. NULL rules = no conditions = ALWAYS VISIBLE.
-- ---------------------------------------------------------------------------
UPDATE nav.NavItem
SET    VisibilityRules = NULL,
       Modified        = GETDATE()
WHERE  RouterLink      = N'communications/push-notification'
  AND  VisibilityRules IS NOT NULL;

-- ---------------------------------------------------------------------------
-- (2) Remove the SuperDirector copy (NavId 113). Delete, matching how the seed
--     script fans rows per role -- a ForSuperDir = 0 row is never inserted at all.
-- ---------------------------------------------------------------------------
DELETE ni
FROM   nav.NavItem ni
JOIN   nav.Nav n        ON n.NavId = ni.NavId
JOIN   dbo.AspNetRoles r ON r.Id = CAST(n.RoleId AS NVARCHAR(450))
WHERE  ni.RouterLink = N'communications/push-notification'
  AND  r.Name        = N'SuperDirector';

-- ---------------------------------------------------------------------------
-- AFTER: expect 2 rows (Director, Superuser), Active = 1, VisibilityRules NULL.
-- ---------------------------------------------------------------------------
SELECT ni.NavItemId, ni.NavId, r.Name AS ForRole, ni.Active, ni.[Text], ni.VisibilityRules
FROM   nav.NavItem ni
JOIN   nav.Nav n        ON n.NavId = ni.NavId
JOIN   dbo.AspNetRoles r ON r.Id = CAST(n.RoleId AS NVARCHAR(450))
WHERE  ni.RouterLink = N'communications/push-notification'
ORDER  BY ni.NavId;
