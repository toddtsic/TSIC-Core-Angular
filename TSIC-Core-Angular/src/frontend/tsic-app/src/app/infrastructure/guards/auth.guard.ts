import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { map, catchError } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';
import { LastLocationService } from '../services/last-location.service';
import { ToastService } from '@shared-ui/toast.service';

/**
 * Unified authentication guard that handles all authentication scenarios
 * 
 * Route data options:
 * - requirePhase2: true - Requires Phase 2 authentication (username + regId + jobPath)
 * - requireSuperUser: true - Requires SuperUser privileges (admin features)
 * - allowAnonymous: true - Allows unauthenticated access (for registration flows)
 * - redirectAuthenticated: true - Redirects authenticated users with selected job away (for landing/login pages)
 * - Default (no flags) - Requires Phase 1 authentication (username only)
 * 
 * For ALL authenticated users: validates jobPath in URL matches token's jobPath claim
 * Redirects to /tsic/login if authentication required but not present
 * Attempts to refresh token if expired
 */
export const authGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);
    const toastService = inject(ToastService);
    const last = inject(LastLocationService);

    const user = authService.getCurrentUser();
    const isAuth = authService.isAuthenticated();
    const requirePhase2 = route.data['requirePhase2'] === true;
    const allowAnonymous = route.data['allowAnonymous'] === true;
    const redirectAuthenticated = route.data['redirectAuthenticated'] === true;
    const requireSuperUser = route.data['requireSuperUser'] === true;

    // If redirectAuthenticated flag is set, handle redirection logic
    if (redirectAuthenticated) {
        if (isAuth) {
            // Authenticated user - check for returnUrl to honor
            const returnUrl = route.queryParamMap.get('returnUrl');
            if (returnUrl) {
                try {
                    const u = new URL(returnUrl, globalThis.location.origin);
                    if (u.origin === globalThis.location.origin) {
                        const internalPath = `${u.pathname}${u.search}${u.hash}`;
                        return router.parseUrl(internalPath);
                    }
                } catch {
                    // malformed returnUrl -> ignore and fall through
                }
            }

            const jobPath = user?.jobPath;

            // If has real job (not 'tsic'), redirect to that job
            if (jobPath && jobPath !== 'tsic') {
                return router.createUrlTree([`/${jobPath}`]);
            }

            // Authenticated but on 'tsic' or no job — go to role selection
            const jobPathForRoleSelect = route.paramMap.get('jobPath') || 'tsic';
            return router.createUrlTree([`/${jobPathForRoleSelect}/role-selection`]);
        } else {
            // Not authenticated - check for force flags or explicit intents
            const force = route.queryParamMap.get('force');
            const hasReturnUrl = !!route.queryParamMap.get('returnUrl');
            const hasIntent = !!route.queryParamMap.get('intent');

            // If explicitly forced to show page, allow
            if (force === '1' || force === 'true' || hasReturnUrl || hasIntent) {
                return true;
            }

            // Otherwise check if there's a last job path to redirect to
            const lastJob = last.getLastJobPath();
            if (lastJob) {
                return router.createUrlTree([`/${lastJob}`]);
            }
        }
        // If authenticated with 'tsic' jobPath or no last job, allow
    }

    // Not authenticated - handle based on allowAnonymous flag
    if (!user || !isAuth) {
        // If anonymous access allowed, permit entry without clearing local storage
        if (allowAnonymous) {
            return true;
        }

        // Grab refresh token and regId BEFORE clearing local state
        // (logoutLocal removes tokens from localStorage and clears currentUser signal)
        const refreshToken = authService.getRefreshToken();
        const regId = user?.regId;

        // Not anonymous-allowed route, clear local state
        authService.logoutLocal();
        if (refreshToken) {
            return authService.refreshAccessToken(refreshToken, regId).pipe(
                map(() => {
                    const refreshedUser = authService.getCurrentUser();
                    // After refresh, check if Phase 2 is required
                    if (requirePhase2 && (!refreshedUser?.regId || !refreshedUser?.jobPath)) {
                        const jobPathForRoleSelect = route.paramMap.get('jobPath') || 'tsic';
                        return router.createUrlTree([`/${jobPathForRoleSelect}/role-selection`]);
                    }
                    return true;
                }),
                catchError(() => {
                    const jobPathForLogin = route.paramMap.get('jobPath') || 'tsic';
                    return [router.createUrlTree([`/${jobPathForLogin}/login`], { queryParams: { returnUrl: state.url } })];
                })
            );
        }

        const jobPathForLogin = route.paramMap.get('jobPath') || 'tsic';
        return router.createUrlTree([`/${jobPathForLogin}/login`], { queryParams: { returnUrl: state.url } });
    }

    // Authenticated - validate jobPath if URL contains one
    // Check parent route for jobPath if not on current route (for child routes like role-selection)
    let urlJobPath = route.paramMap.get('jobPath') || route.parent?.paramMap.get('jobPath');

    // Fallback: extract jobPath from URL if param extraction failed (handles route parameter not yet resolved)
    if (!urlJobPath && state.url) {
        const match = state.url.match(/^\/([a-z0-9-]{3,40})(\/|$|\?)/);
        if (match) {
            urlJobPath = match[1];
        }
    }

    if (urlJobPath && user.jobPath && urlJobPath !== user.jobPath) {
        // Special case: if navigating to 'tsic' but logged into a real job, redirect to that job
        // This handles app startup where default route goes to /tsic
        if (urlJobPath === 'tsic' && user.jobPath !== 'tsic') {
            return router.createUrlTree([`/${user.jobPath}`]);
        }

        // Special case: allow job switching when navigating to role-selection
        if (state.url.includes('/role-selection')) {
            return true;
        }

        toastService.show(
            `You are logged into '${user.jobPath}' but attempted to access '${urlJobPath}'. Please logout first before moving to '${urlJobPath}'.`,
            'danger',
            7000
        );
        // Redirect to their current job path instead of blank screen
        return router.createUrlTree([`/${user.jobPath}`]);
    }

    // Authenticated - check if Phase 2 is required
    if (requirePhase2 && (!user.regId || !user.jobPath)) {
        const jobPathForRoleSelect = route.paramMap.get('jobPath') || user.jobPath || 'tsic';
        return router.createUrlTree([`/${jobPathForRoleSelect}/role-selection`]);
    }

    // Authenticated - check if SuperUser is required
    if (requireSuperUser) {
        if (!authService.isSuperuser()) {
            toastService.show('Access denied. SuperUser privileges required.', 'danger');
            const jobPathForRedirect = user.jobPath || route.paramMap.get('jobPath') || 'tsic';
            return router.createUrlTree([user.jobPath ? `/${user.jobPath}/home` : `/${jobPathForRedirect}/role-selection`]);
        }
        if (!user.jobPath) {
            const jobPathForRoleSelect = route.paramMap.get('jobPath') || 'tsic';
            return router.createUrlTree([`/${jobPathForRoleSelect}/role-selection`]);
        }
    }

    return true;
};
