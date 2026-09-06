import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, filter, map, switchMap } from 'rxjs/operators';
import { catchError, of } from 'rxjs';
import { ChartAllModule, type IPointEventArgs, type SeriesModel } from '@syncfusion/ej2-angular-charts';

import { environment } from '@environments/environment';
import type { UsersByRoleDto, UsersByRoleRowDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import type { UsageQuery } from '../usage-analysis.models';

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

/**
 * Customer-facing roles in a FIXED order, so a role keeps its colour and its column
 * position in every cluster, every window, and every later report. Roles not listed
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

/** Most events charted before the rest fold into one "Other" cluster. The pivot always lists every event. */
const CHART_EVENT_CAP = 12;
const OTHER_KEY = '__other__';

interface EventTotal {
	readonly jobId: string;
	readonly jobName: string;
	readonly customer: number;
	readonly admin: number;
	readonly byRole: ReadonlyMap<string, number>;
}

/** One chart point: the event on x, one field per customer-facing role (r0, r1, ...). */
type ChartPoint = { readonly event: string; readonly jobId: string } & Record<string, string | number>;

/**
 * Report 01 — Users by Role. Distinct people who used the scoped live events in the
 * window, counted by registration, keyed by event, grouped by role.
 *
 * Clustered column chart: x = event, one column per role in each cluster. A Director
 * sees one cluster; Customer and All TSIC scope show one per live event, or one when
 * the shell's event lens is set. Same chart, every role. Clicking a cluster sets the
 * lens. Below it, a pivot mirrors the chart with every event, roles across, a Total,
 * and Admin & staff as a muted last column so setup clicks never pad the people count.
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

	private readonly rows = computed<readonly UsersByRoleRowDto[]>(() => this.data()?.rows ?? []);

	/** Customer-facing roles present, in ROLE_ORDER then alphabetical. */
	readonly roles = computed<readonly string[]>(() => {
		const present = new Set(this.rows().filter(r => !r.isAdmin).map(r => r.roleName));
		const ordered = ROLE_ORDER.filter(r => present.has(r));
		const rest = [...present].filter(r => !ROLE_ORDER.includes(r)).sort((a, b) => a.localeCompare(b));
		return [...ordered, ...rest];
	});

	/** Every event in the answer with its totals, busiest first. The pivot lists all of these. */
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

	/** Events the chart shows: all of them, or the top CHART_EVENT_CAP plus one folded "Other". */
	readonly chartEvents = computed<readonly EventTotal[]>(() => {
		const all = this.events();
		if (all.length <= CHART_EVENT_CAP) return all;
		const shown = all.slice(0, CHART_EVENT_CAP);
		const rest = all.slice(CHART_EVENT_CAP);
		const byRole = new Map<string, number>();
		for (const e of rest) {
			for (const [role, n] of e.byRole) byRole.set(role, (byRole.get(role) ?? 0) + n);
		}
		return [...shown, {
			jobId: OTHER_KEY,
			jobName: `Other (${rest.length} events)`,
			customer: rest.reduce((s, e) => s + e.customer, 0),
			admin: rest.reduce((s, e) => s + e.admin, 0),
			byRole,
		}];
	});

	readonly foldedCount = computed(() => Math.max(0, this.events().length - CHART_EVENT_CAP));

	readonly chartData = computed<readonly ChartPoint[]>(() => {
		const roles = this.roles();
		return this.chartEvents().map(e => {
			const p: Record<string, string | number> = { event: e.jobName, jobId: e.jobId };
			roles.forEach((role, i) => { p[`r${i}`] = e.byRole.get(role) ?? 0; });
			return p as ChartPoint;
		});
	});

	/** One column series per role, bound as a whole so the series count can change with the data. */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const data = this.chartData() as ChartPoint[];
		return this.roles().map((role, i) => ({
			type: 'Column',
			dataSource: data,
			xName: 'event',
			yName: `r${i}`,
			name: role,
			fill: this.palette[i % this.palette.length],
			opacity: 0.85,
			columnWidth: 0.7,
			cornerRadius: { topLeft: 3, topRight: 3 },
		}));
	});

	readonly chartHeight = computed(() => `${this.chartEvents().length > 6 ? 340 : 300}px`);

	readonly primaryXAxis = computed(() => ({
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor, size: '11px' },
		labelIntersectAction: 'Trim' as const,
		maximumLabelWidth: 110,
	}));

	readonly primaryYAxis = computed(() => ({
		title: '',
		majorGridLines: { width: 0.5, color: this.borderColor, dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor, size: '11px' },
		minimum: 0,
		interval: undefined as number | undefined,
	}));

	readonly tooltipSettings = { enable: true, shared: true };

	readonly legendSettings = {
		visible: true,
		position: 'Top' as const,
		alignment: 'Far' as const,
		textStyle: { size: '11px' },
		padding: 4,
		margin: { top: 0, bottom: 4, left: 0, right: 0 },
	};

	readonly chartArea = { border: { width: 0 } };
	readonly margin = { left: 8, right: 8, top: 4, bottom: 4 };

	/** Clicking a cluster is a shortcut for the shell's Event dropdown. "Other" is not an event. */
	readonly canDrill = computed(() => this.state.showEventPicker() && this.state.eventId() === null);

	readonly eventWord = computed(() => {
		if (this.state.eventId()) return this.state.eventLabel();
		const n = this.state.jobCount();
		return n === 1 ? 'this event' : `${n} live events`;
	});

	/**
	 * The query to run: the shell's query whenever the scope is resolved and non-empty,
	 * else null. Emitting null clears the report, so nothing lingers under a scope it was
	 * not fetched with.
	 */
	private readonly runnable = computed<UsageQuery | null>(() =>
		this.state.canQuery() ? this.state.query() : null);

	// Created here, in the injection context; subscribed in ngOnInit.
	private readonly runnable$ = toObservable(this.runnable);

	ngOnInit(): void {
		this.runnable$
			.pipe(
				map(q => q ? JSON.stringify(q) : null),
				distinctUntilChanged(),
				map(key => key ? (JSON.parse(key) as UsageQuery) : null),
				filter((q): q is UsageQuery => {
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
					if (q.eventId) params['eventId'] = q.eventId;
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

	onPointClick(args: IPointEventArgs): void {
		if (!this.canDrill()) return;
		const point = this.chartData()[args.pointIndex ?? -1];
		if (!point || point.jobId === OTHER_KEY) return;
		this.state.setEvent(point.jobId);
	}

	cell(e: EventTotal, role: string): number {
		return e.byRole.get(role) ?? 0;
	}
}
