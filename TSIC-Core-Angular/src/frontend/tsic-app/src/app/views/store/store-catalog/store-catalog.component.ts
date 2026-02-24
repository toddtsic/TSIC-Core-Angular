import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import type { StoreItemSummaryDto, StoreItemDto, StoreSkuDto, SkuAvailabilityDto, StoreCartLineItemDto } from '@core/api';

interface ExpandedItemState {
	item: StoreItemDto;
	availableColors: { id: number; name: string }[];
	availableSizes: { id: number; name: string }[];
	selectedColorId: number | null;
	selectedSizeId: number | null;
	quantity: number;
	availability: SkuAvailabilityDto | null;
	skuAvailabilityMap: Map<number, SkuAvailabilityDto>;
	isCheckingAvailability: boolean;
	isAdding: boolean;
}

@Component({
	selector: 'app-store-catalog',
	standalone: true,
	imports: [CommonModule, FormsModule, RouterLink],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-catalog.component.html',
	styleUrl: './store-catalog.component.scss',
})
export class StoreCatalogComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	readonly items = signal<StoreItemSummaryDto[]>([]);
	readonly isLoading = signal(true);
	readonly errorMessage = signal<string | null>(null);

	// Cart state from shared service
	readonly cartCount = this.store.cartCount;
	readonly cartTotal = this.store.cartTotal;

	// Expanded item state — one item at a time
	readonly expandedId = signal<number | null>(null);
	readonly expandedState = signal<ExpandedItemState | null>(null);
	readonly isExpandLoading = signal(false);

	// Quick-add state for single-SKU items
	readonly quickAddingItemId = signal<number | null>(null);

	// Cart bar pulse animation
	readonly cartPulse = signal(false);

	constructor() {
		this.loadItems();
		this.store.loadCart().subscribe();
	}

	private loadItems(): void {
		this.isLoading.set(true);
		this.store.getItems().subscribe({
			next: items => {
				this.items.set(items.filter(i => i.active && i.activeSkuCount > 0));
				this.isLoading.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load store items');
				this.isLoading.set(false);
			}
		});
	}

	toggleItem(summary: StoreItemSummaryDto): void {
		// Collapse if tapping the already-expanded item
		if (this.expandedId() === summary.storeItemId) {
			this.expandedId.set(null);
			this.expandedState.set(null);
			return;
		}

		// Expand: fetch full detail with SKUs
		this.expandedId.set(summary.storeItemId);
		this.expandedState.set(null);
		this.isExpandLoading.set(true);

		this.store.getItemDetail(summary.storeItemId).subscribe({
			next: item => {
				const activeSkus = item.skus.filter(s => s.active);
				const colorMap = new Map<number, string>();
				const sizeMap = new Map<number, string>();
				for (const sku of activeSkus) {
					if (sku.storeColorId && sku.storeColorName) colorMap.set(sku.storeColorId, sku.storeColorName);
					if (sku.storeSizeId && sku.storeSizeName) sizeMap.set(sku.storeSizeId, sku.storeSizeName);
				}

				const colors = Array.from(colorMap, ([id, name]) => ({ id, name }));
				const sizes = Array.from(sizeMap, ([id, name]) => ({ id, name }));

				// Auto-select if only one option
				const autoColor = colors.length === 1 ? colors[0].id : null;

				// For size auto-select: if color is resolved, only consider sizes available for that color
				let autoSize: number | null = null;
				if (colors.length === 0) {
					// No colors — auto-select if only one size globally
					autoSize = sizes.length === 1 ? sizes[0].id : null;
				} else if (autoColor !== null) {
					// Color auto-selected — filter sizes for that color
					const sizesForColor = sizes.filter(sz => {
						return activeSkus.some(sk => sk.storeColorId === autoColor && sk.storeSizeId === sz.id);
					});
					autoSize = sizesForColor.length === 1 ? sizesForColor[0].id : null;
				}

				const state: ExpandedItemState = {
					item,
					availableColors: colors,
					availableSizes: sizes,
					selectedColorId: autoColor,
					selectedSizeId: autoSize,
					quantity: 1,
					availability: null,
					skuAvailabilityMap: new Map(),
					isCheckingAvailability: false,
					isAdding: false,
				};

				this.expandedState.set(state);
				this.isExpandLoading.set(false);

				// If a color is resolved, batch-fetch availability for all SKUs of that color
				if (autoColor !== null) {
					this.batchCheckAvailability(state, autoColor);
				} else if (colors.length === 0 && sizes.length > 0) {
					// No colors at all — fetch availability for all SKUs
					this.batchCheckAvailability(state, null);
				}
			},
			error: () => {
				this.isExpandLoading.set(false);
				this.expandedId.set(null);
			}
		});
	}

	selectColor(colorId: number): void {
		const s = this.expandedState();
		if (!s) return;

		// Filter sizes available for this color
		const sizesForColor = this.getSizesForColor(s, colorId);
		const autoSize = sizesForColor.length === 1 ? sizesForColor[0].id : null;

		const updated: ExpandedItemState = {
			...s,
			selectedColorId: colorId,
			selectedSizeId: autoSize,
			availability: null,
			skuAvailabilityMap: new Map(),
		};
		this.expandedState.set(updated);

		// Batch-fetch availability for all SKUs of this color
		this.batchCheckAvailability(updated, colorId);
	}

	selectSize(sizeId: number): void {
		const s = this.expandedState();
		if (!s) return;

		const updatedState = { ...s, selectedSizeId: sizeId };
		const sku = this.resolveSelectedSku(updatedState);
		// Pull availability from pre-fetched map
		const avail = sku ? s.skuAvailabilityMap.get(sku.storeSkuId) ?? null : null;
		this.expandedState.set({ ...updatedState, availability: avail });
	}

	/** Returns only sizes that exist as active SKUs for the given color */
	getSizesForColor(state: ExpandedItemState, colorId: number): { id: number; name: string }[] {
		if (state.availableSizes.length === 0) return [];
		const activeSkus = state.item.skus.filter(sk => sk.active && sk.storeColorId === colorId);
		const sizeIds = new Set(activeSkus.map(sk => sk.storeSizeId));
		return state.availableSizes.filter(sz => sizeIds.has(sz.id));
	}

	/** Sizes to display — filtered by selected color when colors exist */
	get filteredSizes(): { id: number; name: string }[] {
		const s = this.expandedState();
		if (!s) return [];
		if (s.availableColors.length === 0) return s.availableSizes;
		if (s.selectedColorId === null) return [];
		return this.getSizesForColor(s, s.selectedColorId);
	}

	setQuantity(qty: number): void {
		const s = this.expandedState();
		if (!s) return;
		const max = s.availability?.availableCount ?? 99;
		this.expandedState.set({ ...s, quantity: Math.max(1, Math.min(qty, max)) });
	}

	resolveSelectedSku(state: ExpandedItemState): StoreSkuDto | null {
		const skus = state.item.skus.filter(s => s.active);
		const hasColors = state.availableColors.length > 0;
		const hasSizes = state.availableSizes.length > 0;
		if (hasColors && state.selectedColorId === null) return null;
		if (hasSizes && state.selectedSizeId === null) return null;

		return skus.find(s => {
			const colorMatch = !hasColors || s.storeColorId === state.selectedColorId;
			const sizeMatch = !hasSizes || s.storeSizeId === state.selectedSizeId;
			return colorMatch && sizeMatch;
		}) ?? null;
	}

	get selectedSku(): StoreSkuDto | null {
		const s = this.expandedState();
		return s ? this.resolveSelectedSku(s) : null;
	}

	get canAddToCart(): boolean {
		const s = this.expandedState();
		if (!s) return false;
		const sku = this.resolveSelectedSku(s);
		if (!sku) return false;
		// Block until availability is confirmed
		if (s.isCheckingAvailability) return false;
		if (!s.availability) return false;
		if (s.availability.availableCount < s.quantity) return false;
		return s.quantity >= 1;
	}

	/** Batch-fetch availability for all SKUs matching a color (or all SKUs if colorId is null) */
	private batchCheckAvailability(state: ExpandedItemState, colorId: number | null): void {
		const activeSkus = state.item.skus.filter(sk => {
			if (!sk.active) return false;
			return colorId === null || sk.storeColorId === colorId;
		});
		if (activeSkus.length === 0) return;

		const skuIds = activeSkus.map(sk => sk.storeSkuId);
		this.expandedState.set({ ...state, isCheckingAvailability: true });

		this.store.checkAvailabilityBatch(skuIds).subscribe({
			next: results => {
				const cur = this.expandedState();
				if (!cur) return;
				const map = new Map<number, SkuAvailabilityDto>();
				for (const a of results) map.set(a.storeSkuId, a);

				// If a SKU is already selected, pull its availability from the batch
				const sku = this.resolveSelectedSku(cur);
				const selectedAvail = sku ? map.get(sku.storeSkuId) ?? null : null;

				this.expandedState.set({
					...cur,
					skuAvailabilityMap: map,
					availability: selectedAvail,
					isCheckingAvailability: false,
				});
			},
			error: () => {
				const cur = this.expandedState();
				if (cur) this.expandedState.set({ ...cur, isCheckingAvailability: false });
			}
		});
	}

	/** Check if a size is out of stock based on the pre-fetched availability map */
	isSizeOutOfStock(sizeId: number): boolean {
		const s = this.expandedState();
		if (!s || s.skuAvailabilityMap.size === 0) return false; // Don't dim while loading
		const colorId = s.selectedColorId;
		const sku = s.item.skus.find(sk =>
			sk.active && sk.storeColorId === colorId && sk.storeSizeId === sizeId
		);
		if (!sku) return true;
		const avail = s.skuAvailabilityMap.get(sku.storeSkuId);
		return avail ? avail.availableCount <= 0 : false;
	}

	addToCart(): void {
		const s = this.expandedState();
		if (!s) return;
		const sku = this.resolveSelectedSku(s);
		if (!sku || s.isAdding) return;

		this.expandedState.set({ ...s, isAdding: true });

		this.store.addToCart({ storeSkuId: sku.storeSkuId, quantity: s.quantity }).subscribe({
			next: () => {
				this.toast.show('Added to cart!', 'success');
				this.triggerCartPulse();
				const cur = this.expandedState();
				if (!cur) return;
				const resetColor = cur.availableColors.length === 1 ? cur.availableColors[0].id : null;
				const updated: ExpandedItemState = {
					...cur,
					isAdding: false,
					selectedColorId: resetColor,
					selectedSizeId: null,
					availability: null,
					skuAvailabilityMap: new Map(),
					quantity: 1,
				};
				this.expandedState.set(updated);
				// Re-fetch availability (stock changed after add)
				if (resetColor !== null) {
					this.batchCheckAvailability(updated, resetColor);
				} else if (cur.availableColors.length === 0 && cur.availableSizes.length > 0) {
					this.batchCheckAvailability(updated, null);
				}
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to add to cart', 'danger');
				const cur = this.expandedState();
				if (cur) this.expandedState.set({ ...cur, isAdding: false });
			}
		});
	}

	/** Quick-add for single-SKU items (no variant selection needed) */
	quickAdd(item: StoreItemSummaryDto, event: Event): void {
		event.stopPropagation(); // Prevent row toggle
		if (!item.singleSkuId || this.quickAddingItemId()) return;

		this.quickAddingItemId.set(item.storeItemId);
		this.store.addToCart({ storeSkuId: item.singleSkuId, quantity: 1 }).subscribe({
			next: () => {
				this.toast.show('Added to cart!', 'success');
				this.triggerCartPulse();
				this.quickAddingItemId.set(null);
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to add to cart', 'danger');
				this.quickAddingItemId.set(null);
			}
		});
	}

	/** Cart line items that belong to the currently expanded item */
	get cartItemsForExpanded(): StoreCartLineItemDto[] {
		const s = this.expandedState();
		if (!s) return [];
		const cart = this.store.cart();
		if (!cart?.lineItems?.length) return [];
		const skuIds = new Set(s.item.skus.map(sk => sk.storeSkuId));
		return cart.lineItems.filter(li => skuIds.has(li.storeSkuId));
	}

	variantLabel(item: StoreCartLineItemDto): string {
		const parts: string[] = [];
		if (item.colorName) parts.push(item.colorName);
		if (item.sizeName) parts.push(item.sizeName);
		return parts.join(' / ');
	}

	private triggerCartPulse(): void {
		this.cartPulse.set(true);
		setTimeout(() => this.cartPulse.set(false), 400);
	}

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}
}
