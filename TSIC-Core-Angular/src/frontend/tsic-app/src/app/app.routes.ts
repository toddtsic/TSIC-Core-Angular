import { Routes } from '@angular/router';
import { authGuard } from './infrastructure/guards/auth.guard';
import { LayoutComponent } from './layouts/client-layout/layout.component';

export const routes: Routes = [
	// Default route - redirect to last visited job or /tsic
	{ path: '', redirectTo: '/tsic', pathMatch: 'full' },

	// 404 route (must be before :jobPath to prevent matching as a jobPath)
	{
		path: 'not-found',
		loadComponent: () => import('./views/errors/not-found/not-found.component').then(m => m.NotFoundComponent)
	},

	// Job-specific routes (includes 'tsic' as special case) - allows both authenticated and anonymous users
	{
		path: ':jobPath',
		component: LayoutComponent,
		canActivate: [authGuard],
		data: { allowAnonymous: true },
		children: [
			{
				path: '',
				loadComponent: () => import('./views/home/landing-router/landing-router.component').then(m => m.LandingRouterComponent)
			},
			// Login page
			{
				path: 'login',
				loadComponent: () => import('./views/auth/login/login.component').then(m => m.LoginComponent),
				canActivate: [authGuard],
				data: { redirectAuthenticated: true }
			},
			// Terms of Service acceptance
			{
				path: 'terms-of-service',
				loadComponent: () => import('./views/auth/terms-of-service/terms-of-service.component').then(m => m.TermsOfServiceComponent)
			},
			// Role selection page
			{
				path: 'role-selection',
				loadComponent: () => import('./views/auth/role-selection/role-selection.component').then(m => m.RoleSelectionComponent)
			},
			// Family Account wizard (create/manage family + children)
			{
				path: 'family-account',
				loadComponent: () => import('./views/registration/wizards/family-account-wizard/family-account-wizard.component').then(m => m.FamilyAccountWizardComponent)
			},
			// Registration entry screen: sign in then choose next action
			{
				path: 'registration',
				loadComponent: () => import('./views/registration/registration-entry/registration-entry.component').then(m => m.RegistrationEntryComponent)
			},
			// Registration wizard route (player-specific)
			{
				path: 'register-player',
				loadComponent: () => import('./views/registration/wizards/player-registration-wizard/player-registration-wizard.component').then(m => m.PlayerRegistrationWizardComponent)
			},
			// Registration wizard route (team-specific)
			{
				path: 'register-team',
				loadComponent: () => import('./views/registration/wizards/team-registration-wizard/team-registration-wizard.component').then(m => m.TeamRegistrationWizardComponent)
			},
			{
				path: 'home',
				loadComponent: () => import('./views/home/job-home/job-home.component').then(m => m.JobHomeComponent)
			},
			// Brand preview (design system showcase)
			{
				path: 'brand-preview',
				loadComponent: () => import('./views/home/job-home/brand-preview/brand-preview.component').then(m => m.BrandPreviewComponent)
			},
			// Admin-only routes for ANY job (SuperUser required)
			{
				path: 'admin',
				canActivate: [authGuard],
				data: { requireSuperUser: true },
				children: [
					{
						path: 'profile-migration',
						loadComponent: () => import('./views/admin/profile-migration/profile-migration.component').then(m => m.ProfileMigrationComponent)
					},
					{
						path: 'profile-editor',
						loadComponent: () => import('./views/admin/profile-editor/profile-editor.component').then(m => m.ProfileEditorComponent)
					},
					{
						path: 'theme',
						loadComponent: () => import('./views/admin/theme-editor/theme-editor.component').then(m => m.ThemeEditorComponent)
					}
				]
			},
			// Report launcher — handles all menu items with Controller=Reporting
			{
				path: 'reporting/:action',
				loadComponent: () => import('./views/reporting/report-launcher/report-launcher.component').then(m => m.ReportLauncherComponent)
			},
			// Legacy-compatible admin routes (match menu system controller/action URLs)
			{
				path: 'menu/admin',
				canActivate: [authGuard],
				data: { requireSuperUser: true },
				loadComponent: () => import('./views/menu-admin/menu-admin.component').then(m => m.MenuAdminComponent)
			},
			{
				path: 'jobadministrator/admin',
				canActivate: [authGuard],
				data: { requireSuperUser: true },
				loadComponent: () => import('./views/admin/administrator-management/administrator-management.component').then(m => m.AdministratorManagementComponent)
			},
			{
				path: 'jobdiscountcodes/admin',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/discount-codes/discount-codes.component').then(m => m.DiscountCodesComponent)
			},
			{
				path: 'ladt/admin',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/ladt-editor/ladt-editor.component').then(m => m.LadtEditorComponent)
			},
			// Roster Swapper
			{
				path: 'admin/roster-swapper',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/roster-swapper/roster-swapper.component').then(m => m.RosterSwapperComponent)
			},
			// Legacy-compatible route for Roster Swapper
			{
				path: 'rosters/swapper',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/roster-swapper/roster-swapper.component').then(m => m.RosterSwapperComponent)
			},
			// Pool Assignment
			{
				path: 'admin/pool-assignment',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/pool-assignment/pool-assignment.component').then(m => m.PoolAssignmentComponent)
			},
			// Legacy-compatible route for Pool Assignment
			{
				path: 'teampoolassignment/index',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/pool-assignment/pool-assignment.component').then(m => m.PoolAssignmentComponent)
			},
			// Registration Search
			{
				path: 'admin/search',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/registration-search/registration-search.component').then(m => m.RegistrationSearchComponent)
			},
			// Legacy-compatible route for Registration Search
			{
				path: 'search/index',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/registration-search/registration-search.component').then(m => m.RegistrationSearchComponent)
			},
			// Team Search
			{
				path: 'admin/team-search',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/team-search/team-search.component').then(m => m.TeamSearchComponent)
			},
			// Legacy-compatible route for Team Search
			{
				path: 'searchteams/index',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/team-search/team-search.component').then(m => m.TeamSearchComponent)
			},
			// Scheduling — Post-scheduling tools (standalone, no shell wrapper)
			{
				path: 'admin/scheduling/view-schedule',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/view-schedule/view-schedule.component').then(m => m.ViewScheduleComponent)
			},
			{
				path: 'admin/scheduling/rescheduler',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/rescheduler/rescheduler.component').then(m => m.ReschedulerComponent)
			},
			// Scheduling — Pipeline shell (dashboard + steps 1–4)
			{
				path: 'admin/scheduling',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/dashboard/scheduling-shell.component').then(m => m.SchedulingShellComponent),
				children: [
					{
						path: '',
						loadComponent: () => import('./views/admin/scheduling/dashboard/scheduling-dashboard.component').then(m => m.SchedulingDashboardComponent)
					},
					{
						path: 'fields',
						loadComponent: () => import('./views/admin/scheduling/fields/manage-fields.component').then(m => m.ManageFieldsComponent)
					},
					{
						path: 'pairings',
						loadComponent: () => import('./views/admin/scheduling/pairings/manage-pairings.component').then(m => m.ManagePairingsComponent)
					},
					{
						path: 'timeslots',
						loadComponent: () => import('./views/admin/scheduling/timeslots/manage-timeslots.component').then(m => m.ManageTimeslotsComponent)
					},
					{
						path: 'schedule-division',
						loadComponent: () => import('./views/admin/scheduling/schedule-division/schedule-division.component').then(m => m.ScheduleDivisionComponent)
					}
				]
			},
			// Legacy-compatible routes (standalone, no shell)
			{
				path: 'fields/index',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/fields/manage-fields.component').then(m => m.ManageFieldsComponent)
			},
			{
				path: 'pairings/index',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/pairings/manage-pairings.component').then(m => m.ManagePairingsComponent)
			},
			{
				path: 'timeslots/index',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/timeslots/manage-timeslots.component').then(m => m.ManageTimeslotsComponent)
			},
			{
				path: 'scheduling/scheduledivision',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/schedule-division/schedule-division.component').then(m => m.ScheduleDivisionComponent)
			},
			{
				path: 'scheduling/schedules',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/view-schedule/view-schedule.component').then(m => m.ViewScheduleComponent)
			},
			{
				path: 'scheduling/rescheduler',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/rescheduler/rescheduler.component').then(m => m.ReschedulerComponent)
			},
			// Public schedule view (anonymous access)
			{
				path: 'schedule',
				data: { publicMode: true },
				loadComponent: () => import('./views/admin/scheduling/view-schedule/view-schedule.component').then(m => m.ViewScheduleComponent)
			}
		]
	},

	// Wildcard route - must be last
	{
		path: '**',
		loadComponent: () => import('./views/errors/not-found/not-found.component').then(m => m.NotFoundComponent)
	}
];
