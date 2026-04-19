import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { map, catchError } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';
import { LastLocationService } from '../services/last-location.service';
import { ToastService } from '@shared-ui/toast.service';
import { Roles, type RoleName } from '../constants/roles.constants';

/**
 * Unified authentication guard.
 *
 * Route data:
 *   allowAnonymous         – skip auth entirely (public pages, registration wizards)
 *   redirectAuthenticated  – bounce logged-in users away (login page, /tsic landing)
 *   roles: RoleName[]      – literal list of roles permitted on this route.
 *                            Reading the route shows exactly who can reach it.
 *                            Omit for any-authenticated-user access.
 *
 * Cold start (browser refresh / direct URL):
 *   Phase 1 tokens (no regId) are stale incomplete sessions.
 *   → logoutLocal, redirect to /:jobPath (loads as anonymous).
 */
export const authGuard: CanActivateFn = (route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const toast = inject(ToastService);
    const last = inject(LastLocationService);

    const user = auth.getCurrentUser();
    const isAuth = auth.isAuthenticated();
    const isColdStart = !router.navigated;

    const flags = {
        allowAnonymous: route.data['allowAnonymous'] === true,
        redirectAuthenticated: route.data['redirectAuthenticated'] === true,
    };
    const allowedRoles = route.data['roles'] as RoleName[] | undefined;

    // ── Helpers ──────────────────────────────────────────────────────
    const jobPath = (): string =>
        route.paramMap.get('jobPath')
        || route.parent?.paramMap.get('jobPath')
        || extractJobPathFromUrl(state.url)
        || 'tsic';

    const toRoleSelection = () => router.createUrlTree([`/${jobPath()}/role-selection`]);
    const toLogin = () => router.createUrlTree([`/${jobPath()}/login`], { queryParams: { returnUrl: state.url } });
    const toJob = (jp: string) => router.createUrlTree([`/${jp}`]);

    // ── Cold start + Phase 1 = stale session ────────────────────────
    if (isColdStart && isAuth && user && !user.regId) {
        const jp = user.jobPath;
        auth.logoutLocal();
        return toJob(jp || 'tsic');
    }

    // ── Cold start + Phase 2 + non-privileged = stale session ────────
    // Only SuperUser and Director persist across refresh.
    // All other roles must re-authenticate.
    if (isColdStart && isAuth && user?.regId
        && user.role !== Roles.Superuser && user.role !== Roles.Director) {
        const jp = user.jobPath;
        auth.logoutLocal();
        return toJob(jp || 'tsic');
    }

    // ── Bounce authenticated users away from login/landing ──────────
    if (flags.redirectAuthenticated) {
        if (!isAuth) {
            const force = route.queryParamMap.get('force');
            if (force === '1' || force === 'true'
                || route.queryParamMap.has('returnUrl')
                || route.queryParamMap.has('intent')) {
                return true;
            }
            const lastJob = last.getLastJobPath();
            return lastJob ? toJob(lastJob) : true;
        }

        const returnUrl = route.queryParamMap.get('returnUrl');
        if (returnUrl) {
            try {
                const u = new URL(returnUrl, globalThis.location.origin);
                if (u.origin === globalThis.location.origin) {
                    return router.parseUrl(`${u.pathname}${u.search}${u.hash}`);
                }
            } catch { /* malformed → fall through */ }
        }

        return user?.jobPath && user.jobPath !== 'tsic'
            ? toJob(user.jobPath)
            : toRoleSelection();
    }

    // ── Not authenticated ────────────────────────────────────────────
    if (!user || !isAuth) {
        if (flags.allowAnonymous) return true;

        const refreshToken = auth.getRefreshToken();
        const regId = user?.regId;
        auth.logoutLocal();

        if (refreshToken) {
            return auth.refreshAccessToken(refreshToken, regId).pipe(
                map(() => true),
                catchError(() => [toLogin()])
            );
        }

        return toLogin();
    }

    // ── jobPath mismatch ─────────────────────────────────────────────
    const urlJob = jobPath();
    if (urlJob && user.jobPath && urlJob !== user.jobPath) {
        if (urlJob === 'tsic' && user.jobPath !== 'tsic') {
            return toJob(user.jobPath);
        }
        if (state.url.includes('/role-selection')) {
            return true; // allow cross-job role switching
        }
        toast.show(
            `You are logged into '${user.jobPath}' but attempted to access '${urlJob}'. Please logout first.`,
            'danger', 7000
        );
        return toJob(user.jobPath);
    }

    // ── Role-set gating ──────────────────────────────────────────────
    // Route declares which concrete roles may access it via data.roles.
    // No array → any authenticated user is fine.
    if (allowedRoles && allowedRoles.length > 0) {
        if (!user.jobPath) return toRoleSelection();
        const userRoles = user.roles ?? (user.role ? [user.role] : []);
        const ok = allowedRoles.some(r => userRoles.includes(r));
        if (!ok) {
            toast.show('Access denied.', 'danger');
            return router.createUrlTree([`/${user.jobPath}`, 'home']);
        }
    }

    return true;
};

function extractJobPathFromUrl(url: string): string | null {
    const match = url?.match(/^\/([a-z0-9-]{3,40})(\/|$|\?)/);
    return match ? match[1] : null;
}
