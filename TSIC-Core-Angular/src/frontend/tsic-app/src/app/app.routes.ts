import { Routes } from '@angular/router';
import { authGuard, roleGuard, landingPageGuard, redirectAuthenticatedGuard, tsicEntryGuard, anonymousJobGuard, superUserGuard } from './core/guards/auth.guard';
import { LayoutComponent } from './layout/layout.component';
import { PublicLayoutComponent } from './layouts/public-layout/public-layout.component';

export const routes: Routes = [
	// Default route redirects to TSIC landing page
	{ path: '', redirectTo: '/tsic', pathMatch: 'full' },

	// TSIC-specific routes (non-job-specific activities)
	{
		path: 'tsic',
		component: PublicLayoutComponent,
		canActivate: [tsicEntryGuard],
		children: [
			// Public landing page
			{
				path: '',
				loadComponent: () => import('./tsic-landing/tsic-landing.component').then(m => m.TsicLandingComponent),
				canActivate: [landingPageGuard]
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
			},
			// TSIC job home when user is registered for TSIC (wrapped in job-specific layout)
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
			// Admin-only routes (SuperUser + jobPath=tsic required)
			{
				path: 'admin',
				component: LayoutComponent,
				canActivate: [superUserGuard],
				children: [
					{
						path: 'profile-migration',
						loadComponent: () => import('./admin/profile-migration/profile-migration.component').then(m => m.ProfileMigrationComponent)
					},
					{
						path: 'profile-editor',
						loadComponent: () => import('./admin/profile-editor/profile-editor.component').then(m => m.ProfileEditorComponent)
					}
				]
			}
		]
	},

	// Job-specific routes - allows both authenticated and anonymous users (for registration)
	{
		path: ':jobPath',
		component: LayoutComponent,
		children: [
			{
				path: '',
				loadComponent: () => import('./job-home/job-home.component').then(m => m.JobHomeComponent),
				canActivate: [anonymousJobGuard]
			}
		]
	}
];
