import { Component, ChangeDetectionStrategy, EventEmitter, signal, computed, inject, viewChild, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '@environments/environment';
import {
	GridAllModule,
	GridComponent,
	EditSettingsModel,
	ToolbarItems,
	PdfExportService as GridPdfExportService,
	ExcelExportService as GridExcelExportService
} from '@syncfusion/ej2-angular-grids';
import {
	PivotViewAllModule,
	PivotViewComponent,
	IDataOptions,
	ColumnRenderEventArgs,
	FieldListService,
	ToolbarService,
	PDFExportService,
	ExcelExportService
} from '@syncfusion/ej2-angular-pivotview';
import { MultiSelectAllModule } from '@syncfusion/ej2-angular-dropdowns';
import { ChartAllModule, MultiLevelLabelService } from '@syncfusion/ej2-angular-charts';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AdminNavPillComponent } from '@shared-ui/components/admin-nav-pill.component';
import type { RevenueRollupResponseDto } from '@core/api';
import type { JobPaymentRecordDto } from '@core/api';
import type { UpdateMonthlyCountRequest } from '@core/api';
import type { LegacyCompareResultDto } from '@core/api';
import type { TeamBillingRecordDto } from '@core/api';
import type { YoyRevenueResponseDto, YoyEventGroupDto } from '@core/api';
import type { AdjustmentRecordDto } from '@core/api';

interface MonthOption {
	startDate: string;
	endDate: string;
	label: string;
	startLabel: string; // first of month, e.g. 6/1/2026 — shown in the Start ddl
	endLabel: string;   // last of month, e.g. 6/30/2026 — shown in the End ddl
}

type ScopeMode = 'jobs' | 'period';
type DetailKey = 'cc' | 'check' | 'echeck';

/**
 * What the Teams/Players to Customer tab is, in one line, shown on screen and carried into
 * every export.
 *
 * The tab answers a different question from the other six. They show TSIC's settlement with
 * the client — what we owe them, netted by our fees. This shows what the client's own
 * registrants owe THEM. Both use the word "owed" pointing opposite directions, which is
 * exactly why it has to be said out loud rather than inferred.
 *
 * Its rows are events: a charge lands in the month the team or player registered, a payment
 * in the month it was received, and Owed is the balance right now. So within any single
 * month the three columns need not relate to one another.
 */
const TEAM_BILLING_BASIS = 'what your teams and players owe you';

/**
 * What the Year-over-Year Review is, in one line, shown on screen.
 *
 * Same book as Teams/Players to Customer — the client's own receivable, not TSIC settlement —
 * but read at a POINT rather than over a period. Each season is measured at the same calendar
 * date, the end date shifted back whole years, so a season still selling is compared against
 * where the prior one stood on that date rather than against what it finished with.
 */
const YOY_BASIS = 'how each event is doing versus the same point in prior seasons';

/**
 * What the Adjustments tab is, in one line, shown on screen.
 *
 * The entity-level detail behind the Adj column on Teams/Players — every team and registrant
 * whose net fee adjustment is not zero. A rollup, never a typed breakdown: fee_discount is a
 * blended column (early bird is stamped from the cascade, discount codes add onto it after),
 * so which part came from where is not recoverable from the data.
 */
const ADJUSTMENTS_BASIS = 'who carries a fee adjustment, and how much';

/**
 * Bars visible before the single chart starts scrolling. Every season of every lineage now
 * shares one axis, so this is a width budget for the whole report rather than a per-event one;
 * past it the bars thin faster than the extra history earns its place and the rest goes behind
 * the scrollbar.
 */
const YOY_VISIBLE_BARS = 16;

/** One season of one lineage, shaped for the chart. */
interface YoyChartPoint {
	/**
	 * The category VALUE, and it must be unique across the whole chart. A Category axis keys
	 * points by their x value, so two lineages both showing a 2025 season would collapse into
	 * one bar. Prefixed with the lineage index; onYoyAxisLabel strips the prefix back off, so
	 * what remains IS the axis label and everything the reader must see has to be in here —
	 * the season and its in-flight asterisk both.
	 */
	key: string;
	/** The cutoff this bar was measured at, printed on its own row under the axis. */
	pinLabel: string;
	rawYear: number;
	billed: number;
	collected: number;
	owed: number;
	adj: number;
	refunds: number;
	asOf: string;
	active: boolean;
	jobNames: string[];
}

/** One event lineage and its seasons — one span of the shared axis. */
interface YoyChartGroup {
	label: string;
	anchorYear: number;
	points: YoyChartPoint[];
	jobLines: string[];
}

/** The whole report as ONE chart: every season of every lineage on a shared axis. */
interface YoyChartView {
	points: YoyChartPoint[];
	/** Per-bar cutoff row, directly under the axis labels. */
	pinLevel: { start: number; end: number; text: string }[];
	/** Lineage row beneath that, one span per event. */
	groupLevel: { start: number; end: number; text: string }[];
	needsScroll: boolean;
	zoomFactor: number;
	groups: YoyChartGroup[];
}

/** Read a CSS custom property off :root so chart fills follow the live palette. */
function cssVar(name: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(name)?.trim() || fallback;
}

/**
 * The app's own type stack, applied to every piece of chart text.
 *
 * ej2 renders its labels into SVG with the theme's built-in family, so a chart left alone sits
 * in a different typeface from the page around it. Axis labels, legend, tooltip and stack
 * labels all take this, so the chart reads as part of the report rather than an embed.
 */
const YOY_FONT_FAMILY = cssVar('--font-family-sans', 'system-ui, -apple-system, sans-serif');

/**
 * The tournament, without the customer that owns it.
 *
 * Job names are "Signature Sports:Lax Clash 2027" — the customer, then the event. Every row on
 * this report belongs to one customer, so the prefix is the same on every group and spends the
 * width that the event name needs. Stripped for DISPLAY only: the group key keeps the full
 * name, and the "Events on this chart" list prints jobs unabridged, which is where a reader
 * checks that a lineage was assembled correctly.
 */
function eventLabel(groupLabel: string): string {
	const cut = groupLabel.lastIndexOf(':');
	const tail = cut >= 0 ? groupLabel.slice(cut + 1).trim() : '';
	return tail.length > 0 ? tail : groupLabel;
}

