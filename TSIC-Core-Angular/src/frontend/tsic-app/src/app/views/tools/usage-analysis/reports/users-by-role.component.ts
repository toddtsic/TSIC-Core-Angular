import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, filter, map, switchMap } from 'rxjs/operators';
import { catchError, of } from 'rxjs';
import { ChartAllModule, type SeriesModel } from '@syncfusion/ej2-angular-charts';

import { environment } from '@environments/environment';
import type { UsersByRoleDto, UsersByRoleRowDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

/**
 * Customer-facing roles in a FIXED order, so a role keeps its colour and its column
 * position in every chart, every window, and every later report. Roles not listed
 * follow alphabetically.
 */
const ROLE_ORDER: readonly string[] = [
	'Family', 'Player', 'Staff', 'Club Rep', 'Unassigned Adult', 'Referee', 'Scorer', 'Recruiter', 'Guest',
];

/** Series palette, by ROLE_ORDER position. Palette-responsive: resolved from :root at init. */
const PALETTE_VARS: readonly [string, string][] = [
	['--bs-primary', '#0d6efd'],
	['--brand-accent', '#6f42c1'],
	['--bs-success', '#198754'],
	['--bs-warning', '#ffc107'],
	['--bs-info', '#0dcaf0'],
	['--bs-danger', '#dc3545'],
	['--bs-secondary', '#6c757d'],
];

interface EventTotal {
	readonly jobId: string;
	readonly jobName: string;
	readonly customer: number;
	readonly admin: number;
	readonly byRole: ReadonlyMap<string, number>;
}

/** What the chart draws: one label, one count per role. An event, or the whole scope rolled up. */
interface ChartTarget {
	readonly label: string;
	readonly byRole: ReadonlyMap<string, number>;
	readonly admin: number;
}

/** What this report fetches with. The event lens is NOT in it: the table is the whole scope. */
interface FetchKey {
	readonly scope: string;
	readonly windowDays: number;
	readonly excludeBots: boolean;
	readonly clientId: number | null;
}

/**
 * Report 01 — Users by Role. Distinct people who used the scoped live events in the
 * window, counted by registration, keyed by event, grouped by role.
 *
 * Two surfaces from one scope-wide fetch:
 *  - The CHART follows the Event dropdown. One event picked: that event's roles, one
 *    column each — a Director's one event and a Superuser's chosen one are the same
 *    chart. "All events": the whole scope rolled up into one cluster, using the server's
 *    scope-wide distinct counts (a family using two events is one person, so this can be
 *    smaller than the table's column sums). Twelve clusters side by side were unreadable.
 *  - The TABLE is the whole scope: every live event, roles across, a Total, and Admin &
 *    staff as a muted last column so setup clicks never pad the people count. The row of
 *    the charted event — or, at All events, the event the caller is standing in — is
 *    highlighted. Clicking an event name moves the dropdown and the chart to it.
 *
 * People only. Anonymous traffic is requests, not people — nothing in the log can turn
 * an anonymous request into a visitor, and a registered user browsing before sign-in is
 * anonymous too — so it is deliberately absent rather than shown as a different unit
 * beside a people count.
 */
@Component({
	selector: 'app-usage-users-by-role',
	standalone: true,
	imports: [ChartAllModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './users-by-role.component.html',
	styleUrl: './users-by-role.component.scss',
})
export class UsersByRoleComponent implements OnInit {
	private readonly http = inject(HttpClient);
	private readonly destroyRef = inject(DestroyRef);
	readonly state = inject(UsageAnalysisStateService);

	readonly data = signal<UsersByRoleDto | null>(null);
	readonly isLoading = signal(false);
	readonly error = signal<string | null>(null);

	// Resolved eagerly so the chart never receives post-init property changes.
	private readonly palette = PALETTE_VARS.map(([v, fb]) => cssVar(v, fb));
	readonly mutedColor = cssVar('--brand-text-muted', '#6c757d');
	readonly borderColor = cssVar('--brand-border', 'rgba(0,0,0,0.1)');
	// ej2 draws SVG text in its own default face; hand it the page font so the axes match the table.
	private readonly fontFamily = cssVar('--bs-body-font-family', 'system-ui, sans-serif');

	private readonly rows = computed<readonly UsersByRoleRowDto[]>(() => this.data()?.rows ?? []);

	/** Customer-facing roles present anywhere in the scope, in ROLE_ORDER then alphabetical. */
	readonly roles = computed<readonly string[]>(() => {
		const present = new Set(this.rows().filter(r => !r.isAdmin).map(r => r.roleName));
		const ordered = ROLE_ORDER.filter(r => present.has(r));
		const rest = [...present].filter(r => !ROLE_ORDER.includes(r)).sort((a, b) => a.localeCompare(b));
		return [...ordered, ...rest];
	});

	/** Every event in the answer with its totals, busiest first. The table lists all of these. */
	readonly events = computed<readonly EventTotal[]>(() => {
		const byJob = new Map<string, { jobName: string; customer: number; admin: number; byRole: Map<string, number> }>();
		for (const r of this.rows()) {
			let e = byJob.get(r.jobId);
			if (!e) {
				e = { jobName: r.jobName, customer: 0, admin: 0, byRole: new Map() };
				byJob.set(r.jobId, e);
			}
			if (r.isAdmin) {
				e.admin += r.users;
			} else {
				e.customer += r.users;
				e.byRole.set(r.roleName, (e.byRole.get(r.roleName) ?? 0) + r.users);
			}
		}
		return [...byJob.entries()]
			.map(([jobId, e]) => ({ jobId, ...e }))
			.sort((a, b) => b.customer - a.customer || a.jobName.localeCompare(b.jobName));
	});

	/** Lower-cased id of the table row to single out: the lens, else the event the caller stands in. */
	readonly highlightId = computed(() => this.state.highlightJobId());

	/** The lens event's totals, when a lens is set and the event had any users at all. */
	private readonly lensEvent = computed<EventTotal | null>(() => {
		const id = this.state.eventId()?.toLowerCase();
		if (!id) return null;
		return this.events().find(e => e.jobId.toLowerCase() === id) ?? null;
	});

	/**
	 * What the chart draws. Lens set: that event. No lens: the scope rolled up from the
	 * server's distinct-per-role totals. Null when a lens is set but the event had no users.
	 */
	readonly chartTarget = computed<ChartTarget | null>(() => {
		if (this.state.eventId()) {
			const e = this.lensEvent();
			return e ? { label: e.jobName, byRole: e.byRole, admin: e.admin } : null;
		}
		const d = this.data();
		if (!d) return null;
		const byRole = new Map<string, number>();
		let admin = 0;
		for (const t of d.totals) {
			if (t.isAdmin) admin += t.users;
			else byRole.set(t.roleName, (byRole.get(t.roleName) ?? 0) + t.users);
		}
		const n = this.state.jobCount();
		const label = n === 1 ? (this.events()[0]?.jobName ?? 'This event') : `All ${n} live events`;
		return { label, byRole, admin };
	});

	/** Whether the chart is the scope rollup rather than one event. */
	readonly isRollup = computed(() => !this.state.eventId());

	/** Roles present in the charted target, in the same fixed order as the table columns. */
	readonly chartRoles = computed<readonly string[]>(() => {
		const t = this.chartTarget();
		return t ? this.roles().filter(r => (t.byRole.get(r) ?? 0) > 0) : [];
	});

	/** One point, one field per charted role (r0, r1, ...). */
	readonly chartData = computed<Record<string, string | number>[]>(() => {
		const t = this.chartTarget();
		if (!t) return [];
		const p: Record<string, string | number> = { x: t.label };
		this.chartRoles().forEach((role, i) => { p['r' + i] = t.byRole.get(role) ?? 0; });
		return [p];
	});

	/** One column series per role, bound as a whole so the series count can change with the data. */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const data = this.chartData();
		const roles = this.roles();
		return this.chartRoles().map((role, i) => ({
			type: 'Column',
			dataSource: data,
			xName: 'x',
			yName: 'r' + i,
			name: role,
			// Colour by the role's position in the SCOPE-wide order, so Player is the same colour
			// whichever event is charted.
			fill: this.palette[roles.indexOf(role) % this.palette.length],
			opacity: 0.85,
			// Fixed width: a proportional width lets one lone column fill the whole band.
			columnWidthInPixel: 36,
			cornerRadius: { topLeft: 3, topRight: 3 },
		}));
	});

	/** Tallest column — drives the y-axis step. */
	private readonly maxCell = computed(() => {
		const t = this.chartTarget();
		let max = 0;
		if (t) for (const n of t.byRole.values()) if (n > max) max = n;
		return max;
	});

	readonly primaryXAxis = computed(() => ({
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor, size: '11px', fontFamily: this.fontFamily },
	}));

	readonly primaryYAxis = computed(() => ({
		title: '',
		majorGridLines: { width: 0.5, color: this.borderColor, dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor, size: '11px', fontFamily: this.fontFamily },
		minimum: 0,
		// People are whole. Step by 1 while the tallest column is small; let ej2 pick above that,
		// with the n0 format so it never labels 1.2 people.
		interval: this.maxCell() <= 10 ? 1 : undefined,
		labelFormat: 'n0',
	}));

	readonly tooltipSettings = { enable: true, shared: true };

	readonly legendSettings = computed(() => ({
		visible: true,
		position: 'Top' as const,
		alignment: 'Far' as const,
		textStyle: { size: '11px', fontFamily: this.fontFamily },
		padding: 4,
		margin: { top: 0, bottom: 4, left: 0, right: 0 },
	}));

	readonly chartArea = { border: { width: 0 } };
	readonly margin = { left: 8, right: 8, top: 4, bottom: 4 };

	/** Event names in the table are links to the lens wherever there is a set to pick from. */
	readonly canDrill = computed(() => this.state.showEventPicker());

	readonly eventWord = computed(() => {
		const n = this.state.jobCount();
		return n === 1 ? 'this event' : `${n} live events`;
	});

	/**
	 * What to fetch: the shell's query minus the event lens, whenever the scope is resolved
	 * and non-empty; null otherwise. Emitting null clears the report, so nothing lingers
	 * under a scope it was not fetched with. The lens only moves the chart and the
	 * highlight, both derived from the same scope-wide answer — no refetch on a row click.
	 */
	private readonly fetchKey = computed<FetchKey | null>(() => {
		if (!this.state.canQuery()) return null;
		const q = this.state.query();
		return { scope: q.scope, windowDays: q.windowDays, excludeBots: q.excludeBots, clientId: q.clientId };
	});

	// Created here, in the injection context; subscribed in ngOnInit.
	private readonly fetchKey$ = toObservable(this.fetchKey);

	ngOnInit(): void {
		this.fetchKey$
			.pipe(
				map(q => q ? JSON.stringify(q) : null),
				distinctUntilChanged(),
				map(key => key ? (JSON.parse(key) as FetchKey) : null),
				filter((q): q is FetchKey => {
					if (q) return true;
					this.data.set(null);
					return false;
				}),
				switchMap(q => {
					this.isLoading.set(true);
					this.error.set(null);
					const params: Record<string, string | number | boolean> = {
						scope: q.scope, windowDays: q.windowDays, excludeBots: q.excludeBots,
					};
					if (q.clientId !== null) params['clientId'] = q.clientId;
					return this.http.get<UsersByRoleDto>(`${environment.apiUrl}/usage-analysis/users-by-role`, { params }).pipe(
						catchError(err => {
							this.error.set(err?.status === 403
								? 'That scope is not available to your role.'
								: 'Unable to load users by role.');
							return of(null);
						}),
					);
				}),
				takeUntilDestroyed(this.destroyRef),
			)
			.subscribe(d => {
				this.data.set(d);
				this.isLoading.set(false);
			});
	}

	isHighlighted(e: EventTotal): boolean {
		return e.jobId.toLowerCase() === this.highlightId();
	}

	cell(e: EventTotal, role: string): number {
		return e.byRole.get(role) ?? 0;
	}
}
