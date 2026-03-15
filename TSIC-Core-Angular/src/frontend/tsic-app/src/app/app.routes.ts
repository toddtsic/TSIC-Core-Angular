import { Routes } from '@angular/router';
import { authGuard } from './infrastructure/guards/auth.guard';
import { storeGuard } from './infrastructure/guards/store.guard';
import { unsavedChangesGuard } from './infrastructure/guards/unsaved-changes.guard';
import { LayoutComponent } from './layouts/client-layout/layout.component';

export const routes: Routes = [
	// Default route - redirect to last visited job or /tsic
	{ path: '', redirectTo: '/tsic', pathMatch: 'full' },

	// TSIC corporate landing — standalone marketing page (no layout chrome)
	{
		path: 'tsic',
		canActivate: [authGuard],
		data: { redirectAuthenticated: true },
		loadComponent: () => import('./views/home/tsic-landing/tsic-landing.component').then(m => m.TsicLandingComponent)
	},

	// Privacy Policy — public, no auth required, no layout chrome
	{
		path: 'privacy-policy',
		loadComponent: () => import('./views/home/privacy-policy/privacy-policy.component').then(m => m.PrivacyPolicyComponent)
	},

	// Password reset (top-level — not job-scoped, since users span multiple jobs)
	{
		path: 'forgot-password',
		loadComponent: () => import('./views/auth/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent)
	},
	{
		path: 'reset-password',
		loadComponent: () => import('./views/auth/reset-password/reset-password.component').then(m => m.ResetPasswordComponent)
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
			// Auth
			{
				path: 'login',
				loadComponent: () => import('./views/auth/login/login.component').then(m => m.LoginComponent),
				canActivate: [authGuard],
				data: { redirectAuthenticated: true }
			},
			{
				path: 'terms-of-service',
				loadComponent: () => import('./views/auth/terms-of-service/terms-of-service.component').then(m => m.TermsOfServiceComponent)
			},
			{
				path: 'role-selection',
				loadComponent: () => import('./views/auth/role-selection/role-selection.component').then(m => m.RoleSelectionComponent)
			},
			// Registration (Controller/Action)
			{
				path: 'registration',
				children: [
					{
						path: 'entry',
						loadComponent: () => import('./views/registration/entry/entry.component').then(m => m.RegistrationEntryComponent)
					},
					{
						path: 'player',
						loadComponent: () => import('./views/registration/player/player.component').then(m => m.PlayerWizardV2Component)
					},
					{
						path: 'team',
						loadComponent: () => import('./views/registration/team/team.component').then(m => m.TeamWizardV2Component)
					},
					{
						path: 'adult',
						loadComponent: () => import('./views/registration/adult/adult.component').then(m => m.AdultWizardV2Component)
					},
					{
						path: 'family',
						loadComponent: () => import('./views/registration/family/family.component').then(m => m.FamilyWizardV2Component)
					}
				]
			},
			{
				path: 'home',
				loadComponent: () => import('./views/home/job-home/job-home.component').then(m => m.JobHomeComponent)
			},
			{
				path: 'brand-preview',
				loadComponent: () => import('./views/home/brand-preview/brand-preview.component').then(m => m.BrandPreviewComponent)
			},
			// Configure — job & platform settings
			{
				path: 'configure',
				children: [
					{
						path: 'job',
						canActivate: [authGuard],
						canDeactivate: [unsavedChangesGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/configure/job/job-config.component').then(m => m.JobConfigComponent)
					},
					{
						path: 'administrators',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/configure/administrators/administrators.component').then(m => m.AdministratorManagementComponent)
					},
					{
						path: 'discount-codes',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/configure/discount-codes/discount-codes.component').then(m => m.DiscountCodesComponent)
					},
					{
						path: 'customer-groups',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/configure/customer-groups/customer-groups.component').then(m => m.CustomerGroupsComponent)
					},
					{
						path: 'age-ranges',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/configure/age-ranges/configure-age-ranges.component').then(m => m.ConfigureAgeRangesComponent)
					},
					{
						path: 'ddl-options',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/configure/ddl-options/ddl-options.component').then(m => m.DdlOptionsComponent)
					},
					{
						path: 'customers',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/configure/customers/customer-configure.component').then(m => m.CustomerConfigureComponent)
					},
					{
						path: 'theme',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/configure/theme/theme-editor.component').then(m => m.ThemeEditorComponent)
					},
					{
						path: 'nav-editor',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/configure/nav-editor/nav-editor.component').then(m => m.NavEditorComponent)
					},
					{
						path: 'widget-editor',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/configure/widget-editor/widget-editor.component').then(m => m.WidgetEditorComponent)
					},
					{
						path: 'job-clone',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/configure/job-clone/job-clone.component').then(m => m.JobCloneComponent)
					},
					{
						path: 'uniform-upload',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/configure/uniform-upload/uniform-upload.component').then(m => m.UniformUploadComponent)
					}
				]
			},
			// Search — player & team lookup
			{
				path: 'search',
				children: [
					{
						path: 'players',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/search/players/search-players.component').then(m => m.RegistrationSearchComponent)
					},
					{
						path: 'teams',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/search/teams/search-teams.component').then(m => m.TeamSearchComponent)
					}
				]
			},
			// Communications — bulletins, email, push
			{
				path: 'communications',
				children: [
					{
						path: 'bulletins',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/communications/bulletins/bulletin-editor.component').then(m => m.BulletinEditorComponent)
					},
					{
						path: 'email-log',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/communications/email-log/email-log.component').then(m => m.EmailLogComponent)
					},
					{
						path: 'push-notification',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/communications/push-notification/push-notification.component').then(m => m.PushNotificationComponent)
					}
				]
			},
			// LADT — leagues, agegroups, divisions, teams
			{
				path: 'ladt',
				children: [
					{
						path: 'editor',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/ladt/editor/ladt.component').then(m => m.LadtEditorComponent)
					},
					{
						path: 'roster-swapper',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/ladt/roster-swapper/roster-swapper.component').then(m => m.RosterSwapperComponent)
					},
					{
						path: 'pool-assignment',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/ladt/pool-assignment/pool-assignment.component').then(m => m.PoolAssignmentComponent)
					}
				]
			},
			// ARB — automatic recurring billing
			{
				path: 'arb',
				children: [
					{
						path: 'health',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/arb/health/arb-health.component').then(m => m.ArbHealthComponent)
					},
					{
						path: 'update-cc/:registrationId',
						canActivate: [authGuard],
						loadComponent: () => import('./views/arb/arb-update-cc.component').then(m => m.ArbUpdateCcComponent)
					}
				]
			},
			// Tools — utilities & one-off tools
			{
				path: 'tools',
				children: [
					{
						path: 'uslax-test',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/tools/uslax-test/uslax-test.component').then(m => m.UsLaxTestComponent)
					},
					{
						path: 'uslax-rankings',
						canActivate: [authGuard],
						data: { requireAdmin: true },
						loadComponent: () => import('./views/tools/uslax-rankings/uslax-rankings.component').then(m => m.UsLaxRankingsComponent)
					},
					{
						path: 'profile-migration',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/tools/profile-migration/profile-migration.component').then(m => m.ProfileMigrationComponent)
					},
					{
						path: 'profile-editor',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/tools/profile-editor/profile-editor.component').then(m => m.ProfileEditorComponent)
					},
					{
						path: 'change-password',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/tools/change-password/change-password.component').then(m => m.ChangePasswordComponent)
					},
					{
						path: 'customer-job-revenue',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/tools/customer-job-revenue/customer-job-revenue.component').then(m => m.CustomerJobRevenueComponent)
					}
				]
			},
			// Store
			{
				path: 'store/walk-up',
				canActivate: [storeGuard],
				data: { storeMode: 'walk-up' },
				loadComponent: () => import('./views/store/walk-up/walk-up.component').then(m => m.StoreWalkUpComponent)
			},
			{
				path: 'store/login',
				canActivate: [storeGuard],
				data: { storeMode: 'login' },
				loadComponent: () => import('./views/store/login/login.component').then(m => m.StoreLoginComponent)
			},
			{
				path: 'store',
				canActivate: [storeGuard],
				loadComponent: () => import('./views/store/catalog/catalog.component').then(m => m.StoreCatalogComponent)
			},
			{
				path: 'store/item/:storeItemId',
				canActivate: [storeGuard],
				loadComponent: () => import('./views/store/item-detail/item-detail.component').then(m => m.StoreItemDetailComponent)
			},
			{
				path: 'store/cart',
				canActivate: [storeGuard],
				loadComponent: () => import('./views/store/cart/cart.component').then(m => m.StoreCartComponent)
			},
			{
				path: 'store/checkout',
				canActivate: [storeGuard],
				loadComponent: () => import('./views/store/checkout/checkout.component').then(m => m.StoreCheckoutComponent)
			},
			{
				path: 'store/admin',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				loadComponent: () => import('./views/store/admin/store-admin.component').then(m => m.StoreAdminComponent)
			},
			// Reporting
			{
				path: 'reporting/:action',
				loadComponent: () => import('./views/reporting/report-launcher/report-launcher.component').then(m => m.ReportLauncherComponent)
			},
			// Scheduling — standalone tools (no shell)
			{
				path: 'scheduling/view-schedule',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				loadComponent: () => import('./views/scheduling/view-schedule/view-schedule.component').then(m => m.ViewScheduleComponent)
			},
			{
				path: 'scheduling/master-schedule',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				loadComponent: () => import('./views/scheduling/master-schedule/master-schedule.component').then(m => m.MasterScheduleComponent)
			},
			{
				path: 'scheduling/rescheduler',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				loadComponent: () => import('./views/scheduling/rescheduler/rescheduler.component').then(m => m.ReschedulerComponent)
			},
			{
				path: 'scheduling/tournament-parking',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				loadComponent: () => import('./views/scheduling/tournament-parking/tournament-parking.component').then(m => m.TournamentParkingComponent)
			},
			{
				path: 'scheduling/referee-assignment',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				loadComponent: () => import('./views/scheduling/referee-assignment/referee-assignment.component').then(m => m.RefereeAssignmentComponent)
			},
			{
				path: 'scheduling/referee-calendar',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				loadComponent: () => import('./views/scheduling/referee-calendar/referee-calendar.component').then(m => m.RefereeCalendarComponent)
			},
			{
				path: 'scheduling/mobile-scorers',
				canActivate: [authGuard],
				data: { requireAdmin: true, title: 'Mobile Scorers' },
				loadComponent: () => import('./views/scheduling/mobile-scorers/mobile-scorers.component').then(m => m.MobileScorersComponent)
			},
			// Scheduling — pipeline shell (dashboard + steps)
			{
				path: 'scheduling',
				canActivate: [authGuard],
				data: { requireAdmin: true },
				loadComponent: () => import('./views/scheduling/dashboard/scheduling-shell.component').then(m => m.SchedulingShellComponent),
				children: [
					{
						path: '',
						loadComponent: () => import('./views/scheduling/dashboard/scheduling-dashboard.component').then(m => m.SchedulingDashboardComponent)
					},
					{
						path: 'fields',
						loadComponent: () => import('./views/scheduling/fields/manage-fields.component').then(m => m.ManageFieldsComponent)
					},
					{
						path: 'pairings',
						loadComponent: () => import('./views/scheduling/pairings/manage-pairings.component').then(m => m.ManagePairingsComponent)
					},
					{
						path: 'timeslots',
						loadComponent: () => import('./views/scheduling/timeslots/manage-timeslots.component').then(m => m.ManageTimeslotsComponent)
					},
					{
						path: 'schedule-hub',
						loadComponent: () => import('./views/scheduling/schedule-division/schedule-division.component').then(m => m.ScheduleDivisionComponent)
					},
					{
						path: 'qa-results',
						loadComponent: () => import('./views/scheduling/qa-results/qa-results.component').then(m => m.QaResultsComponent)
					}
				]
			},
			// Public schedule view (anonymous access)
			{
				path: 'schedule',
				data: { publicMode: true },
				loadComponent: () => import('./views/scheduling/view-schedule/view-schedule.component').then(m => m.ViewScheduleComponent)
			}
		]
	},

	// Wildcard route - must be last
	{
		path: '**',
		loadComponent: () => import('./views/errors/not-found/not-found.component').then(m => m.NotFoundComponent)
	}
];
