import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import type { StoreCartLineItemDto } from '@core/api';

@Component({
	selector: 'app-store-cart',
	standalone: true,
	imports: [CommonModule, FormsModule, RouterLink],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-cart.component.html',
})
export class StoreCartComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	readonly cart = this.store.cart;
	readonly isLoading = this.store.isCartLoading;
	readonly errorMessage = signal<string | null>(null);
	readonly updatingId = signal<number | null>(null);

	readonly lineItems = computed(() => this.cart()?.lineItems ?? []);
	readonly isEmpty = computed(() => this.lineItems().length === 0);
	readonly subtotal = computed(() => this.cart()?.subtotal ?? 0);
	readonly totalFees = computed(() => this.cart()?.totalFees ?? 0);
	readonly totalTax = computed(() => this.cart()?.totalTax ?? 0);
	readonly grandTotal = computed(() => this.cart()?.grandTotal ?? 0);

	constructor() {
		this.store.loadCart().subscribe({
			error: err => this.errorMessage.set(err?.error?.message || 'Failed to load cart')
		});
	}

	updateQuantity(item: StoreCartLineItemDto, newQty: number): void {
		if (newQty < 1 || this.updatingId()) return;
		this.updatingId.set(item.storeCartBatchSkuId);

		this.store.updateQuantity(item.storeCartBatchSkuId, { quantity: newQty }).subscribe({
			next: () => {
				this.updatingId.set(null);
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to update quantity', 'danger');
				this.updatingId.set(null);
			}
		});
	}

	removeItem(item: StoreCartLineItemDto): void {
		this.updatingId.set(item.storeCartBatchSkuId);
		this.store.removeFromCart(item.storeCartBatchSkuId).subscribe({
			next: () => {
				this.toast.show('Item removed', 'success');
				this.updatingId.set(null);
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to remove item', 'danger');
				this.updatingId.set(null);
			}
		});
	}

	variantLabel(item: StoreCartLineItemDto): string {
		const parts: string[] = [];
		if (item.colorName) parts.push(item.colorName);
		if (item.sizeName) parts.push(item.sizeName);
		return parts.join(' / ');
	}

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}
}
