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
			// Family Account wizard (create/manage family + children)
			{
				path: 'family-account',
				loadComponent: () => import('./registration-wizards/family-account-wizard/family-account-wizard.component').then(m => m.FamilyAccountWizardComponent)
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
			},
			// Registration entry screen: sign in then choose next action
			{
				path: 'registration',
				loadComponent: () => import('./registration/registration-entry.component').then(m => m.RegistrationEntryComponent),
				canActivate: [anonymousJobGuard]
			},
			// Registration wizard route (player-specific)
			{
				path: 'register-player',
				loadComponent: () => import('./registration-wizards/player-registration-wizard/player-registration-wizard.component').then(m => m.PlayerRegistrationWizardComponent),
				canActivate: [anonymousJobGuard]
			},
			{
				path: 'home',
				loadComponent: () => import('./job-home/job-home.component').then(m => m.JobHomeComponent),
				canActivate: [anonymousJobGuard]
			},
			// Admin-only routes for ANY job (SuperUser required)
			{
				path: 'admin',
				canActivate: [superUserGuard],
				children: [
					{
						path: 'profile-migration',
						loadComponent: () => import('./admin/profile-migration/profile-migration.component').then(m => m.ProfileMigrationComponent)
					},
					{
						path: 'profile-editor',
						loadComponent: () => import('./admin/profile-editor/profile-editor.component').then(m => m.ProfileEditorComponent)
					},
					{
						path: 'theme',
						loadComponent: () => import('./admin/theme-editor/theme-editor.component').then(m => m.ThemeEditorComponent)
					}
				]
			}
		]
	}
];
