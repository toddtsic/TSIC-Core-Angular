import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import type { SeriesModel } from '@syncfusion/ej2-angular-charts';

import type { UsersByRoleOverTimeDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import type { UsageBucket } from '../usage-analysis.models';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import {
	DIRECTOR_ROLE,
	categoryAxis,
	countAxis,
	orderRoles,
	resolveChartTheme,
	resolvePalette,
	useUsageReportFetch,
	type UsagePivotRow,
	type UsageTile,
} from './usage-report-shared';

/**
 * How a bucket's start is named, per unit. Starts arrive server-local with no offset, so
 * `new Date(start)` reads them as local and the label is the day the server meant.
 */
const BUCKET_LABEL: Record<UsageBucket, Intl.DateTimeFormatOptions> = {
	day: { month: 'short', day: 'numeric' },
	week: { month: 'short', day: 'numeric' },
	month: { month: 'short', year: 'numeric' },
};

const ROW_HEADER: Record<UsageBucket, string> = { day: 'Day', week: 'Week of', month: 'Month' };

/** The bucket after `start`, for the end of the last bucket in the span. */
function nextStart(unit: UsageBucket, start: Date): Date {
	const d = new Date(start);
	if (unit === 'day') d.setDate(d.getDate() + 1);
	else if (unit === 'week') d.setDate(d.getDate() + 7);
	else d.setMonth(d.getMonth() + 1);
	return d;
}

/**
 * One bucket of the span: its start (the row id), its label, and report 01's numbers for
 * it. `noData` marks a bucket that ended before the log existed: not zero, unknown.
 */
interface BucketRow {
	readonly start: string;
	readonly label: string;
	readonly noData: boolean;
	total: number;
	readonly cells: Map<string, number>;
}

/**
 * Report 03 — Users by Role over Time. Report 01's question asked once per bucket:
 * distinct registrations that used the scoped live events, grouped by role, per day over
 * the last 30 days, per week over the last 12 weeks, or per month over the last 12 months.
 * The Bucket dropdown stands in for Window while this report is on screen — the bucket
 * IS the window.
 *
 * A registration counts in every bucket it was active in, so buckets do not sum to 01's
 * window total: 01 is distinct over the span, this is distinct per slice.
 *
 * Rows are buckets, newest on top; columns are the same roles in the same order and
 * colours as 01. The chart is the whole table, oldest to newest, one LINE per role so
 * every role sits on the same baseline and its trend reads on its own; the Total column
 * carries "people that bucket". The Event and Client lenses narrow the fetch, since a
 * per-bucket table has nowhere to keep the other events.
 *
 * Honesty rules the chart keeps:
 *  - a role with no users in a bucket is 0 and the line drops to the axis;
 *  - a bucket that ended before the log existed is NO DATA: null, drawn as a gap in every
 *    line and as the words "no data" in the table, never as 0;
 *  - the newest bucket is the current one, still filling, and its label says "so far".
 */
@Component({
	selector: 'app-usage-users-by-role-over-time',
	standalone: true,
	imports: [UsagePivotReportComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './users-by-role-over-time.component.html',
})
export class UsersByRoleOverTimeComponent {
	private readonly state = inject(UsageAnalysisStateService);
	readonly fetch = useUsageReportFetch<UsersByRoleOverTimeDto>(
		'users-by-role-over-time',
		'Unable to load users over time.',
		{ timeAxis: 'bucket', lensNarrowsFetch: true },
	);

	private readonly palette = resolvePalette();
	private readonly theme = resolveChartTheme();

	readonly isEmpty = computed(() => (this.fetch.data()?.rows.length ?? 0) === 0);

	/** The unit the ANSWER was bucketed by — the server's word, so labels never describe a bucket the data was not cut in. */
	private readonly unit = computed<UsageBucket>(() =>
		(this.fetch.data()?.bucket as UsageBucket | undefined) ?? this.state.bucket());

	readonly rowHeader = computed(() => ROW_HEADER[this.unit()]);

	/** Every role present anywhere in the span, in the shared order. */
	readonly columns = computed<readonly string[]>(() =>
		orderRoles(new Set((this.fetch.data()?.rows ?? []).map(r => r.roleName))));

	/** Every bucket in the span, oldest first: quiet buckets as zero rows, pre-log buckets as no data. The chart's order. */
	private readonly buckets = computed<readonly BucketRow[]>(() => {
		const d = this.fetch.data();
		if (!d) return [];
		const unit = this.unit();
		const fmt = new Intl.DateTimeFormat(undefined, BUCKET_LABEL[unit]);
		const firstRecorded = d.firstRecordedAt ? new Date(d.firstRecordedAt) : null;
		const last = d.buckets.length - 1;
		const byStart = new Map<string, BucketRow>();
		d.buckets.forEach((start, i) => {
			const startDate = new Date(start);
			const end = i < last ? new Date(d.buckets[i + 1]) : nextStart(unit, startDate);
			// No data = the whole bucket ended before the first row the log has. A bucket the log
			// began inside is partial and keeps its number, like the current one.
			const noData = firstRecorded !== null && end <= firstRecorded;
			const label = fmt.format(startDate) + (i === last ? ' · so far' : '');
			byStart.set(start, { start, label, noData, total: 0, cells: new Map() });
		});
		for (const r of d.rows) {
			const b = byStart.get(r.bucketStart);
			if (!b) continue;
			// A registration holds ONE role, so summing role counts within a bucket is still distinct registrations.
			b.total += r.users;
			b.cells.set(r.roleName, (b.cells.get(r.roleName) ?? 0) + r.users);
		}
		return [...byStart.values()];
	});

	/** The table: newest bucket on top. */
	readonly rows = computed<readonly UsagePivotRow[]>(() =>
		[...this.buckets()].reverse().map(b => ({ id: b.start, name: b.label, total: b.total, cells: b.cells, noData: b.noData })));

	readonly tiles = computed<readonly UsageTile[]>(() => {
		const unit = this.unit();
		const people = peak(this.buckets(), b => b.total);
		const directors = peak(this.buckets(), b => b.cells.get(DIRECTOR_ROLE) ?? 0);
		return [
			{ value: people.value, label: `People, busiest ${unit}${people.when}`, primary: true },
			{ value: directors.value, label: `Directors, busiest ${unit}${directors.when}` },
		];
	});

	/** The chart draws the whole span; its title is what the span covers. */
	readonly chartTitle = computed(() => {
		if (this.state.eventId()) return this.state.eventLabel();
		const n = this.state.jobCount();
		return n === 1 ? (this.state.jobNames()[0] ?? 'This event') : `All ${n} live events`;
	});

	readonly chartSubtitle = computed(() => {
		const unit = this.unit();
		const gap = this.buckets().some(b => b.noData) ? ' — a gap is before the log began, not zero' : '';
		return `distinct registrations per ${unit} — active in three ${unit}s counts in each${gap}`;
	});

	/** One line per role over every bucket, all on the same baseline. A no-data bucket is null: a gap, not a drop to zero. */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const columns = this.columns();
		const data = this.buckets().map(b => {
			const point: Record<string, string | number | null> = { x: b.label };
			columns.forEach((role, i) => { point['r' + i] = b.noData ? null : (b.cells.get(role) ?? 0); });
			return point;
		});
		if (data.length === 0) return [];
		return columns.map((role, i) => ({
			type: 'Line',
			dataSource: data,
			xName: 'x',
			yName: 'r' + i,
			name: role,
			fill: this.palette[i % this.palette.length],
			width: 2,
			marker: { visible: true, width: 6, height: 6, shape: 'Circle' },
			emptyPointSettings: { mode: 'Gap' },
		}));
	});

	private readonly maxCell = computed(() =>
		Math.max(0, ...this.buckets().flatMap(b => [...b.cells.values()])));

	readonly primaryXAxis = computed(() => categoryAxis(this.theme, { labelIntersectAction: 'Rotate45' }));
	readonly primaryYAxis = computed(() => countAxis(this.theme, this.maxCell()));
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

/** The bucket where `pick` is largest: its value, and " · label" for the tile when there is one. */
function peak(buckets: readonly BucketRow[], pick: (b: BucketRow) => number): { value: number; when: string } {
	let best: BucketRow | null = null;
	let value = 0;
	for (const b of buckets) {
		const v = pick(b);
		if (v > value) { value = v; best = b; }
	}
	return { value, when: best ? ` · ${best.label}` : '' };
}
