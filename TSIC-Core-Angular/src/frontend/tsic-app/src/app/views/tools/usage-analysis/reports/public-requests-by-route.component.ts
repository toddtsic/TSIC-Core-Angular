import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import type { SeriesModel } from '@syncfusion/ej2-angular-charts';

import type { PublicRequestsByRouteDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import {
	allRowName,
	categoryAxis,
	chartRowFor,
	countAxis,
	outerDataLabel,
	resolveChartTheme,
	resolvePalette,
	useUsageReportFetch,
	type UsagePivotRow,
	type UsageTile,
} from './usage-report-shared';

/** Most routes carried as table columns before the rest fold into Other. The chart shows the same set. */
const ROUTE_CAP = 12;
const OTHER = 'Other';

/**
 * Report 02 — Public Requests by Route. What the public asked for without signing in:
 * anonymous requests against the scoped live events in the window, keyed by event,
 * grouped by the API route they hit. Columns are the busiest routes across the scope
 * (capped, then Other); the chart is horizontal bars, busiest on top, for the Event
 * dropdown's pick or the scope summed at All.
 *
 * Same layout as 01 so the eye learns once — but a different UNIT. 01 counts
 * registrations; this counts requests, because nothing in the log can turn an anonymous
 * request into a visitor and the report never pretends otherwise.
 *
 * Routes are Controller/Action — the API endpoint, not the page. One public page fires
 * several endpoints, and every page load fires the chrome (job metadata, theme, nav)
 * signed in or not. Shown raw on purpose until the data has been looked at; an
 * exclusion list or a content-only toggle is a decision for after that.
 */
@Component({
	selector: 'app-usage-public-requests-by-route',
	standalone: true,
	imports: [UsagePivotReportComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './public-requests-by-route.component.html',
})
export class PublicRequestsByRouteComponent {
	private readonly state = inject(UsageAnalysisStateService);
	readonly fetch = useUsageReportFetch<PublicRequestsByRouteDto>('public-requests-by-route', 'Unable to load public requests.');

	private readonly palette = resolvePalette();
	private readonly theme = resolveChartTheme();

	readonly isEmpty = computed(() => (this.fetch.data()?.rows.length ?? 0) === 0);

	/**
	 * Table columns: the busiest routes across the scope, up to ROUTE_CAP, then Other.
	 * Ranked scope-wide so a route keeps its column and its colour whichever event is charted.
	 */
	readonly columns = computed<readonly string[]>(() => {
		const ranked = [...(this.fetch.data()?.totals ?? [])]
			.sort((a, b) => b.requests - a.requests || a.route.localeCompare(b.route))
			.map(t => t.route);
		return ranked.length <= ROUTE_CAP ? ranked : [...ranked.slice(0, ROUTE_CAP), OTHER];
	});

	/** Routes folded into Other, for the note. */
	readonly foldedCount = computed(() => Math.max(0, (this.fetch.data()?.totals.length ?? 0) - ROUTE_CAP));

	private columnFor(route: string): string {
		return this.columns().includes(route) ? route : OTHER;
	}

	/** Every event in the answer, busiest first. */
	readonly rows = computed<readonly UsagePivotRow[]>(() => {
		const byJob = new Map<string, { name: string; total: number; cells: Map<string, number> }>();
		for (const r of this.fetch.data()?.rows ?? []) {
			let e = byJob.get(r.jobId);
			if (!e) {
				e = { name: r.jobName, total: 0, cells: new Map() };
				byJob.set(r.jobId, e);
			}
			e.total += r.requests;
			const col = this.columnFor(r.route);
			e.cells.set(col, (e.cells.get(col) ?? 0) + r.requests);
		}
		return [...byJob.entries()]
			.map(([id, e]) => ({ id, ...e }))
			.sort((a, b) => b.total - a.total || a.name.localeCompare(b.name));
	});

	/** The whole scope summed. Requests are additive, so this is the plain sum. */
	readonly allRow = computed<UsagePivotRow | null>(() => {
		const d = this.fetch.data();
		if (!d) return null;
		const cells = new Map<string, number>();
		for (const t of d.totals) {
			const col = this.columnFor(t.route);
			cells.set(col, (cells.get(col) ?? 0) + t.requests);
		}
		return { id: '', name: allRowName(this.state.jobCount(), this.rows()), total: d.totalRequests, cells };
	});

	readonly tiles = computed<readonly UsageTile[]>(() => {
		const d = this.fetch.data();
		const word = this.state.jobCount() === 1 ? 'this event' : `${this.state.jobCount()} live events`;
		return [
			{ value: d?.totalRequests ?? 0, label: `Public requests on ${word}`, primary: true },
			{ value: d?.failedRequests ?? 0, label: 'Failed (4xx / 5xx), not counted below' },
		];
	});

	readonly chartRow = computed(() => chartRowFor(this.state.eventId(), this.rows(), this.allRow()));

	/** One bar per route with requests, busiest first. Colour by scope-wide column position. */
	private readonly chartPoints = computed(() => {
		const row = this.chartRow();
		if (!row) return [];
		return this.columns()
			.map((route, i) => ({ x: route, y: row.cells.get(route) ?? 0, color: this.palette[i % this.palette.length] }))
			.filter(p => p.y > 0)
			.sort((a, b) => b.y - a.y);
	});

	/** One series, one bar per route, colour from the point. */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const points = this.chartPoints();
		if (points.length === 0) return [];
		return [{
			type: 'Bar',
			dataSource: points,
			xName: 'x',
			yName: 'y',
			pointColorMapping: 'color',
			opacity: 0.85,
			columnWidthInPixel: 18,
			cornerRadius: { topRight: 3, bottomRight: 3 },
			marker: outerDataLabel(this.theme),
		}];
	});

	/** One row of bars per route, so bars never squash as routes grow. */
	readonly chartHeight = computed(() => `${Math.max(120, 40 + this.chartPoints().length * 30)}px`);

	private readonly maxCell = computed(() => this.chartPoints().reduce((m, p) => Math.max(m, p.y), 0));

	// Category axis on a Bar runs bottom-up; inverted so the busiest route is on top.
	readonly primaryXAxis = computed(() => categoryAxis(this.theme, { isInversed: true, maximumLabelWidth: 240 }));
	readonly primaryYAxis = computed(() => countAxis(this.theme, this.maxCell()));
	readonly tooltip = { enable: true, format: '${point.x}: ${point.y}' };
	readonly chartMargin = { left: 8, right: 24, top: 4, bottom: 4 };
}
