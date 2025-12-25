import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { LayoutComponent } from './layouts/client-layout/layout.component';
import { TsicLayoutComponent } from './layouts/tsic-layout/tsic-layout.component';

export const routes: Routes = [
	// Default route redirects to TSIC landing page
	{ path: '', redirectTo: '/tsic', pathMatch: 'full' },

	// TSIC-specific routes (non-job-specific activities)
	{
		path: 'tsic',
		component: TsicLayoutComponent,
		canActivate: [authGuard],
		data: { allowAnonymous: true },
		children: [
			// Public landing page
			{
				path: '',
				loadComponent: () => import('./tsic-landing/tsic-landing.component').then(m => m.TsicLandingComponent),
				canActivate: [authGuard],
				data: { redirectAuthenticated: true }
			},
			// Login page
			{
				path: 'login',
				loadComponent: () => import('./login/login.component').then(m => m.LoginComponent),
				canActivate: [authGuard],
				data: { redirectAuthenticated: true }
			},
			// Terms of Service acceptance
			{
				path: 'terms-of-service',
				loadComponent: () => import('./terms-of-service/terms-of-service.component').then(m => m.TermsOfServiceComponent)
			},
			// Role selection page
			{
				path: 'role-selection',
				loadComponent: () => import('./role-selection/role-selection.component').then(m => m.RoleSelectionComponent)
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
						data: { requirePhase2: true }
					}
				]
			}
		]
	},

	// Job-specific routes - allows both authenticated and anonymous users (for registration)
	{
		path: ':jobPath',
		component: LayoutComponent,
		canActivate: [authGuard],
		data: { allowAnonymous: true },
		children: [
			{
				path: '',
				loadComponent: () => import('./job-landing/job-landing.component').then(m => m.JobLandingComponent)
			},
			// Registration entry screen: sign in then choose next action
			{
				path: 'registration',
				loadComponent: () => import('./registration/registration-entry.component').then(m => m.RegistrationEntryComponent)
			},
			// Registration wizard route (player-specific)
			{
				path: 'register-player',
				loadComponent: () => import('./registration-wizards/player-registration-wizard/player-registration-wizard.component').then(m => m.PlayerRegistrationWizardComponent)
			},
			// Registration wizard route (team-specific)
			{
				path: 'register-team',
				loadComponent: () => import('./registration-wizards/team-registration-wizard/team-registration-wizard.component').then(m => m.TeamRegistrationWizardComponent)
			},
			{
				path: 'home',
				loadComponent: () => import('./job-home/job-home.component').then(m => m.JobHomeComponent)
			},
			// Brand preview (design system showcase)
			{
				path: 'brand-preview',
				loadComponent: () => import('./job-home/brand-preview/brand-preview.component').then(m => m.BrandPreviewComponent)
			},
			// Admin-only routes for ANY job (SuperUser required)
			{
				path: 'admin',
				canActivate: [authGuard],
				data: { requireSuperUser: true },
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
