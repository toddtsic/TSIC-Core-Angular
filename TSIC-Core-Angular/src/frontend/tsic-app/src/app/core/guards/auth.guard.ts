﻿import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';

// Shared helper to centralize redirect decisions and avoid duplication between guards
function resolveAuthRedirect(
    context: 'landing' | 'login',
    router: Router,
    isAuthenticated: boolean,
    user: { jobPath?: string } | null
) {
    const jobPath = user?.jobPath;

    // If a concrete job is selected that is not the special 'tsic' path, go there
    if (jobPath && jobPath !== 'tsic') {
        return router.createUrlTree([`/${jobPath}`]);
    }

    if (context === 'landing') {
        // On landing:
        // - If authenticated with NO job selected -> role selection.
        // - If authenticated with TSIC job -> allow (parent guard may redirect to /tsic/home when appropriate).
        if (isAuthenticated && !jobPath) {
            return router.createUrlTree(['/tsic/role-selection']);
        }
        // jobPath === 'tsic' or unauthenticated -> allow landing to render
        return true;
    }

    // context === 'login'
    // On login: any authenticated user should not see login; send to role-selection as safe target
    if (isAuthenticated) {
        return router.createUrlTree(['/tsic/role-selection']);
    }
    return true;
}

/**
 * Guard for the 'tsic' route entry. If the authenticated user has selected the
 * TSIC job (jobPath === 'tsic'), redirect them to '/tsic/home' which renders
 * the job home under the TSIC namespace. Otherwise allow the landing/login
 * children to render.
 */
export const tsicEntryGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();
    if (user?.jobPath === 'tsic' && !!user.regId) {
        // Only redirect from the bare /tsic to avoid loops when already at /tsic/home (or other children)
        if (state.url === '/tsic' || state.url === '/tsic/') {
            return router.createUrlTree(['/tsic/home']);
        }
        // Already navigating under /tsic/... (e.g., /tsic/home, /tsic/role-selection) -> allow
        return true;
    }
    return true;
};

/**
 * Guard that checks if user has any valid JWT token (Phase 1 or Phase 2)
 * Redirects to /tsic/login if not authenticated
 */
export const authGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const isAuth = authService.isAuthenticated();
    if (isAuth) {
        return true;
    }

    return router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Guard that checks if user has Phase 2 authentication (regId + jobPath in token)
 * Redirects to /tsic/role-selection if only Phase 1 authenticated
 * Redirects to /tsic/login if not authenticated at all
 */
export const roleGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();

    if (user?.regId && user?.jobPath) {
        return true;
    }

    if (authService.isAuthenticated()) {
        return router.createUrlTree(['/tsic/role-selection']);
    }

    return router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Guard that redirects authenticated users away from the landing page
 */
export const landingPageGuard: CanActivateFn = () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();

    return resolveAuthRedirect('landing', router, authService.isAuthenticated(), user);
};

/**
 * Guard that prevents authenticated users from accessing login page
 */
export const redirectAuthenticatedGuard: CanActivateFn = () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();

    return resolveAuthRedirect('login', router, authService.isAuthenticated(), user);
};

/**
 * Guard for anonymous job access - allows both authenticated and unauthenticated users
 * Used for job pages that support registration flows for new users
 */
export const anonymousJobGuard: CanActivateFn = () => {
    // Always allow access - job-home component will handle logic for authenticated vs anonymous users
    return true;
};

/**
 * Guard that checks if user is a SuperUser with jobPath === 'tsic'
 * Redirects to /tsic/home if not authorized
 * Used for admin-only features like profile migration and metadata editing
 */
export const superUserGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();

    // Check if user is SuperUser and has selected the TSIC job
    if (authService.isSuperuser() && user?.jobPath === 'tsic') {
        return true;
    }

    // Not authorized - redirect to home
    return router.createUrlTree(['/tsic/home']);
};
