import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import type { SeriesModel } from '@syncfusion/ej2-angular-charts';

import type { ThirdPartyExportsDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import {
	categoryAxis,
	countAxis,
	outerDataLabel,
	resolveChartTheme,
	resolvePalette,
	useUsageReportFetch,
	type UsagePivotRow,
	type UsageTile,
} from './usage-report-shared';

/**
 * Third-Party Roster Exports — who has taken roster data out of an event, from which
 * event, and when.
 *
 * The EVENT is the group and its months are the bars inside it (Todd, 2026-09-20), each
 * bar a month's successful exports — successes only, because a refused run exported
 * nothing. The dated log beneath is the report's real answer: a count says data left, a
 * row says who took it and on what day.
 *
 * Columns are the months that saw an export anywhere in the window, "yyyy/MM", oldest
 * first. Months nobody exported in are not columns: a year-long window would otherwise be
 * mostly empty. A month one event sat out is a gap in its group, never a zero bar with a
 * "0" printed over it.
 *
 * SOURCE IS NOT THE USAGE LOG. Every other report on this page reads logs.AppUsage; this
 * one reads Jobs.JobReportExportHistory, written by the export endpoint itself on each
 * successful run. The log would have been the wrong source and quietly so: it begins
 * 2026-09-04, the export shipped 2026-08-06, and it admits a row only when the request
 * carried a client tag. On this box the history holds eight runs and the log holds one.
 *
 * Rows are events but `rowsAreEvents` is false, which is what puts the events on the x
 * axis: the layout's event mode charts ONE row (the lens event) with its columns as bars,
 * and this report wants every event side by side. The trade is the All row and row
 * drilling, neither of which a single-column report has any use for — so the Event
 * dropdown narrows the fetch instead, the same as it does on the bucketed reports.
 *
 * An event that exported nothing is not a bar and not a row. A zero here would read as a
 * finding when it is usually just an event that released no age groups.
 */
@Component({
	selector: 'app-usage-third-party-exports',
	standalone: true,
	imports: [UsagePivotReportComponent, DatePipe],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './third-party-exports.component.html',
	styleUrl: './third-party-exports.component.scss',
})
export class ThirdPartyExportsComponent {
	readonly state = inject(UsageAnalysisStateService);

	readonly fetch = useUsageReportFetch<ThirdPartyExportsDto>(
		'third-party-exports',
		'Unable to load third-party exports.',
		{ lensNarrowsFetch: true },
	);

	private readonly palette = resolvePalette();
	private readonly theme = resolveChartTheme();

	readonly isEmpty = computed(() => (this.fetch.data()?.rows.length ?? 0) === 0);

	/** The months that saw an export, oldest first: the table's columns and the bars inside each event's group. */
	readonly columns = computed<readonly string[]>(() => this.fetch.data()?.months ?? []);

	readonly rows = computed<readonly UsagePivotRow[]>(() =>
		(this.fetch.data()?.rows ?? []).map(r => ({
			id: r.jobId,
			name: r.jobName,
			total: r.exports,
			cells: new Map<string, number>(r.monthCounts.map(m => [m.month, m.exports])),
		})));

	readonly log = computed(() => this.fetch.data()?.log ?? []);

	readonly tiles = computed<readonly UsageTile[]>(() => {
		const d = this.fetch.data();
		if (!d) return [];
		return [
			{ value: d.totalExports, label: d.totalExports === 1 ? 'Export' : 'Exports', primary: true },
			{ value: d.eventsExported, label: d.eventsExported === 1 ? 'Event exported' : 'Events exported' },
			{ value: d.exporters, label: d.exporters === 1 ? 'Login' : 'Logins' },
		];
	});

	readonly chartTitle = computed(() => {
		if (this.state.eventId()) return this.state.eventLabel();
		const n = this.fetch.data()?.eventsExported ?? 0;
		return n === 1 ? '1 event exported' : `${n} events exported`;
	});

	readonly chartSubtitle = computed(() => {
		const d = this.fetch.data();
		if (!d) return '';
		const quiet = d.jobCount - d.eventsExported;
		const tail = quiet > 0 ? ` — ${quiet} live ${quiet === 1 ? 'event' : 'events'} exported nothing and ${quiet === 1 ? 'is' : 'are'} not drawn` : '';
		return `successful exports per event, by month${tail}`;
	});

	/**
	 * The event is the group, its months are the bars inside it. A month the event did not
	 * export in is null rather than 0 — ej2 draws no column and, more to the point, prints
	 * no "0" label over an empty slot.
	 */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const months = this.columns();
		const rows = this.rows();
		if (rows.length === 0 || months.length === 0) return [];
		const data = rows.map(r => {
			const point: Record<string, string | number | null> = { x: r.name };
			months.forEach((m, i) => { point['m' + i] = r.cells.get(m) ?? null; });
			return point;
		});
		return months.map((m, i) => ({
			type: 'Column',
			dataSource: data,
			xName: 'x',
			yName: 'm' + i,
			name: m,
			fill: this.palette[i % this.palette.length],
			opacity: 0.85,
			// Fixed width: a proportional width lets one lone month fill an event's whole band.
			columnWidthInPixel: 28,
			cornerRadius: { topLeft: 3, topRight: 3 },
			emptyPointSettings: { mode: 'Gap' },
			// ej2 reads the label off the MARKER, not the series — at series level it silently does nothing.
			marker: outerDataLabel(this.theme),
		}));
	});

	/** The tallest single bar, which is a month, not an event total. */
	private readonly maxExports = computed(() =>
		Math.max(0, ...this.rows().flatMap(r => [...r.cells.values()])));

	/**
	 * Event names are long ("American Select Lacrosse:New Jersey 2026"). Rotated and given
	 * room rather than truncated: the whole point of the axis is knowing WHICH event.
	 */
	readonly primaryXAxis = computed(() => categoryAxis(this.theme, {
		labelIntersectAction: 'Rotate45',
		labelStyle: { color: this.theme.muted, size: '11px', fontFamily: this.theme.fontFamily },
	}));

	readonly primaryYAxis = computed(() => countAxis(this.theme, this.maxExports()));

	readonly chartMargin = { left: 8, right: 8, top: 4, bottom: 8 };

	/** Months are series now, so the legend is what names them. */
	readonly legendSettings = {
		visible: true,
		position: 'Top' as const,
		alignment: 'Far' as const,
		textStyle: { size: '11px', fontFamily: this.theme.fontFamily },
		padding: 4,
		margin: { top: 0, bottom: 4, left: 0, right: 0 },
	};

	readonly tooltip = { enable: true, shared: true };
}
