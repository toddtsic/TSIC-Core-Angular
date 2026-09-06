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
 * Every role by its exact name, in a FIXED order, so a role keeps its colour and its
 * column position in every chart, every window, and every later report: the customer's
 * people first, then the roles that run an event. No bucket — how much Directors are
 * using is vital information (Todd, 2026-09-06), and "Staff" is itself a role. Roles not
 * listed follow alphabetically.
 */
const ROLE_ORDER: readonly string[] = [
	'Family', 'Player', 'Staff', 'Club Rep', 'Unassigned Adult', 'Referee', 'Scorer', 'Recruiter', 'Guest',
	'Director', 'SuperDirector', 'Superuser', 'Ref Assignor', 'Store Admin', 'STPAdmin', 'ApiAuthorized',
];

/** The role whose usage gets its own tile. Exact AspNetRoles name. */
const DIRECTOR_ROLE = 'Director';

/** Series palette, by ROLE_ORDER position. Palette-responsive: resolved from :root at init. */
const PALETTE_VARS: readonly [string, string][] = [
	['--bs-primary', '#0d6efd'],
	['--brand-accent', '#6f42c1'],
	['--bs-success', '#198754'],
	['--bs-warning', '#ffc107'],
	['--bs-info', '#0dcaf0'],
	['--bs-danger', '#dc3545'],
	['--bs-secondary', '#6c757d'],
	['--bs-indigo', '#6610f2'],
	['--bs-pink', '#d63384'],
	['--bs-orange', '#fd7e14'],
	['--bs-teal', '#20c997'],
	['--bs-dark', '#212529'],
];

/** One table row: an event, or the All rollup (jobId empty). */
interface EventTotal {
	readonly jobId: string;
	readonly jobName: string;
	/** Distinct people across every role. */
	readonly total: number;
	readonly byRole: ReadonlyMap<string, number>;
}

/** What the chart draws: one label, one count per role. An event, or the whole scope rolled up. */
interface ChartTarget {
	readonly label: string;
	readonly byRole: ReadonlyMap<string, number>;
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
 *  - The CHART follows the Event dropdown. "All events" (the default): the whole scope
 *    rolled up into one cluster, from the server's scope-wide distinct-registration
 *    counts. A registration is per event, so a user in two events counts in each; the
 *    rollup only dedups a registration that made requests about more than one event. One event picked: that event's roles, one column each — a Director's one
 *    event and a Superuser's chosen one are the same chart. Twelve clusters side by side
 *    were unreadable; one is not. Counts sit above the columns.
 *  - The TABLE is the whole scope: every live event, every role across by name, and a
 *    Total. Where All is a choice, an All row (the same distinct rollup) leads the table.
 *    The charted row is highlighted. Clicking a row name moves the dropdown and the
 *    chart to it.
 *
 * Every role is first-class. The server still flags the admin tier on each row, but the
 * page does not fold it away: Director usage is one of the vital numbers here, so it is a
 * named column like any other, plus its own tile.
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
	private readonly labelColor = cssVar('--brand-text', '#212529');
	// ej2 draws SVG text in its own default face; hand it the page font so the axes match the table.
	private readonly fontFamily = cssVar('--bs-body-font-family', 'system-ui, sans-serif');

	private readonly rows = computed<readonly UsersByRoleRowDto[]>(() => this.data()?.rows ?? []);

	/** Every role present anywhere in the scope, in ROLE_ORDER then alphabetical. */
	readonly roles = computed<readonly string[]>(() => {
		const present = new Set(this.rows().map(r => r.roleName));
		const ordered = ROLE_ORDER.filter(r => present.has(r));
		const rest = [...present].filter(r => !ROLE_ORDER.includes(r)).sort((a, b) => a.localeCompare(b));
		return [...ordered, ...rest];
	});

	/** Every event in the answer with its totals, busiest first. The table lists all of these. */
	readonly events = computed<readonly EventTotal[]>(() => {
		const byJob = new Map<string, { jobName: string; total: number; byRole: Map<string, number> }>();
		for (const r of this.rows()) {
			let e = byJob.get(r.jobId);
			if (!e) {
				e = { jobName: r.jobName, total: 0, byRole: new Map() };
				byJob.set(r.jobId, e);
			}
			// A registration holds ONE role, so summing role counts within an event is still distinct people.
			e.total += r.users;
			e.byRole.set(r.roleName, (e.byRole.get(r.roleName) ?? 0) + r.users);
		}
		return [...byJob.entries()]
			.map(([jobId, e]) => ({ jobId, ...e }))
			.sort((a, b) => b.total - a.total || a.jobName.localeCompare(b.jobName));
	});

	/**
	 * The whole scope rolled up, from the server's distinct-per-role totals: distinct
	 * REGISTRATIONS per role and overall. Registrations are per event, so this is close to
	 * the column sums and only differs where one registration touched several events.
	 */
	readonly allRow = computed<EventTotal | null>(() => {
		const d = this.data();
		if (!d) return null;
		const byRole = new Map<string, number>();
		for (const t of d.totals) byRole.set(t.roleName, (byRole.get(t.roleName) ?? 0) + t.users);
		const n = this.state.jobCount();
		const jobName = n === 1 ? (this.events()[0]?.jobName ?? 'This event') : `All ${n} live events`;
		// The two server totals are each distinct, and a registration is in exactly one tier, so their sum is distinct too.
		return { jobId: '', jobName, total: d.customerUsers + d.adminUsers, byRole };
	});

	/** Distinct registrations across the scope, every role. */
	readonly totalPeople = computed(() => this.allRow()?.total ?? 0);

	/** Distinct Director registrations that used anything in the scope in the window — vital, so it gets a tile. */
	readonly directors = computed(() => this.allRow()?.byRole.get(DIRECTOR_ROLE) ?? 0);

	/** The All row leads the table wherever All is a choice — wherever there is a set of events to pick from. */
	readonly showAllRow = computed(() => this.state.showEventPicker());

	/** Whether the chart is the scope rollup rather than one event. */
	readonly isRollup = computed(() => this.state.eventId() === null);

	/** The lens event's totals, when a lens is set and the event had any users at all. */
	private readonly lensEvent = computed<EventTotal | null>(() => {
		const id = this.state.eventId()?.toLowerCase();
		if (!id) return null;
		return this.events().find(e => e.jobId.toLowerCase() === id) ?? null;
	});

	/**
	 * What the chart draws. Lens set: that event. No lens: the rollup. Null when a lens is
	 * set but the event had no users.
	 */
	readonly chartTarget = computed<ChartTarget | null>(() => {
		if (this.state.eventId()) {
			const e = this.lensEvent();
			return e ? { label: e.jobName, byRole: e.byRole } : null;
		}
		const all = this.allRow();
		return all ? { label: all.jobName, byRole: all.byRole } : null;
	});

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
			// The count sits ABOVE each column (Outer): a 36px column holding 2 people is too short
			// to carry a label inside, and a reader should never need the tooltip for the number.
			marker: {
				dataLabel: {
					visible: true,
					position: 'Outer',
					format: 'n0',
					font: { color: this.labelColor, size: '11px', fontWeight: '600', fontFamily: this.fontFamily },
				},
			},
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

	/** The one highlighted event row is the charted one: the lens event. (The All row highlights itself at All.) */
	isHighlighted(e: EventTotal): boolean {
		const lens = this.state.eventId();
		return lens !== null && e.jobId.toLowerCase() === lens.toLowerCase();
	}

	cell(e: EventTotal, role: string): number {
		return e.byRole.get(role) ?? 0;
	}
}
