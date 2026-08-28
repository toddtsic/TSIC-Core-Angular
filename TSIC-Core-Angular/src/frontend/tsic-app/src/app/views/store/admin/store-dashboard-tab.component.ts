import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
	PivotViewAllModule,
	FieldListService,
	ToolbarService,
	PivotChartService,
	PDFExportService,
	ExcelExportService,
	IDataOptions,
} from '@syncfusion/ej2-angular-pivotview';
import { StoreService } from '../../../infrastructure/services/store.service';
import type { StoreSalesPivotDto } from '@core/api';

/**
 * Store Dashboard — port of legacy StoreDashboard/Index.
 *
 * Legacy rendered three `ejs-pivotview` instances: a Sales Rollup table, a Product Sales chart
 * and a Sales Rollup chart. It fed the first and third from `GetJobPurchasesPivotData` and the
 * second from a separate inline projection of the SAME table. Here all three read one dataset,
 * so a number can never differ between the table and the chart above it.
 */
@Component({
	selector: 'app-store-dashboard-tab',
	standalone: true,
	imports: [CommonModule, PivotViewAllModule],
	// The chart renderer and the export services are NOT bundled by PivotViewAllModule — without
	// PivotChartService a `view: 'Chart'` pivot renders as an empty box.
	providers: [
		FieldListService, ToolbarService, PivotChartService,
		PDFExportService, ExcelExportService,
	],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-dashboard-tab.component.html',
	styleUrl: './store-dashboard-tab.component.scss',
})
export class StoreDashboardTabComponent {
	private readonly store = inject(StoreService);

	readonly isLoading = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly rows = signal<StoreSalesPivotDto[]>([]);

	/**
	 * Pivot records. `yearMonth` is legacy's derived column for the Product Sales chart
	 * (`$"{Year}-{Month:D2}"`); it is computed here rather than shipped, so the payload carries
	 * year and month once and the two chart groupings both read the same numbers.
	 */
	private readonly records = computed(() =>
		this.rows().map(r => ({
			itemName: r.itemName,
			skuLabel: r.skuLabel,
			year: r.year,
			month: r.month,
			yearMonth: `${r.year}-${String(r.month).padStart(2, '0')}`,
			unitsSold: r.unitsSold,
			revenue: r.revenue,
		})));

	readonly hasData = computed(() => this.rows().length > 0);

	readonly totalUnits = computed(() =>
		this.rows().reduce((sum, r) => sum + r.unitsSold, 0));

	readonly totalRevenue = computed(() =>
		this.rows().reduce((sum, r) => sum + r.revenue, 0));

	// ═══════════════════════════════════════
	//  PIVOT CONFIGURATIONS
	//  Field names, captions, formats and expand state are legacy's, verbatim.
	// ═══════════════════════════════════════

	/** D-01/D-02 — `pvSalesRollup`: rows item→sku, columns year→month, Units + Sales. */
	readonly salesRollup = computed<IDataOptions>(() => ({
		dataSource: this.records(),
		expandAll: true,
		enableSorting: true,
		allowLabelFilter: true,
		allowValueFilter: true,
		rows: [
			{ name: 'itemName', caption: 'Item' },
			{ name: 'skuLabel', caption: 'SKU' },
		],
		columns: [
			{ name: 'year', caption: 'Year' },
			{ name: 'month', caption: 'Month' },
		],
		values: [
			{ name: 'unitsSold', caption: 'Units', type: 'Sum' },
			{ name: 'revenue', caption: 'Sales', type: 'Sum' },
		],
		formatSettings: [
			{ name: 'revenue', format: 'C2', useGrouping: true },
		],
	}));

	/**
	 * D-03 — `pvProductSalesStacked`: rows year-month, columns product, Sold count.
	 * Legacy's element id says "Stacked"; its `e-chartSeries` says `type="Column"`. The source
	 * wins over the id.
	 */
	readonly productSales = computed<IDataOptions>(() => ({
		dataSource: this.records(),
		expandAll: false,
		enableSorting: true,
		rows: [{ name: 'yearMonth', caption: 'Year-Month' }],
		columns: [{ name: 'itemName', caption: 'Product' }],
		values: [{ name: 'unitsSold', caption: 'Sold', type: 'Sum' }],
	}));

	readonly productSalesChart = {
		title: 'Sales Analysis',
		chartSeries: { type: 'Column' as const },
	};

	/** D-04 — `pvSalesRollupChart`: rows item, columns year, Units + Sales, whole dollars. */
	readonly salesRollupChart = computed<IDataOptions>(() => ({
		dataSource: this.records(),
		expandAll: true,
		enableSorting: true,
		allowLabelFilter: true,
		allowValueFilter: true,
		rows: [{ name: 'itemName', caption: 'Item' }],
		columns: [{ name: 'year', caption: 'Year' }],
		values: [
			{ name: 'unitsSold', caption: 'Units', type: 'Sum' },
			{ name: 'revenue', caption: 'Sales', type: 'Sum' },
		],
		formatSettings: [
			{ name: 'revenue', format: 'C0', maximumSignificantDigits: 10, minimumSignificantDigits: 1, useGrouping: true },
		],
	}));

	readonly salesRollupChartSettings = {
		chartSeries: { type: 'Column' as const },
	};

	readonly chartDisplay = { view: 'Chart' as const };

	constructor() {
		this.load();
	}

	load(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.store.getSalesPivot().subscribe({
			next: rows => {
				this.rows.set(rows);
				this.isLoading.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message ?? 'Could not load the dashboard.');
				this.isLoading.set(false);
			},
		});
	}

	formatCurrency(value: number): string {
		return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);
	}
}
