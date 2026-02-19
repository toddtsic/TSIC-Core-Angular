import { Type } from '@angular/core';

import { ClientBannerComponent } from '@widgets/layout/client-banner/client-banner.component';
import { BulletinsComponent } from '@widgets/communications/bulletins.component';
import { EventContactWidgetComponent } from '@widgets/event-info/event-contact-widget/event-contact-widget.component';
import { PlayerTrendWidgetComponent } from '@widgets/registration/player-trend-widget/player-trend-widget.component';
import { TeamTrendWidgetComponent } from '@widgets/registration/team-trend-widget/team-trend-widget.component';
import { AgegroupDistributionWidgetComponent } from '@widgets/registration/agegroup-distribution-widget/agegroup-distribution-widget.component';
import { YearOverYearWidgetComponent } from '@widgets/scheduling/year-over-year-widget/year-over-year-widget.component';

// ════════════════════════════════════════════════════════════
// Widget Manifest — Single Source of Truth
//
// Every widget that can appear on the dashboard has an entry here.
// Widgets with Angular components set `component`; config-only
// doorbells (link-tile, status-tile) omit it.
//
// The Widget Editor imports this manifest to:
//   1. Populate the Component Key dropdown (no typos)
//   2. Auto-fill form fields on selection
//   3. Detect uncovered routes (manifest keys missing from DB)
//
// The Dashboard imports the derived WIDGET_REGISTRY (below) for
// NgComponentOutlet rendering — unchanged from before.
// ════════════════════════════════════════════════════════════

export interface WidgetManifestEntry {
	/** Angular component class — only for content/chart-tile that need NgComponentOutlet */
	component?: Type<unknown>;
	/** Human-readable widget name */
	label: string;
	/** Bootstrap icon class (e.g. 'bi-search') */
	icon: string;
	/** Widget rendering shape */
	widgetType: 'content' | 'chart-tile' | 'status-tile' | 'link-tile';
	/** Natural workspace this belongs in */
	workspace: string;
	/** Relative route from job root (for link-tiles) */
	route?: string;
	/** Short description */
	description?: string;
	/** Default displayStyle value */
	displayStyle?: string;
}

