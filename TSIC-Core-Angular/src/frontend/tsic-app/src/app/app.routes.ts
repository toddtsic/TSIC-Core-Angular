import { Routes } from '@angular/router';
import { authGuard, roleGuard, landingPageGuard, redirectAuthenticatedGuard, tsicEntryGuard } from './core/guards/auth.guard';
import { LayoutComponent } from './layout/layout.component';

export const routes: Routes = [
	// Default route redirects to TSIC landing page
	{ path: '', redirectTo: '/tsic', pathMatch: 'full' },

	// TSIC-specific routes (non-job-specific activities)
	{
		path: 'tsic',
		canActivate: [tsicEntryGuard],
		children: [
			// Public landing page
			{
				path: '',
				loadComponent: () => import('./tsic-landing/tsic-landing.component').then(m => m.TsicLandingComponent),
				canActivate: [landingPageGuard]
			},
			// TSIC job home when user is registered for TSIC (wrapped in layout)
			{
				path: 'home',
				component: LayoutComponent,
				children: [
					{
						path: '',
						loadComponent: () => import('./job-home/job-home.component').then(m => m.JobHomeComponent),
						canActivate: [roleGuard]
					}
				]
			},
			// Login page
			{
				path: 'login',
				loadComponent: () => import('./login/login.component').then(m => m.LoginComponent),
				canActivate: [redirectAuthenticatedGuard]
			},
			// Role selection page
			{
				path: 'role-selection',
				loadComponent: () => import('./role-selection/role-selection.component').then(m => m.RoleSelectionComponent),
				canActivate: [authGuard]
			}
		]
	},

	// Job-specific routes - requires Phase 2 authentication (regId + jobPath in token)
	{
		path: ':jobPath',
		component: LayoutComponent,
		children: [
			{
				path: '',
				loadComponent: () => import('./job-home/job-home.component').then(m => m.JobHomeComponent),
				canActivate: [roleGuard]
			}
		]
	}
];
