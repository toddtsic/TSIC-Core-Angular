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
import { ReportingService } from '../../../infrastructure/services/reporting.service';
import { ToastService } from '../../../shared-ui/toast.service';
import type { StoreSalesPivotDto } from '@core/api';
import { formatCurrency } from '@shared/utils/money.util';

/** One entry in legacy's "Labels" menu group. */
interface StoreReportLink {
	/** ReportingController action — the Crystal report name is resolved server-side. */
	action: string;
	label: string;
	description: string;
	icon: string;
}

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
	private readonly reporting = inject(ReportingService);
	private readonly toast = inject(ToastService);

	// ═══════════════════════════════════════
	//  LABELS — legacy's _StoreAdminMenu "Labels" group
	// ═══════════════════════════════════════

	/**
	 * The three entries legacy's store-admin menu shows, in its order and with its wording.
	 * A fourth action, StorePickupSignoff, is live on the server but COMMENTED OUT of legacy's
	 * menu; it stays unlinked here so the visible surface matches.
	 *
	 * <p>These are now server-side code-gen PDFs, not Crystal proxies — the three Crystal reports
	 * they named never existed. The descriptions say what the sheet IS, including the label stock,
	 * because a director has to buy the right paper before clicking the first one.</p>
	 */
	readonly reportLinks: StoreReportLink[] = [
		{
			action: 'StoreLabels',
			label: 'Store Bag Labels',
			description: 'One label per player, on Avery 5163 sheets (10 per page).',
			icon: 'bi-tags',
		},
		{
			action: 'StorePerPlayerPickup',
			label: 'Per Family Pickup Signoff',
			description: 'Signature sheet handed over at the pickup table.',
			icon: 'bi-pen',
		},
		{
			action: 'StorePerPlayerPivot',
			label: 'Per Family Pivot',
			description: 'Every family and what they ordered, one row each.',
			icon: 'bi-table',
		},
	];

	readonly downloadingAction = signal<string | null>(null);

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

	/**
	 * D-05 — `pvcRevenueByItem`: the doughnut that sits to the RIGHT of Product Sales in legacy's
	 * one "Useful Sales Graphics Here..." card. Revenue share per product, no time dimension.
	 *
	 * <p>It reads the same pivot rows as everything else on this screen. Legacy's controller also
	 * computes a `ListSalesByItemPieData` that LOOKS like this chart's source — a private method
	 * with its own filter — but the view references it zero times, so it is dead and stays
	 * unported. Feeding this off the pivot data is what legacy actually does.</p>
	 */
	readonly revenueByItem = computed<IDataOptions>(() => ({
		dataSource: this.records(),
		expandAll: true,
		enableSorting: true,
		allowLabelFilter: true,
		allowValueFilter: true,
		rows: [{ name: 'itemName', caption: 'Store Item' }],
		values: [{ name: 'revenue', caption: 'Revenue', type: 'Sum' }],
		formatSettings: [
			{ name: 'revenue', format: 'C', maximumSignificantDigits: 10, minimumSignificantDigits: 1, useGrouping: true },
		],
	}));

	readonly revenueByItemChart = {
		title: 'Revenue by Item',
		chartSeries: { type: 'Doughnut' as const },
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

	/**
	 * PDF download. `exportFormat: 1` is Crystal's Pdf enum value and is legacy's default for all
	 * three of these. The blob plumbing (Content-Disposition filename, popup-blocker-safe anchor)
	 * is the shared ReportingService one every other report surface uses.
	 */
	downloadReport(link: StoreReportLink): void {
		if (this.downloadingAction()) return;
		this.downloadingAction.set(link.action);

		this.reporting.downloadReport(link.action, { exportFormat: '1' }).subscribe({
			next: response => {
				this.downloadingAction.set(null);

				// Two ways this comes back 200 but useless: the proxy wraps a Crystal refusal as
				// text/plain, and the stopped cr2025 host answers every path with the Angular
				// app's index.html (text/html). Neither is a PDF — say so rather than handing
				// the browser a file that will not open.
				if (this.reporting.isErrorPayload(response)) {
					this.toast.show(
						`${link.label} could not be generated — the reporting service did not return a PDF.`,
						'danger', 6000);
					return;
				}

				this.reporting.triggerDownload(response, link.label.replace(/\s+/g, '-'));
			},
			error: () => {
				this.downloadingAction.set(null);
				this.toast.show(`${link.label} could not be generated.`, 'danger', 5000);
			},
		});
	}

	readonly formatCurrency = formatCurrency;
}
