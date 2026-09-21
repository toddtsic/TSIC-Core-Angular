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
 * One bar per event, the bar's height the number of successful exports (Todd,
 * 2026-09-20: count successes only — a refused run exported nothing). The dated log
 * beneath is the report's real answer: a count says data left, a row says who took it and
 * on what day.
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

	/** No per-event columns: the table is the event and its export count, and the Total column carries it. */
	readonly columns: readonly string[] = [];

	readonly rows = computed<readonly UsagePivotRow[]>(() =>
		(this.fetch.data()?.rows ?? []).map(r => ({
			id: r.jobId,
			name: r.jobName,
			total: r.exports,
			cells: new Map<string, number>(),
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
		return `successful exports per event${tail}`;
	});

	/** One bar per event. Busiest first, as the server ordered them. */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const rows = this.rows();
		if (rows.length === 0) return [];
		return [{
			type: 'Column',
			dataSource: rows.map(r => ({ x: r.name, y: r.total })),
			xName: 'x',
			yName: 'y',
			name: 'Exports',
			fill: this.palette[0],
			columnWidth: 0.6,
			cornerRadius: { topLeft: 3, topRight: 3 },
			...outerDataLabel(this.theme),
		}];
	});

	private readonly maxExports = computed(() => Math.max(0, ...this.rows().map(r => r.total)));

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
	readonly tooltip = { enable: true };
}
