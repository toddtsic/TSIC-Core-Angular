import { Component, ChangeDetectionStrategy, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import type { StoreItemSummaryDto, StoreItemDto, StoreSkuDto, SkuAvailabilityDto, StoreCartLineItemDto } from '@core/api';
import { clampAddQuantity, maxAddQuantity } from '../store-quantity';
import { compareSizeNames, isPlaceholderVariantName, orderSizes } from '../store-size-order';
import { variantLabel } from '../store-variant-label';
import { StoreFrontInfoComponent } from '../store-front-info.component';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';
import { StoreShellComponent } from '../store-shell.component';
import { formatCurrency } from '@shared/utils/money.util';

interface ExpandedItemState {
	item: StoreItemDto;
	availableColors: { id: number; name: string }[];
	availableSizes: { id: number; name: string }[];
	selectedColorId: number | null;
	selectedSizeId: number | null;
	selectedDirectToRegId: string | null;
	quantity: number;
	availability: SkuAvailabilityDto | null;
	skuAvailabilityMap: Map<number, SkuAvailabilityDto>;
	isCheckingAvailability: boolean;
	isAdding: boolean;
}

@Component({
	selector: 'app-catalog',
	standalone: true,
	imports: [CommonModule, FormsModule, RouterLink, StoreFrontInfoComponent, TsicDialogComponent, StoreShellComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './catalog.component.html',
	styleUrl: './catalog.component.scss',
})
export class StoreCatalogComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);
	private readonly route = inject(ActivatedRoute);
	private readonly router = inject(Router);

	readonly items = signal<StoreItemSummaryDto[]>([]);
	readonly isLoading = signal(true);
	readonly errorMessage = signal<string | null>(null);

	// Cart state from shared service
	readonly cartCount = this.store.cartCount;
	readonly cartTotal = this.store.cartTotal;

	// Completed purchases, for the history badge (A-09)
	readonly purchaseCount = this.store.purchaseCount;

	// Family players (for DirectTo dropdown)
	readonly familyPlayers = this.store.familyPlayers;

	// Expanded item state — one item at a time
	/**
	 * Which image the open item is showing. Legacy gave every item an EJ2 carousel;
	 * no store item in the database has ever had more than one image, so this is a
	 * thumbnail strip rather than a carousel — same capability, no widget.
	 */
	readonly galleryIndex = signal(0);

	readonly expandedId = signal<number | null>(null);
	readonly expandedState = signal<ExpandedItemState | null>(null);
	readonly isExpandLoading = signal(false);

	/**
	 * The open item's GRID row, which is already in hand when the dialog opens. It carries the
	 * name, the price and the sold-out labels, so the dialog header is populated on the first
	 * frame and only the body waits on the detail fetch — an empty title bar over a spinner
	 * reads as a broken dialog rather than a loading one.
	 */
	readonly expandedSummary = computed(() => {
		const id = this.expandedId();
		return id === null ? null : this.items().find(i => i.storeItemId === id) ?? null;
	});

	// Quick-add state for single-SKU items
	readonly quickAddingItemId = signal<number | null>(null);

	// Cart bar pulse animation
	readonly cartPulse = signal(false);

	constructor() {
		this.loadItems();
		this.store.loadCart().subscribe();
		this.store.loadFamilyPlayers().subscribe();
		// Badge only; a shopper with no history sees nothing, so a failure here is silent.
		this.store.loadPurchaseHistory().subscribe({ error: () => { } });
	}

	private loadItems(): void {
		this.isLoading.set(true);
		this.store.getItems().subscribe({
			next: items => {
				// LEGACY (GetListActiveJobStoreItems): an ACTIVE item with at least one SKU of any
				// state is listed. A product whose variants are all sold out or deactivated still
				// appears, with those variants named — it is not hidden from the store.
				this.items.set(items.filter(i => i.active && i.skuCount > 0));
				this.isLoading.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load store items');
				this.isLoading.set(false);
			}
		});
	}

	/** Dismiss the quick-view. Nothing is retained: reopening re-fetches and re-arms the picker. */
	closeItem(): void {
		this.expandedId.set(null);
		this.expandedState.set(null);
	}

	openItem(summary: StoreItemSummaryDto): void {
		if (this.expandedId() === summary.storeItemId) {
			this.closeItem();
			return;
		}

		// Fetch full detail with SKUs. The dialog opens on this frame, on expandedId alone.
		this.galleryIndex.set(0);
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

				// Smallest first. The API hands these over in legacy's order, which is
				// `OrderBy(StoreSizeName)` — alphabetical — so untouched the picker offers
				// "Adult Large, Adult Medium, Adult Small, Adult XL, Youth Large…". Nobody
				// shops a size ladder alphabetically, and the card above it states a range,
				// which the chips would then contradict.
				const sizes = Array.from(sizeMap, ([id, name]) => ({ id, name }))
					.sort((a, b) => compareSizeNames(a.name, b.name));

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

				// Auto-select DirectTo if only one family player
				const players = this.familyPlayers();
				const autoDirectTo = players.length === 1 ? players[0].registrationId : null;

				const state: ExpandedItemState = {
					item,
					availableColors: colors,
					availableSizes: sizes,
					selectedColorId: autoColor,
					selectedSizeId: autoSize,
					selectedDirectToRegId: autoDirectTo,
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
		this.setState({ ...updatedState, availability: avail });
	}

	selectDirectTo(regId: string): void {
		const s = this.expandedState();
		if (!s) return;
		this.expandedState.set({ ...s, selectedDirectToRegId: regId });
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

	/** Legacy's 5-per-add ceiling, met with the shelf. See `store-quantity.ts`. */
	get maxQuantity(): number {
		return maxAddQuantity(this.expandedState()?.availability?.availableCount);
	}

	/**
	 * Every write of `availability` goes through here, so switching to a variant with less stock
	 * can never leave behind a quantity the new ceiling forbids. The alternative — an effect
	 * watching availability — is banned, and would be re-entrant besides: it writes the same
	 * `expandedState` it reads.
	 */
	private setState(next: ExpandedItemState): void {
		this.expandedState.set({
			...next,
			quantity: clampAddQuantity(next.quantity, next.availability?.availableCount),
		});
	}

	setQuantity(qty: number): void {
		const s = this.expandedState();
		if (!s) return;
		this.expandedState.set({
			...s,
			quantity: clampAddQuantity(qty, s.availability?.availableCount),
		});
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
		// If family players exist, require DirectTo selection
		if (this.familyPlayers().length > 0 && !s.selectedDirectToRegId) return false;
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

				this.setState({
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

	/**
	 * Legacy offers TWO add buttons per item — `PreSubmit(true)` "Add to Cart and Keep Shopping"
	 * and `PreSubmit(false)` "Add to Cart and Checkout" — and only the first was ported.
	 *
	 * <p>Note where the second one actually goes: `ShoppingCart`, not Checkout. The label is
	 * legacy's own and it is wrong about its own destination, so ours is named for where it
	 * lands.</p>
	 */
	addToCart(thenGoToCart = false): void {
		const s = this.expandedState();
		if (!s) return;
		const sku = this.resolveSelectedSku(s);
		if (!sku || s.isAdding) return;

		this.expandedState.set({ ...s, isAdding: true });

		this.store.addToCart({
			storeSkuId: sku.storeSkuId,
			quantity: s.quantity,
			directToRegId: s.selectedDirectToRegId,
		}).subscribe({
			next: () => {
				if (thenGoToCart) {
					// No toast and no re-arming of the picker — the cart page is the confirmation.
					this.router.navigate(['..', 'store', 'cart'], { relativeTo: this.route });
					return;
				}

				// The dialog closes on a successful add and the cart bar pulses behind it. It
				// used to stay open and re-arm the picker, which is the right move for an inline
				// panel and the wrong one for a modal: the shopper is left staring at a form they
				// have finished with, and has to dismiss it before seeing that anything happened.
				// "Keep shopping" is what the button says, so put them back in the shop.
				this.closeItem();
				this.toast.show('Added to cart!', 'success');
				this.triggerCartPulse();
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
		event.stopPropagation(); // Prevent the card opening behind it
		if (!item.singleSkuId || this.quickAddingItemId()) return;
		// When family players exist, open the dialog instead — DirectTo has to be chosen.
		if (this.familyPlayers().length > 0) {
			this.openItem(item);
			return;
		}

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

	readonly variantLabel = variantLabel;

	private triggerCartPulse(): void {
		this.cartPulse.set(true);
		setTimeout(() => this.cartPulse.set(false), 400);
	}

	/**
	 * Strip the item name off a SKU label. The backend builds these as
	 * `Item:Size:Colour` (legacy's own shape, see `StoreSkuLabel.Build`), and the list
	 * renders inside the card that already carries the item name, so the prefix is
	 * three words of noise per row. Falls back to the whole label if it does not
	 * start with the name — never show less than the backend said.
	 */
	variantOnly(label: string, itemName: string): string {
		const prefix = `${itemName}:`;
		return label.startsWith(prefix) ? label.slice(prefix.length).replace(/:/g, ' · ') : label;
	}

	/**
	 * What the item comes in, in a shopper's terms. The card used to say "14 options", which is
	 * the row count of a SKU matrix and tells nobody anything — the question being asked is
	 * "does it come in my size, and in a colour I want".
	 *
	 * <p>Sizes read as a RANGE rather than a list — "Youth Small – Adult XL" is both shorter and
	 * more informative than eight chips. The ends come from `orderSizes`, NOT from the order the
	 * API returns: legacy sorts sizes by name (`OrderBy(StoreSizeName)`), so the raw order is
	 * alphabetical and every garment here read "Adult Large – Youth Small", a range running
	 * backwards. Colors are counted rather than named, since the swatches beside this name them.</p>
	 *
	 * <p>Returns null when nothing is left to say — a single-variant item whose only size and
	 * color are both the "Standard" placeholder.</p>
	 */
	variantSummary(item: StoreItemSummaryDto): string | null {
		const parts: string[] = [];

		// "Standard" is what item create writes when a director defines no variants at all. It
		// is a placeholder on BOTH dimensions, which is how a card came to read "Standard ·
		// Standard" — two words that answer nothing.
		const sizes = orderSizes(item.sizeNames.filter(n => !isPlaceholderVariantName(n)));
		const colors = item.colorNames.filter(n => !isPlaceholderVariantName(n));

		if (sizes.length > 2) {
			parts.push(`${sizes[0]} – ${sizes[sizes.length - 1]}`);
		} else if (sizes.length) {
			parts.push(sizes.join(' · '));
		}

		if (colors.length > 1) {
			parts.push(`${colors.length} colors`);
		} else if (colors.length === 1) {
			parts.push(colors[0]);
		}

		// Nothing worth saying — the item has one variant and the picker will show it.
		return parts.join(' · ') || null;
	}

	/**
	 * A CSS colour for a swatch, derived from the colour's NAME — there is no colour value in the
	 * database, only `StoreColorName`, and adding one is a schema change on a table shared by
	 * every store on the platform.
	 *
	 * <p>Names that CSS already knows ("Black", "Navy", "Gray") resolve on their own, which covers
	 * every colour currently in use. Anything CSS does not recognise falls back to a neutral chip
	 * rather than rendering as transparent — a swatch that shows nothing is worse than one that
	 * shows "some colour, hover for the name".</p>
	 */
	swatchColor(name: string): string {
		const css = name.trim().toLowerCase().replace(/\s+/g, '');
		return StoreCatalogComponent.CSS_COLOR_NAMES.has(css)
			? css
			: 'var(--bs-secondary-color)';
	}

	/**
	 * CSS named colours, restricted to the ones a garment is plausibly described by. Deliberately
	 * not the full 148: "tomato" and "peru" are real CSS colours and a false positive there would
	 * paint a swatch a confidently wrong shade.
	 */
	private static readonly CSS_COLOR_NAMES = new Set([
		'black', 'white', 'gray', 'grey', 'silver', 'navy', 'blue', 'lightblue', 'royalblue',
		'darkblue', 'red', 'darkred', 'maroon', 'crimson', 'green', 'darkgreen', 'forestgreen',
		'lime', 'olive', 'yellow', 'gold', 'orange', 'purple', 'violet', 'pink', 'hotpink',
		'brown', 'tan', 'beige', 'teal', 'turquoise', 'cyan', 'magenta'
	]);

	/**
	 * The director's description, or null when there isn't a real one.
	 *
	 * <p>Item create seeds the field with the literal string "new item Comments" — 2 of the 32
	 * items in the database carry it and nothing else does. Rendering that to a shopper would be
	 * worse than rendering nothing, so the placeholder is treated as absent.</p>
	 */
	itemDescription(item: StoreItemDto): string | null {
		const text = item.storeItemComments?.trim();
		if (!text) return null;
		return text.toLowerCase() === 'new item comments' ? null : text;
	}

	readonly formatCurrency = formatCurrency;
}
