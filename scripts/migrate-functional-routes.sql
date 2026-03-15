-- ============================================================
-- Functional Domain Routing — Nav Item RouterLink Migration
-- Moves all admin/ routes to functional domain prefixes.
-- Idempotent: safe to run multiple times.
-- ============================================================

UPDATE nav.NavItem SET RouterLink = 'configure/job'            WHERE RouterLink = 'admin/job-config';
UPDATE nav.NavItem SET RouterLink = 'configure/administrators'  WHERE RouterLink = 'admin/administrators';
UPDATE nav.NavItem SET RouterLink = 'configure/discount-codes'  WHERE RouterLink = 'admin/discount-codes';
UPDATE nav.NavItem SET RouterLink = 'configure/customer-groups' WHERE RouterLink = 'admin/customer-groups';
UPDATE nav.NavItem SET RouterLink = 'configure/nav-editor'     WHERE RouterLink = 'admin/nav-editor';
UPDATE nav.NavItem SET RouterLink = 'configure/widget-editor'  WHERE RouterLink = 'admin/widget-editor';
UPDATE nav.NavItem SET RouterLink = 'configure/job-clone'      WHERE RouterLink = 'admin/job-clone';
UPDATE nav.NavItem SET RouterLink = 'configure/theme'          WHERE RouterLink = 'admin/theme';
UPDATE nav.NavItem SET RouterLink = 'configure/customers'      WHERE RouterLink = 'admin/customer-configure';
UPDATE nav.NavItem SET RouterLink = 'search/players'           WHERE RouterLink = 'admin/search-players';
UPDATE nav.NavItem SET RouterLink = 'search/teams'             WHERE RouterLink = 'admin/search-teams';
UPDATE nav.NavItem SET RouterLink = 'communications/email-log' WHERE RouterLink = 'admin/email-log';
UPDATE nav.NavItem SET RouterLink = 'ladt/editor'              WHERE RouterLink = 'admin/ladt';
UPDATE nav.NavItem SET RouterLink = 'ladt/roster-swapper'      WHERE RouterLink = 'admin/roster-swapper';
UPDATE nav.NavItem SET RouterLink = 'ladt/pool-assignment'     WHERE RouterLink = 'admin/pool-assignment';
UPDATE nav.NavItem SET RouterLink = 'arb/health'               WHERE RouterLink = 'admin/arb-health';
UPDATE nav.NavItem SET RouterLink = 'tools/uslax-test'         WHERE RouterLink = 'admin/uslax-test';
UPDATE nav.NavItem SET RouterLink = 'tools/uslax-rankings'     WHERE RouterLink = 'admin/uslax-rankings';
UPDATE nav.NavItem SET RouterLink = 'tools/profile-migration'  WHERE RouterLink = 'admin/profile-migration';
UPDATE nav.NavItem SET RouterLink = 'tools/profile-editor'     WHERE RouterLink = 'admin/profile-editor';
UPDATE nav.NavItem SET RouterLink = 'scheduling/mobile-scorers' WHERE RouterLink = 'admin/mobile-scorers';
UPDATE nav.NavItem SET RouterLink = 'store/admin'              WHERE RouterLink = 'admin/store';
UPDATE nav.NavItem SET RouterLink = 'tools/log-viewer'         WHERE RouterLink = 'admin/log-viewer';

-- Verification
SELECT RouterLink, COUNT(*) AS Cnt
FROM nav.NavItem
WHERE RouterLink IS NOT NULL
GROUP BY RouterLink
ORDER BY RouterLink;
