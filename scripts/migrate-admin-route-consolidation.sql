-- ============================================================
-- Admin Route Consolidation — Nav Item RouterLink Migration
-- Run against any environment to update nav items to canonical admin/ routes.
-- Idempotent: safe to run multiple times (0 rows affected on re-run).
-- ============================================================

UPDATE nav.NavItem SET RouterLink = 'admin/search-players'  WHERE RouterLink = 'search/players';
UPDATE nav.NavItem SET RouterLink = 'admin/search-teams'    WHERE RouterLink = 'search/teams';
UPDATE nav.NavItem SET RouterLink = 'admin/administrators'  WHERE RouterLink = 'configure/administrators';
UPDATE nav.NavItem SET RouterLink = 'admin/discount-codes'  WHERE RouterLink = 'configure/discount-codes';
UPDATE nav.NavItem SET RouterLink = 'admin/customer-groups' WHERE RouterLink = 'configure/customer-groups';
UPDATE nav.NavItem SET RouterLink = 'admin/ladt'            WHERE RouterLink = 'ladt/admin';

-- Verification
SELECT RouterLink, COUNT(*) AS Cnt
FROM nav.NavItem
WHERE RouterLink IS NOT NULL
GROUP BY RouterLink
ORDER BY RouterLink;
