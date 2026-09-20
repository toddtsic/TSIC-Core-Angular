import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';

import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import type { FeederPaceDto, FeederPaceSeasonDto, FeederPaceSiteDto } from '@core/api';

/** One segment of one season's column. */
interface SeasonBar {
	readonly season: string;
	readonly y: number;
	/** Prepared tooltip line — ej2 surfaces it as `${point.tooltip}`. */
	readonly tip: string;
}

/**
 * Four series, two stacks. Each season is one stacked column of registrations beside one
 * stacked column of dollars; within a stack the SOLID segment is where that season stood on
 * this calendar date and the faded segment is the rest of the season.
 */
interface SeasonBars {
	readonly countsToDate: SeasonBar[];
	readonly countsRest: SeasonBar[];
	readonly moneyToDate: SeasonBar[];
	readonly moneyRest: SeasonBar[];
}

/** One region's card: the same chart as the rollup, that region alone. */
interface SiteCard {
	readonly site: FeederPaceSiteDto;
	/** Trimmed for display — see `stripCommonPrefix`. */
	readonly label: string;
	readonly bars: SeasonBars;
	/** Live season, year to date. */
	readonly current: number;
	/** What the most recent COMPLETED season finished at — the size being paced against. */
	readonly lastFinal: number;
	/** What that same season had taken by THIS date — the like-for-like figure. */
	readonly lastToDate: number;
	readonly lastFinalSeason: number | null;
	readonly currentSeasonLabel: string;
	readonly hasPriorSeason: boolean;
	readonly isRetired: boolean;
	/** True once this region has taken a registration in the live season. */
	readonly isOpen: boolean;
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
 * Not series opacity: ej2 draws legend swatches from `series.fill` and does not carry the
 * series opacity onto them, so two series distinguished only by opacity produce identical
 * legend keys. The fade has to be a real colour.
 */
function mixOver(fg: string, bg: string, ratio: number): string {
	const a = parseColor(fg);
	const b = parseColor(bg);
	if (!a || !b) return fg;
	const ch = (i: number) => Math.round(a[i] * ratio + b[i] * (1 - ratio));
	return `#${[0, 1, 2].map(i => ch(i).toString(16).padStart(2, '0')).join('')}`;
}

/** Whole dollars, for a tooltip line. */
function money(n: number): string {
	return n.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

/**
 * Drop the part of the name every site shares — "American Select Lacrosse:" — so a grid of 28
 * cards spends its width on what differs. Only trims at a `:` or a space boundary, and only
 * when something is left over, so a genuinely common name is never cut to nothing.
 */
function stripCommonPrefix(names: readonly string[]): (name: string) => string {
	if (names.length < 2) return n => n;

	let prefix = names[0];
	for (const n of names) {
		let i = 0;
		while (i < prefix.length && i < n.length && prefix[i] === n[i]) i++;
		prefix = prefix.slice(0, i);
		if (!prefix) break;
	}

	const cut = Math.max(prefix.lastIndexOf(':'), prefix.lastIndexOf(' '));
	if (cut < 0) return n => n;

	const trim = cut + 1;
	return n => (n.length > trim ? n.slice(trim) : n);
}

/**
 * Year over Year — All Events.
 *
 * Every event this customer runs in the season, against prior seasons: one column group per
 * season, registrations on the left axis and money collected on the right. Once for the whole
 * customer, then the same chart again for every region.
 *
 * THE PAIR IS SPLIT BY SCOPE, NOT BY PERIOD. Both widgets compare seasons; both are year over
 * year. 'year-over-year' reads THIS EVENT against its own prior seasons as full-season
 * cumulative curves, which is right for a customer running one event a season, and it stays
 * exactly as it is. This one reads EVERY event the customer runs. Naming this one "Year to
 * Date" implied the sibling was not, which is false — do not reintroduce that split
 * (Todd, 2026-09-20).
 *
 * EVERY SEASON IS DRAWN TWICE, STACKED: solid = where that season stood on THIS calendar date,
 * faded = the rest of that season. Neither reading works alone, and both failures are real ones
 * this widget went through (Todd, 2026-09-20):
 *
 *   - Pin only. American Select opened season 2025 on 15 October and 2024 on 25 October, so at
 *     a 20 September pin both read zero. 17 of 28 regions came out with six empty columns.
 *   - Full season only. The live season is six weeks into ten months — 478 against 7,758, $72K
 *     against $2.6M. Every region then reads as a collapse rather than as an early season.
 *
 * Stacked, the solid segments compare like-for-like at the same date (the pin intact, which is
 * the whole point of it) while the full heights say what each season finished at. The live
 * season is the column with no faded cap — it has not happened yet. Nothing is blank and
 * nothing is overstated, and the opening date drifting stays visible as the signal it is
 * rather than being normalised away.
 *
 * THE ROLLUP COUNTS REGISTRATIONS, NOT ATHLETES. The final event is a peer of the tryout sites
 * and sits inside the total, and most of its registrants also registered at a regional. Every
 * label says registrations for that reason; none may say players or athletes.
 */
@Component({
	selector: 'app-feeder-pace',
	standalone: true,
	imports: [CurrencyPipe, DatePipe, DecimalPipe, ChartAllModule],
	templateUrl: './feeder-pace.component.html',
	styleUrl: './feeder-pace.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeederPaceComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly data = signal<FeederPaceDto | null>(null);
	readonly isLoading = signal(true);
	readonly hasError = signal(false);

	private readonly surface = signal(cssVar('--brand-surface', '#ffffff'));
	private readonly muted = signal(cssVar('--brand-text-muted', '#78716c'));
	private readonly border = signal(cssVar('--brand-border', '#e7e5e4'));
	private readonly primary = signal(cssVar('--bs-primary', '#0ea5e9'));
	private readonly success = signal(cssVar('--brand-success', '#22c55e'));

	// ── Headline ──────────────────────────────────────────────────────────────

	readonly currentSeason = computed(() => this.seasonOf(this.data()?.seasons ?? [], 0));
	readonly priorSeason = computed(() => this.seasonOf(this.data()?.seasons ?? [], 1));

	/** Where the year starts — August for a customer whose season runs Aug–Jul. */
	readonly ytdFrom = computed(() => this.currentSeason()?.ytdFrom ?? null);

	/**
	 * Pace against the prior season AT THE SAME CALENDAR DATE — the one comparison on this
	 * widget that is like-for-like, and the only place the pin is still read. Null when there
	 * is no prior season, or when it stood at zero on that date: a percentage against nothing
	 * is not a number, and "+∞%" is not an insight.
	 */
	readonly pacePct = computed(() => pct(this.currentSeason()?.registrations, this.priorSeason()?.registrations));

	readonly moneyPct = computed(() => pct(this.currentSeason()?.collected, this.priorSeason()?.collected));

	readonly isAhead = computed(() => (this.pacePct() ?? 0) >= 0);

	/**
	 * How far into the prior season its pin fell. The pace figure above is only as meaningful as
	 * this is stable, and for this customer it is not — so the number is on screen beside it
	 * rather than left for the reader to assume.
	 */
	readonly priorPinShare = computed(() => {
		const p = this.priorSeason();
		if (!p || p.totalRegistrations === 0) return null;
		return Math.round((p.registrations / p.totalRegistrations) * 100);
	});

	/** Regions that have taken a registration in the live season. */
	readonly sitesOpen = computed(() => this.siteCards().filter(s => s.isOpen).length);

	/** Regions running this season at all — the denominator for "3 of 25 open". */
	readonly sitesRunning = computed(() => this.siteCards().filter(s => !s.isRetired).length);

	// ── Charts ────────────────────────────────────────────────────────────────

	readonly rollupBars = computed(() => this.barsFor(this.data()?.seasons ?? []));

	readonly hasRollup = computed(() => this.rollupBars().countsToDate.length > 0);

	/**
	 * Four series across two stacking groups — see the class remark for why both readings have
	 * to be on the column at once.
	 *
	 * Colour is per SERIES, not per point: the four legend swatches are the key to solid-versus-
	 * faded, so a point colour that contradicted its own swatch would make the legend a lie. The
	 * live season needs no highlight of its own — it is the only column with nothing stacked on
	 * top, which is precisely what "still selling" looks like.
	 *
	 * `rest` is clamped at zero. It is a subtraction of two independently-read figures, and a
	 * stacked series given a negative draws BELOW the axis, which would be read as a refund.
	 */
	private barsFor(seasons: readonly FeederPaceSeasonDto[]): SeasonBars {
		const current = this.data()?.currentSeason;
		const by = this.asOfLabel();
		const n = (v: number) => v.toLocaleString();

		const rest = (total: number, toDate: number) => Math.max(0, total - toDate);

		// Each tip is the WHOLE hover line. The chart renders `${point.x}` above it and nothing
		// else — series name and raw `${point.y}` are both off, because the series name repeated
		// the phrase the tip already ends with and `${point.y}` repeated the figure it opens
		// with. One segment, one sentence (Todd, 2026-09-20: "popover needs work, redundant").
		return {
			countsToDate: seasons.map(s => ({
				season: String(s.season),
				y: s.registrations,
				// jobCount 0 means no job carried THIS NAME that season — every site carries a
				// row for every charted season so the charts share one x-axis, and without this
				// the empty slot hovers as a real zero.
				//
				// It says "under this name" and not "did not run", because a gap is exactly
				// where a rename, split or merge hides and the two are indistinguishable from
				// here. American Select ran California 2021-2025, split it into NorCal and
				// SoCal for 2026, and merged it back for 2027; regions are grouped by job NAME
				// and nothing in Jobs.Jobs records that lineage (Todd, 2026-09-20).
				tip: s.jobCount === 0
					? 'no event under this name this season'
					: s.season === current
						? `${n(s.registrations)} registrations so far`
						: `${n(s.registrations)} registrations by ${by} · finished at ${n(s.totalRegistrations)}`,
			})),
			countsRest: seasons.map(s => ({
				season: String(s.season),
				y: rest(s.totalRegistrations, s.registrations),
				tip: `${n(rest(s.totalRegistrations, s.registrations))} more after ${by} · finished at ${n(s.totalRegistrations)}`,
			})),
			moneyToDate: seasons.map(s => ({
				season: String(s.season),
				y: Number(s.collected),
				tip: s.season === current
					? `${money(Number(s.collected))} collected so far · ${money(Number(s.billed))} billed`
					: `${money(Number(s.collected))} collected by ${by} · finished at ${money(Number(s.totalCollected))}`,
			})),
			moneyRest: seasons.map(s => ({
				season: String(s.season),
				y: rest(Number(s.totalCollected), Number(s.collected)),
				tip: `${money(rest(Number(s.totalCollected), Number(s.collected)))} more after ${by} · finished at ${money(Number(s.totalCollected))}`,
			})),
		};
	}

	/** "Sep 20" — the pin, spelled once and reused by every tooltip. */
	private asOfLabel(): string {
		const raw = this.data()?.asOfDate;
		if (!raw) return 'this date';
		const d = new Date(raw);
		return isNaN(d.getTime())
			? 'this date'
			: d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
	}

	// Solid is the season-to-date segment; the cap is the same hue mixed FAR back — 0.12, not
	// the 0.22 this started at. At card width a column is ~30px and the eye is comparing a
	// segment against its own hue two shades away; anything closer collapses into one block
	// (Todd, 2026-09-20: "can't distinguish ytd vs final"). The solid segment also carries a
	// border in the same full-strength colour, so its top edge stays a hard line at any size —
	// shade ALONE is not carrying this distinction.
	readonly countsFill = computed(() => this.primary());
	readonly countsRestFill = computed(() => mixOver(this.primary(), this.surface(), 0.12));
	readonly moneyFill = computed(() => this.success());
	readonly moneyRestFill = computed(() => mixOver(this.success(), this.surface(), 0.12));

	readonly countsBorder = computed(() => ({ width: 1, color: this.primary() }));
	readonly moneyBorder = computed(() => ({ width: 1, color: this.success() }));

	readonly primaryXAxis = computed(() => ({
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0.5, color: this.border() },
		labelStyle: { color: this.muted(), size: '11px' },
	}));

	readonly primaryYAxis = computed(() => ({
		title: 'Registrations',
		titleStyle: { color: this.muted(), size: '11px' },
		majorGridLines: { width: 0.5, color: this.border(), dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.muted(), size: '11px' },
		minimum: 0,
	}));

	/** The dollars axis, opposed. Its gridlines are off — two grids over one plot is noise. */
	readonly secondaryAxes = computed(() => ([{
		name: 'dollars',
		opposedPosition: true,
		title: 'Collected',
		titleStyle: { color: this.muted(), size: '11px' },
		labelFormat: 'c0',
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.muted(), size: '11px' },
		minimum: 0,
	}]));

	// ── Card charts ───────────────────────────────────────────────────────────
	//
	// Same two axes, same colours, same reading — stripped of the furniture that only needs
	// saying once. Axis TITLES come off (the rollup above names both axes), the legend comes
	// off, and the dollar labels come off: 28 cards of "$70,890" down the right edge is noise,
	// and the figure is one hover away.

	readonly cardXAxis = computed(() => ({
		...this.primaryXAxis(),
		labelStyle: { color: this.muted(), size: '10px' },
	}));

	readonly cardYAxis = computed(() => ({
		...this.primaryYAxis(),
		title: '',
		labelStyle: { color: this.muted(), size: '10px' },
	}));

	readonly cardAxes = computed(() => ([{
		...this.secondaryAxes()[0],
		title: '',
		labelStyle: { color: 'transparent', size: '10px' },
	}]));

	// The season, then one sentence. `${series.name}` and `${point.y}` are deliberately absent —
	// see the note in barsFor. `header: ''` suppresses ej2's own default header, which would
	// otherwise put the series name back above the line.
	readonly tooltipSettings = {
		enable: true,
		shared: false,
		header: '',
		format: '<b>${point.x}</b><br/>${point.tooltip}',
	};

	readonly legendSettings = {
		visible: true,
		position: 'Bottom' as const,
		textStyle: { size: '11px' },
	};

	readonly cardLegendSettings = { visible: false };

	readonly chartArea = { border: { width: 0 } };
	readonly margin = { left: 8, right: 8, top: 4, bottom: 4 };
	readonly cardMargin = { left: 4, right: 4, top: 4, bottom: 4 };

	// ── Region grid ───────────────────────────────────────────────────────────

	/**
	 * One card per region, in the order the backend set: open regions first and biggest first,
	 * then everything still to open at the size it reached last time it ran.
	 */
	readonly siteCards = computed((): SiteCard[] => {
		const d = this.data();
		if (!d) return [];

		const label = stripCommonPrefix(d.sites.map(s => s.site));

		return d.sites.map(site => {
			const current = this.seasonOf(site.seasons, 0);
			// The most recent earlier season THIS REGION ACTUALLY RAN — jobCount > 0. Every
			// site carries a row for every charted season so the charts share one x-axis, so
			// the newest row below the current one may be a placeholder of zeros. Taking it
			// blind captioned California "2026 final 0" when California skipped 2026 entirely
			// and last finished at 146 in 2025 (Todd, 2026-09-20).
			const completed = site.seasons
				.filter(s => s.season < d.currentSeason && s.jobCount > 0)
				.at(-1) ?? null;
			const cur = current?.registrations ?? 0;

			return {
				site,
				label: label(site.site),
				bars: this.barsFor(site.seasons),
				current: cur,
				lastFinal: completed?.totalRegistrations ?? 0,
				lastToDate: completed?.registrations ?? 0,
				lastFinalSeason: completed?.season ?? null,
				currentSeasonLabel: `${d.currentSeason} so far`,
				hasPriorSeason: site.hasPriorSeason,
				isRetired: site.isRetired,
				isOpen: !site.isRetired && cur > 0,
			};
		});
	});

	readonly ungrouped = computed(() => this.data()?.ungroupedJobNames ?? []);

	/** Composing job names, for the title attribute — the name-grouping safety rail. */
	jobNamesOf(card: SiteCard): string {
		return card.site.jobNames.join('\n');
	}

	/**
	 * Nth-newest season of a series, 0 being the newest. The prior season is NOT assumed to be
	 * current − 1: a customer can skip a year.
	 */
	private seasonOf(seasons: readonly FeederPaceSeasonDto[], back: number): FeederPaceSeasonDto | null {
		return seasons.at(-1 - back) ?? null;
	}

	load(): void {
		this.isLoading.set(true);
		this.hasError.set(false);
		this.svc.getFeederPace().subscribe({
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

	ngOnInit(): void {
		this.load();
	}
}

/** Percentage change, or null when the base is nothing to compare against. */
function pct(current: number | undefined, prior: number | undefined): number | null {
	if (current == null || prior == null || prior === 0) return null;
	return Math.round(((current - prior) / prior) * 100);
}
