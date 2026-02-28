import { Component, ChangeDetectionStrategy, signal, computed, inject, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '@environments/environment';
import { GridAllModule, GridComponent, EditSettingsModel, ToolbarItems } from '@syncfusion/ej2-angular-grids';
import {
	PivotViewAllModule,
	PivotViewComponent,
	IDataOptions,
	FieldListService,
	ToolbarService
} from '@syncfusion/ej2-angular-pivotview';
import { MultiSelectAllModule } from '@syncfusion/ej2-angular-dropdowns';
import { AuthService } from '../../../infrastructure/services/auth.service';
import type { JobRevenueDataDto } from '@core/api';
import type { UpdateMonthlyCountRequest } from '@core/api';

interface MonthOption {
	startDate: string;
	endDate: string;
	label: string;
}

@Component({
	selector: 'app-customer-job-revenue',
	standalone: true,
	imports: [CommonModule, FormsModule, GridAllModule, PivotViewAllModule, MultiSelectAllModule],
	providers: [FieldListService, ToolbarService],
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
	activeTab = signal<'rollup' | 'counts' | 'adminFees' | 'ccRecords' | 'checkRecords'>('rollup');

	// Data
	revenueData = signal<JobRevenueDataDto | null>(null);

	// Derived
	availableJobs = computed(() => this.revenueData()?.availableJobs ?? []);
	revenueRecords = computed(() => this.revenueData()?.revenueRecords ?? []);
	monthlyCounts = computed(() => this.revenueData()?.monthlyCounts ?? []);
	adminFees = computed(() => this.revenueData()?.adminFees ?? []);
	creditCardRecords = computed(() => this.revenueData()?.creditCardRecords ?? []);
	checkRecords = computed(() => this.revenueData()?.checkRecords ?? []);

	// Date range options (monthly buckets from last month back to Jan 2022)
	monthOptions: MonthOption[] = [];

	// Selected filters
	selectedStartDate = '';
	selectedEndDate = '';
	selectedJobs: string[] = [];

	// Pivot config
	pivotDataSource: IDataOptions = {
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
	};

	// Grid edit settings (counts tab — SuperUser only)
	isSuperUser = computed(() => {
		const user = this.auth.currentUser();
		return user?.role === 'Superuser';
	});
	countsEditSettings: EditSettingsModel = { allowEditing: true, allowAdding: false, allowDeleting: false };
	countsToolbar = computed<ToolbarItems[]>(() =>
		this.isSuperUser() ? ['Edit', 'Cancel', 'Update', 'ExcelExport'] : ['ExcelExport']
	);
	readOnlyToolbar: ToolbarItems[] = ['ExcelExport'];

	@ViewChild('pivotView') pivotView!: PivotViewComponent;
	@ViewChild('countsGrid') countsGrid!: GridComponent;
	@ViewChild('adminFeesGrid') adminFeesGrid!: GridComponent;
	@ViewChild('ccGrid') ccGrid!: GridComponent;
	@ViewChild('checkGrid') checkGrid!: GridComponent;

	constructor() {
		this.buildMonthOptions();
		// Default to first option (last month)
		if (this.monthOptions.length > 0) {
			this.selectedStartDate = this.monthOptions[0].startDate;
			this.selectedEndDate = this.monthOptions[0].endDate;
		}
		this.loadData();
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
				label: cursor.toLocaleDateString('en-US', { month: 'short', year: 'numeric' })
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

	loadData(): void {
		this.isLoading.set(true);
		this.errorMessage.set('');

		let params = new HttpParams()
			.set('startDate', this.selectedStartDate)
			.set('endDate', this.selectedEndDate);

		for (const job of this.selectedJobs) {
			params = params.append('jobNames', job);
		}

		this.http.get<JobRevenueDataDto>(this.apiUrl, { params }).subscribe({
			next: (data) => {
				this.revenueData.set(data);
				this.updatePivotDataSource(data);
				this.isLoading.set(false);
			},
			error: (err) => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message || 'Failed to load revenue data');
			}
		});
	}

	private updatePivotDataSource(data: JobRevenueDataDto): void {
		this.pivotDataSource = {
			...this.pivotDataSource,
			dataSource: data.revenueRecords as any
		};
	}

	onRefresh(): void {
		this.loadData();
	}

	setTab(tab: typeof this.activeTab extends ReturnType<typeof signal<infer T>> ? T : never): void {
		this.activeTab.set(tab);
	}

	// Pivot toolbar actions
	expandAll(): void {
		if (this.pivotView) {
			this.pivotView.dataSourceSettings.expandAll = true;
		}
	}

	collapseAll(): void {
		if (this.pivotView) {
			this.pivotView.dataSourceSettings.expandAll = false;
		}
	}

	// Grid toolbar click handlers
	onCountsToolbarClick(args: any): void {
		if (args.item?.id?.includes('excelexport')) {
			this.countsGrid.excelExport();
		}
	}

	onAdminFeesToolbarClick(args: any): void {
		if (args.item?.id?.includes('excelexport')) {
			this.adminFeesGrid.excelExport();
		}
	}

	onCcToolbarClick(args: any): void {
		if (args.item?.id?.includes('excelexport')) {
			this.ccGrid.excelExport();
		}
	}

	onCheckToolbarClick(args: any): void {
		if (args.item?.id?.includes('excelexport')) {
			this.checkGrid.excelExport();
		}
	}

	// Inline edit save for monthly counts
	onCountsActionComplete(args: any): void {
		if (args.requestType === 'save' && args.action === 'edit') {
			const row = args.data;
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