export const WIDGET_MANIFEST: Record<string, WidgetManifestEntry> = {

	// ── Content widgets (have Angular components) ──

	'client-banner': {
		component:    ClientBannerComponent,
		label:        'Client Banner',
		icon:         'bi-image',
		widgetType:   'content',
		workspace:    'public',
		description:  'Job banner with logo and images',
		displayStyle: 'banner',
	},
	'bulletins': {
		component:    BulletinsComponent,
		label:        'Bulletins',
		icon:         'bi-megaphone',
		widgetType:   'content',
		workspace:    'public',
		description:  'Active job bulletins and announcements',
		displayStyle: 'feed',
	},
	'event-contact': {
		component:    EventContactWidgetComponent,
		label:        'Event Contact',
		icon:         'bi-envelope',
		widgetType:   'content',
		workspace:    'public',
		description:  'Contact name and email for event inquiries',
		displayStyle: 'block',
	},

	// ── Chart-tile widgets (have Angular components) ──

	'player-trend-chart': {
		component:   PlayerTrendWidgetComponent,
		label:       'Player Registration Trend',
		icon:        'bi-graph-up',
		widgetType:  'chart-tile',
		workspace:   'dashboard',
		description: 'Daily player registration counts and cumulative revenue over time',
	},
	'team-trend-chart': {
		component:   TeamTrendWidgetComponent,
		label:       'Team Registration Trend',
		icon:        'bi-graph-up-arrow',
		widgetType:  'chart-tile',
		workspace:   'dashboard',
		description: 'Daily team registration counts and cumulative revenue over time',
	},
	'agegroup-distribution': {
		component:   AgegroupDistributionWidgetComponent,
		label:       'Age Group Distribution',
		icon:        'bi-bar-chart',
		widgetType:  'chart-tile',
		workspace:   'dashboard',
		description: 'Player and team counts broken down by age group',
	},
	'year-over-year': {
		component:   YearOverYearWidgetComponent,
		label:       'Year-over-Year Comparison',
		icon:        'bi-arrow-repeat',
		widgetType:  'chart-tile',
		workspace:   'dashboard',
		description: 'Registration comparison between current and prior year',
	},

	// ── Status-tile widgets (dormant — no Angular component yet) ──

	'registration-status': {
		label:       'Registration Status',
		icon:        'bi-people-fill',
		widgetType:  'status-tile',
		workspace:   'dashboard',
		route:       'admin/search',
		description: 'Active registration count and trend indicator',
	},
	'financial-status': {
		label:       'Financial Status',
		icon:        'bi-currency-dollar',
		widgetType:  'status-tile',
		workspace:   'dashboard',
		route:       'reporting/financials',
		description: 'Revenue collected vs outstanding balance summary',
	},
	'scheduling-status': {
		label:       'Scheduling Status',
		icon:        'bi-calendar-check',
		widgetType:  'status-tile',
		workspace:   'dashboard',
		route:       'admin/scheduling',
		description: 'Schedule completion percentage and game count',
	},

	// ── Link-tile doorbells: Job Configuration ──

	'ladt-editor': {
		label:       'LADT Editor',
		icon:        'bi-diagram-3',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'ladt/admin',
		description: 'Configure leagues, age groups, divisions, and teams',
	},
	'fee-config': {
		label:       'Fee Configuration',
		icon:        'bi-tags',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'ladt/admin',
		description: 'Configure registration fees and payment options',
	},
	'job-settings': {
		label:       'Job Settings',
		icon:        'bi-sliders',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'ladt/admin',
		description: 'General event configuration and settings',
	},
	'widget-editor': {
		label:       'Widget Editor',
		icon:        'bi-gear-fill',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'admin/widget-editor',
		description: 'Configure widget assignments and dashboard layout (SuperUser only)',
	},
	'administrator-management': {
		label:       'Administrator Management',
		icon:        'bi-person-gear',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'jobadministrator/admin',
		description: 'Manage admin accounts and permissions for this event',
	},
	'ddl-options': {
		label:       'DDL Options',
		icon:        'bi-list-ul',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'admin/ddl-options',
		description: 'Manage dropdown list options for job configuration',
	},
	'discount-codes': {
		label:       'Discount Codes',
		icon:        'bi-percent',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'jobdiscountcodes/admin',
		description: 'Create and manage registration discount codes',
	},
	'job-clone': {
		label:       'Job Clone',
		icon:        'bi-copy',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'admin/job-clone',
		description: 'Clone this event to create a new year or copy',
	},
	'profile-editor': {
		label:       'Profile Editor',
		icon:        'bi-card-list',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'admin/profile-editor',
		description: 'Create and edit custom registration profile types',
	},
	'profile-migration': {
		label:       'Profile Migration',
		icon:        'bi-arrow-left-right',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'admin/profile-migration',
		description: 'Batch migrate registrations between profile types',
	},
	'theme-editor': {
		label:       'Theme Editor',
		icon:        'bi-palette',
		widgetType:  'link-tile',
		workspace:   'job-config',
		route:       'admin/theme',
		description: 'Configure color palette and theme for this event',
	},

	// ── Link-tile doorbells: Player Registration ──

	'search-registrations': {
		label:       'Search Registrations',
		icon:        'bi-search',
		widgetType:  'link-tile',
		workspace:   'player-reg',
		route:       'admin/search',
		description: 'Search and manage player registrations',
	},
	'roster-swapper': {
		label:       'Roster Swapper',
		icon:        'bi-arrow-left-right',
		widgetType:  'link-tile',
		workspace:   'player-reg',
		route:       'admin/roster-swapper',
		description: 'Move players between team rosters',
	},

	// ── Link-tile doorbells: Team Registration ──

	'view-by-club': {
		label:       'View by Club',
		icon:        'bi-building',
		widgetType:  'link-tile',
		workspace:   'team-reg',
		route:       'admin/team-search',
		description: 'Browse registrations grouped by club',
	},

	// ── Link-tile doorbells: Scheduling ──

	'scheduling-pipeline': {
		label:       'Scheduling Pipeline',
		icon:        'bi-kanban',
		widgetType:  'link-tile',
		workspace:   'scheduling',
		route:       'admin/scheduling',
		description: 'Step-by-step scheduling workflow',
	},
	'pool-assignment': {
		label:       'Pool Assignment',
		icon:        'bi-people-fill',
		widgetType:  'link-tile',
		workspace:   'scheduling',
		route:       'admin/pool-assignment',
		description: 'Assign teams to pools',
	},
	'view-schedule': {
		label:       'View Schedule',
		icon:        'bi-calendar-check',
		widgetType:  'link-tile',
		workspace:   'scheduling',
		route:       'admin/scheduling/view-schedule',
		description: 'View published game schedule',
	},
	'rescheduler': {
		label:       'Rescheduler',
		icon:        'bi-calendar-x',
		widgetType:  'link-tile',
		workspace:   'scheduling',
		route:       'admin/scheduling/rescheduler',
		description: 'Reschedule or swap individual games',
	},

	// ── Link-tile doorbells: Finances ──

	'financial-overview': {
		label:       'Financial Overview',
		icon:        'bi-cash-stack',
		widgetType:  'link-tile',
		workspace:   'fin-per-job',
		route:       'reporting/financials',
		description: 'Payment status and outstanding balance reports',
	},
	'my-payments': {
		label:       'My Payments',
		icon:        'bi-wallet2',
		widgetType:  'link-tile',
		workspace:   'fin-per-customer',
		route:       'reporting/financials',
		description: 'View your payment history and outstanding balances',
	},
};

/**
 * Derived component registry for dashboard NgComponentOutlet.
 * Only includes manifest entries that have an Angular component class.
 * Dashboard import is unchanged: `import { WIDGET_REGISTRY } from '@widgets/widget-registry'`
 */
export const WIDGET_REGISTRY: Record<string, Type<unknown>> = Object.fromEntries(
	Object.entries(WIDGET_MANIFEST)
		.filter(([, e]) => e.component != null)
		.map(([key, e]) => [key, e.component!]),
);
