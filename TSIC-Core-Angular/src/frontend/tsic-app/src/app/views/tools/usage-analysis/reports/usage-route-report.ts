import { type Signal, computed, inject } from '@angular/core';
import type { SeriesModel } from '@syncfusion/ej2-angular-charts';

import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import {
	allRowName,
	categoryAxis,
	chartRowFor,
	countAxis,
	outerDataLabel,
	resolveChartTheme,
	resolvePalette,
	type UsagePivotRow,
} from './usage-report-shared';

/**
 * Bars the chart draws. A chart compares sizes, and past a couple of dozen routes there is
 * nothing left to compare — the tail is bars a pixel wide. The full list is the route table.
 * No Other bar (Todd, 2026-09-16): it is not a route, and summed it outranked every real one.
 */
const CHART_CAP = 25;

/** The events table's one count column besides Total. */
const FAILED = 'Failed';

/**
 * The answer every requests-by-route report shares: counts per (event, route) and per route
 * scope-wide, page shell already taken out server-side. `people` is optional — the public
 * cannot be counted as people, signed-in users can.
 */
export interface RouteReportData {
	readonly rows: readonly { readonly jobId: string; readonly jobName: string; readonly route: string; readonly requests: number; readonly failedRequests: number; readonly people?: number }[];
	readonly totals: readonly { readonly route: string; readonly requests: number; readonly failedRequests: number; readonly people?: number }[];
	readonly totalRequests: number;
	readonly failedRequests: number;
}

/** One line of the route table: a route under the current event lens. */
export interface UsageRouteLine {
	readonly route: string;
	readonly requests: number;
	readonly failed: number;
	/** Absent where the report cannot count people (public). */
	readonly people?: number;
}

export interface RouteReport {
	readonly isEmpty: Signal<boolean>;
	/** The events table's columns (Total is added by the layout). */
	readonly columns: readonly string[];
	readonly rows: Signal<readonly UsagePivotRow[]>;
	readonly allRow: Signal<UsagePivotRow | null>;
	readonly chartRow: Signal<UsagePivotRow | null>;
	/** Every route under the event lens — the route table's rows. */
	readonly routes: Signal<readonly UsageRouteLine[]>;
	/** Routes with requests under the lens that the chart does not draw. */
	readonly routesNotCharted: Signal<number>;
	readonly chartSeries: Signal<SeriesModel[]>;
	readonly chartHeight: Signal<string>;
	readonly primaryXAxis: Signal<object>;
	readonly primaryYAxis: Signal<object>;
	readonly tooltip: object;
	readonly chartMargin: object;
}

/** The chart's subtitle, saying out loud when the chart is not the whole list. */
export function routeChartSubtitle(what: string, notCharted: number): string {
	const base = `${what} by API route, succeeded only`;
	return notCharted > 0 ? `${base} · busiest ${CHART_CAP} shown, ${notCharted.toLocaleString()} more in the table below` : base;
}

/**
 * The maths and chart for a requests-by-route report. Call from a field initializer (it
 * injects). The events table is events × (Failed, Total) — route columns do not scale past a
 * dozen — and picking an event moves the chart and the route table together. The chart is
 * the busiest CHART_CAP routes under the lens, horizontal bars busiest on top; the route
 * table carries every route. A route's colour is its scope-wide rank, so it keeps its colour
 * whichever event is charted.
 */
export function useRouteReport(data: Signal<RouteReportData | null>): RouteReport {
	const state = inject(UsageAnalysisStateService);
	const palette = resolvePalette();
	const theme = resolveChartTheme();

	const isEmpty = computed(() => (data()?.rows.length ?? 0) === 0);

	/** Scope-wide rank of each route, for colour. */
	const rankByRoute = computed(() => {
		const ranked = [...(data()?.totals ?? [])]
			.sort((a, b) => b.requests - a.requests || a.route.localeCompare(b.route));
		return new Map(ranked.map((t, i) => [t.route, i]));
	});

	const rows = computed<readonly UsagePivotRow[]>(() => {
		const byJob = new Map<string, { name: string; total: number; failed: number }>();
		for (const r of data()?.rows ?? []) {
			let e = byJob.get(r.jobId);
			if (!e) {
				e = { name: r.jobName, total: 0, failed: 0 };
				byJob.set(r.jobId, e);
			}
			e.total += r.requests;
			e.failed += r.failedRequests;
		}
		return [...byJob.entries()]
			.map(([id, e]) => ({ id, name: e.name, total: e.total, cells: new Map([[FAILED, e.failed]]) }))
			.sort((a, b) => b.total - a.total || a.name.localeCompare(b.name));
	});

	const allRow = computed<UsagePivotRow | null>(() => {
		const d = data();
		if (!d) return null;
		return {
			id: '',
			name: allRowName(state.jobCount(), rows()),
			total: d.totalRequests,
			cells: new Map([[FAILED, d.failedRequests]]),
		};
	});

	const chartRow = computed(() => chartRowFor(state.eventId(), rows(), allRow()));

	/** Every route under the lens: the lens event's rows, or the scope totals at All. Busiest first. */
	const routes = computed<readonly UsageRouteLine[]>(() => {
		const d = data();
		const row = chartRow();
		if (!d || !row) return [];
		const source = row.id === ''
			? d.totals
			: d.rows.filter(r => r.jobId.toLowerCase() === row.id.toLowerCase());
		return source
			.map(r => ({ route: r.route, requests: r.requests, failed: r.failedRequests, people: r.people }))
			.sort((a, b) => b.requests - a.requests || a.route.localeCompare(b.route));
	});

	const chartPoints = computed(() => {
		const ranks = rankByRoute();
		return routes()
			.filter(r => r.requests > 0)
			.slice(0, CHART_CAP)
			.map(r => {
				const who = r.people === undefined ? '' : ` · ${r.people.toLocaleString()} ${r.people === 1 ? 'person' : 'people'}`;
				return {
					x: r.route,
					y: r.requests,
					color: palette[(ranks.get(r.route) ?? 0) % palette.length],
					tip: `${r.route}: ${r.requests.toLocaleString()} requests${who}`,
				};
			});
	});

	const routesNotCharted = computed(() =>
		Math.max(0, routes().filter(r => r.requests > 0).length - chartPoints().length));

	const chartSeries = computed<SeriesModel[]>(() => {
		const points = chartPoints();
		if (points.length === 0) return [];
		return [{
			type: 'Bar',
			dataSource: points,
			xName: 'x',
			yName: 'y',
			pointColorMapping: 'color',
			tooltipMappingName: 'tip',
			opacity: 0.85,
			columnWidthInPixel: 18,
			cornerRadius: { topRight: 3, bottomRight: 3 },
			marker: outerDataLabel(theme),
		}];
	});

	/** One row of bars per route, so bars never squash. */
	const chartHeight = computed(() => `${Math.max(120, 40 + chartPoints().length * 30)}px`);

	const maxCell = computed(() => chartPoints().reduce((m, p) => Math.max(m, p.y), 0));

	return {
		isEmpty,
		columns: [FAILED],
		rows,
		allRow,
		chartRow,
		routes,
		routesNotCharted,
		chartSeries,
		chartHeight,
		// Category axis on a Bar runs bottom-up; inverted so the busiest route is on top.
		primaryXAxis: computed(() => categoryAxis(theme, { isInversed: true, maximumLabelWidth: 240 })),
		primaryYAxis: computed(() => countAxis(theme, maxCell())),
		tooltip: { enable: true, format: '${point.tooltip}' },
		chartMargin: { left: 8, right: 24, top: 4, bottom: 4 },
	};
}
