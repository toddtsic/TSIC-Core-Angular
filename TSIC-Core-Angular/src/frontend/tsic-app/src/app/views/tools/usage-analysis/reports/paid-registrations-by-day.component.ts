import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import type { IPointRenderEventArgs, SeriesModel } from '@syncfusion/ej2-angular-charts';

import type { PaidRegistrationsByDayDto, PaidRegistrationsDayRowDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import {
	categoryAxis,
	countAxis,
	formatMoney,
	orderRoles,
	resolveChartTheme,
	resolvePalette,
	useUsageReportFetch,
	type UsageTile,
} from './usage-report-shared';

/** The ej2 axis name the dollars line binds to. */
const PAID_AXIS = 'paid';

/**
 * A count and its money — one cell pair of the table, one point of the chart. Money is held
 * in whole CENTS and added as integers, so a sum of hundreds of payments cannot drift a penny.
 */
interface Tally {
	registrations: number;
	cents: number;
}

/** The wire amount (dollars, two places) as whole cents. */
function toCents(dollars: number): number {
	return Math.round(dollars * 100);
}

/** One table line: a role's pair per column, plus the line's total pair. */
interface TableLine {
	readonly id: string;
	/** The event this line is, or null for a day subtotal and the All line. */
	readonly jobId: string | null;
	readonly day: string;
	readonly event: string;
	readonly byRole: ReadonlyMap<string, Tally>;
	readonly total: Tally;
	readonly kind: 'all' | 'day' | 'event';
}

/** Days are server-local midnights with no offset, so `new Date(day)` reads the day the server meant. */
const DAY_LABEL = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' });
const DAY_LONG = new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric' });

/**
 * Paid Registrations by Day — how many new registrations came in each day, and what has been
 * paid on them, per event, per role. TSICV5 only, dated by RegistrationTs.
 *
 * Rulings behind it (Todd, 2026-09-28):
 *  - Paid ones only (PaidTotal > 0). The dollars are that PaidTotal as of now, not the cash
 *    that arrived that day: a registration made on the 5th and paid on the 10th sits on the
 *    5th with its payment.
 *  - Club Rep counts CLUB REPS, on the registration's rollup PaidTotal across its teams. A
 *    rep who adds teams later carries that money back to the day the rep first registered.
 *  - Admin roles are out: provisioning, and job clone rewrites their RegistrationTs.
 *
 * The chart is two lines on two scales — count on the left, dollars on the right — so a
 * trend in either reads at a glance; stacked columns hid it. Both axes start at zero so the
 * two shapes stay comparable. The chart follows the Event and Role dropdowns; the table is
 * always the whole scope, one event per line under each day, the charted event highlighted.
 * Neither lens refetches: the answer is scope-wide and both are applied here.
 */
@Component({
	selector: 'app-usage-paid-registrations-by-day',
	standalone: true,
	imports: [UsagePivotReportComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './paid-registrations-by-day.component.html',
	styleUrl: './paid-registrations-by-day.component.scss',
})
export class PaidRegistrationsByDayComponent {
	readonly state = inject(UsageAnalysisStateService);

	readonly fetch = useUsageReportFetch<PaidRegistrationsByDayDto>(
		'paid-registrations-by-day',
		'Unable to load paid registrations.',
		{ onData: d => this.state.setRoleOptions(orderRoles(new Set(d.rows.map(r => r.roleName)))) },
	);

	private readonly palette = resolvePalette();
	private readonly theme = resolveChartTheme();
	readonly money = formatMoney;

	readonly isEmpty = computed(() => (this.fetch.data()?.rows.length ?? 0) === 0);

	/** Roles present anywhere in the scope, in the page's shared role order: the table's column pairs. */
	readonly roles = computed<readonly string[]>(() =>
		orderRoles(new Set((this.fetch.data()?.rows ?? []).map(r => r.roleName))));

	/** More than one event in the answer's scope: only then does the table nest events under days. */
	readonly multiEvent = computed(() => (this.fetch.data()?.jobCount ?? 0) > 1);

	// ── Chart and tiles: the lens slice ─────────────────────────

	/** The rows the chart and tiles are drawn from: the Event and Role dropdowns applied. */
	private readonly lensRows = computed<readonly PaidRegistrationsDayRowDto[]>(() => {
		const eventId = this.state.eventId()?.toLowerCase() ?? null;
		const role = this.state.roleName();
		return (this.fetch.data()?.rows ?? []).filter(r =>
			(eventId === null || r.jobId.toLowerCase() === eventId) && (role === null || r.roleName === role));
	});

	/** Every day in the span, oldest first, quiet days as zero — a day with nothing paid is a real zero, not a gap. */
	private readonly series = computed<readonly { readonly label: string; readonly tally: Tally }[]>(() => {
		const d = this.fetch.data();
		if (!d) return [];
		const byDay = new Map<string, Tally>(d.days.map(day => [day, { registrations: 0, cents: 0 }]));
		for (const r of this.lensRows()) {
			const t = byDay.get(r.day);
			if (!t) continue;
			t.registrations += r.registrations;
			t.cents += toCents(r.paid);
		}
		return d.days.map(day => ({ label: DAY_LABEL.format(new Date(day)), tally: byDay.get(day)! }));
	});

	private readonly lensTotal = computed<Tally>(() =>
		this.series().reduce((t, p) => ({ registrations: t.registrations + p.tally.registrations, cents: t.cents + p.tally.cents }), { registrations: 0, cents: 0 }));

	private readonly spanWords = computed(() => {
		const n = this.fetch.data()?.windowDays ?? this.state.windowDays();
		return n === 1 ? 'today' : `last ${n} days`;
	});

	/** Tiles follow the chart: whatever the two dropdowns point at, over the whole span. */
	readonly tiles = computed<readonly UsageTile[]>(() => {
		const t = this.lensTotal();
		const who = this.state.roleName() ? `${this.state.roleName()} registrations` : 'Paid registrations';
		return [
			{ value: t.registrations, label: `${who} · ${this.spanWords()}`, primary: true },
			{ value: t.cents / 100, label: 'Paid on them, as of now', money: true },
		];
	});

	readonly chartTitle = computed(() => {
		const role = this.state.roleName();
		const suffix = role ? ` · ${role}` : '';
		if (this.state.eventId()) return this.state.eventLabel() + suffix;
		const n = this.fetch.data()?.jobCount ?? 0;
		if (n === 1) return (this.state.jobNames()[0] ?? 'This event') + suffix;
		return `All ${n} events live during the span${suffix}`;
	});

	readonly chartSubtitle = 'registrations per day on the left, dollars paid on them on the right — hollow point: today, still filling';

	private readonly countColor = computed(() => this.palette[0]);
	private readonly paidColor = computed(() => this.palette[2]);

	readonly chartSeries = computed<SeriesModel[]>(() => {
		const data = this.series().map(p => ({ x: p.label, n: p.tally.registrations, paid: p.tally.cents / 100 }));
		if (data.length === 0) return [];
		return [
			{
				type: 'Line', dataSource: data, xName: 'x', yName: 'n', name: 'Registrations',
				fill: this.countColor(), width: 2,
				marker: { visible: true, width: 6, height: 6, shape: 'Circle' },
			},
			{
				type: 'Line', dataSource: data, xName: 'x', yName: 'paid', name: 'Paid',
				fill: this.paidColor(), width: 2, dashArray: '5,3', yAxisName: PAID_AXIS,
				marker: { visible: true, width: 6, height: 6, shape: 'Diamond' },
			},
		] satisfies SeriesModel[];
	});

	/** ej2 draws today's markers hollow: today is still filling. */
	readonly pointRender = (args: IPointRenderEventArgs): void => {
		if (args.point.index !== this.series().length - 1) return;
		args.fill = this.theme.surface;
		args.border = { width: 2, color: args.series.interior };
	};

	readonly primaryXAxis = computed(() => categoryAxis(this.theme, { labelIntersectAction: 'Rotate45' }));

	/** Registrations, titled and tinted to its line so neither scale has to be worked out. Starts at zero. */
	readonly primaryYAxis = computed(() => {
		const max = Math.max(0, ...this.series().map(p => p.tally.registrations));
		const color = this.countColor();
		return {
			...countAxis(this.theme, max),
			title: 'Registrations',
			titleStyle: { color, size: '11px', fontFamily: this.theme.fontFamily },
			labelStyle: { color, size: '11px', fontFamily: this.theme.fontFamily },
		};
	});

	/** Dollars, opposed, also from zero. No gridlines of its own — two sets at different intervals read as a moiré. */
	readonly axes = computed<object[]>(() => {
		const color = this.paidColor();
		return [{
			name: PAID_AXIS,
			opposedPosition: true,
			minimum: 0,
			title: 'Paid',
			titleStyle: { color, size: '11px', fontFamily: this.theme.fontFamily },
			labelStyle: { color, size: '11px', fontFamily: this.theme.fontFamily },
			labelFormat: '$#,##0',
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

	// ── Table: the whole scope ──────────────────────────────────

	/**
	 * All line first (the span, every event, every role), then each day that took a paid
	 * registration, newest first. With more than one event, a day is a subtotal line with its
	 * events beneath it, busiest first; with one event, the day line is the event.
	 */
	readonly lines = computed<readonly TableLine[]>(() => {
		const d = this.fetch.data();
		if (!d) return [];

		const all = new Map<string, Tally>();
		const days = new Map<string, { byRole: Map<string, Tally>; events: Map<string, { name: string; byRole: Map<string, Tally> }> }>();
		for (const r of d.rows) {
			add(all, r);
			let day = days.get(r.day);
			if (!day) days.set(r.day, day = { byRole: new Map(), events: new Map() });
			add(day.byRole, r);
			let ev = day.events.get(r.jobId);
			if (!ev) day.events.set(r.jobId, ev = { name: r.jobName, byRole: new Map() });
			add(ev.byRole, r);
		}

		const multi = this.multiEvent();
		const out: TableLine[] = [{
			id: 'all', jobId: null, kind: 'all', byRole: all, total: sum(all),
			day: this.spanWords(), event: multi ? `All ${d.jobCount} events` : '',
		}];
		for (const [day, v] of [...days.entries()].sort(([a], [b]) => b.localeCompare(a))) {
			const label = DAY_LONG.format(new Date(day));
			if (!multi) {
				out.push({ id: day, jobId: null, kind: 'day', day: label, event: '', byRole: v.byRole, total: sum(v.byRole) });
				continue;
			}
			out.push({
				id: day, jobId: null, kind: 'day', day: label,
				event: v.events.size === 1 ? '1 event' : `${v.events.size} events`,
				byRole: v.byRole, total: sum(v.byRole),
			});
			const events = [...v.events.entries()]
				.map(([jobId, e]) => ({ jobId, name: e.name, byRole: e.byRole, total: sum(e.byRole) }))
				.sort((a, b) => b.total.cents - a.total.cents || a.name.localeCompare(b.name));
			for (const e of events) {
				out.push({ id: `${day}|${e.jobId}`, jobId: e.jobId, kind: 'event', day: '', event: e.name, byRole: e.byRole, total: e.total });
			}
		}
		return out;
	});

	isCharted(line: TableLine): boolean {
		const lens = this.state.eventId();
		return line.kind === 'event' && lens !== null && line.jobId!.toLowerCase() === lens.toLowerCase();
	}

	count(t: Tally | undefined): string {
		return t && t.registrations !== 0 ? t.registrations.toLocaleString() : '·';
	}

	dollars(t: Tally | undefined): string {
		return t && t.registrations !== 0 ? formatMoney(t.cents / 100) : '·';
	}
}

function add(into: Map<string, Tally>, r: PaidRegistrationsDayRowDto): void {
	const t = into.get(r.roleName);
	if (t) {
		t.registrations += r.registrations;
		t.cents += toCents(r.paid);
	} else {
		into.set(r.roleName, { registrations: r.registrations, cents: toCents(r.paid) });
	}
}

function sum(byRole: ReadonlyMap<string, Tally>): Tally {
	let registrations = 0;
	let cents = 0;
	for (const t of byRole.values()) {
		registrations += t.registrations;
		cents += t.cents;
	}
	return { registrations, cents };
}
