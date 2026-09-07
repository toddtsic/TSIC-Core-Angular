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

/** One bucket of the span: its start (the row id), its label, and report 01's numbers for it. */
interface BucketRow {
	readonly start: string;
	readonly label: string;
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
 * colours as 01. The chart is the whole table, oldest to newest, one stacked column per
 * bucket — height is people that bucket, segments are who. The Event and Client lenses
 * narrow the fetch, since a per-bucket table has nowhere to keep the other events.
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

	/** Every bucket in the span, oldest first, quiet buckets as zero rows — the chart's order. */
	private readonly buckets = computed<readonly BucketRow[]>(() => {
		const d = this.fetch.data();
		if (!d) return [];
		const fmt = new Intl.DateTimeFormat(undefined, BUCKET_LABEL[this.unit()]);
		const byStart = new Map<string, BucketRow>();
		for (const start of d.buckets) {
			byStart.set(start, { start, label: fmt.format(new Date(start)), total: 0, cells: new Map() });
		}
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
		[...this.buckets()].reverse().map(b => ({ id: b.start, name: b.label, total: b.total, cells: b.cells })));

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

	readonly chartSubtitle = computed(() =>
		`distinct registrations per ${this.unit()} — active in three ${this.unit()}s counts in each`);

	/** One stacked-column series per role over every bucket, so a column is one bucket's people and its segments are who. */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const columns = this.columns();
		const data = this.buckets().map(b => {
			const point: Record<string, string | number> = { x: b.label };
			columns.forEach((role, i) => { point['r' + i] = b.cells.get(role) ?? 0; });
			return point;
		});
		if (data.length === 0) return [];
		return columns.map((role, i) => ({
			type: 'StackingColumn',
			dataSource: data,
			xName: 'x',
			yName: 'r' + i,
			name: role,
			fill: this.palette[i % this.palette.length],
			opacity: 0.85,
			columnWidth: 0.7,
		}));
	});

	private readonly maxTotal = computed(() => Math.max(0, ...this.buckets().map(b => b.total)));

	readonly primaryXAxis = computed(() => categoryAxis(this.theme, { labelIntersectAction: 'Rotate45' }));
	readonly primaryYAxis = computed(() => countAxis(this.theme, this.maxTotal()));
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
