import { Routes } from '@angular/router';
import { authGuard, roleGuard, landingPageGuard, redirectAuthenticatedGuard } from './core/guards/auth.guard';

export const routes: Routes = [
    // Default route redirects to landing page
    { path: '', redirectTo: '/tsic', pathMatch: 'full' },

    // Public landing page - redirects authenticated users to appropriate location
    {
        path: 'tsic',
        loadComponent: () => import('./tsic-landing/tsic-landing.component').then(m => m.TsicLandingComponent),
        canActivate: [landingPageGuard]
    },

    // Login page - redirects authenticated users to appropriate location
    {
        path: 'login',
        loadComponent: () => import('./login/login.component').then(m => m.LoginComponent),
        canActivate: [redirectAuthenticatedGuard]
    },

    // Role selection - requires Phase 1 authentication (username token)
    {
        path: 'role-selection',
        loadComponent: () => import('./role-selection/role-selection.component').then(m => m.RoleSelectionComponent),
        canActivate: [authGuard]
    },

    // Job-specific home pages - requires Phase 2 authentication (regId + jobPath in token)
    {
        path: ':jobPath',
        loadComponent: () => import('./job-home/job-home.component').then(m => m.JobHomeComponent),
        canActivate: [roleGuard]
    }
];
