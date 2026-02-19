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
// widgets (status-tile) omit it.
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
	widgetType: 'content' | 'chart-tile' | 'status-tile';
	/** Natural workspace this belongs in */
	workspace: string;
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
