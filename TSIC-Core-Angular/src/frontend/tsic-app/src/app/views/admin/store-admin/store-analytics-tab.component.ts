import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
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

type AnalyticsSection = 'sales-by-item' | 'sales-pivot' | 'payments' | 'family-purchases' | 'refunded' | 'restocked';

@Component({
	selector: 'app-store-analytics-tab',
	standalone: true,
	imports: [CommonModule, FormsModule, TsicDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-analytics-tab.component.html',
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

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}

	formatDate(dateStr: string): string {
		return new Date(dateStr).toLocaleDateString();
	}
}