/**
 * The season's cutoff, short. Formatted off the ISO date PARTS, never through `new Date(iso)` —
 * parsing a bare date string as UTC and rendering it local slides it a day backwards west of
 * Greenwich, which would print the pin as 8/30 for half the country.
 */
function shortPin(iso: string): string {
	const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
	return Number.isFinite(y) && Number.isFinite(m) && Number.isFinite(d)
		? `${m}/${d}/${String(y).slice(2)}`
		: iso;
}

/**
 * The scope the currently-displayed data was fetched with. Rendered as the audit-stamp
 * banner and embedded in every export header — a revenue figure never travels without
 * a statement of what it covers. (Born of two real overpayment incidents where silently
 * unscoped totals were treated as single-job revenue.)
 */
interface SubmittedScope {
	mode: ScopeMode;
	jobs: string[];
	startDate: string | null;
	endDate: string | null;
	label: string;
}

@Component({
	selector: 'app-customer-job-revenue',
	standalone: true,
	imports: [CommonModule, FormsModule, GridAllModule, PivotViewAllModule, MultiSelectAllModule, ChartAllModule, AdminNavPillComponent],
	// The export services are NOT bundled by the *AllModules (pivot or grid) — without
	// them pdfExport()/excelExport() are silent no-ops.
	providers: [
		MultiLevelLabelService,
		FieldListService, ToolbarService, PDFExportService, ExcelExportService,
		GridPdfExportService, GridExcelExportService
	],
	templateUrl: './customer-job-revenue.component.html',
	styleUrls: ['./customer-job-revenue.component.scss'],
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class CustomerJobRevenueComponent {
	private readonly http = inject(HttpClient);
	private readonly auth = inject(AuthService);
	private readonly pulseService = inject(JobPulseService);
	private readonly apiUrl = `${environment.apiUrl}/customer-job-revenue`;

	/**
	 * Reciprocal of the JobRegCountsAndDollars widget's link out — the widget gives the
	 * glance, this page gives the drill, and the pill is the way back.
	 *
	 * Gated on the SAME pulse flag as the other two dashboard doors (job-landing pill,
	 * client-header-bar menu entry) so all three can never disagree about whether a
	 * dashboard exists. `=== true` on purpose: the flag is null while the pulse is in
	 * flight and null for a non-admin, and neither is "yes". The route already limits
	 * this page to Superuser/SuperDirector, so no separate role clause is needed.
	 */
	readonly showDashboardLink = computed(() =>
		this.pulseService.pulse()?.myHasDashboardWidgets === true);

	readonly dashboardLink = computed(() =>
		['/', this.auth.currentUser()?.jobPath ?? '', 'dashboard']);

	// UI state
	isLoading = signal(false);
	errorMessage = signal('');
	activeTab = signal<'rollup' | 'counts' | 'adminFees' | 'ccRecords' | 'checkRecords' | 'echeckRecords' | 'teamBilling' | 'adjustments' | 'yoy'>('rollup');

	// Guided scope flow — lands on All jobs · date range (pickers preset to last month);
	// nothing runs until the user clicks Run Report.
	scopeMode = signal<ScopeMode | null>('period');
	availableJobs = signal<string[]>([]);
	submittedScope = signal<SubmittedScope | null>(null);

	// SU live QA vs legacy sprocs — sandbox environments only (backend 404s in Production)
	readonly qaEnabled = environment.envName !== 'production';
	qaRunning = signal(false);
	qaResult = signal<LegacyCompareResultDto | null>(null);

	// Data — rollup arrives on Submit; detail tabs are fetched lazily on first open
	rollup = signal<RevenueRollupResponseDto | null>(null);
	private readonly ccDetail = signal<JobPaymentRecordDto[] | null>(null);
	private readonly checkDetail = signal<JobPaymentRecordDto[] | null>(null);
	private readonly echeckDetail = signal<JobPaymentRecordDto[] | null>(null);
	detailLoading = signal<DetailKey | null>(null);

	// Team Billing tab — lazily fetched on first open, same invalidation rules as the
	// detail tabs. Separate endpoint: this one is team-driven, so it carries teams the
	// payment-driven rollup can't see (registered, never paid).
	private readonly teamBilling = signal<TeamBillingRecordDto[] | null>(null);
	teamBillingLoading = signal(false);
	readonly teamBillingBasis = TEAM_BILLING_BASIS;

	// The reading rules are needed ONCE. Left expanded they are ~200px of permanent chrome
	// between the reader and the numbers, so only the basis line is always on — enough to
	// stop anyone reading this tab as the rollup — and the rest is one click away. Collapsed
	// by default; the state is per-visit, deliberately not persisted.
	teamBillingHelpOpen = signal(false);

	toggleTeamBillingHelp(): void {
		this.teamBillingHelpOpen.update(open => !open);
	}
	teamBillingRecords = computed(() => this.teamBilling() ?? []);

	// Adjustments tab — the entity-level detail behind Teams/Players' Adj column. Same lazy
	// fetch and same invalidation as the detail tabs.
	private readonly adjustments = signal<AdjustmentRecordDto[] | null>(null);
	adjustmentsLoading = signal(false);
	readonly adjustmentsBasis = ADJUSTMENTS_BASIS;
	adjustmentsHelpOpen = signal(false);

	toggleAdjustmentsHelp(): void {
		this.adjustmentsHelpOpen.update(open => !open);
	}
	adjustmentRecords = computed(() => this.adjustments() ?? []);

	// Derived
	monthlyCounts = computed(() => this.rollup()?.monthlyCounts ?? []);
	adminFees = computed(() => this.rollup()?.adminFees ?? []);
	creditCardRecords = computed(() => this.ccDetail() ?? []);
	checkRecords = computed(() => this.checkDetail() ?? []);
	echeckRecords = computed(() => this.echeckDetail() ?? []);

	/**
	 * Team Billing pivot. Deliberately a DIFFERENT shape from the revenue rollup:
	 *
	 * - Rows go one level deeper — club, then the team itself — because the grain is the
	 *   team, not the payment. Club is a real level (a club-rep payment belongs wholly to
	 *   one club, so its subtotal is honest); age group is folded into the team's LABEL
	 *   rather than made a level, because one club-rep payment can span up to 11 age
	 *   groups and a per-age-group subtotal would be a number the data can't support.
	 * - Three value fields instead of a pay-category axis.
	 * - Year/Month come from the EVENT: a charge is dated at registration, a payment at the
	 *   ledger row. That is what lets a deposit and its balance sit in different months, an
	 *   ARB plan spread across its drafts, and an unpaid team exist on the timeline at all —
	 *   being charged is itself a dated event, so it needs no special case.
	 */
	readonly teamBillingDataSource = signal<IDataOptions>({
		dataSource: [],
		enableSorting: true,
		expandAll: false,
		emptyCellsTextContent: '$0.00',
		rows: [
			{ name: 'jobName', caption: 'Job' },
			{ name: 'year', caption: 'Year' },
			{ name: 'month', caption: 'Month' },
			{ name: 'clubName', caption: 'Club' },
			{ name: 'teamLabel', caption: 'Team' }
		],
		columns: [],
		// With nothing on the column axis the pivot captions the value headers itself as
		// "Total Sum of {caption}" — the aggregation name plus the grand-total prefix. These
		// are three balances, not an aggregation the reader needs narrated, so suppress it.
		showAggregationOnValueField: false,
		// Ordered charges → receipts → balance, with each memo column sitting beside the
		// figure it is part of, and CAPTIONED as a memo ("of which") so no one adds it in.
		//
		// Adj replaced separate Discounts and Corrections columns (Todd, 2026-09-01). It is
		// one signed number — lateFee - discount - correction — matching the "Fee-Adj" the
		// player and club-rep grids already show. It is a memo against BOTH neighbours at
		// once: its charge-side terms are already inside Billed, its correction term already
		// inside Collected. That is exactly why it is not a column anyone can total against
		// the others, and why the Adjustments tab exists to break it down by entity.
		//
		// Owed goes LAST, not beside Billed and Collected (Todd, 2026-08-31). It is the
		// bottom line, and Billed - Collected = Owed does NOT hold row by row — Owed sits
		// only on each team's charge month — so adjacency implied arithmetic the data has
		// nowhere except at the totals.
		values: [
			{ name: 'billed', caption: 'Billed', type: 'Sum' },
			{ name: 'adj', caption: 'of which Adj', type: 'Sum' },
			{ name: 'collected', caption: 'Collected', type: 'Sum' },
			{ name: 'refunds', caption: 'of which Refunds', type: 'Sum' },
			{ name: 'owed', caption: 'Owed', type: 'Sum' }
		],
		formatSettings: [
			{ name: 'billed', format: 'C2', useGrouping: true },
			{ name: 'adj', format: 'C2', useGrouping: true },
			{ name: 'collected', format: 'C2', useGrouping: true },
			{ name: 'refunds', format: 'C2', useGrouping: true },
			{ name: 'owed', format: 'C2', useGrouping: true }
		]
	});

	// Date range options (monthly buckets from last month back to Jan 2022)
	monthOptions: MonthOption[] = [];

	// Selected filters (template ngModel fields)
	selectedStartDate = '';
	selectedEndDate = '';
	selectedJobs: string[] = [];

	// Taller pivot (AM-050 part 4): grow with the viewport instead of a fixed 600px
	private readonly viewportHeight = signal(typeof window !== 'undefined' ? window.innerHeight : 900);
	pivotHeight = computed(() => Math.max(500, this.viewportHeight() - 340));

	@HostListener('window:resize')
	onWindowResize(): void {
		this.viewportHeight.set(window.innerHeight);
	}

	// Ann: job names display in full without wrapping. The flat columnWidth applies to
	// every column including the row-header one, so the header column is re-sized per
	// run to fit the longest job name in the result set (see measureRowHeaderWidth).
	//
	// The handler must be an EventEmitter, not a plain callback: the ej2 Angular
	// wrapper's trigger() invokes handler.next(args), so a bare function throws
	// mid-layout and blanks the whole grid.
	private rowHeaderWidth = 240;
	private readonly onPivotColumnRender = new EventEmitter<ColumnRenderEventArgs>();
	readonly pivotGridSettings = {
		columnWidth: 120,
		allowTextWrap: true,
		columnRender: this.onPivotColumnRender as unknown as (args: ColumnRenderEventArgs) => void
	};

	// Pivot config
	readonly pivotDataSource = signal<IDataOptions>({
		dataSource: [],
		enableSorting: true,
		expandAll: false,
		emptyCellsTextContent: '$0.00',
		rows: [
			{ name: 'jobName', caption: 'Job' },
			{ name: 'year', caption: 'Year' },
			{ name: 'month', caption: 'Month' }
		],
		columns: [
			{ name: 'payMethod', caption: 'Pay Category' }
		],
		values: [
			{ name: 'payAmount', caption: 'Payment', type: 'Sum' }
		],
		formatSettings: [
			{ name: 'payAmount', format: 'C2', useGrouping: true }
		]
	});

	// Grid edit settings (counts tab — SuperUser only)
	isSuperUser = computed(() => {
		const user = this.auth.currentUser();
		return user?.role === 'Superuser';
	});
	countsEditSettings: EditSettingsModel = { allowEditing: true, allowAdding: false, allowDeleting: false };
	// Exports moved to the shared per-tab buttons; the counts toolbar only carries
	// the SuperUser inline-edit commands (no toolbar at all for everyone else).
	countsToolbar = computed<ToolbarItems[] | undefined>(() =>
		this.isSuperUser() ? ['Edit', 'Cancel', 'Update'] : undefined
	);

	readonly pivotView = viewChild.required<PivotViewComponent>('pivotView');
	// Not `.required` — the Team Billing pivot only exists while its tab is open.
	readonly teamBillingPivot = viewChild<PivotViewComponent>('teamBillingPivot');
	readonly countsGrid = viewChild.required<GridComponent>('countsGrid');
	readonly adminFeesGrid = viewChild.required<GridComponent>('adminFeesGrid');
	readonly ccGrid = viewChild.required<GridComponent>('ccGrid');
	readonly checkGrid = viewChild.required<GridComponent>('checkGrid');
	readonly echeckGrid = viewChild.required<GridComponent>('echeckGrid');
	readonly adjustmentsGrid = viewChild<GridComponent>('adjustmentsGrid');

	constructor() {
		// Emitter and subscriber share this component's lifetime — no teardown needed.
		this.onPivotColumnRender.subscribe((args: ColumnRenderEventArgs) => {
			if (args.columns.length === 0) {
				return;
			}
			args.columns[0].width = this.rowHeaderWidth;
			// The pivot pre-stretched the value columns to fill the container using its
			// DEFAULT first-column width; widening col 0 after that overflows the total
			// by the difference and shows a phantom h-scrollbar. Re-stretch the value
			// columns against the real row-header width (120px floor — below that the
			// scrollbar is legitimate).
			const host = document.querySelector<HTMLElement>('ejs-pivotview');
			const valueCols = args.columns.length - 1;
			if (host && valueCols > 0) {
				const avail = host.clientWidth - this.rowHeaderWidth - 20; // v-scrollbar + borders
				const width = Math.max(120, Math.floor(avail / valueCols));
				for (let i = 1; i < args.columns.length; i++) {
					args.columns[i].width = width;
				}
			}
		});
		this.buildMonthOptions();
		// Default the period pickers to last month; no report runs until the user scopes one.
		if (this.monthOptions.length > 0) {
			this.selectedStartDate = this.monthOptions[0].startDate;
			this.selectedEndDate = this.monthOptions[0].endDate;
		}
		this.loadAvailableJobs();
	}

	private buildMonthOptions(): void {
		const now = new Date();
		let cursor = new Date(now.getFullYear(), now.getMonth() - 1, 1);
		const oldest = new Date(2022, 0, 1); // Jan 2022

		while (cursor >= oldest) {
			const endOfMonth = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0);
			this.monthOptions.push({
				startDate: this.formatDate(cursor),
				endDate: this.formatDate(endOfMonth),
				label: cursor.toLocaleDateString('en-US', { month: 'short', year: 'numeric' }),
				startLabel: cursor.toLocaleDateString('en-US'),
				endLabel: endOfMonth.toLocaleDateString('en-US')
			});
			cursor = new Date(cursor.getFullYear(), cursor.getMonth() - 1, 1);
		}
	}

	private formatDate(d: Date): string {
		const y = d.getFullYear();
		const m = String(d.getMonth() + 1).padStart(2, '0');
		const day = String(d.getDate()).padStart(2, '0');
		return `${y}-${m}-${day}`;
	}

	/** '2026-06-01' → '6/1/26' (Ann's Legacy-parity range format). */
	private formatShort(iso: string): string {
		const [y, m, d] = iso.split('-').map(Number);
		return `${m}/${d}/${String(y).slice(2)}`;
	}

	private loadAvailableJobs(): void {
		this.http.get<string[]>(`${this.apiUrl}/jobs`).subscribe({
			next: (jobs) => this.availableJobs.set(jobs),
			error: (err) => this.errorMessage.set(err.error?.message || 'Failed to load job list')
		});
	}

	setScopeMode(mode: ScopeMode): void {
		if (this.scopeMode() === mode) {
			return; // re-clicking the selected card keeps current results
		}
		this.scopeMode.set(mode);
		// Changing scope invalidates everything on screen — hide results until re-run,
		// so numbers never linger under a scope they weren't fetched with.
		this.submittedScope.set(null);
		this.rollup.set(null);
		this.ccDetail.set(null);
		this.checkDetail.set(null);
		this.echeckDetail.set(null);
		this.teamBilling.set(null);
		this.yoy.set(null);
		this.qaResult.set(null);
		this.errorMessage.set('');
		this.activeTab.set('rollup');
	}

	canSubmit(): boolean {
		if (this.isLoading()) {
			return false;
		}
		const mode = this.scopeMode();
		if (mode === 'jobs') {
			return this.selectedJobs.length > 0;
		}
		if (mode === 'period') {
			return !!this.selectedStartDate && !!this.selectedEndDate
				&& this.selectedStartDate <= this.selectedEndDate;
		}
		return false;
	}

	onSubmit(): void {
		const mode = this.scopeMode();
		if (!mode || !this.canSubmit()) {
			return;
		}

		const today = new Date();
		const todayShort = `${today.getMonth() + 1}/${today.getDate()}/${String(today.getFullYear()).slice(2)}`;
		const scope: SubmittedScope = mode === 'jobs'
			? {
				mode,
				jobs: [...this.selectedJobs],
				startDate: null,
				endDate: null,
				label: `${this.selectedJobs.join(' + ')} — complete history (through ${todayShort})`
			}
			: {
				mode,
				jobs: [],
				startDate: this.selectedStartDate,
				endDate: this.selectedEndDate,
				label: `All jobs · ${this.formatShort(this.selectedStartDate)} – ${this.formatShort(this.selectedEndDate)}`
			};

		this.isLoading.set(true);
		this.errorMessage.set('');

		this.http.get<RevenueRollupResponseDto>(`${this.apiUrl}/rollup`, { params: this.scopeParams(scope) }).subscribe({
			next: (data) => {
				this.rollup.set(data);
				this.submittedScope.set(scope);
				// New scope invalidates every lazily-cached detail tab and any QA verdict.
				this.ccDetail.set(null);
				this.checkDetail.set(null);
				this.echeckDetail.set(null);
				this.teamBilling.set(null);
				this.adjustments.set(null);
				this.yoy.set(null);
				this.qaResult.set(null);
				this.rowHeaderWidth = this.measureRowHeaderWidth(data.revenueRecords);
				this.pivotDataSource.set({
					...this.pivotDataSource(),
					dataSource: data.revenueRecords as never[]
				});
				this.activeTab.set('rollup');
				this.isLoading.set(false);
			},
			error: (err) => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message || 'Failed to load revenue data');
			}
		});
	}

	private scopeParams(scope: SubmittedScope): HttpParams {
		let params = new HttpParams();
		if (scope.mode === 'jobs') {
			for (const job of scope.jobs) {
				params = params.append('jobNames', job);
			}
		} else {
			params = params.set('startDate', scope.startDate!).set('endDate', scope.endDate!);
		}
		return params;
	}

	setTab(tab: typeof this.activeTab extends ReturnType<typeof signal<infer T>> ? T : never): void {
		this.activeTab.set(tab);
		const detailKey: DetailKey | null =
			tab === 'ccRecords' ? 'cc'
			: tab === 'checkRecords' ? 'check'
			: tab === 'echeckRecords' ? 'echeck'
			: null;
		if (detailKey) {
			this.fetchDetailIfNeeded(detailKey);
		}
		if (tab === 'teamBilling') {
			this.fetchTeamBillingIfNeeded();
		}
		if (tab === 'adjustments') {
			this.fetchAdjustmentsIfNeeded();
		}
		if (tab === 'yoy') {
			this.fetchYoyIfNeeded();
		}
	}

	private fetchTeamBillingIfNeeded(): void {
		const scope = this.submittedScope();
		if (!scope || this.teamBilling() !== null || this.teamBillingLoading()) {
			return;
		}
		this.teamBillingLoading.set(true);
		this.http.get<TeamBillingRecordDto[]>(`${this.apiUrl}/team-billing`, { params: this.scopeParams(scope) }).subscribe({
			next: (records) => {
				this.teamBilling.set(records);
				this.teamBillingDataSource.set({
					...this.teamBillingDataSource(),
					dataSource: records as never[]
				});
				this.teamBillingLoading.set(false);
			},
			error: (err) => {
				this.teamBillingLoading.set(false);
				this.errorMessage.set(err.error?.message || 'Failed to load team billing');
			}
		});
	}

	/**
	 * Adjustments detail. Unscoped by date on purpose: two of the three terms (fee_discount,
	 * fee_latefee) are stamped columns with no timestamp, so the rows carry no Year/Month —
	 * see the repository for why inventing one would be a fiction. The scope still applies,
	 * through which entities are included and where the correction rows are cut.
	 */
	private fetchAdjustmentsIfNeeded(): void {
		const scope = this.submittedScope();
		if (!scope || this.adjustments() !== null || this.adjustmentsLoading()) {
			return;
		}
		this.adjustmentsLoading.set(true);
		this.http.get<AdjustmentRecordDto[]>(`${this.apiUrl}/adjustments`, { params: this.scopeParams(scope) }).subscribe({
			next: (records) => {
				this.adjustments.set(records);
				this.adjustmentsLoading.set(false);
			},
			error: (err) => {
				this.adjustmentsLoading.set(false);
				this.errorMessage.set(err.error?.message || 'Failed to load adjustments');
			}
		});
	}

	// ===================================================================
	// YEAR-OVER-YEAR REVIEW
	//
	// One chart per event lineage, years oldest to newest, three bars a year.
	// Every column is measured at the SAME calendar point — the end date shifted back whole
	// years — so a season still selling is read against where last season stood on that date,
	// not against what it finished with.
	// ===================================================================

	private readonly yoy = signal<YoyRevenueResponseDto | null>(null);
	yoyLoading = signal(false);
	readonly yoyBasis = YOY_BASIS;

	yoyHelpOpen = signal(false);
	toggleYoyHelp(): void {
		this.yoyHelpOpen.update(open => !open);
	}

	/**
	 * Series colours, read once from the live palette. Semantic rather than decorative:
	 * charges, money in, money still out. Never rely on the colour alone — the legend names
	 * every series and the axis marks in-flight seasons in text.
	 */
	readonly yoyCollectedColor = signal(cssVar('--brand-success', '#22c55e'));
	readonly yoyOwedColor = signal(cssVar('--brand-danger', '#ef4444'));
	private readonly yoyMuted = signal(cssVar('--brand-text-muted', '#78716c'));
	private readonly yoyText = signal(cssVar('--brand-text', '#1c1917'));
	private readonly yoyBorder = signal(cssVar('--brand-border', '#e7e5e4'));

	readonly yoyAsOfDate = computed(() => this.yoy()?.asOfDate ?? null);
	readonly yoyUngrouped = computed(() => this.yoy()?.ungroupedJobNames ?? []);
	readonly yoyGroups = computed<YoyChartGroup[]>(() =>
		(this.yoy()?.groups ?? []).map((g, i) => this.toChartGroup(g, i)));

	/**
	 * Everything on ONE chart (Todd, 2026-09-01): lineages as groups along a shared axis,
	 * their seasons as the bars inside each group. A chart per event made the reader compare
	 * across cards with a different y-scale on each; one axis makes the events comparable to
	 * each other as well as to their own history.
	 */
	readonly yoyChart = computed<YoyChartView>(() => {
		const groups = this.yoyGroups();
		const points: YoyChartPoint[] = [];
		const pinLevel: { start: number; end: number; text: string }[] = [];
		const groupLevel: { start: number; end: number; text: string }[] = [];

		// A span is measured as `endX - startX - padding`, so start === end computes to a
		// NEGATIVE width and ej2 renders the label as "...". Categories sit at integer indices
		// and their band runs half a step either side, so every span is bracketed rather than
		// pinned to the tick — without which a one-season event, and every per-bar cutoff, is
		// an ellipsis.
		const BAND = 0.5;

		for (const g of groups) {
			if (g.points.length === 0) {
				continue;
			}
			const start = points.length;
			for (const p of g.points) {
				pinLevel.push({
					start: points.length - BAND,
					end: points.length + BAND,
					text: p.pinLabel
				});
				points.push(p);
			}
			groupLevel.push({
				start: start - BAND,
				end: points.length - 1 + BAND,
				text: eventLabel(g.label)
			});
		}

		const visible = Math.min(YOY_VISIBLE_BARS, points.length);
		const needsScroll = points.length > YOY_VISIBLE_BARS;

		return {
			points,
			pinLevel,
			groupLevel,
			needsScroll,
			// Opens on the most recent lineages; the scrollbar reaches back for the rest.
			zoomFactor: needsScroll ? visible / points.length : 1,
			groups
		};
	});

	/**
	 * YoY needs dates: the whole report is an as-of pin shifted back whole years, and a
	 * job-name scope carries no date to pin to. Rather than fail silently, the tab says so.
	 */
	readonly yoyNeedsDateScope = computed(() => this.submittedScope()?.mode === 'jobs');

	/** Any season still open at its own cutoff — drives whether the footnote is worth printing. */
	readonly yoyHasActiveColumn = computed(() =>
		this.yoyChart().points.some(p => p.active));

	private toChartGroup(g: YoyEventGroupDto, index: number): YoyChartGroup {
		const points: YoyChartPoint[] = g.years.map(y => ({
			// The asterisk is the in-flight marker, and it is TEXT on purpose — a season that
			// has not finished must not be distinguished by colour alone. It rides INSIDE the
			// key: the axis renders the key, so a marker held anywhere else never reaches the
			// screen while the footnote explaining it still prints.
			key: y.isActive ? `${index}|${y.year}*` : `${index}|${y.year}`,
			// The pin is a SECOND row, never the axis label itself. It is offset from the
			// lineage's anchor, not from the season's own calendar year, so a 2024 season can
			// legitimately be measured at 8/31/23 — a label showing only the cutoff would put
			// "8/31/23" under a 2024 bar and name the wrong season.
			pinLabel: shortPin(y.asOf),
			rawYear: y.year,
			billed: y.billed,
			collected: y.collected,
			owed: y.owed,
			adj: y.adj,
			refunds: y.refunds,
			asOf: y.asOf,
			active: y.isActive,
			jobNames: y.jobNames
		}));

		return {
			label: g.groupLabel,
			anchorYear: g.anchorYear,
			points,
			// The composing jobs, printed under every chart. Name grouping is a heuristic and
			// its failure mode is a confident chart against a wrong baseline — a reader spots
			// a bad pairing instantly where no parser will.
			jobLines: g.years.flatMap(y => y.jobNames.map(n => `${y.year} — ${n}`))
		};
	}

	private fetchYoyIfNeeded(): void {
		const scope = this.submittedScope();
		if (!scope || scope.mode === 'jobs' || this.yoy() !== null || this.yoyLoading()) {
			return;
		}
		this.yoyLoading.set(true);
		const params = new HttpParams()
			.set('startDate', scope.startDate!)
			.set('endDate', scope.endDate!);
		this.http.get<YoyRevenueResponseDto>(`${this.apiUrl}/yoy`, { params }).subscribe({
			next: (data) => {
				this.yoy.set(data);
				this.yoyLoading.set(false);
			},
			error: (err) => {
				this.yoyLoading.set(false);
				this.errorMessage.set(err.error?.message || 'Failed to load year-over-year review');
			}
		});
	}

	/**
	 * Category axis — seasons, with two label rows beneath: each bar's cutoff, then the event
	 * it belongs to. The lineage row is what turns one long axis back into readable groups.
	 */
	yoyXAxis(view: YoyChartView): object {
		return {
			valueType: 'Category',
			majorGridLines: { width: 0 },
			majorTickLines: { width: 0 },
			lineStyle: { width: 0.5, color: this.yoyBorder() },
			labelStyle: { color: this.yoyText(), size: '12px', fontFamily: YOY_FONT_FAMILY },
			multiLevelLabels: [
				{
					border: { type: 'WithoutTopandBottomBorder', width: 0 },
					categories: view.pinLevel,
					alignment: 'Center',
					overflow: 'None',
					textStyle: { color: this.yoyMuted(), size: '10px', fontFamily: YOY_FONT_FAMILY }
				},
				{
					border: { type: 'Brace', width: 1, color: this.yoyBorder() },
					categories: view.groupLevel,
					alignment: 'Center',
					overflow: 'Wrap',
					textStyle: { color: this.yoyText(), size: '12px', fontFamily: YOY_FONT_FAMILY }
				}
			],
			// Present only when there is more than fits; a full view must not open zoomed, or
			// the newest lineage looks like the only one.
			...(view.needsScroll ? { zoomFactor: view.zoomFactor, zoomPosition: 1 } : {})
		};
	}

	/** Value axis — dollars, abbreviated by onYoyAxisLabel. */
	yoyYAxis(): object {
		return {
			majorGridLines: { width: 0.5, color: this.yoyBorder() },
			majorTickLines: { width: 0 },
			lineStyle: { width: 0 },
			labelStyle: { color: this.yoyMuted(), size: '11px', fontFamily: YOY_FONT_FAMILY }
		};
	}

	/**
	 * Pan-only. Mouse-wheel zooming is OFF deliberately: this tab is a vertical stack of
	 * charts, and a wheel handler on each one would hijack page scrolling. Selection zooming
	 * and the toolbar are off for the same reason — the scrollbar is the whole interaction.
	 */
	yoyZoom(view: YoyChartView): object {
		return {
			enableScrollbar: view.needsScroll,
			enablePan: view.needsScroll,
			mode: 'X',
			enableMouseWheelZooming: false,
			enableSelectionZooming: false,
			enablePinchZooming: false,
			enableDeferredZooming: false,
			toolbarItems: []
		};
	}

	readonly yoyLegend = {
		visible: true, position: 'Top' as const, alignment: 'Far' as const, padding: 8,
		textStyle: { fontFamily: YOY_FONT_FAMILY, size: '12px' }
	};
	readonly yoyTooltip = {
		enable: true, shared: true,
		textStyle: { fontFamily: YOY_FONT_FAMILY, size: '12px' }
	};

	/**
	 * The total sitting above each bar: Collected + Owed, which IS Billed — the one figure a
	 * stacked bar cannot show as a segment because it is the whole bar.
	 *
	 * 'C0' rather than '{value}': a format string without the placeholder is routed through
	 * ej2's number formatter, so this picks up currency and the grouping separator instead of
	 * printing a bare 1420512.5. Whole dollars — cents above a bar are noise at this scale.
	 */
	readonly yoyStackLabels = {
		visible: true,
		format: 'C0',
		font: { fontFamily: YOY_FONT_FAMILY, size: '12px', fontWeight: '600', color: this.yoyText() }
	};
	readonly yoyChartArea = { border: { width: 0 } };
	// Bottom carries the season labels, the cutoff row and the braced event row.
	readonly yoyMargin = { left: 8, right: 16, top: 4, bottom: 12 };

	/**
	 * Both axes. Horizontal: strip the lineage prefix that keeps categories unique, so the
	 * reader sees the season and never the key. Vertical: abbreviate dollars so six-figure
	 * labels do not crowd the plot.
	 */
	onYoyAxisLabel(args: { axis?: { orientation?: string }; value?: number; text?: string }): void {
		if (args.axis?.orientation !== 'Vertical') {
			if (args.text) {
				args.text = args.text.slice(args.text.indexOf('|') + 1);
			}
			return;
		}
		if (args.value == null) {
			return;
		}
		const v = args.value;
		const abs = Math.abs(v);
		const sign = v < 0 ? '-' : '';
		// toFixed BEFORE formatting, always. ej2 derives its own tick values, and a 0-to-1
		// default range steps in thirds — which is how "$0.6000000000000001" reached the axis
		// of every empty chart. Rounding only the large branches leaves the small one exposed.
		args.text =
			abs >= 1_000_000 ? `${sign}$${(abs / 1_000_000).toFixed(1)}M`
			: abs >= 1_000 ? `${sign}$${Math.round(abs / 1_000)}K`
			: `${sign}$${Number(abs.toFixed(2))}`;
	}

	/**
	 * Full dollars in the tooltip — the axis is abbreviated and the stack label is rounded to
	 * whole dollars, so this is the only place the exact figure appears.
	 */
	onYoyTooltip(args: { text?: string; point?: { y?: number }; series?: { name?: string } }): void {
		if (args.point?.y == null) {
			return;
		}
		const amount = args.point.y.toLocaleString('en-US', {
			style: 'currency', currency: 'USD', minimumFractionDigits: 2
		});
		args.text = `${args.series?.name ?? ''}: ${amount}`;
	}

	/** Accent Credit Card Credit rows so they jump out when scanning the CC grid. */
	onCcRowDataBound(args: { data?: JobPaymentRecordDto; row?: HTMLElement }): void {
		if (args.data?.paymentMethod === 'Credit Card Credit' && args.row) {
			args.row.classList.add('row-cc-credit');
		}
	}

	private detailSignal(key: DetailKey) {
		return key === 'cc' ? this.ccDetail : key === 'check' ? this.checkDetail : this.echeckDetail;
	}

	private fetchDetailIfNeeded(key: DetailKey): void {
		const scope = this.submittedScope();
		if (!scope || this.detailSignal(key)() !== null || this.detailLoading() === key) {
			return;
		}
		this.detailLoading.set(key);
		this.http.get<JobPaymentRecordDto[]>(`${this.apiUrl}/details/${key}`, { params: this.scopeParams(scope) }).subscribe({
			next: (records) => {
				this.detailSignal(key).set(records);
				this.detailLoading.set(null);
			},
			error: (err) => {
				this.detailLoading.set(null);
				this.errorMessage.set(err.error?.message || 'Failed to load payment details');
			}
		});
	}

	/** SU-only, sandbox-only: server runs legacy sprocs + EF port over the current scope and diffs them. */
	runLegacyCompare(): void {
		const scope = this.submittedScope();
		if (!scope || this.qaRunning()) {
			return;
		}
		this.qaRunning.set(true);
		this.http.get<LegacyCompareResultDto>(`${this.apiUrl}/legacy-compare`, { params: this.scopeParams(scope) }).subscribe({
			next: (result) => {
				this.qaResult.set(result);
				this.qaRunning.set(false);
			},
			error: (err) => {
				this.qaRunning.set(false);
				this.errorMessage.set(err.error?.message || 'Legacy comparison failed');
			}
		});
	}

	/**
	 * Width (px) of the widest job name at the row-header font, plus expand-caret and
	 * cell-padding chrome. Clamped so one absurd name can't take over the viewport —
	 * past the cap, allowTextWrap takes back over as the fallback.
	 */
	private measureRowHeaderWidth(records: RevenueRollupResponseDto['revenueRecords']): number {
		const ctx = document.createElement('canvas').getContext('2d');
		if (!ctx) {
			return 240;
		}
		// Bold weight (member cells render bold) + 3% slack + caret/padding chrome.
		// Err slightly wide: spare column is invisible; a few px short wraps the name.
		ctx.font = `700 14px ${getComputedStyle(document.body).fontFamily || 'sans-serif'}`;
		let widest = 0;
		for (const name of new Set(records.map((r) => r.jobName))) {
			widest = Math.max(widest, ctx.measureText(name).width);
		}
		return Math.min(640, Math.max(180, Math.ceil(widest) + 48));
	}

	// Pivot toolbar actions
	expandAll(): void {
		this.setExpandAll(true);
	}

	collapseAll(): void {
		this.setExpandAll(false);
	}

	/**
	 * Every manual caret click lands in dataSourceSettings.drilledMembers, and the engine
	 * renders a member in that list as the OPPOSITE of expandAll (engine.js: a member in
	 * fieldDrillCollection resolves to !isExpandAll). So flipping the flag alone inverts
	 * exactly the rows the user drilled by hand — Expand All collapses them while the rest
	 * expand — which reads as the buttons being backwards. Clear the list so the flag is the
	 * only input to the drill state.
	 *
	 * setProperties merges: dataSourceSettings is a @Complex accessor, so only the two keys
	 * passed here are touched — rows/columns/values/dataSource survive.
	 */
	private setExpandAll(expand: boolean): void {
		// Whichever pivot is on screen — same drilledMembers caveat applies to both.
		const pivotView = this.activeTab() === 'teamBilling' ? this.teamBillingPivot() : this.pivotView();
		if (pivotView) {
			pivotView.setProperties({ dataSourceSettings: { expandAll: expand, drilledMembers: [] } });
		}
	}

	// Visible export buttons (AM-050 part 2) — identical on every tab, and every export
	// carries the audit-stamp scope label in its header.
	exportActive(kind: 'pdf' | 'excel'): void {
		const tab = this.activeTab();
		if (tab === 'rollup') {
			this.exportPivot(kind);
			return;
		}
		if (tab === 'teamBilling') {
			// Excel only — the PDF button is not rendered on this tab.
			if (kind === 'excel') {
				this.exportTeamBillingPivot();
			}
			return;
		}
		if (tab === 'adjustments') {
			const grid = this.adjustmentsGrid();
			if (!grid) {
				return; // still loading — nothing rendered to export
			}
			if (kind === 'pdf') {
				grid.pdfExport({ fileName: 'CustomerJobRevenue-Adjustments.pdf', header: this.pdfHeader() });
			} else {
				grid.excelExport({ fileName: 'CustomerJobRevenue-Adjustments.xlsx', header: this.excelHeader(5) });
			}
			return;
		}
		if (this.detailLoading() !== null) {
			return; // detail grid not rendered yet — nothing to export
		}
		const target =
			tab === 'counts' ? { grid: this.countsGrid(), name: 'Counts', cols: 9 }
			: tab === 'adminFees' ? { grid: this.adminFeesGrid(), name: 'AdminFees', cols: 6 }
			: tab === 'ccRecords' ? { grid: this.ccGrid(), name: 'CreditCardRecords', cols: 5 }
			: tab === 'checkRecords' ? { grid: this.checkGrid(), name: 'CheckRecords', cols: 5 }
			: { grid: this.echeckGrid(), name: 'ECheckRecords', cols: 5 };
		if (kind === 'pdf') {
			target.grid.pdfExport({
				fileName: `CustomerJobRevenue-${target.name}.pdf`,
				header: this.pdfHeader()
			});
		} else {
			target.grid.excelExport({
				fileName: `CustomerJobRevenue-${target.name}.xlsx`,
				header: this.excelHeader(target.cols)
			});
		}
	}

	private exportPivot(kind: 'pdf' | 'excel'): void {
		const pivot = this.pivotView();
		if (!pivot) {
			return;
		}
		if (kind === 'pdf') {
			// The rollup is WIDE, not long: a 640px row header plus ten ~120px category
			// columns is ~1,840px, which portrait Letter (~816px) splits across three pages
			// side by side. Landscape Ledger (~1,632px) brings it back to a single column of
			// pages. Drop pageSize to 'Letter' if these ever need to print on office paper.
			pivot.pdfExport({
				fileName: 'CustomerJobRevenue.pdf',
				header: this.pdfHeader(),
				pageOrientation: 'Landscape',
				pageSize: 'Ledger'
			});
		} else {
			pivot.excelExport({ fileName: 'CustomerJobRevenue.xlsx', header: this.excelHeader(8) });
		}
	}

	/**
	 * Team Billing export — EXCEL ONLY, deliberately. The PDF button is hidden on this tab
	 * (see the template) for two reasons:
	 *
	 * 1. It would THROW. PDF standard fonts are WinAnsi-only, which is why `toPdfSafe`
	 *    exists; the grids sanitize cells through `pdfQueryCellInfo`, but the pivot exposes
	 *    no equivalent per-cell hook. The rollup pivot gets away with it because every job
	 *    name in the system is ASCII — but club and team names are user-entered free text,
	 *    and 15 of Top Threat's carry curly apostrophes ("Women's", "Santa's").
	 * 2. Even sanitized it is the wrong shape: 5 nested row levels, team labels up to 84
	 *    characters, 7,942 team rows. Excel handles both the Unicode and the volume.
	 *
	 * The header carries the basis qualifier because an exported sheet travels without the
	 * on-screen caption, and the shared scope label alone reads as a money window.
	 */
	private exportTeamBillingPivot(): void {
		const pivot = this.teamBillingPivot();
		if (!pivot) {
			return;
		}
		const label = `${this.submittedScope()?.label ?? ''} — ${TEAM_BILLING_BASIS}`;
		pivot.excelExport({
			fileName: 'CustomerJobRevenue-TeamsPlayersToCustomer.xlsx',
			header: {
				headerRows: 1,
				// 5 row levels + 6 value columns.
				rows: [{ cells: [{ colSpan: 11, value: `Teams/Players to Customer — ${label}`, style: { fontSize: 13, bold: true } }] }]
			}
		});
	}

	private excelHeader(colSpan: number) {
		const label = this.submittedScope()?.label ?? '';
		return {
			headerRows: 1,
			rows: [{ cells: [{ colSpan, value: `Customer Job Revenue — ${label}`, style: { fontSize: 13, bold: true } }] }]
		};
	}

	// The PDF standard fonts only cover WinAnsi — curly quotes, accents, en/em dashes
	// throw "character is not supported by the font". Fold to ASCII for PDF rendering
	// only; the screen and Excel keep the original text.
	private toPdfSafe(text: string): string {
		return text
			.normalize('NFKD')
			.replace(/[̀-ͯ]/g, '') // combining diacritics left over from NFKD (é -> e)
			.replace(/[‘’‚]/g, "'")
			.replace(/[“”„]/g, '"')
			.replace(/[–—]/g, '-')
			.replace(/·/g, '-')
			.replace(/[^\x20-\x7E]/g, '');
	}

	/** PDF-only cell sanitizer — human-entered names (registrant, club) carry curly quotes/accents. */
	onPdfQueryCellInfo(args: { value?: unknown }): void {
		if (typeof args.value === 'string') {
			args.value = this.toPdfSafe(args.value);
		}
	}

	/** The grid PDF module rejects with the PdfDocument object and hides the real error here. */
	onGridActionFailure(args: unknown): void {
		console.error('[CustomerJobRevenue] grid action failure', args);
	}

	private pdfHeader() {
		const label = this.submittedScope()?.label ?? '';
		const pdfLabel = this.toPdfSafe(`Customer Job Revenue - ${label}`);
		return {
			fromTop: 0,
			height: 50,
			contents: [{
				type: 'Text' as const,
				value: pdfLabel,
				position: { x: 0, y: 15 },
				style: { textBrushColor: '#000000', fontSize: 12 }
			}]
		};
	}

	// Inline edit save for monthly counts
	onCountsActionComplete(args: { requestType?: string; action?: string; data?: Record<string, unknown> }): void {
		if (args.requestType === 'save' && args.action === 'edit') {
			const row = args.data as {
				aid: number;
				countActivePlayersToDate: number;
				countActivePlayersToDateLastMonth: number;
				countNewPlayersThisMonth: number;
				countActiveTeamsToDate: number;
				countActiveTeamsToDateLastMonth: number;
				countNewTeamsThisMonth: number;
			};
			const request: UpdateMonthlyCountRequest = {
				aid: row.aid,
				countActivePlayersToDate: row.countActivePlayersToDate,
				countActivePlayersToDateLastMonth: row.countActivePlayersToDateLastMonth,
				countNewPlayersThisMonth: row.countNewPlayersThisMonth,
				countActiveTeamsToDate: row.countActiveTeamsToDate,
				countActiveTeamsToDateLastMonth: row.countActiveTeamsToDateLastMonth,
				countNewTeamsThisMonth: row.countNewTeamsThisMonth
			};

			this.http.put(`${this.apiUrl}/monthly-counts/${row.aid}`, request).subscribe({
				error: (err) => {
					this.errorMessage.set(err.error?.message || 'Failed to save monthly count update');
				}
			});
		}
	}
}
