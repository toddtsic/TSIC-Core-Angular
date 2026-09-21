import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import type { SeriesModel } from '@syncfusion/ej2-angular-charts';

import type { ThirdPartyExportsDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import {
	categoryAxis,
	countAxis,
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
 * Months run along the x axis and each event is its own LINE (Todd, 2026-09-20), so the
 * report reads as a trend: when an agency started pulling, and whether it still is.
 * Successes only, because a refused run exported nothing. The dated log beneath is the
 * report's real answer: a count says data left, a row says who took it and on what day.
 *
 * Columns are the months that saw an export anywhere in the window, "yyyy/MM", oldest
 * first. Months nobody exported in are not columns: a year-long window would otherwise be
 * mostly empty. Within the months that did happen, an event that took nothing is 0 and its
 * line drops to the axis — the month is real and so is the nothing.
 *
 * Opens on a 1y window (`defaultWindowDays`). At the page's 7d default this report is
 * empty almost every day of the year, which reads as "never used" rather than "not this
 * week".
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
			// ?? [] guards an API older than this page: the field is required by the contract,
			// but a half-rendered view with stale tiles beside an empty chart is a worse
			// failure than an honest empty one.
			cells: new Map<string, number>((r.monthCounts ?? []).map(m => [m.month, m.exports])),
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
		return `successful exports per month, one line per event${tail}`;
	});

	/**
	 * Time on the x axis, one line per event. A month an event did not export in is 0 and
	 * its line drops to the axis rather than breaking — the same honesty rule report 03
	 * keeps, and here it is the true reading: the month happened and that event took
	 * nothing. Only a month NOBODY exported in is absent, and then it is not a column.
	 */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const months = this.columns();
		const rows = this.rows();
		if (rows.length === 0 || months.length === 0) return [];
		const data = months.map(m => {
			const point: Record<string, string | number> = { x: m };
			rows.forEach((r, i) => { point['j' + i] = r.cells.get(m) ?? 0; });
			return point;
		});
		return rows.map((r, i) => ({
			type: 'Line',
			dataSource: data,
			xName: 'x',
			yName: 'j' + i,
			name: r.name,
			fill: this.palette[i % this.palette.length],
			width: 2,
			marker: { visible: true, width: 7, height: 7, shape: 'Circle' },
		}));
	});

	/** The tallest single bar, which is a month, not an event total. */
	private readonly maxExports = computed(() =>
		Math.max(0, ...this.rows().flatMap(r => [...r.cells.values()])));

	/**
	 * Months, so the labels are short and upright. Putting time here is also what gets the
	 * event names off the axis: "American Select Lacrosse:New Jersey 2026" is unreadable
	 * rotated under a bar, and belongs in the legend, where it has a whole line to itself.
	 */
	readonly primaryXAxis = computed(() => categoryAxis(this.theme));

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
