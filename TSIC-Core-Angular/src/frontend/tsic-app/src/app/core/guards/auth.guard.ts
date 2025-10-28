import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Guard that checks if user has any valid JWT token (Phase 1 or Phase 2)
 * Redirects to /login if not authenticated
 */
export const authGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (authService.isAuthenticated()) {
        return true;
    }

    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Guard that checks if user has Phase 2 authentication (regId + jobPath in token)
 * Redirects to /role-selection if only Phase 1 authenticated
 * Redirects to /login if not authenticated at all
 */
export const roleGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();

    if (user?.regId && user?.jobPath) {
        return true;
    }

    if (authService.isAuthenticated()) {
        return router.createUrlTree(['/role-selection']);
    }

    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Guard that redirects authenticated users away from the landing page
 */
export const landingPageGuard: CanActivateFn = () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();

    if (user?.jobPath) {
        return router.createUrlTree([`/${user.jobPath}`]);
    }

    if (authService.isAuthenticated()) {
        return router.createUrlTree(['/role-selection']);
    }

    return true;
};

/**
 * Guard that prevents authenticated users from accessing login page
 */
export const redirectAuthenticatedGuard: CanActivateFn = () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();

    if (user?.jobPath) {
        return router.createUrlTree([`/${user.jobPath}`]);
    }

    if (authService.isAuthenticated()) {
        return router.createUrlTree(['/role-selection']);
    }

    return true;
};
