import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import { StoreAnalyticsTabComponent } from './store-analytics-tab.component';
import { StoreImagesTabComponent } from './store-images-tab.component';
import { StoreSalesTabComponent } from './store-sales-tab.component';
import { StoreCampaignsTabComponent } from './store-campaigns-tab.component';
import { StoreDashboardTabComponent } from './store-dashboard-tab.component';
import { StoreStaffTabComponent } from './store-staff-tab.component';
import { StoreExportButtonComponent } from './store-export-button.component';
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
import { formatCurrency } from '@shared/utils/money.util';

type TabKey = 'items' | 'images' | 'sales' | 'campaigns' | 'dashboard' | 'colors' | 'sizes' | 'analytics' | 'staff';

@Component({
	selector: 'app-store-admin',
	standalone: true,
	imports: [
		CommonModule,
		FormsModule,
		TsicDialogComponent,
		ConfirmDialogComponent,
		StoreAnalyticsTabComponent,
		StoreImagesTabComponent,
		StoreSalesTabComponent,
		StoreCampaignsTabComponent,
		StoreDashboardTabComponent,
		StoreStaffTabComponent,
		StoreExportButtonComponent,
	],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-admin.component.html',
	styleUrl: './store-admin.component.scss',
})
export class StoreAdminComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	// ── Tab state ──
	readonly activeTab = signal<TabKey>('items');

	// Bound as fields, not methods, so the template's [fetch] input keeps a stable reference and
	// `this` stays the component when the button calls back.
	readonly exportItems = () => this.store.exportItems();
	readonly exportSkus = () => this.store.exportSkus();

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
	// Legacy (Views/StoreItems/Index.cshtml) collects sizes and colours as free text,
	// semicolon-delimited, and resolves them BY NAME against the global StoreSizes /
	// StoreColors dictionary server-side, creating any name that does not exist yet.
	// There is no MaxCanSell at creation - stock is set afterwards on the SKUs screen.
	readonly formItemSizes = signal('');
	readonly formItemColors = signal('');
	// Edit writes Active + SortOrder ONLY (legacy StoreItemsController.UpdateItem). Name, price
	// and comments are displayed read-only so the form cannot promise a write the server discards.
	readonly formItemActive = signal(true);
	readonly formItemSortOrder = signal(0);

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
	readonly deleteTarget =
		signal<{ type: 'color' | 'size' | 'item' | 'sku'; id: number; name: string } | null>(null);

	// ── Computed ──
	readonly isEditingItem = computed(() => this.editingItem() !== null);

	/**
	 * The API returns items in STOREFRONT order (SortOrder, 0 last, then name); legacy's admin
	 * grid sorts alphabetically by Item instead. Both are offered, defaulting to legacy's.
	 *
	 * <p>Alphabetical is right for finding a product to edit, and it is what legacy showed. But
	 * Sort Order is an editable field on that same edit modal, and with only the alphabetical
	 * view a director sets a number and has no way to see what it did without leaving for the
	 * storefront. R-06.</p>
	 */
	readonly itemSort = signal<'name' | 'storefront'>('name');

	readonly sortedItems = computed(() => {
		const items = this.items();
		// Storefront order is what the API already returned — re-sorting would only risk
		// disagreeing with it. The one rule worth restating: 0 means "no preference", and the
		// server sorts those LAST (A-02), so this view must not push them to the top.
		if (this.itemSort() === 'storefront') return items;
		return [...items].sort((a, b) => a.storeItemName.localeCompare(b.storeItemName));
	});

	/** Mirrors the server-side split: ';' delimited, empties removed, each name trimmed. */
	private static parseNames(raw: string): string[] {
		return raw.split(';').map(n => n.trim()).filter(n => n.length > 0);
	}

	readonly parsedSizeNames = computed(() => StoreAdminComponent.parseNames(this.formItemSizes()));
	readonly parsedColorNames = computed(() => StoreAdminComponent.parseNames(this.formItemColors()));

	/** SKU count the server will generate: cross-product, or one dimension, or a single default SKU. */
	readonly projectedSkuCount = computed(() =>
		(this.parsedSizeNames().length || 1) * (this.parsedColorNames().length || 1));

	/** Legacy submit gate: `if (itemName && itemPrice)` - a zero price is falsy and blocks. */
	readonly canSaveItem = computed(() =>
		this.formItemName().trim().length > 0
		&& (this.isEditingItem() || this.formItemPrice() > 0));

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
		this.formItemSizes.set('');
		this.formItemColors.set('');
		this.showItemModal.set(true);
	}

	editItem(item: StoreItemSummaryDto): void {
		this.editingItem.set(item);
		this.formItemName.set(item.storeItemName);
		this.formItemPrice.set(item.storeItemPrice);
		this.formItemComments.set('');
		this.formItemActive.set(item.active);
		this.formItemSortOrder.set(item.sortOrder);

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

	saveItem(): void {
		if (!this.canSaveItem()) return;
		this.isSaving.set(true);

		if (this.editingItem()) {
			// Only active + sortOrder are honoured server-side; the rest round-trip unchanged.
			const request: UpdateStoreItemRequest = {
				storeItemName: this.formItemName().trim(),
				storeItemPrice: this.formItemPrice(),
				storeItemComments: this.formItemComments().trim() || null,
				active: this.formItemActive(),
				sortOrder: this.formItemSortOrder(),
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
				itemSizes: this.formItemSizes().trim() || null,
				itemColors: this.formItemColors().trim() || null,
			};
			this.store.createItem(request).subscribe({
				next: () => {
					// SKUs are created at MaxCanSell = 0 (legacy CreateSku), so the item is not
					// sellable until stock is set per SKU. Say so, or it looks silently broken.
					this.toast.show('Item created - now set stock per SKU', 'success');
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

	/**
	 * Legacy StoreSkusController.UpdateSku, action "batch": deleting an item removes all of its
	 * SKUs first, then the item. The server does that in one call.
	 */
	confirmDeleteItem(item: StoreItemSummaryDto): void {
		this.deleteTarget.set({ type: 'item', id: item.storeItemId, name: item.storeItemName });
		this.showDeleteConfirm.set(true);
	}

	/** Legacy action "remove": delete a single SKU. */
	confirmDeleteSku(sku: StoreSkuDto): void {
		this.deleteTarget.set({ type: 'sku', id: sku.storeSkuId, name: sku.skuLabel });
		this.showDeleteConfirm.set(true);
	}

	readonly deleteTargetLabel = computed(() => {
		switch (this.deleteTarget()?.type) {
			case 'color': return 'Color';
			case 'size': return 'Size';
			case 'item': return 'Item';
			case 'sku': return 'SKU';
			default: return '';
		}
	});

	/**
	 * Deleting an item takes every SKU with it, so the warning has to say so — the confirm text
	 * is the last point at which that is recoverable information.
	 */
	readonly deleteTargetWarning = computed(() =>
		this.deleteTarget()?.type === 'item'
			? ' Every SKU under it is deleted too.'
			: '');

	onDeleteConfirmed(): void {
		const target = this.deleteTarget();
		if (!target) return;

		this.isSaving.set(true);
		const delete$ =
			target.type === 'color' ? this.store.deleteColor(target.id)
			: target.type === 'size' ? this.store.deleteSize(target.id)
			: target.type === 'item' ? this.store.deleteItem(target.id)
			: this.store.deleteSku(target.id);

		delete$.subscribe({
			next: () => {
				switch (target.type) {
					case 'color':
						this.colors.update(list => list.filter(c => c.storeColorId !== target.id));
						break;
					case 'size':
						this.sizes.update(list => list.filter(s => s.storeSizeId !== target.id));
						break;
					case 'item':
						this.items.update(list => list.filter(i => i.storeItemId !== target.id));
						if (this.expandedItemId() === target.id) {
							this.expandedItemId.set(null);
						}
						break;
					case 'sku':
						this.expandedSkus.update(list => list.filter(s => s.storeSkuId !== target.id));
						// SKU counts on the parent row are now stale.
						this.refreshItems();
						break;
				}
				this.toast.show(`${this.deleteTargetLabel()} deleted`, 'success');
				this.showDeleteConfirm.set(false);
				this.isSaving.set(false);
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Cannot delete — in use', 'danger');
				this.showDeleteConfirm.set(false);
				this.isSaving.set(false);
			}
		});
	}

	// ── Helpers ──

	readonly formatCurrency = formatCurrency;

	skuLabel(sku: StoreSkuDto): string {
		const parts: string[] = [];
		if (sku.storeColorName) parts.push(sku.storeColorName);
		if (sku.storeSizeName) parts.push(sku.storeSizeName);
		return parts.length > 0 ? parts.join(' / ') : 'Default';
	}
}
