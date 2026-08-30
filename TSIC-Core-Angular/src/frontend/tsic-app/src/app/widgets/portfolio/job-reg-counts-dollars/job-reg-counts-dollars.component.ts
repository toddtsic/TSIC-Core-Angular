import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';
import type {
	IAxisLabelRenderEventArgs, ITextRenderEventArgs, ITooltipRenderEventArgs,
} from '@syncfusion/ej2-charts';

import { AuthService } from '@infrastructure/services/auth.service';
import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import { dateKey, tableSort, textKey } from '@shared-ui/table-sort';
import type { JobRegCountsAndDollarsDto, JobRegCountsAndDollarsRowDto } from '@core/api';

/** Sortable columns. */
type SortCol = 'event' | 'type' | 'starts' | 'players' | 'teams' | 'fees' | 'paid' | 'owed';

/**
 * How many events get their own bar before the tail is folded into one "All others".
 *
 * Set ABOVE the common case on purpose: a typical portfolio is high-teens (Top Threat runs
 * 18 live events) and folding a handful of those into an anonymous bar hides real events
 * for no gain. The fold exists for the genuine outliers — LIECommish runs 73 live jobs,
 * where 73 bars is 3,000px of card and the labels are unreadable.
 */
const CHART_BAR_LIMIT = 20;

/** One bar on the Billed vs Collected chart. */
interface EventMoneyBar {
	readonly name: string;
	readonly fees: number;
	readonly paid: number;
	/**
	 * Owed clamped at zero — the GEOMETRY only. A stacking bar cannot render a negative
	 * segment (it draws backwards through the Paid segment), and 6 of 166 live jobs are
	 * net overpaid. The true signed figure rides on `owedActual` for the tooltip, and an
	 * overpaid job simply shows a longer Paid bar, which is what actually happened.
	 */
	readonly owed: number;
	readonly owedActual: number;
	/** Share of fees banked; null when a job carries no fees at all (0/0 is not 0%). */
	readonly pctCollected: number | null;
}

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

/** Parse `#rgb`, `#rrggbb` or `rgb()/rgba()` into channels; null when unrecognised. */
function parseColor(c: string): [number, number, number] | null {
	const hex = c.match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i);
	if (hex) {
		const h = hex[1].length === 3 ? hex[1].replace(/./g, m => m + m) : hex[1];
		return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];
	}
	const rgb = c.match(/^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)/i);
	return rgb ? [Number(rgb[1]), Number(rgb[2]), Number(rgb[3])] : null;
}

/**
 * Blend `ratio` of `fg` over `bg` and return a SOLID hex.
 *
 * Why not just set the series `opacity`: ej2 draws legend swatches from `series.fill` and
 * does not carry the series opacity onto them, so two segments distinguished only by
 * opacity produce two IDENTICAL legend keys. The tint has to be a real colour.
 *
 * Falls back to `fg` unchanged if either colour is in a form we cannot parse — a slightly
 * wrong shade beats a chart with no fill at all.
 */
function mixOver(fg: string, bg: string, ratio: number): string {
	const a = parseColor(fg);
	const b = parseColor(bg);
	if (!a || !b) return fg;
	const ch = (i: number) => Math.round(a[i] * ratio + b[i] * (1 - ratio));
	return `#${[0, 1, 2].map(i => ch(i).toString(16).padStart(2, '0')).join('')}`;
}

/**
 * JobRegCountsAndDollars — live-jobs portfolio.
 *
 * ONE summary line, TWO accordions. Collapsed, the widget is a single rollup row; opening
 * the first reveals the per-job rows, and the second holds the Billed vs Collected chart.
 * (Not an accordion per job — that made the reader open N drawers to answer a question
 * the table already answers in its columns.)
 *
 * The chart and the table render the SAME response, so they can never disagree on screen;
 * the table is also the chart's table view, which is what keeps every value reachable
 * without a hover.
 *
 * Scope is LIVE jobs (ExpiryUsers > now) of the customer the caller is standing in,
 * resolved server-side from the token.
 *
 * BOTH count units ride every row alongside the job type name. The registration-allow
 * flags are an open/closed door switch, NOT a billable-unit classification — 30% of live
 * jobs have both flags off while still holding players, teams and money — so column
 * visibility is never driven off them. A blank count is information, not a gap.
 */
