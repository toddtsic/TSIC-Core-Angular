import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';
import type {
	StoreSalesByItemDto,
	StoreSalesPivotDto,
	StorePaymentDetailDto,
	StoreFamilyPurchaseDto,
	StoreRefundedItemDto,
	StoreRestockedItemDto,
} from '@core/api';
import { formatCurrency } from '@shared/utils/money.util';

type AnalyticsSection = 'sales-by-item' | 'sales-pivot' | 'payments' | 'family-purchases' | 'refunded' | 'restocked';

/** Units and revenue summed over a table's rows. */
function total<T>(rows: T[], units: (row: T) => number, revenue: (row: T) => number) {
	return rows.reduce(
		(sum, row) => ({ units: sum.units + units(row), revenue: sum.revenue + revenue(row) }),
		{ units: 0, revenue: 0 });
}

@Component({
	selector: 'app-store-analytics-tab',
	standalone: true,
	imports: [CommonModule, FormsModule, TsicDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-analytics-tab.component.html',
	styleUrl: './store-analytics-tab.component.scss',
})
export class StoreAnalyticsTabComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	// ── Section toggle ──
	readonly activeSection = signal<AnalyticsSection>('sales-by-item');
	readonly isLoading = signal(false);

	// ── Data signals ──
	readonly salesByItem = signal<StoreSalesByItemDto[]>([]);
	readonly salesPivot = signal<StoreSalesPivotDto[]>([]);

	/**
	 * The endpoint reports at SKU grain (the Dashboard's pivot drills item → SKU). This flat
	 * table is the item-level summary, so roll the SKUs of an item back up within each period.
	 */
	readonly salesPivotByItem = computed(() => {
		const buckets = new Map<string, { itemName: string; month: number; year: number; unitsSold: number; revenue: number }>();

		for (const row of this.salesPivot()) {
			const key = `${row.itemName}|${row.year}|${row.month}`;
			const bucket = buckets.get(key);
			if (bucket) {
				bucket.unitsSold += row.unitsSold;
				bucket.revenue += row.revenue;
			} else {
				buckets.set(key, {
					itemName: row.itemName,
					month: row.month,
					year: row.year,
					unitsSold: row.unitsSold,
					revenue: row.revenue,
				});
			}
		}

		return [...buckets.values()];
	});

	/**
	 * Grand totals. Both of these tables descend from the Dashboard's EJ2 pivot views, which
	 * carried their own grand-total row; flattening them to a table dropped it, and "what did
	 * this store take in" was the one number the screen no longer answered. Legacy's plain
	 * admin grids had no aggregates, which is why Payments and Family Purchases have none.
	 */
	readonly salesByItemTotals = computed(() => total(
		this.salesByItem(), r => r.totalUnitsSold, r => r.totalRevenue));

	readonly salesPivotTotals = computed(() => total(
		this.salesPivotByItem(), r => r.unitsSold, r => r.revenue));

	readonly payments = signal<StorePaymentDetailDto[]>([]);
	readonly familyPurchases = signal<StoreFamilyPurchaseDto[]>([]);
	readonly refundedItems = signal<StoreRefundedItemDto[]>([]);
	readonly restockedItems = signal<StoreRestockedItemDto[]>([]);

	// ── Filters ──
	readonly walkUpOnly = signal(false);

	// ── Expanded family ──
	readonly expandedFamilyUserId = signal<string | null>(null);
	readonly expandedFamilyDetail = signal<StoreFamilyPurchaseDto | null>(null);

	// ── Restock modal ──
	readonly showRestockModal = signal(false);
	readonly restockBatchSkuId = signal(0);
	readonly restockCount = signal(0);
	readonly isSaving = signal(false);

	// ── Pickup modal ──
	readonly showPickupModal = signal(false);
	readonly pickupBatchId = signal(0);
	readonly pickupSignedForBy = signal('');

	constructor() {
		this.loadSection('sales-by-item');
	}

	selectSection(section: AnalyticsSection): void {
		this.activeSection.set(section);
		this.loadSection(section);
	}

	loadSection(section: AnalyticsSection): void {
		this.isLoading.set(true);

		switch (section) {
			case 'sales-by-item':
				this.store.getSalesByItem().subscribe({
					next: data => { this.salesByItem.set(data); this.isLoading.set(false); },
					error: () => this.isLoading.set(false)
				});
				break;
			case 'sales-pivot':
				this.store.getSalesPivot().subscribe({
					next: data => { this.salesPivot.set(data); this.isLoading.set(false); },
					error: () => this.isLoading.set(false)
				});
				break;
			case 'payments':
				this.store.getPaymentDetails(this.walkUpOnly()).subscribe({
					next: data => { this.payments.set(data); this.isLoading.set(false); },
					error: () => this.isLoading.set(false)
				});
				break;
			case 'family-purchases':
				this.store.getFamilyPurchases().subscribe({
					next: data => { this.familyPurchases.set(data); this.isLoading.set(false); },
					error: () => this.isLoading.set(false)
				});
				break;
			case 'refunded':
				this.store.getRefundedItems().subscribe({
					next: data => { this.refundedItems.set(data); this.isLoading.set(false); },
					error: () => this.isLoading.set(false)
				});
				break;
			case 'restocked':
				this.store.getRestockedItems().subscribe({
					next: data => { this.restockedItems.set(data); this.isLoading.set(false); },
					error: () => this.isLoading.set(false)
				});
				break;
		}
	}

	toggleWalkUpOnly(): void {
		this.walkUpOnly.set(!this.walkUpOnly());
		this.loadSection('payments');
	}

	expandFamily(familyUserId: string): void {
		if (this.expandedFamilyUserId() === familyUserId) {
			this.expandedFamilyUserId.set(null);
			return;
		}
		this.expandedFamilyUserId.set(familyUserId);
		this.store.getFamilyPurchaseHistory(familyUserId).subscribe({
			next: detail => this.expandedFamilyDetail.set(detail)
		});
	}

	// ── Restock ──

	openRestockModal(): void {
		this.restockBatchSkuId.set(0);
		this.restockCount.set(0);
		this.showRestockModal.set(true);
	}

	submitRestock(): void {
		if (!this.restockBatchSkuId() || this.restockCount() < 1) return;
		this.isSaving.set(true);
		this.store.logRestock({
			storeCartBatchSkuId: this.restockBatchSkuId(),
			restockCount: this.restockCount()
		}).subscribe({
			next: () => {
				this.toast.show('Restock logged', 'success');
				this.showRestockModal.set(false);
				this.isSaving.set(false);
				this.loadSection('restocked');
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Restock failed', 'danger');
				this.isSaving.set(false);
			}
		});
	}

	// ── Pickup ──

	openPickupModal(): void {
		this.pickupBatchId.set(0);
		this.pickupSignedForBy.set('');
		this.showPickupModal.set(true);
	}

	submitPickup(): void {
		if (!this.pickupBatchId() || !this.pickupSignedForBy().trim()) return;
		this.isSaving.set(true);
		this.store.signForPickup({
			storeCartBatchId: this.pickupBatchId(),
			signedForBy: this.pickupSignedForBy().trim()
		}).subscribe({
			next: () => {
				this.toast.show('Pickup signed', 'success');
				this.showPickupModal.set(false);
				this.isSaving.set(false);
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Pickup sign-off failed', 'danger');
				this.isSaving.set(false);
			}
		});
	}

	readonly formatCurrency = formatCurrency;

	formatDate(dateStr: string): string {
		return new Date(dateStr).toLocaleDateString();
	}
}
