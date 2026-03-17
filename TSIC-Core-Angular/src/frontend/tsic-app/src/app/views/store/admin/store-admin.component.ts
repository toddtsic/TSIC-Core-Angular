import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import { StoreAnalyticsTabComponent } from './store-analytics-tab.component';
import type {
	StoreItemSummaryDto,
	StoreItemDto,
	StoreSkuDto,
	StoreColorDto,
	StoreSizeDto,
	CreateStoreItemRequest,
	UpdateStoreItemRequest,
	UpdateStoreSkuRequest,
} from '@core/api';

type TabKey = 'items' | 'colors' | 'sizes' | 'analytics';

@Component({
	selector: 'app-store-admin',
	standalone: true,
	imports: [CommonModule, FormsModule, TsicDialogComponent, ConfirmDialogComponent, StoreAnalyticsTabComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-admin.component.html',
	styleUrl: './store-admin.component.scss',
})
export class StoreAdminComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	// ── Tab state ──
	readonly activeTab = signal<TabKey>('items');

	// ── Loading/saving ──
	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);

	// ── Data signals ──
	readonly items = signal<StoreItemSummaryDto[]>([]);
	readonly colors = signal<StoreColorDto[]>([]);
	readonly sizes = signal<StoreSizeDto[]>([]);

	// ── Item modal ──
	readonly showItemModal = signal(false);
	readonly editingItem = signal<StoreItemSummaryDto | null>(null);
	readonly formItemName = signal('');
	readonly formItemPrice = signal(0);
	readonly formItemComments = signal('');
	readonly formMaxCanSell = signal(100);
	readonly formSelectedColorIds = signal<number[]>([]);
	readonly formSelectedSizeIds = signal<number[]>([]);

	// ── SKU expansion ──
	readonly expandedItemId = signal<number | null>(null);
	readonly expandedSkus = signal<StoreSkuDto[]>([]);
	readonly isLoadingSkus = signal(false);

	// ── SKU edit modal ──
	readonly showSkuModal = signal(false);
	readonly editingSku = signal<StoreSkuDto | null>(null);
	readonly formSkuMaxCanSell = signal(0);
	readonly formSkuActive = signal(true);

	// ── Color modal ──
	readonly showColorModal = signal(false);
	readonly editingColor = signal<StoreColorDto | null>(null);
	readonly formColorName = signal('');

	// ── Size modal ──
	readonly showSizeModal = signal(false);
	readonly editingSize = signal<StoreSizeDto | null>(null);
	readonly formSizeName = signal('');

	// ── Delete confirmation ──
	readonly showDeleteConfirm = signal(false);
	readonly deleteTarget = signal<{ type: 'color' | 'size'; id: number; name: string } | null>(null);

	// ── Computed ──
	readonly isEditingItem = computed(() => this.editingItem() !== null);

	constructor() {
		this.loadAll();
	}

	// ═══════════════════════════════════════
	//  DATA LOADING
	// ═══════════════════════════════════════

	loadAll(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);

		// Load items first, then colors and sizes
		this.store.getItems().subscribe({
			next: items => {
				this.items.set(items);
				this.store.getColors().subscribe({
					next: colors => {
						this.colors.set(colors);
						this.store.getSizes().subscribe({
							next: sizes => {
								this.sizes.set(sizes);
								this.isLoading.set(false);
							},
							error: err => this.handleLoadError(err)
						});
					},
					error: err => this.handleLoadError(err)
				});
			},
			error: err => this.handleLoadError(err)
		});
	}

	private handleLoadError(err: any): void {
		this.isLoading.set(false);
		this.errorMessage.set(err?.error?.message || 'Failed to load store data');
	}

	refresh(): void {
		this.expandedItemId.set(null);
		this.loadAll();
	}

	// ═══════════════════════════════════════
	//  ITEMS
	// ═══════════════════════════════════════

	openNewItemModal(): void {
		this.editingItem.set(null);
		this.formItemName.set('');
		this.formItemPrice.set(0);
		this.formItemComments.set('');
		this.formMaxCanSell.set(100);
		this.formSelectedColorIds.set([]);
		this.formSelectedSizeIds.set([]);
		this.showItemModal.set(true);
	}

	editItem(item: StoreItemSummaryDto): void {
		this.editingItem.set(item);
		this.formItemName.set(item.storeItemName);
		this.formItemPrice.set(item.storeItemPrice);
		this.formItemComments.set('');

		// Load full detail before showing modal to prevent saving stale/empty comments
		this.store.getItemDetail(item.storeItemId).subscribe({
			next: detail => {
				this.formItemComments.set(detail.storeItemComments ?? '');
				this.showItemModal.set(true);
			},
			error: () => {
				this.toast.show('Failed to load item details', 'danger');
			}
		});
	}

	toggleColorSelection(colorId: number): void {
		const current = this.formSelectedColorIds();
		if (current.includes(colorId)) {
			this.formSelectedColorIds.set(current.filter(id => id !== colorId));
		} else {
			this.formSelectedColorIds.set([...current, colorId]);
		}
	}

	toggleSizeSelection(sizeId: number): void {
		const current = this.formSelectedSizeIds();
		if (current.includes(sizeId)) {
			this.formSelectedSizeIds.set(current.filter(id => id !== sizeId));
		} else {
			this.formSelectedSizeIds.set([...current, sizeId]);
		}
	}

	saveItem(): void {
		if (!this.formItemName().trim()) return;
		this.isSaving.set(true);

		if (this.editingItem()) {
			const request: UpdateStoreItemRequest = {
				storeItemName: this.formItemName().trim(),
				storeItemPrice: this.formItemPrice(),
				storeItemComments: this.formItemComments().trim() || null,
				active: this.editingItem()!.active,
				sortOrder: this.editingItem()!.sortOrder,
			};
			this.store.updateItem(this.editingItem()!.storeItemId, request).subscribe({
				next: () => {
					this.toast.show('Item updated', 'success');
					this.showItemModal.set(false);
					this.isSaving.set(false);
					this.refreshItems();
				},
				error: err => {
					this.toast.show(err?.error?.message || 'Failed to update item', 'danger');
					this.isSaving.set(false);
				}
			});
		} else {
			const request: CreateStoreItemRequest = {
				storeItemName: this.formItemName().trim(),
				storeItemPrice: this.formItemPrice(),
				storeItemComments: this.formItemComments().trim() || null,
				colorIds: this.formSelectedColorIds(),
				sizeIds: this.formSelectedSizeIds(),
				maxCanSell: this.formMaxCanSell(),
			};
			this.store.createItem(request).subscribe({
				next: () => {
					this.toast.show('Item created with SKU matrix', 'success');
					this.showItemModal.set(false);
					this.isSaving.set(false);
					this.refreshItems();
				},
				error: err => {
					this.toast.show(err?.error?.message || 'Failed to create item', 'danger');
					this.isSaving.set(false);
				}
			});
		}
	}

	toggleItemActive(item: StoreItemSummaryDto): void {
		const request: UpdateStoreItemRequest = {
			storeItemName: item.storeItemName,
			storeItemPrice: item.storeItemPrice,
			storeItemComments: null,
			active: !item.active,
			sortOrder: item.sortOrder,
		};
		this.store.updateItem(item.storeItemId, request).subscribe({
			next: () => {
				this.toast.show(item.active ? 'Item deactivated' : 'Item activated', 'success');
				this.refreshItems();
			},
			error: err => this.toast.show(err?.error?.message || 'Failed to update', 'danger')
		});
	}

	private refreshItems(): void {
		this.store.getItems().subscribe({
			next: items => this.items.set(items)
		});
	}

	// ── SKU expansion ──

	toggleSkuExpansion(itemId: number): void {
		if (this.expandedItemId() === itemId) {
			this.expandedItemId.set(null);
			return;
		}
		this.expandedItemId.set(itemId);
		this.isLoadingSkus.set(true);
		this.store.getSkus(itemId).subscribe({
			next: skus => {
				this.expandedSkus.set(skus);
				this.isLoadingSkus.set(false);
			},
			error: () => this.isLoadingSkus.set(false)
		});
	}

	openSkuEditModal(sku: StoreSkuDto): void {
		this.editingSku.set(sku);
		this.formSkuMaxCanSell.set(sku.maxCanSell);
		this.formSkuActive.set(sku.active);
		this.showSkuModal.set(true);
	}

	saveSku(): void {
		const sku = this.editingSku();
		if (!sku) return;
		this.isSaving.set(true);

		const request: UpdateStoreSkuRequest = {
			active: this.formSkuActive(),
			maxCanSell: this.formSkuMaxCanSell(),
		};
		this.store.updateSku(sku.storeSkuId, request).subscribe({
			next: updated => {
				this.expandedSkus.update(list =>
					list.map(s => s.storeSkuId === updated.storeSkuId ? updated : s)
				);
				this.toast.show('SKU updated', 'success');
				this.showSkuModal.set(false);
				this.isSaving.set(false);
				this.refreshItems();
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to update SKU', 'danger');
				this.isSaving.set(false);
			}
		});
	}

	// ═══════════════════════════════════════
	//  COLORS
	// ═══════════════════════════════════════

	openNewColorModal(): void {
		this.editingColor.set(null);
		this.formColorName.set('');
		this.showColorModal.set(true);
	}

	editColor(color: StoreColorDto): void {
		this.editingColor.set(color);
		this.formColorName.set(color.storeColorName);
		this.showColorModal.set(true);
	}

	saveColor(): void {
		if (!this.formColorName().trim()) return;
		this.isSaving.set(true);

		if (this.editingColor()) {
			this.store.updateColor(this.editingColor()!.storeColorId, {
				storeColorName: this.formColorName().trim()
			}).subscribe({
				next: updated => {
					this.colors.update(list =>
						list.map(c => c.storeColorId === updated.storeColorId ? updated : c)
					);
					this.toast.show('Color updated', 'success');
					this.showColorModal.set(false);
					this.isSaving.set(false);
				},
				error: err => {
					this.toast.show(err?.error?.message || 'Failed to update color', 'danger');
					this.isSaving.set(false);
				}
			});
		} else {
			this.store.createColor({ storeColorName: this.formColorName().trim() }).subscribe({
				next: created => {
					this.colors.update(list => [...list, created]);
					this.toast.show('Color created', 'success');
					this.showColorModal.set(false);
					this.isSaving.set(false);
				},
				error: err => {
					this.toast.show(err?.error?.message || 'Failed to create color', 'danger');
					this.isSaving.set(false);
				}
			});
		}
	}

	confirmDeleteColor(color: StoreColorDto): void {
		this.deleteTarget.set({ type: 'color', id: color.storeColorId, name: color.storeColorName });
		this.showDeleteConfirm.set(true);
	}

	// ═══════════════════════════════════════
	//  SIZES
	// ═══════════════════════════════════════

	openNewSizeModal(): void {
		this.editingSize.set(null);
		this.formSizeName.set('');
		this.showSizeModal.set(true);
	}

	editSize(size: StoreSizeDto): void {
		this.editingSize.set(size);
		this.formSizeName.set(size.storeSizeName);
		this.showSizeModal.set(true);
	}

	saveSize(): void {
		if (!this.formSizeName().trim()) return;
		this.isSaving.set(true);

		if (this.editingSize()) {
			this.store.updateSize(this.editingSize()!.storeSizeId, {
				storeSizeName: this.formSizeName().trim()
			}).subscribe({
				next: updated => {
					this.sizes.update(list =>
						list.map(s => s.storeSizeId === updated.storeSizeId ? updated : s)
					);
					this.toast.show('Size updated', 'success');
					this.showSizeModal.set(false);
					this.isSaving.set(false);
				},
				error: err => {
					this.toast.show(err?.error?.message || 'Failed to update size', 'danger');
					this.isSaving.set(false);
				}
			});
		} else {
			this.store.createSize({ storeSizeName: this.formSizeName().trim() }).subscribe({
				next: created => {
					this.sizes.update(list => [...list, created]);
					this.toast.show('Size created', 'success');
					this.showSizeModal.set(false);
					this.isSaving.set(false);
				},
				error: err => {
					this.toast.show(err?.error?.message || 'Failed to create size', 'danger');
					this.isSaving.set(false);
				}
			});
		}
	}

	confirmDeleteSize(size: StoreSizeDto): void {
		this.deleteTarget.set({ type: 'size', id: size.storeSizeId, name: size.storeSizeName });
		this.showDeleteConfirm.set(true);
	}

	// ═══════════════════════════════════════
	//  DELETE CONFIRMATION
	// ═══════════════════════════════════════

	onDeleteConfirmed(): void {
		const target = this.deleteTarget();
		if (!target) return;

		this.isSaving.set(true);
		const delete$ = target.type === 'color'
			? this.store.deleteColor(target.id)
			: this.store.deleteSize(target.id);

		delete$.subscribe({
			next: () => {
				if (target.type === 'color') {
					this.colors.update(list => list.filter(c => c.storeColorId !== target.id));
				} else {
					this.sizes.update(list => list.filter(s => s.storeSizeId !== target.id));
				}
				this.toast.show(`${target.type === 'color' ? 'Color' : 'Size'} deleted`, 'success');
				this.showDeleteConfirm.set(false);
				this.isSaving.set(false);
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Cannot delete — in use by SKUs', 'danger');
				this.showDeleteConfirm.set(false);
				this.isSaving.set(false);
			}
		});
	}

	// ── Helpers ──

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}

	skuLabel(sku: StoreSkuDto): string {
		const parts: string[] = [];
		if (sku.storeColorName) parts.push(sku.storeColorName);
		if (sku.storeSizeName) parts.push(sku.storeSizeName);
		return parts.length > 0 ? parts.join(' / ') : 'Default';
	}
}
