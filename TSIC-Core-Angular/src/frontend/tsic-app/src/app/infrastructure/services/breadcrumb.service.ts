import { Injectable, inject, signal, computed } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';

export interface BreadcrumbSegment {
	label: string;
	url: string | null;   // null = current page (no link)
	icon?: string;
}

/**
 * Route path → page title mapping.
 * Key is the route path AFTER jobPath (e.g., 'ladt/admin', 'admin/scheduling').
 */
const ROUTE_TITLE_MAP: Record<string, string> = {
	'ladt/admin':                          'LADT Editor',
	'admin/scheduling':                    'Scheduling Pipeline',
	'admin/scheduling/fields':             'Fields',
	'admin/scheduling/pairings':           'Pairings',
	'admin/scheduling/timeslots':          'Timeslots',
	'admin/scheduling/schedule-division':  'Schedule Division',
	'admin/scheduling/view-schedule':      'View Schedule',
	'admin/scheduling/rescheduler':        'Rescheduler',
	'admin/pool-assignment':               'Pool Assignment',
	'admin/search':                        'Registration Search',
	'admin/team-search':                   'Team Search',
	'admin/roster-swapper':                'Roster Swapper',
	'jobdiscountcodes/admin':              'Discount Codes',
	'jobadministrator/admin':              'Administrator Management',
	'admin/profile-migration':             'Profile Migration',
	'admin/profile-editor':                'Profile Editor',
	'admin/theme':                         'Theme Editor',
	'admin/widget-editor':                 'Widget Editor',
	'admin/nav-editor':                    'Nav Editor',
	'admin/job-clone':                     'Job Clone',
	'admin/ddl-options':                   'DDL Options',
	'admin/job-config':                    'Job Configuration',
	// Legacy routes
	'fields/index':                        'Fields',
	'pairings/index':                      'Pairings',
	'timeslots/index':                     'Timeslots',
	'rosters/swapper':                     'Roster Swapper',
	'search/index':                        'Registration Search',
	'searchteams/index':                   'Team Search',
	'teampoolassignment/index':            'Pool Assignment',
	'scheduling/schedules':                'View Schedule',
	'scheduling/rescheduler':              'Rescheduler',
	'scheduling/scheduledivision':         'Schedule Division',
};

/** Routes where breadcrumbs should NOT be shown */
const EXCLUDED_PREFIXES = new Set([
	'login', 'role-selection', 'terms-of-service', 'registration',
	'register-player', 'register-team', 'family-account', 'home',
	'brand-preview',
]);

@Injectable({ providedIn: 'root' })
export class BreadcrumbService {
	private readonly router = inject(Router);

	private readonly _trail = signal<BreadcrumbSegment[]>([]);

	/** Public breadcrumb trail for the component */
	readonly trail = this._trail.asReadonly();

	/** Whether the breadcrumb bar should be visible */
	readonly visible = computed(() => this._trail().length > 0);

	constructor() {
		this.router.events.pipe(
			filter((e): e is NavigationEnd => e instanceof NavigationEnd)
		).subscribe(e => this.updateTrail(e.urlAfterRedirects || e.url));
	}

	private updateTrail(fullUrl: string): void {
		const [pathPart] = fullUrl.split('?');
		const segments = pathPart.split('/').filter(Boolean);

		if (segments.length === 0) {
			this._trail.set([]);
			return;
		}

		const jobPath = segments[0];

		// Skip TSIC landing and not-found
		if (jobPath === 'tsic' || jobPath === 'not-found') {
			this._trail.set([]);
			return;
		}

		const remainingSegments = segments.slice(1);
		const remainingPath = remainingSegments.join('/');

		// Skip excluded routes
		if (remainingSegments.length > 0 && EXCLUDED_PREFIXES.has(remainingSegments[0])) {
			this._trail.set([]);
			return;
		}

		// Hub page (no remaining path or legacy /dashboard redirect)
		if (remainingPath === '' || remainingPath === 'dashboard') {
			this._trail.set([
				{ label: 'Home', url: null, icon: 'bi-house-door' },
			]);
			return;
		}

		// Function page — resolve title
		const pageTitle = this.resolveTitle(remainingPath);

		if (!pageTitle) {
			// Unknown page — minimal breadcrumb
			this._trail.set([
				{ label: 'Home', url: `/${jobPath}`, icon: 'bi-house-door' },
				{ label: this.humanize(remainingPath), url: null },
			]);
			return;
		}

		const trail: BreadcrumbSegment[] = [
			{ label: 'Home', url: `/${jobPath}`, icon: 'bi-house-door' },
		];

		// Scheduling shell sub-pages get "Scheduling Pipeline" as intermediate
		if (remainingPath.startsWith('admin/scheduling/') && remainingPath !== 'admin/scheduling') {
			trail.push({
				label: 'Scheduling Pipeline',
				url: `/${jobPath}/admin/scheduling`,
			});
		}

		trail.push({ label: pageTitle, url: null });

		this._trail.set(trail);
	}

	private resolveTitle(routePath: string): string | null {
		if (ROUTE_TITLE_MAP[routePath]) return ROUTE_TITLE_MAP[routePath];

		// Reporting routes: reporting/:action
		if (routePath.startsWith('reporting/')) {
			const action = routePath.split('/')[1];
			return action ? this.humanize(action) : 'Reports';
		}

		// Public schedule
		if (routePath === 'schedule') return 'Schedule';

		return null;
	}

	private humanize(path: string): string {
		return path.split('/').pop()?.replace(/[-_]/g, ' ')
			.replace(/\b\w/g, c => c.toUpperCase()) || path;
	}
}
