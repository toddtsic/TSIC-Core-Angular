import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { map, catchError } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';
import { LastLocationService } from '../services/last-location.service';
import { ToastService } from '../../shared/toast.service';

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

            // If authenticated but no job, go to role selection
            if (!jobPath) {
                return router.createUrlTree(['/tsic/role-selection']);
            }
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

        // Not anonymous-allowed route, clear local state
        authService.logoutLocal();

        // Try refresh or redirect to login
        const refreshToken = authService.getRefreshToken();
        if (refreshToken) {
            return authService.refreshAccessToken().pipe(
                map(() => {
                    const refreshedUser = authService.getCurrentUser();
                    // After refresh, check if Phase 2 is required
                    if (requirePhase2 && (!refreshedUser?.regId || !refreshedUser?.jobPath)) {
                        return router.createUrlTree(['/tsic/role-selection']);
                    }
                    return true;
                }),
                catchError(() => {
                    return [router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } })];
                })
            );
        }

        return router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } });
    }

    // Authenticated - validate jobPath if URL contains one
    const urlJobPath = route.paramMap.get('jobPath');
    if (urlJobPath && user.jobPath && urlJobPath !== user.jobPath) {
        toastService.show(
            `You are logged into '${user.jobPath}' but attempted to access '${urlJobPath}'. Please logout first before moving to '${urlJobPath}'.`,
            'danger',
            7000
        );
        return false;
    }

    // Authenticated - check if Phase 2 is required
    if (requirePhase2 && (!user.regId || !user.jobPath)) {
        return router.createUrlTree(['/tsic/role-selection']);
    }

    // Authenticated - check if SuperUser is required
    if (requireSuperUser) {
        if (!authService.isSuperuser()) {
            toastService.show('Access denied. SuperUser privileges required.', 'danger');
            return router.createUrlTree([user.jobPath ? `/${user.jobPath}/home` : '/tsic/role-selection']);
        }
        if (!user.jobPath) {
            return router.createUrlTree(['/tsic/role-selection']);
        }
    }

    return true;
};
