import { Component, ChangeDetectionStrategy, signal, computed, inject, viewChild, HostListener } from '@angular/core';
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
	FieldListService,
	ToolbarService,
	PDFExportService,
	ExcelExportService
} from '@syncfusion/ej2-angular-pivotview';
import { MultiSelectAllModule } from '@syncfusion/ej2-angular-dropdowns';
import { AuthService } from '../../../infrastructure/services/auth.service';
import type { RevenueRollupResponseDto } from '@core/api';
import type { JobPaymentRecordDto } from '@core/api';
import type { UpdateMonthlyCountRequest } from '@core/api';
import type { LegacyCompareResultDto } from '@core/api';

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
	imports: [CommonModule, FormsModule, GridAllModule, PivotViewAllModule, MultiSelectAllModule],
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
	private readonly apiUrl = `${environment.apiUrl}/customer-job-revenue`;

	// UI state
	isLoading = signal(false);
	errorMessage = signal('');
	activeTab = signal<'rollup' | 'counts' | 'adminFees' | 'ccRecords' | 'checkRecords' | 'echeckRecords'>('rollup');

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

	// Derived
	monthlyCounts = computed(() => this.rollup()?.monthlyCounts ?? []);
	adminFees = computed(() => this.rollup()?.adminFees ?? []);
	creditCardRecords = computed(() => this.ccDetail() ?? []);
	checkRecords = computed(() => this.checkDetail() ?? []);
	echeckRecords = computed(() => this.echeckDetail() ?? []);

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
	readonly countsGrid = viewChild.required<GridComponent>('countsGrid');
	readonly adminFeesGrid = viewChild.required<GridComponent>('adminFeesGrid');
	readonly ccGrid = viewChild.required<GridComponent>('ccGrid');
	readonly checkGrid = viewChild.required<GridComponent>('checkGrid');
	readonly echeckGrid = viewChild.required<GridComponent>('echeckGrid');

	constructor() {
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
				this.qaResult.set(null);
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

	// Pivot toolbar actions
	expandAll(): void {
		const pivotView = this.pivotView();
		if (pivotView) {
			pivotView.dataSourceSettings.expandAll = true;
		}
	}

	collapseAll(): void {
		const pivotView = this.pivotView();
		if (pivotView) {
			pivotView.dataSourceSettings.expandAll = false;
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
