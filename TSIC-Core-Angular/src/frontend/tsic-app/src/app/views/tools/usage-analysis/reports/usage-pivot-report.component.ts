import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { ChartAllModule, type SeriesModel } from '@syncfusion/ej2-angular-charts';

import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import type { UsagePivotRow, UsageTile } from './usage-report-shared';

/**
 * The layout every usage report renders into: headline tiles, one chart driven by the
 * Event dropdown, the whole scope in a table with an All row on top, and a note. The eye
 * learns it once; each report supplies only its columns, its rows and its chart series.
 *
 * Owned here, so no report repeats it:
 *  - the loading / error / empty states;
 *  - the All row, shown wherever All is a choice, i.e. wherever the Event dropdown is;
 *  - the highlight: one row, whichever is charted (the All row at All, else the lens event);
 *  - clicking a row name, which sets the lens so the dropdown and the chart move together;
 *  - the chart card, its title (the charted row's name) and its no-data wording.
 *
 * The chart's series and axes are inputs because they differ per report (columns per
 * role, bars per route); the card around them does not.
 */
@Component({
	selector: 'app-usage-pivot-report',
	standalone: true,
	imports: [ChartAllModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './usage-pivot-report.component.html',
	styleUrl: './usage-pivot-report.component.scss',
})
export class UsagePivotReportComponent {
	readonly state = inject(UsageAnalysisStateService);

	// States
	readonly isLoading = input.required<boolean>();
	readonly error = input.required<string | null>();
	/** True when the answer arrived and had no rows. */
	readonly isEmpty = input.required<boolean>();
	readonly loadingMessage = input('Loading…');
	readonly emptyMessage = input('Nothing in this window.');

	// Content
	readonly tiles = input.required<readonly UsageTile[]>();
	readonly columns = input.required<readonly string[]>();
	readonly rows = input.required<readonly UsagePivotRow[]>();
	readonly allRow = input.required<UsagePivotRow | null>();

	// Chart
	/** The row the chart draws; null when the lens event had no rows. */
	readonly chartRow = input.required<UsagePivotRow | null>();
	readonly chartSubtitle = input('');
	readonly chartSeries = input.required<SeriesModel[]>();
	readonly primaryXAxis = input.required<object>();
	readonly primaryYAxis = input.required<object>();
	readonly legendSettings = input<object>({ visible: false });
	readonly tooltip = input<object>({ enable: true });
	readonly chartHeight = input('280px');
	readonly chartMargin = input<object>({ left: 8, right: 8, top: 4, bottom: 4 });
	readonly chartEmptyMessage = input('Nothing in the window.');
	readonly lensEmptyMessage = input('Nothing about this event in the window. Pick another event above, or a row below.');

	readonly chartArea = { border: { width: 0 } };

	/** The All row leads the table wherever All is a choice. */
	readonly showAllRow = computed(() => this.state.showEventPicker() && this.allRow() !== null);

	/** Row names are links to the lens wherever there is a set to pick from. */
	readonly canDrill = computed(() => this.state.showEventPicker());

	readonly isRollup = computed(() => this.state.eventId() === null);

	/** True when the chart has at least one bar to draw. */
	readonly hasChart = computed(() => this.chartSeries().some(s => (s.dataSource as unknown[] | undefined)?.length));

	isHighlighted(row: UsagePivotRow): boolean {
		const lens = this.state.eventId();
		return lens !== null && row.id.toLowerCase() === lens.toLowerCase();
	}

	cell(row: UsagePivotRow, column: string): string {
		const n = row.cells.get(column) ?? 0;
		return n === 0 ? '·' : n.toLocaleString();
	}
}
