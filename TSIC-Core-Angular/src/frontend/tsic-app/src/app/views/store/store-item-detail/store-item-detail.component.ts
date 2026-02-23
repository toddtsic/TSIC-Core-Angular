import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import type { StoreItemDto, StoreSkuDto, SkuAvailabilityDto } from '@core/api';

@Component({
	selector: 'app-store-item-detail',
	standalone: true,
	imports: [CommonModule, FormsModule, RouterLink],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-item-detail.component.html',
	styleUrl: './store-item-detail.component.scss',
})
export class StoreItemDetailComponent {
	private readonly route = inject(ActivatedRoute);
	private readonly router = inject(Router);
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	readonly item = signal<StoreItemDto | null>(null);
	readonly isLoading = signal(true);
	readonly errorMessage = signal<string | null>(null);
	readonly isAdding = signal(false);

	// Cart badge
	readonly cartCount = this.store.cartCount;

	// Variant selection
	readonly selectedColorId = signal<number | null>(null);
	readonly selectedSizeId = signal<number | null>(null);
	readonly quantity = signal(1);

	// Availability
	readonly availability = signal<SkuAvailabilityDto | null>(null);
	readonly isCheckingAvailability = signal(false);

	// Derived: unique colors and sizes from active SKUs
	readonly availableColors = computed(() => {
		const skus = this.item()?.skus?.filter(s => s.active) ?? [];
		const map = new Map<number, string>();
		for (const sku of skus) {
			if (sku.storeColorId && sku.storeColorName) {
				map.set(sku.storeColorId, sku.storeColorName);
			}
		}
		return Array.from(map, ([id, name]) => ({ id, name }));
	});

	readonly availableSizes = computed(() => {
		const skus = this.item()?.skus?.filter(s => s.active) ?? [];
		const map = new Map<number, string>();
		for (const sku of skus) {
			if (sku.storeSizeId && sku.storeSizeName) {
				map.set(sku.storeSizeId, sku.storeSizeName);
			}
		}
		return Array.from(map, ([id, name]) => ({ id, name }));
	});

	readonly hasColors = computed(() => this.availableColors().length > 0);
	readonly hasSizes = computed(() => this.availableSizes().length > 0);

	// Resolved SKU based on selections
	readonly selectedSku = computed(() => {
		const skus = this.item()?.skus?.filter(s => s.active) ?? [];
		const colorId = this.selectedColorId();
		const sizeId = this.selectedSizeId();

		if (this.hasColors() && colorId === null) return null;
		if (this.hasSizes() && sizeId === null) return null;

		return skus.find(s => {
			const colorMatch = !this.hasColors() || s.storeColorId === colorId;
			const sizeMatch = !this.hasSizes() || s.storeSizeId === sizeId;
			return colorMatch && sizeMatch;
		}) ?? null;
	});

	readonly canAddToCart = computed(() => {
		const sku = this.selectedSku();
		const avail = this.availability();
		if (!sku) return false;
		if (avail && avail.availableCount < this.quantity()) return false;
		return this.quantity() >= 1;
	});

	constructor() {
		const storeItemId = Number(this.route.snapshot.paramMap.get('storeItemId'));
		if (storeItemId) {
			this.loadItem(storeItemId);
		} else {
			this.errorMessage.set('Invalid item ID');
			this.isLoading.set(false);
		}
	}

	private loadItem(storeItemId: number): void {
		this.isLoading.set(true);
		this.store.getItemDetail(storeItemId).subscribe({
			next: item => {
				this.item.set(item);
				this.isLoading.set(false);

				// Auto-select if only one option
				const activeSkus = item.skus.filter(s => s.active);
				const colors = new Set(activeSkus.map(s => s.storeColorId).filter(Boolean));
				const sizes = new Set(activeSkus.map(s => s.storeSizeId).filter(Boolean));

				if (colors.size === 1) {
					this.selectedColorId.set([...colors][0]!);
				}
				if (sizes.size === 1) {
					this.selectedSizeId.set([...sizes][0]!);
				}

				// If default SKU (no color, no size), check availability right away
				if (colors.size === 0 && sizes.size === 0 && activeSkus.length === 1) {
					this.checkAvailability(activeSkus[0].storeSkuId);
				}
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load item');
				this.isLoading.set(false);
			}
		});
	}

	selectColor(colorId: number): void {
		this.selectedColorId.set(colorId);
		this.availability.set(null);
		this.checkSelectedSkuAvailability();
	}

	selectSize(sizeId: number): void {
		this.selectedSizeId.set(sizeId);
		this.availability.set(null);
		this.checkSelectedSkuAvailability();
	}

	private checkSelectedSkuAvailability(): void {
		const sku = this.selectedSku();
		if (sku) {
			this.checkAvailability(sku.storeSkuId);
		}
	}

	private checkAvailability(storeSkuId: number): void {
		this.isCheckingAvailability.set(true);
		this.store.checkAvailability(storeSkuId).subscribe({
			next: avail => {
				this.availability.set(avail);
				this.isCheckingAvailability.set(false);
			},
			error: () => this.isCheckingAvailability.set(false)
		});
	}

	setQuantity(qty: number): void {
		const max = this.availability()?.availableCount ?? 99;
		this.quantity.set(Math.max(1, Math.min(qty, max)));
	}

	addToCart(): void {
		const sku = this.selectedSku();
		if (!sku || this.isAdding()) return;

		this.isAdding.set(true);
		this.store.addToCart({ storeSkuId: sku.storeSkuId, quantity: this.quantity() }).subscribe({
			next: () => {
				this.toast.show('Added to cart!', 'success');
				this.isAdding.set(false);
				// Refresh availability
				this.checkAvailability(sku.storeSkuId);
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to add to cart', 'danger');
				this.isAdding.set(false);
			}
		});
	}

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}
}
