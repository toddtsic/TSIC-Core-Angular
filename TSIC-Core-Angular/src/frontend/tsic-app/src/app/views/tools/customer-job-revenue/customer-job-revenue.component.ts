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
import { AuthService } from '../../../infrastructure/services/auth.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AdminNavPillComponent } from '@shared-ui/components/admin-nav-pill.component';
import type { RevenueRollupResponseDto } from '@core/api';
import type { JobPaymentRecordDto } from '@core/api';
import type { UpdateMonthlyCountRequest } from '@core/api';
import type { LegacyCompareResultDto } from '@core/api';
import type { TeamBillingRecordDto } from '@core/api';

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
 * The Team Billing tab reuses the shared date pickers, but the window means something
 * different there: on every other tab it bounds WHEN MONEY MOVED, here it bounds WHEN THE
 * TEAM REGISTERED, and the balances shown are current. A team that registered in January
 * and paid in March reports its Collected under January — so the two tabs will not
 * reconcile month-to-month, by design. Stated on screen and in every export.
 */
const TEAM_BILLING_BASIS = 'teams registered in this window; balances as of today';

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
	imports: [CommonModule, FormsModule, GridAllModule, PivotViewAllModule, MultiSelectAllModule, AdminNavPillComponent],
	// The export services are NOT bundled by the *AllModules (pivot or grid) — without
	// them pdfExport()/excelExport() are silent no-ops.
	providers: [
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
	activeTab = signal<'rollup' | 'counts' | 'adminFees' | 'ccRecords' | 'checkRecords' | 'echeckRecords' | 'teamBilling'>('rollup');

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
	teamBillingRecords = computed(() => this.teamBilling() ?? []);

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
	 * - Three value fields instead of a pay-category axis: these are balances, not a
	 *   transaction breakdown.
	 * - Year/Month come from teams.createdate (when the team REGISTERED). That is what
	 *   lets an unpaid team exist on the timeline at all — it has no payment to be dated by.
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
		values: [
			{ name: 'billed', caption: 'Billed', type: 'Sum' },
			{ name: 'collected', caption: 'Collected', type: 'Sum' },
			{ name: 'owed', caption: 'Owed', type: 'Sum' }
		],
		formatSettings: [
			{ name: 'billed', format: 'C2', useGrouping: true },
			{ name: 'collected', format: 'C2', useGrouping: true },
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
			pivot.pdfExport({ fileName: 'CustomerJobRevenue.pdf', header: this.pdfHeader() });
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
			fileName: 'CustomerJobRevenue-TeamBilling.xlsx',
			header: {
				headerRows: 1,
				rows: [{ cells: [{ colSpan: 8, value: `Team Billing — ${label}`, style: { fontSize: 13, bold: true } }] }]
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
