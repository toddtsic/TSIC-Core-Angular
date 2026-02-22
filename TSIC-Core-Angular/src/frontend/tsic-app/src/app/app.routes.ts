import { Routes } from '@angular/router';
import { authGuard } from './infrastructure/guards/auth.guard';
import { unsavedChangesGuard } from './infrastructure/guards/unsaved-changes.guard';
import { LayoutComponent } from './layouts/client-layout/layout.component';

export const routes: Routes = [
	// Default route - redirect to last visited job or /tsic
	{ path: '', redirectTo: '/tsic', pathMatch: 'full' },

	// TSIC corporate landing — standalone marketing page (no layout chrome)
	// Guard redirects authenticated users to their job, anonymous users with a lastJobPath to that job
	{
		path: 'tsic',
		canActivate: [authGuard],
		data: { redirectAuthenticated: true },
		loadComponent: () => import('./views/home/tsic-landing/tsic-landing.component').then(m => m.TsicLandingComponent)
	},

	// V1 snapshot for A/B comparison
	{
		path: 'tsic-v1',
		loadComponent: () => import('./views/home/tsic-landing-v1/tsic-landing-v1.component').then(m => m.TsicLandingV1Component)
	},

	// V2 snapshot for A/B comparison
	{
		path: 'tsic-v2',
		loadComponent: () => import('./views/home/tsic-landing-v2/tsic-landing-v2.component').then(m => m.TsicLandingV2Component)
	},

	// V3 snapshot for A/B comparison
	{
		path: 'tsic-v3',
		loadComponent: () => import('./views/home/tsic-landing-v3/tsic-landing-v3.component').then(m => m.TsicLandingV3Component)
	},

	// 404 route (must be before :jobPath to prevent matching as a jobPath)
	{
		path: 'not-found',
		loadComponent: () => import('./views/errors/not-found/not-found.component').then(m => m.NotFoundComponent)
	},

	// Job-specific routes - allows both authenticated and anonymous users
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
			// Family Account wizard (v2)
			{
				path: 'family-account',
				loadComponent: () => import('./views/registration/wizards-v2/family/family-wizard.component').then(m => m.FamilyWizardV2Component)
			},
			// Registration entry screen: sign in then choose next action
			{
				path: 'registration',
				loadComponent: () => import('./views/registration/registration-entry/registration-entry.component').then(m => m.RegistrationEntryComponent)
			},
			// Player registration wizard (v2)
			{
				path: 'register-player',
				loadComponent: () => import('./views/registration/wizards-v2/player/player-wizard.component').then(m => m.PlayerWizardV2Component)
			},
			// Team registration wizard (v2)
			{
				path: 'register-team',
				loadComponent: () => import('./views/registration/wizards-v2/team/team-wizard.component').then(m => m.TeamWizardV2Component)
			},
			{
				path: 'home',
				loadComponent: () => import('./views/home/job-home/job-home.component').then(m => m.JobHomeComponent)
			},
			// Legacy /dashboard → redirect to index (hub dashboard renders at /:jobPath)
			{ path: 'dashboard', redirectTo: '', pathMatch: 'full' },
			// Legacy /workspace/:key → redirect to hub (spokes removed — nav menu handles navigation)
			{ path: 'workspace/:workspaceKey', redirectTo: '', pathMatch: 'prefix' },
			// Brand preview (design system showcase)
			{
				path: 'brand-preview',
				loadComponent: () => import('./views/home/job-home/brand-preview/brand-preview.component').then(m => m.BrandPreviewComponent)
			},
			// Admin routes — parent requires Admin (Director, SuperDirector, SuperUser)
			// Children that are SuperUser-only get explicit requireSuperUser data
			{
				path: 'admin',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				children: [
					{
						path: 'profile-migration',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/profile-migration/profile-migration.component').then(m => m.ProfileMigrationComponent)
					},
					{
						path: 'profile-editor',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/profile-editor/profile-editor.component').then(m => m.ProfileEditorComponent)
					},
					{
						path: 'theme',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/theme-editor/theme-editor.component').then(m => m.ThemeEditorComponent)
					},
					{
						path: 'widget-editor',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/widget-editor/widget-editor.component').then(m => m.WidgetEditorComponent)
					},
					{
						path: 'nav-editor',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/menu-admin/menu-admin.component').then(m => m.MenuAdminComponent)
					},
					{
						path: 'job-clone',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/job-clone/job-clone.component').then(m => m.JobCloneComponent)
					},
					{
						path: 'ddl-options',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/ddl-options/ddl-options.component').then(m => m.DdlOptionsComponent)
					},
					{
						path: 'job-config',
						canDeactivate: [unsavedChangesGuard],
						loadComponent: () => import('./views/admin/job-config/job-config.component').then(m => m.JobConfigComponent)
					},
					{
						path: 'log-viewer',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/log-viewer/log-viewer.component').then(m => m.LogViewerComponent)
					}
				]
			},
			// Report launcher — handles all menu items with Controller=Reporting
			{
				path: 'reporting/:action',
				loadComponent: () => import('./views/reporting/report-launcher/report-launcher.component').then(m => m.ReportLauncherComponent)
			},
			// Legacy redirect: menu/admin → admin/nav-editor
			{ path: 'menu/admin', redirectTo: 'admin/nav-editor', pathMatch: 'full' },
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
			// Nav-convention routes (match nav menu hierarchy)
			{
				path: 'search/players',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/registration-search/registration-search.component').then(m => m.RegistrationSearchComponent)
			},
			{
				path: 'search/teams',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/team-search/team-search.component').then(m => m.TeamSearchComponent)
			},
			{
				path: 'configure/administrators',
				canActivate: [authGuard],
				data: { requireSuperUser: true },
				loadComponent: () => import('./views/admin/administrator-management/administrator-management.component').then(m => m.AdministratorManagementComponent)
			},
			{
				path: 'configure/discount-codes',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/discount-codes/discount-codes.component').then(m => m.DiscountCodesComponent)
			},
			// Legacy-compatible routes (kept for external link compatibility)
			{
				path: 'admin/search',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/registration-search/registration-search.component').then(m => m.RegistrationSearchComponent)
			},
			{
				path: 'search/index',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/registration-search/registration-search.component').then(m => m.RegistrationSearchComponent)
			},
			{
				path: 'admin/team-search',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/team-search/team-search.component').then(m => m.TeamSearchComponent)
			},
			{
				path: 'searchteams/index',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/team-search/team-search.component').then(m => m.TeamSearchComponent)
			},
			// Scheduling — Post-scheduling tools (standalone, no shell wrapper)
			{
				path: 'scheduling/view-schedule',
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
			// Scheduling — Pipeline shell (dashboard + steps 1–4)
			{
				path: 'scheduling',
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
					},
					{
						path: 'auto-build',
						loadComponent: () => import('./views/admin/scheduling/auto-build/auto-build.component').then(m => m.AutoBuildComponent)
					},
					{
						path: 'qa-results',
						loadComponent: () => import('./views/admin/scheduling/qa-results/qa-results.component').then(m => m.QaResultsComponent)
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
