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

/** The roles the chart draws when the Role dropdown is on All. Exact AspNetRoles names. */
const CHART_ROLES: readonly string[] = ['Player', 'Club Rep'];

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
 * The chart is lines on two scales — count on the left, dollars on the right — so a trend in
 * either reads at a glance; stacked columns hid it. One pair per role, Player and Club Rep
 * (Todd, 2026-09-28): colour is the role, solid/dashed is the measure. Both axes start at
 * zero so the shapes stay comparable. The chart follows the Event and Role dropdowns; the table is
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

	/**
	 * The roles the chart draws, each as its own pair of lines. Player and Club Rep (Todd,
	 * 2026-09-28) — the two that carry the event's money. Picking a role in the Role dropdown
	 * charts that role alone, whichever it is.
	 */
	readonly chartRoles = computed<readonly string[]>(() => {
		const role = this.state.roleName();
		return role ? [role] : CHART_ROLES;
	});

	/** The rows the chart and tiles are drawn from: the Event dropdown applied, charted roles only. */
	private readonly lensRows = computed<readonly PaidRegistrationsDayRowDto[]>(() => {
		const eventId = this.state.eventId()?.toLowerCase() ?? null;
		const roles = new Set(this.chartRoles());
		return (this.fetch.data()?.rows ?? []).filter(r =>
			(eventId === null || r.jobId.toLowerCase() === eventId) && roles.has(r.roleName));
	});

	/**
	 * Every day in the span, oldest first, with a tally per charted role. Quiet days are zero —
	 * a day with nothing paid is a real zero, not a gap.
	 */
	private readonly series = computed<readonly { readonly label: string; readonly byRole: ReadonlyMap<string, Tally> }[]>(() => {
		const d = this.fetch.data();
		if (!d) return [];
		const roles = this.chartRoles();
		const byDay = new Map<string, Map<string, Tally>>(
			d.days.map(day => [day, new Map(roles.map(role => [role, { registrations: 0, cents: 0 }]))]));
		for (const r of this.lensRows()) {
			const t = byDay.get(r.day)?.get(r.roleName);
			if (!t) continue;
			t.registrations += r.registrations;
			t.cents += toCents(r.paid);
		}
		return d.days.map(day => ({ label: DAY_LABEL.format(new Date(day)), byRole: byDay.get(day)! }));
	});

	private readonly spanWords = computed(() => {
		const n = this.fetch.data()?.windowDays ?? this.state.windowDays();
		return n === 1 ? 'today' : `last ${n} days`;
	});

	/** Tiles follow the chart: a count and its dollars per charted role, over the whole span. */
	readonly tiles = computed<readonly UsageTile[]>(() => {
		const series = this.series();
		const span = this.spanWords();
		return this.chartRoles().flatMap((role, i) => {
			const t = series.reduce(
				(acc, p) => {
					const x = p.byRole.get(role);
					return x ? { registrations: acc.registrations + x.registrations, cents: acc.cents + x.cents } : acc;
				},
				{ registrations: 0, cents: 0 });
			return [
				{ value: t.registrations, label: `${plural(role)} paid · ${span}`, primary: i === 0 },
				{ value: t.cents / 100, label: `Paid on ${plural(role)}, as of now`, money: true },
			] satisfies UsageTile[];
		});
	});

	readonly chartTitle = computed(() => {
		if (this.state.eventId()) return this.state.eventLabel();
		const n = this.fetch.data()?.jobCount ?? 0;
		if (n === 1) return this.state.jobNames()[0] ?? 'This event';
		return `All ${n} events live during the span`;
	});

	readonly chartSubtitle = computed(() =>
		`${this.chartRoles().join(' and ')} per day — solid: registrations (left axis), dashed: dollars paid on them (right axis) — hollow point: today, still filling`);

	/** A role's colour: its place among the charted roles. Both of its lines wear it; the dash says which measure. */
	private roleColor(i: number): string {
		return this.palette[i % this.palette.length];
	}

	/**
	 * Two lines per charted role, one colour per role: solid circles for the count on the left
	 * axis, dashed diamonds for the dollars on the right. Colour answers "which role", line style
	 * answers "which measure" — so four lines still read as two pairs.
	 */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const roles = this.chartRoles();
		const data = this.series().map(p => {
			const point: Record<string, string | number> = { x: p.label };
			roles.forEach((role, i) => {
				const t = p.byRole.get(role);
				point['n' + i] = t?.registrations ?? 0;
				point['p' + i] = (t?.cents ?? 0) / 100;
			});
			return point;
		});
		if (data.length === 0) return [];
		return roles.flatMap((role, i) => [
			{
				type: 'Line', dataSource: data, xName: 'x', yName: 'n' + i, name: `${role} #`,
				fill: this.roleColor(i), width: 2,
				marker: { visible: true, width: 6, height: 6, shape: 'Circle' },
			},
			{
				type: 'Line', dataSource: data, xName: 'x', yName: 'p' + i, name: `${role} $`,
				fill: this.roleColor(i), width: 2, dashArray: '5,3', yAxisName: PAID_AXIS,
				marker: { visible: true, width: 6, height: 6, shape: 'Diamond' },
			},
		] satisfies SeriesModel[]);
	});

	/** ej2 draws today's markers hollow: today is still filling. */
	readonly pointRender = (args: IPointRenderEventArgs): void => {
		if (args.point.index !== this.series().length - 1) return;
		args.fill = this.theme.surface;
		args.border = { width: 2, color: args.series.interior };
	};

	readonly primaryXAxis = computed(() => categoryAxis(this.theme, { labelIntersectAction: 'Rotate45' }));

	/** Registrations, from zero. Neutral: two roles share it, and the legend carries the colours. */
	readonly primaryYAxis = computed(() => {
		const max = Math.max(0, ...this.series().flatMap(p => [...p.byRole.values()].map(t => t.registrations)));
		return {
			...countAxis(this.theme, max),
			title: 'Registrations (solid)',
			titleStyle: { color: this.theme.muted, size: '11px', fontFamily: this.theme.fontFamily },
		};
	});

	/** Dollars, opposed, also from zero. No gridlines of its own — two sets at different intervals read as a moiré. */
	readonly axes = computed<object[]>(() => [{
		name: PAID_AXIS,
		opposedPosition: true,
		minimum: 0,
		title: 'Paid (dashed)',
		titleStyle: { color: this.theme.muted, size: '11px', fontFamily: this.theme.fontFamily },
		labelStyle: { color: this.theme.muted, size: '11px', fontFamily: this.theme.fontFamily },
		labelFormat: '$#,##0',
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
	}]);

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

/** A role name's plural, for a tile label. Role names are singular ("Player", "Club Rep"). */
function plural(role: string): string {
	return role.endsWith('s') ? role : `${role}s`;
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
