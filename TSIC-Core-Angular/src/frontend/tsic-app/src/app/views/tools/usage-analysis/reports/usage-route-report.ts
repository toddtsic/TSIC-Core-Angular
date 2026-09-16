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

/** Most routes carried as table columns before the rest fold into Other. The chart shows the same set. */
const ROUTE_CAP = 12;
const OTHER = 'Other';

/**
 * The answer every requests-by-route report shares: counts per (event, route) and per route
 * scope-wide. `people` is optional — the public cannot be counted as people, signed-in users can.
 */
export interface RouteReportData {
	readonly rows: readonly { readonly jobId: string; readonly jobName: string; readonly route: string; readonly requests: number; readonly people?: number }[];
	readonly totals: readonly { readonly route: string; readonly requests: number; readonly people?: number }[];
	readonly totalRequests: number;
}

export interface RouteReport {
	readonly isEmpty: Signal<boolean>;
	readonly columns: Signal<readonly string[]>;
	/** Routes folded into Other, for the note. */
	readonly foldedCount: Signal<number>;
	readonly rows: Signal<readonly UsagePivotRow[]>;
	readonly allRow: Signal<UsagePivotRow | null>;
	readonly chartRow: Signal<UsagePivotRow | null>;
	readonly chartSeries: Signal<SeriesModel[]>;
	readonly chartHeight: Signal<string>;
	readonly primaryXAxis: Signal<object>;
	readonly primaryYAxis: Signal<object>;
	readonly tooltip: object;
	readonly chartMargin: object;
}

/**
 * The column maths and chart for a requests-by-route report. Call from a field initializer
 * (it injects). Columns are the busiest routes across the scope (capped, then Other), ranked
 * scope-wide so a route keeps its column and colour whichever event is charted; rows are the
 * events, busiest first; the chart is horizontal bars, busiest on top, for the Event
 * dropdown's pick or the whole scope at All.
 *
 * Where the answer carries people, the chart's tooltip says how many people made a route's
 * requests. Other has no people figure: distinct people do not add across routes.
 */
export function useRouteReport(data: Signal<RouteReportData | null>): RouteReport {
	const state = inject(UsageAnalysisStateService);
	const palette = resolvePalette();
	const theme = resolveChartTheme();

	const isEmpty = computed(() => (data()?.rows.length ?? 0) === 0);

	const columns = computed<readonly string[]>(() => {
		const ranked = [...(data()?.totals ?? [])]
			.sort((a, b) => b.requests - a.requests || a.route.localeCompare(b.route))
			.map(t => t.route);
		return ranked.length <= ROUTE_CAP ? ranked : [...ranked.slice(0, ROUTE_CAP), OTHER];
	});

	const foldedCount = computed(() => Math.max(0, (data()?.totals.length ?? 0) - ROUTE_CAP));

	const columnFor = (route: string): string => columns().includes(route) ? route : OTHER;

	const rows = computed<readonly UsagePivotRow[]>(() => {
		const byJob = new Map<string, { name: string; total: number; cells: Map<string, number> }>();
		for (const r of data()?.rows ?? []) {
			let e = byJob.get(r.jobId);
			if (!e) {
				e = { name: r.jobName, total: 0, cells: new Map() };
				byJob.set(r.jobId, e);
			}
			e.total += r.requests;
			const col = columnFor(r.route);
			e.cells.set(col, (e.cells.get(col) ?? 0) + r.requests);
		}
		return [...byJob.entries()]
			.map(([id, e]) => ({ id, ...e }))
			.sort((a, b) => b.total - a.total || a.name.localeCompare(b.name));
	});

	/** The whole scope summed. Requests are additive, so this is the plain sum. */
	const allRow = computed<UsagePivotRow | null>(() => {
		const d = data();
		if (!d) return null;
		const cells = new Map<string, number>();
		for (const t of d.totals) {
			const col = columnFor(t.route);
			cells.set(col, (cells.get(col) ?? 0) + t.requests);
		}
		return { id: '', name: allRowName(state.jobCount(), rows()), total: d.totalRequests, cells };
	});

	const chartRow = computed(() => chartRowFor(state.eventId(), rows(), allRow()));

	/** People per route for the charted row: the lens event's rows, or the scope totals at All. */
	const chartPeople = computed<ReadonlyMap<string, number>>(() => {
		const d = data();
		const row = chartRow();
		const people = new Map<string, number>();
		if (!d || !row) return people;
		const source = row.id === ''
			? d.totals
			: d.rows.filter(r => r.jobId.toLowerCase() === row.id.toLowerCase());
		for (const r of source) {
			if (r.people !== undefined) people.set(r.route, r.people);
		}
		return people;
	});

	/** One bar per route with requests, busiest first. Colour by scope-wide column position. */
	const chartPoints = computed(() => {
		const row = chartRow();
		if (!row) return [];
		const people = chartPeople();
		return columns()
			.map((route, i) => {
				const y = row.cells.get(route) ?? 0;
				const p = people.get(route);
				const who = p === undefined ? '' : ` · ${p.toLocaleString()} ${p === 1 ? 'person' : 'people'}`;
				return { x: route, y, color: palette[i % palette.length], tip: `${route}: ${y.toLocaleString()} requests${who}` };
			})
			.filter(p => p.y > 0)
			.sort((a, b) => b.y - a.y);
	});

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

	/** One row of bars per route, so bars never squash as routes grow. */
	const chartHeight = computed(() => `${Math.max(120, 40 + chartPoints().length * 30)}px`);

	const maxCell = computed(() => chartPoints().reduce((m, p) => Math.max(m, p.y), 0));

	return {
		isEmpty,
		columns,
		foldedCount,
		rows,
		allRow,
		chartRow,
		chartSeries,
		chartHeight,
		// Category axis on a Bar runs bottom-up; inverted so the busiest route is on top.
		primaryXAxis: computed(() => categoryAxis(theme, { isInversed: true, maximumLabelWidth: 240 })),
		primaryYAxis: computed(() => countAxis(theme, maxCell())),
		tooltip: { enable: true, format: '${point.tooltip}' },
		chartMargin: { left: 8, right: 24, top: 4, bottom: 4 },
	};
}
