import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import type { IPointRenderEventArgs, SeriesModel } from '@syncfusion/ej2-angular-charts';

import type { RegistrationsOverTimeDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import type { UsageBucket } from '../usage-analysis.models';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import {
	categoryAxis,
	countAxis,
	orderRoles,
	resolveChartTheme,
	resolvePalette,
	useUsageReportFetch,
	type UsagePivotRow,
	type UsageTile,
} from './usage-report-shared';

/** The series the server names for teams. Not a role: its own column, its own axis, outside the People total. */
const TEAMS_SERIES = 'Teams';

/** The ej2 axis name the Teams series binds to. */
const TEAMS_AXIS = 'teams';

/** How a bucket's start is named, per unit. Starts arrive server-local with no offset, so `new Date(start)` reads them as the day the server meant. */
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
 * One bucket of the span: its start (the row id), its labels, and what came in during it.
 * `people` is the Total column — players plus club reps, both registrations. Teams are in
 * `cells` but never in `people`: a team is not a person.
 */
interface BucketRow {
	readonly start: string;
	readonly label: string;
	readonly name: string;
	people: number;
	readonly cells: Map<string, number>;
}

/**
 * Registrations over Time — what came IN per bucket. The only report on this page that
 * reads no log: Player and Club Rep registrations from Jobs.Registrations (by
 * RegistrationTs, active rows only) and teams from Leagues.teams (by createdate).
 *
 * Unlike Users by Role over Time, these numbers ADD UP. A registration is created once, so
 * the span total is the sum of its buckets and the tiles can report it — where a user who
 * came back on three days is three daily rows and one person, and must never be summed.
 *
 * Why Teams is its own series rather than the Club Rep count: a club rep's registration is
 * minted once per (user, event) and then reused for every team they add, so a day can carry
 * 30 new teams and zero new club reps. The two answer different questions and the report
 * shows both.
 *
 * Why Teams gets a second axis: players run an order of magnitude above teams (hundreds a
 * day against tens), and on one scale the teams line would sit flat on the baseline.
 *
 * What the report deliberately does NOT show:
 *  - admin registrations. Provisioning, not intake — and job clone rewrites their
 *    RegistrationTs off the source event, so an admin series would claim signups on days
 *    nobody worked. Scorer rides with them.
 *  - abandoned carts. A row exists from PreSubmit; activation happens at payment (or at
 *    completion of a free event), so the server counts active rows only.
 *  - a Client lens. A registration carries no client, so the dropdown is hidden instead of
 *    sitting there filtering nothing.
 */
@Component({
	selector: 'app-usage-registrations-over-time',
	standalone: true,
	imports: [UsagePivotReportComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './registrations-over-time.component.html',
})
export class RegistrationsOverTimeComponent {
	private readonly state = inject(UsageAnalysisStateService);
	readonly fetch = useUsageReportFetch<RegistrationsOverTimeDto>(
		'registrations-over-time',
		'Unable to load registrations over time.',
		{ timeAxis: 'bucket', lensNarrowsFetch: true },
	);

	private readonly palette = resolvePalette();
	private readonly theme = resolveChartTheme();

	readonly isEmpty = computed(() => (this.fetch.data()?.rows.length ?? 0) === 0);

	/** The unit the ANSWER was bucketed by — the server's word, so a label never describes a slice the data was not cut in. */
	private readonly unit = computed<UsageBucket>(() =>
		(this.fetch.data()?.bucket as UsageBucket | undefined) ?? this.state.bucket());

	readonly rowHeader = computed(() => ROW_HEADER[this.unit()]);

	/**
	 * The people series present, in the shared role order, then Teams last. Teams is not in
	 * ROLE_ORDER, so orderRoles already sorts it into the tail; naming it here keeps the
	 * column order stable even in a span that had teams and no registrations.
	 */
	readonly columns = computed<readonly string[]>(() => {
		const present = new Set((this.fetch.data()?.rows ?? []).map(r => r.seriesName));
		const teams = present.delete(TEAMS_SERIES);
		return [...orderRoles(present), ...(teams ? [TEAMS_SERIES] : [])];
	});

	/** The people columns, in column order — what the Total adds and what the primary axis draws. */
	private readonly peopleColumns = computed<readonly string[]>(() =>
		this.columns().filter(c => c !== TEAMS_SERIES));

	/** Every bucket in the span, oldest first, quiet buckets as zero rows. The chart's order. */
	private readonly buckets = computed<readonly BucketRow[]>(() => {
		const d = this.fetch.data();
		if (!d) return [];
		const fmt = new Intl.DateTimeFormat(undefined, BUCKET_LABEL[this.unit()]);
		const last = d.buckets.length - 1;
		const byStart = new Map<string, BucketRow>();
		d.buckets.forEach((start, i) => {
			const label = fmt.format(new Date(start));
			// The newest bucket is the current one and is still filling; the axis label stays a
			// bare date so it never crowds its neighbour.
			byStart.set(start, { start, label, name: i === last ? `${label} · so far` : label, people: 0, cells: new Map() });
		});
		for (const r of d.rows) {
			const b = byStart.get(r.bucketStart);
			if (!b) continue;
			b.cells.set(r.seriesName, (b.cells.get(r.seriesName) ?? 0) + r.count);
			// Teams are counted in their column and nowhere else: adding them to people would invent a unit.
			if (r.isPeople) b.people += r.count;
		}
		return [...byStart.values()];
	});

	/** The table: newest bucket on top. `total` is the People column. */
	readonly rows = computed<readonly UsagePivotRow[]>(() =>
		[...this.buckets()].reverse().map(b => ({ id: b.start, name: b.name, total: b.people, cells: b.cells })));

	/**
	 * Span totals, not peaks. Registrations are created once, so summing the buckets is
	 * honest here — the distinction that makes this report different from the usage ones.
	 */
	readonly tiles = computed<readonly UsageTile[]>(() => {
		const buckets = this.buckets();
		const span = this.spanWords();
		const sum = (series: string) => buckets.reduce((n, b) => n + (b.cells.get(series) ?? 0), 0);
		const tiles: UsageTile[] = this.peopleColumns().map((series, i) => ({
			value: sum(series),
			label: `${plural(series)} registered · ${span}`,
			primary: i === 0,
		}));
		if (this.columns().includes(TEAMS_SERIES)) {
			tiles.push({ value: sum(TEAMS_SERIES), label: `Teams registered · ${span}` });
		}
		return tiles;
	});

	/** The span in words, from the bucket the ANSWER was cut in: "last 30 days", "last 12 weeks", "last 12 months". */
	private readonly spanWords = computed(() => {
		switch (this.unit()) {
			case 'week': return 'last 12 weeks';
			case 'month': return 'last 12 months';
			default: return 'last 30 days';
		}
	});

	/**
	 * The chart draws the whole span; its title is what the span covers. The count is the
	 * ANSWER's (events live at any point in the span), not the page's (events live now).
	 */
	readonly chartTitle = computed(() => {
		if (this.state.eventId()) return this.state.eventLabel();
		const n = this.fetch.data()?.jobCount ?? 0;
		if (n === 1 && this.state.jobCount() === 1) return this.state.jobNames()[0] ?? 'This event';
		return n === 1 ? '1 event live during the span' : `All ${n} events live during the span`;
	});

	readonly chartSubtitle = computed(() => {
		const unit = this.unit();
		const teams = this.columns().includes(TEAMS_SERIES) ? ' — teams on the right axis, a different scale' : '';
		return `new registrations per ${unit} — hollow point: the current ${unit}, still filling${teams}`;
	});

	/** ej2 draws the newest bucket's markers hollow: the series colour as a ring around the card's surface. */
	readonly pointRender = (args: IPointRenderEventArgs): void => {
		if (args.point.index !== this.buckets().length - 1) return;
		args.fill = this.theme.surface;
		args.border = { width: 2, color: args.series.interior };
	};

	/**
	 * One line per people series on the primary axis, plus Teams on its own axis, dashed so
	 * the different scale reads even in one glance at the legend. No Total line: the People
	 * total is two series added, and a third line over two would just retrace them.
	 */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const columns = this.columns();
		const data = this.buckets().map(b => {
			const point: Record<string, string | number> = { x: b.label };
			columns.forEach((series, i) => { point['s' + i] = b.cells.get(series) ?? 0; });
			return point;
		});
		if (data.length === 0) return [];
		return columns.map((series, i) => {
			const isTeams = series === TEAMS_SERIES;
			return {
				type: 'Line',
				dataSource: data,
				xName: 'x',
				yName: 's' + i,
				name: series,
				fill: this.palette[i % this.palette.length],
				width: 2,
				dashArray: isTeams ? '5,3' : undefined,
				yAxisName: isTeams ? TEAMS_AXIS : undefined,
				marker: { visible: true, width: 6, height: 6, shape: isTeams ? 'Diamond' : 'Circle' },
			} satisfies SeriesModel;
		});
	});

	private readonly maxPeople = computed(() =>
		Math.max(0, ...this.buckets().flatMap(b => this.peopleColumns().map(c => b.cells.get(c) ?? 0))));

	private readonly maxTeams = computed(() =>
		Math.max(0, ...this.buckets().map(b => b.cells.get(TEAMS_SERIES) ?? 0)));

	readonly primaryXAxis = computed(() => categoryAxis(this.theme, { labelIntersectAction: 'Rotate45' }));

	/** People. Scaled to the tallest PEOPLE series, so teams riding a second axis cannot stretch it. */
	readonly primaryYAxis = computed(() => countAxis(this.theme, this.maxPeople()));

	/**
	 * Teams, opposed. Titled and tinted to the Teams line so the reader never has to work out
	 * which scale belongs to which series, and with no gridlines of its own — a second set at
	 * different intervals reads as a moiré rather than as a scale.
	 */
	readonly axes = computed<object[]>(() => {
		if (!this.columns().includes(TEAMS_SERIES)) return [];
		const color = this.palette[this.columns().indexOf(TEAMS_SERIES) % this.palette.length];
		return [{
			name: TEAMS_AXIS,
			opposedPosition: true,
			minimum: 0,
			interval: this.maxTeams() <= 10 ? 1 : undefined,
			title: 'Teams',
			titleStyle: { color, size: '11px', fontFamily: this.theme.fontFamily },
			labelStyle: { color, size: '11px', fontFamily: this.theme.fontFamily },
			labelFormat: 'n0',
			majorGridLines: { width: 0 },
			majorTickLines: { width: 0 },
			lineStyle: { width: 0 },
		}];
	});

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

/** The column header's plural, for a tile label. Role names are singular ("Player", "Club Rep"); "Teams" already is not. */
function plural(series: string): string {
	return series.endsWith('s') ? series : `${series}s`;
}
