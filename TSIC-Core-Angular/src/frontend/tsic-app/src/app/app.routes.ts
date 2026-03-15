import { Routes } from '@angular/router';
import { authGuard } from './infrastructure/guards/auth.guard';
import { storeGuard } from './infrastructure/guards/store.guard';
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
			// Registration routes (Controller/Action: registration/{action})
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
			// Brand preview (design system showcase)
			{
				path: 'brand-preview',
				loadComponent: () => import('./views/home/brand-preview/brand-preview.component').then(m => m.BrandPreviewComponent)
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
						loadComponent: () => import('./views/admin/nav-editor/nav-editor.component').then(m => m.NavEditorComponent)
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
						path: 'uslax-test',
						loadComponent: () => import('./views/admin/uslax-test/uslax-test.component').then(m => m.UsLaxTestComponent)
					},
					{
						path: 'uslax-rankings',
						loadComponent: () => import('./views/admin/uslax-rankings/uslax-rankings.component').then(m => m.UsLaxRankingsComponent)
					},
					{
						path: 'email-log',
						loadComponent: () => import('./views/admin/email-log/email-log.component').then(m => m.EmailLogComponent)
					},
					{
						path: 'bulletin-editor',
						loadComponent: () => import('./views/admin/bulletin-editor/bulletin-editor.component').then(m => m.BulletinEditorComponent)
					},
					{
						path: 'configure-age-ranges',
						loadComponent: () => import('./views/admin/configure-age-ranges/configure-age-ranges.component').then(m => m.ConfigureAgeRangesComponent)
					},
					{
						path: 'store',
						loadComponent: () => import('./views/admin/store-admin/store-admin.component').then(m => m.StoreAdminComponent)
					},
					{
						path: 'customer-configure',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/customer-configure/customer-configure.component').then(m => m.CustomerConfigureComponent)
					},
					{
						path: 'arb-health',
						loadComponent: () => import('./views/admin/arb-health/arb-health.component').then(m => m.ArbHealthComponent)
					},
					{
						path: 'mobile-scorers',
						loadComponent: () => import('./views/admin/mobile-scorers/mobile-scorers.component').then(m => m.MobileScorersComponent),
						data: { title: 'Mobile Scorers' }
					},
					{
						path: 'change-password',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/change-password/change-password.component').then(m => m.ChangePasswordComponent)
					},
					{
						path: 'uniform-upload',
						loadComponent: () => import('./views/admin/uniform-upload/uniform-upload.component').then(m => m.UniformUploadComponent)
					},
					{
						path: 'push-notification',
						loadComponent: () => import('./views/admin/push-notification/push-notification.component').then(m => m.PushNotificationComponent)
					},
					{
						path: 'customer-job-revenue',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/customer-job-revenue/customer-job-revenue.component').then(m => m.CustomerJobRevenueComponent)
					},
					{
						path: 'ladt',
						canActivate: [authGuard],
						data: { requirePhase2: true },
						loadComponent: () => import('./views/admin/ladt/ladt.component').then(m => m.LadtEditorComponent)
					},
					{
						path: 'administrators',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/administrators/administrators.component').then(m => m.AdministratorManagementComponent)
					},
					{
						path: 'discount-codes',
						canActivate: [authGuard],
						data: { requirePhase2: true },
						loadComponent: () => import('./views/admin/discount-codes/discount-codes.component').then(m => m.DiscountCodesComponent)
					},
					{
						path: 'customer-groups',
						canActivate: [authGuard],
						data: { requireSuperUser: true },
						loadComponent: () => import('./views/admin/customer-groups/customer-groups.component').then(m => m.CustomerGroupsComponent)
					},
					{
						path: 'search-players',
						canActivate: [authGuard],
						data: { requirePhase2: true },
						loadComponent: () => import('./views/admin/search-players/search-players.component').then(m => m.RegistrationSearchComponent)
					},
					{
						path: 'search-teams',
						canActivate: [authGuard],
						data: { requirePhase2: true },
						loadComponent: () => import('./views/admin/search-teams/search-teams.component').then(m => m.TeamSearchComponent)
					},
					{
						path: 'roster-swapper',
						canActivate: [authGuard],
						data: { requirePhase2: true },
						loadComponent: () => import('./views/admin/roster-swapper/roster-swapper.component').then(m => m.RosterSwapperComponent)
					},
					{
						path: 'pool-assignment',
						canActivate: [authGuard],
						data: { requirePhase2: true },
						loadComponent: () => import('./views/admin/pool-assignment/pool-assignment.component').then(m => m.PoolAssignmentComponent)
					}
				]
			},
			// ARB self-service: update credit card on subscription
			{
				path: 'arb/update-cc/:registrationId',
				canActivate: [authGuard],
				loadComponent: () => import('./views/arb/arb-update-cc.component').then(m => m.ArbUpdateCcComponent)
			},
			// Store — walk-up kiosk (always forces clean slate)
			{
				path: 'store/walk-up',
				canActivate: [storeGuard],
				data: { storeMode: 'walk-up' },
				loadComponent: () => import('./views/store/walk-up/walk-up.component').then(m => m.StoreWalkUpComponent)
			},
			// Store — focused login page (family sign-in + guest option)
			{
				path: 'store/login',
				canActivate: [storeGuard],
				data: { storeMode: 'login' },
				loadComponent: () => import('./views/store/login/login.component').then(m => m.StoreLoginComponent)
			},
			// Store — authenticated storefront (requires Player/Family role)
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
			// Report launcher — handles all menu items with Controller=Reporting
			{
				path: 'reporting/:action',
				loadComponent: () => import('./views/reporting/report-launcher/report-launcher.component').then(m => m.ReportLauncherComponent)
			},
			// Scheduling — Post-scheduling tools (standalone, no shell wrapper)
			{
				path: 'scheduling/view-schedule',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/view-schedule/view-schedule.component').then(m => m.ViewScheduleComponent)
			},
			{
				path: 'scheduling/master-schedule',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/master-schedule/master-schedule.component').then(m => m.MasterScheduleComponent)
			},
			{
				path: 'scheduling/rescheduler',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/rescheduler/rescheduler.component').then(m => m.ReschedulerComponent)
			},
			{
				path: 'scheduling/tournament-parking',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/tournament-parking/tournament-parking.component').then(m => m.TournamentParkingComponent)
			},
			{
				path: 'scheduling/referee-assignment',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/referee-assignment/referee-assignment.component').then(m => m.RefereeAssignmentComponent)
			},
			{
				path: 'scheduling/referee-calendar',
				canActivate: [authGuard],
				data: { requirePhase2: true },
				loadComponent: () => import('./views/admin/scheduling/referee-calendar/referee-calendar.component').then(m => m.RefereeCalendarComponent)
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
						path: 'schedule-hub',
						loadComponent: () => import('./views/admin/scheduling/schedule-division/schedule-division.component').then(m => m.ScheduleDivisionComponent)
					},
					{
						path: 'qa-results',
						loadComponent: () => import('./views/admin/scheduling/qa-results/qa-results.component').then(m => m.QaResultsComponent)
					}
				]
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
