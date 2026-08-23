/*
    Remove the visibility gate from the Push Notification nav item.

    It was gated on {"requiresFlags":["mobileEnabled"]}, and mobileEnabled derives
    solely from Jobs.BEnableSTP -- the Stay-To-Play flag, which has nothing to do
    with mobile. That hid the screen on 197 of the 204 jobs that actually have
    registered devices.

    The screen is now always reachable and reports its own unmet conditions.
    Matching change: VisibilityRulesEvaluator no longer needs to feed this item,
    and "5) Re-Set Nav System.sql" line 148 now seeds NULL.

    Verify first, then run the UPDATE.
*/

-- 1. Inspect what will change (expect 3 rows: Director, SuperDirector, SuperUser navs)
SELECT NavItemId, NavId, Active, [Text], RouterLink, VisibilityRules
FROM   nav.NavItem
WHERE  RouterLink = N'communications/push-notification';

-- 2. Apply
UPDATE nav.NavItem
SET    VisibilityRules = NULL,
       Modified        = GETDATE()
WHERE  RouterLink      = N'communications/push-notification'
  AND  VisibilityRules IS NOT NULL;

-- 3. Confirm (VisibilityRules should be NULL on all 3)
SELECT NavItemId, [Text], RouterLink, VisibilityRules
FROM   nav.NavItem
WHERE  RouterLink = N'communications/push-notification';