@Component({
	selector: 'app-job-reg-counts-dollars',
	standalone: true,
	imports: [CurrencyPipe, DatePipe, DecimalPipe, RouterLink, ChartAllModule],
	templateUrl: './job-reg-counts-dollars.component.html',
	styleUrl: './job-reg-counts-dollars.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobRegCountsAndDollarsComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);
	private readonly auth = inject(AuthService);

	readonly data = signal<JobRegCountsAndDollarsDto | null>(null);
	readonly isLoading = signal(true);
	readonly hasError = signal(false);

	/** First accordion: closed shows the rollup line only. */
	readonly isOpen = signal(false);

	/**
	 * Second accordion: the chart. Opens by DEFAULT — this is the at-a-glance element,
	 * and a chart nobody expands is not doing its job.
	 *
	 * It is also why the panel is an `@if` rather than a hidden block: an ej2 chart that
	 * first renders inside a collapsed container measures a zero-width parent and stays
	 * broken when it is later revealed. Destroying and recreating sidesteps that with no
	 * refresh() plumbing.
	 */
	readonly isChartOpen = signal(true);

	readonly rows = computed(() => this.data()?.rows ?? []);
	readonly isEmpty = computed(() => !this.isLoading() && !this.hasError() && this.rows().length === 0);

	/**
	 * Shared heading-sort state (shared-ui/table-sort). Opens on event date ascending —
	 * soonest first is the working order for "what needs me next". Money and counts open
	 * DESCENDING because someone clicking Owed or Teams is hunting the biggest, not the
	 * smallest; text columns open A–Z.
	 */
	readonly sort = tableSort<SortCol>('starts', {
		players: 'desc', teams: 'desc', fees: 'desc', paid: 'desc', owed: 'desc',
	});

	/**
	 * Comparators are written ASCENDING; table-sort applies direction. Event and Type sort
	 * on what is DISPLAYED (stripped name, abbreviated type), not the stored value — a
	 * column that sorts by text the reader cannot see reads as broken.
	 */
	readonly sortedRows = this.sort.applyTo(this.rows, (col, a, b) => {
		switch (col) {
			case 'event':   return textKey(this.eventName(a), this.eventName(b));
			case 'type':    return textKey(this.typeLabel(a), this.typeLabel(b));
			case 'starts':  return dateKey(a.eventStartDate) - dateKey(b.eventStartDate);
			case 'players': return a.playerCount - b.playerCount;
			case 'teams':   return a.teamCount - b.teamCount;
			case 'fees':    return a.fees - b.fees;
			case 'paid':    return a.paid - b.paid;
			case 'owed':    return a.owed - b.owed;
		}
	});

	// ── Billed vs Collected chart ───────────────────────────────────────────
	// Resolved eagerly, like the other chart widgets: ej2 reads these once at init, so a
	// value arriving later as a property change is not picked up.

	readonly paidColor = signal(cssVar('--bs-primary', '#0d6efd'));
	readonly mutedColor = signal(cssVar('--brand-text-muted', '#6c757d'));
	readonly borderColor = signal(cssVar('--bs-border-color', 'rgba(0,0,0,0.1)'));
	/**
	 * Painted BETWEEN the two stacked segments as a 2px border, which is how a gap is
	 * drawn without a contrasting outline around each mark. Reading the card colour rather
	 * than assuming white keeps the gap invisible in the dark theme too.
	 */
	readonly surfaceColor = signal(cssVar('--brand-surface', '#ffffff'));

	/**
	 * ej2 falls back to its OWN theme font for axis labels, data labels, legend and
	 * tooltip — which renders as a serif against this app's sans UI and makes the chart
	 * look bolted on. Every text style below has to name this explicitly; there is no
	 * inherit-from-CSS path into the chart's SVG.
	 */
	readonly chartFont = signal(cssVar('--brand-font-sans', 'Inter, system-ui, sans-serif'));

	/**
	 * Owed = the SAME hue stepped toward the card colour, as a solid fill. Part-to-whole of
	 * one measure (dollars), so a second hue would assert an identity distinction that is
	 * not there — and a status colour is doubly wrong here, since the footnote states Owed
	 * is not past due.
	 *
	 * 40% keeps it clearly lighter than Paid while staying visible as a bar; the same step
	 * runs the other way in the dark theme (primary #cbd5e1 toward surface #44403c), so one
	 * expression covers both without a theme branch.
	 */
	readonly owedColor = computed(() => mixOver(this.paidColor(), this.surfaceColor(), 0.4));

	/**
	 * Bars, largest contract first. Sorted here on `fees` INDEPENDENTLY of the table's
	 * heading sort: the chart answers "who is big and how much is banked", and re-ordering
	 * it every time someone sorts the table by start date would destroy that reading.
	 *
	 * The tail beyond CHART_BAR_LIMIT folds into one "All others" bar rather than being
	 * dropped — a chart that silently omits events would understate the portfolio.
	 */
	readonly chartBars = computed<EventMoneyBar[]>(() => {
		const all = this.billingRows().sort((a, b) => b.fees - a.fees);
		const head = all.slice(0, CHART_BAR_LIMIT).map(r => this.toBar(this.barLabel(r), r.fees, r.paid, r.owed));
		const tail = all.slice(CHART_BAR_LIMIT);

		if (tail.length > 0) {
			head.push(this.toBar(
				`All others (${tail.length})`,
				tail.reduce((s, r) => s + r.fees, 0),
				tail.reduce((s, r) => s + r.paid, 0),
				tail.reduce((s, r) => s + r.owed, 0),
			));
		}

		// ej2 lays a Bar category axis out bottom-up, so the array is reversed to put the
		// largest contract at the TOP where the eye starts.
		return head.reverse();
	});

	/**
	 * Events with something billed. An event with zero fees has not started billing, so it
	 * is not a small bar — it is not a data point, and six zero-length rows cost 240px to
	 * say nothing. They stay in the TABLE, which is a roster; the chart is a comparison.
	 *
	 * Measured on the demo customer: every zero-fee live event is 258–300 days out.
	 */
	private readonly billingRows = computed(() => this.rows().filter(r => r.fees > 0));

	/** Live events carrying no fees yet — named in the header note so the omission is stated. */
	readonly notBillingCount = computed(() => this.rows().length - this.billingRows().length);

	/**
	 * Header note. States BOTH ways the chart can show fewer bars than the table has rows —
	 * a chart that quietly drops events would understate the portfolio.
	 */
	readonly chartNote = computed(() => {
		const parts: string[] = [];
		const folded = this.billingRows().length - Math.min(this.billingRows().length, CHART_BAR_LIMIT);
		if (folded > 0) parts.push(`top ${CHART_BAR_LIMIT} of ${this.billingRows().length}`);
		if (this.notBillingCount() > 0) parts.push(`${this.notBillingCount()} not yet billing`);
		return parts.join(' · ');
	});

	/**
	 * Category label: event name plus how far out it is.
	 *
	 * Days-out rather than a date, for width and for the reading it supports — "76d" against
	 * "313d" is the comparison being made, and a date makes the reader compute it. It is
	 * also what explains the shape of this chart: fees track days-out almost exactly, so
	 * without it the small bars look like underperformance rather than events that have
	 * barely opened.
	 *
	 * Omitted once an event is underway (registration can stay open past the start date) —
	 * "-5d" reads as a defect.
	 */
	private barLabel(row: JobRegCountsAndDollarsRowDto): string {
		const d = this.daysOut(row);
		return d !== null && d >= 0 ? `${this.eventName(row)} · ${d}d` : this.eventName(row);
	}

	/**
	 * Explicit pixel height — ej2 will not grow a chart to fit its categories, and a fixed
	 * height would either crush 13 bars or leave whitespace under 3. Includes the legend
	 * and value-axis bands so the card never gets a nested scrollbar.
	 *
	 * 40px per category, not the ~30 the bars alone need: categories are spaced evenly by
	 * height/count regardless of how tall a label is, so a wrapped two-line name collides
	 * with its neighbour unless the row owns the space for both lines.
	 */
	readonly chartHeight = computed(() => `${Math.max(200, this.chartBars().length * 40 + 72)}px`);

	/**
	 * Category axis (vertical on a Bar chart) — one tick per event.
	 *
	 * Labels WRAP at maximumLabelWidth rather than ellipsing. Event names run long
	 * ("Halloween Invitational 2026" even after the customer half is stripped) and they are
	 * the only thing naming each bar — an ellipsis truncates exactly the year and qualifier
	 * that tell two events of the same series apart. An uncapped label is the other failure:
	 * it steals plot width from the bars it is naming, so the cap stays and the overflow
	 * goes downward into a second line.
	 */
	readonly chartXAxis = computed(() => ({
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0.5, color: this.borderColor() },
		labelStyle: { color: this.mutedColor(), size: '11px', fontFamily: this.chartFont() },
		maximumLabelWidth: 150,
		enableWrap: true,
	}));

	/** Value axis (horizontal on a Bar chart) — dollars, abbreviated by onChartAxisLabel. */
	readonly chartYAxis = computed(() => ({
		majorGridLines: { width: 0.5, color: this.borderColor() },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px', fontFamily: this.chartFont() },
		minimum: 0,
	}));

	readonly chartTooltip = computed(() => ({
		enable: true,
		shared: false,
		textStyle: { fontFamily: this.chartFont() },
	}));

	readonly chartLegend = computed(() => ({
		visible: true,
		position: 'Top' as const,
		alignment: 'Far' as const,
		textStyle: { size: '11px', fontFamily: this.chartFont() },
		padding: 8,
	}));

	readonly chartArea = { border: { width: 0 } };

	/**
	 * Right margin holds the fee-total labels sitting off the end of each bar. At 16px they
	 * clipped: "$1,234,567" needs ~72px of its own.
	 */
	readonly chartMargin = { left: 8, right: 80, top: 4, bottom: 4 };

	/**
	 * The fee total, printed at the end of each bar.
	 *
	 * Declared on the OWED series because it is the outer segment, so 'Outer' places the
	 * label past the end of the whole stack rather than mid-bar. `showZero` matters: on a
	 * fully-collected event Owed is 0, and without it that bar would be the one silently
	 * missing its total.
	 *
	 * Text wears the muted INK token, not the series colour — the bar beside it already
	 * carries the identity, and a coloured number reads as a third encoding.
	 */
	readonly feeTotalLabel = computed(() => ({
		visible: true,
		position: 'Outer' as const,
		showZero: true,
		fill: 'transparent',
		border: { width: 0 },
		font: { color: this.mutedColor(), size: '11px', fontWeight: '600', fontFamily: this.chartFont() },
	}));

	/**
	 * Tooltip carries the real numbers, INCLUDING a negative owed that the bar geometry
	 * had to clamp — otherwise an overpaid job would read as fully collected with no
	 * indication anywhere that it is actually in credit.
	 */
	onChartTooltip(args: ITooltipRenderEventArgs): void {
		const bar = args.data?.pointIndex != null ? this.chartBars()[args.data.pointIndex] : null;
		if (!bar) return;

		const pct = bar.pctCollected === null ? '—' : `${Math.round(bar.pctCollected)}%`;
		const credit = bar.owedActual < 0
			? `<br/>Overpaid by ${this.money(-bar.owedActual)}`
			: '';

		args.text = `${bar.name}<br/>Fees ${this.money(bar.fees)}`
			+ `<br/>Paid ${this.money(bar.paid)} (${pct})`
			+ `<br/>Owed ${this.money(bar.owedActual)}${credit}`;
	}

	/**
	 * Swaps the Owed segment's own value for the bar's FEE TOTAL.
	 *
	 * The label is attached to Owed purely to get 'Outer' to land past the end of the
	 * stack; the number it would print by default (the owed amount) is already the
	 * lighter segment's visible length. The total is the one figure the geometry cannot
	 * convey, and it should not require a hover to read.
	 */
	onChartTextRender(args: ITextRenderEventArgs): void {
		const bar = this.chartBars()[args.point?.index ?? -1];
		if (bar) args.text = this.money(bar.fees);
	}

	/**
	 * Value-axis ticks as $12k rather than $12,000 — at a dozen ticks the full figures
	 * collide, and the exact number is one hover (or one table row) away.
	 */
	onChartAxisLabel(args: IAxisLabelRenderEventArgs): void {
		if (args.axis?.name !== 'primaryYAxis') return;
		const val = Number(args.text?.replace(/,/g, '') ?? 0);
		args.text = Math.abs(val) >= 1000 ? `$${Math.round(val / 1000)}k` : `$${val}`;
	}

	/** Builds one bar, clamping the owed GEOMETRY while keeping the signed figure. */
	private toBar(name: string, fees: number, paid: number, owed: number): EventMoneyBar {
		return {
			name,
			fees,
			paid,
			owed: Math.max(0, owed),
			owedActual: owed,
			pctCollected: fees > 0 ? (paid / fees) * 100 : null,
		};
	}

	private money(v: number): string {
		return new Intl.NumberFormat('en-US', {
			style: 'currency', currency: 'USD', maximumFractionDigits: 0,
		}).format(v);
	}

	toggleChart(): void {
		this.isChartOpen.set(!this.isChartOpen());
	}

	/**
	 * Deep detail lives on Customer Job Revenue — already gated to the same audience
	 * (Superuser + SuperDirector). Segment array naming the jobPath explicitly, the
	 * same shape admin-nav-pill uses: a relative link would resolve under the
	 * dashboard route instead of the job root.
	 */
	readonly detailLink = computed(() => {
		const jobPath = this.auth.currentUser()?.jobPath ?? '';
		return ['/', jobPath, 'tools', 'customer-job-revenue'];
	});

	ngOnInit(): void {
		this.load();
	}

	load(): void {
		this.isLoading.set(true);
		this.hasError.set(false);

		this.svc.getJobRegCountsAndDollars().subscribe({
			next: d => {
				this.data.set(d);
				this.isLoading.set(false);
			},
			error: () => {
				this.hasError.set(true);
				this.isLoading.set(false);
			},
		});
	}

	toggle(): void {
		this.isOpen.set(!this.isOpen());
	}

	/** Days until the event starts; null when the job carries no event date. */
	daysOut(row: JobRegCountsAndDollarsRowDto): number | null {
		if (!row.eventStartDate) return null;
		const start = new Date(row.eventStartDate).getTime();
		const today = new Date().setHours(0, 0, 0, 0);
		return Math.round((start - today) / 86_400_000);
	}

	/**
	 * Short labels for reference.JobTypes. The stored names carry a trailing noun that
	 * is pure column width here ("Tournament Scheduling", "Showcase Registration") —
	 * the distinguishing word is the first one.
	 *
	 * An explicit map, NOT string surgery on the suffix: a new or renamed job type
	 * falls through to its full stored name, which is merely wide. Trimming blindly
	 * would silently mangle it instead.
	 */
	private static readonly TYPE_LABELS: Readonly<Record<string, string>> = {
		'Tournament Scheduling': 'Tournament',
		'League Scheduling': 'League',
		'Club Sport Registration': 'Club Sport',
		'Camp Registration': 'Camp',
		'Showcase Registration': 'Showcase',
		'Sales Venue': 'Sales',
		'Customer Root': 'Root',
	};

	/** Abbreviated job type, falling back to the stored name when unmapped. */
	typeLabel(row: JobRegCountsAndDollarsRowDto): string {
		return JobRegCountsAndDollarsComponent.TYPE_LABELS[row.jobTypeName] ?? row.jobTypeName;
	}

	/**
	 * Job names are stored "Customer:Event" ("Top Threat Tournaments:Fall Draw 2026").
	 * The customer half is identical on every row — the table is one customer's live
	 * jobs by definition — so it is dropped from the primary line and the event kept.
	 * Split on the FIRST colon only: event names may contain their own.
	 */
	eventName(row: JobRegCountsAndDollarsRowDto): string {
		const i = row.jobName.indexOf(':');
		return i < 0 ? row.jobName : row.jobName.slice(i + 1).trim();
	}

	/** The dropped customer half, or '' when the name carries no prefix. */
	customerName(row: JobRegCountsAndDollarsRowDto): string {
		const i = row.jobName.indexOf(':');
		return i < 0 ? '' : row.jobName.slice(0, i).trim();
	}

	/** Stable identity for @for. */
	trackRow(_i: number, row: JobRegCountsAndDollarsRowDto): string {
		return row.jobId;
	}
}
