import { inject, Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, timer, switchMap, takeWhile, last } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	StoreDto,
	StoreItemSummaryDto,
	StoreItemDto,
	StoreSkuDto,
	StoreColorDto,
	StoreSizeDto,
	StoreCartBatchDto,
	StoreCartLineItemDto,
	SkuAvailabilityDto,
	StoreCheckoutResultDto,
	CreateStoreItemRequest,
	UpdateStoreItemRequest,
	UpdateStoreSkuRequest,
	CreateStoreColorRequest,
	UpdateStoreColorRequest,
	CreateStoreSizeRequest,
	UpdateStoreSizeRequest,
	AddToCartRequest,
	UpdateCartQuantityRequest,
	StoreCheckoutRequest,
	StoreSalesPivotDto,
	StoreSalesByItemDto,
	StorePaymentDetailDto,
	StoreFamilyPurchaseDto,
	StoreRefundedItemDto,
	StoreRestockedItemDto,
	LogRestockRequest,
	SignForPickupRequest,
	PaymentMethodOptionDto,
	StoreWalkUpRegisterRequest,
	StoreWalkUpRegisterResponse,
	StoreFamilyPlayerDto,
	StoreItemImageDto,
	StoreSaleLineDto,
	StoreSwapOptionDto,
	StoreSwapRequest,
	StoreRefundRequest,
	StoreRefundResponse,
	StoreBatchSettledStatusDto,
	StoreCampaignKind,
	StoreCampaignSetupDto,
	StoreCampaignSendRequest,
	StoreCampaignSendResponse,
	StoreCheckoutPrepareDto,
	StoreQuantityAdjustmentDto,
	StoreAdminRosterRowDto,
	StoreAdminAddRequest,
	StoreAdminUpdateRequest,
	UserSearchResponseDto,
	EmailBatchJobStatus,
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class StoreService {
	private readonly http = inject(HttpClient);
	private readonly base = `${environment.apiUrl}/store`;

	// ── Cart state (persists across route navigation) ──
	public readonly cart = signal<StoreCartBatchDto | null>(null);
	public readonly isCartLoading = signal(false);
	public readonly cartCount = computed(() => this.cart()?.lineItems?.length ?? 0);
	public readonly cartTotal = computed(() => this.cart()?.grandTotal ?? 0);

	// ── Family players (for DirectTo dropdown) ──
	public readonly familyPlayers = signal<StoreFamilyPlayerDto[]>([]);

	// ═══════════════════════════════════════
	//  CATALOG — Admin
	// ═══════════════════════════════════════

	getStore(): Observable<StoreDto> {
		return this.http.get<StoreDto>(this.base);
	}

	getItems(): Observable<StoreItemSummaryDto[]> {
		return this.http.get<StoreItemSummaryDto[]>(`${this.base}/items`);
	}

	getItemDetail(storeItemId: number): Observable<StoreItemDto> {
		return this.http.get<StoreItemDto>(`${this.base}/items/${storeItemId}`);
	}

	createItem(request: CreateStoreItemRequest): Observable<StoreItemDto> {
		return this.http.post<StoreItemDto>(`${this.base}/items`, request);
	}

	updateItem(storeItemId: number, request: UpdateStoreItemRequest): Observable<StoreItemDto> {
		return this.http.put<StoreItemDto>(`${this.base}/items/${storeItemId}`, request);
	}

	getSkus(storeItemId: number): Observable<StoreSkuDto[]> {
		return this.http.get<StoreSkuDto[]>(`${this.base}/items/${storeItemId}/skus`);
	}

	updateSku(storeSkuId: number, request: UpdateStoreSkuRequest): Observable<StoreSkuDto> {
		return this.http.put<StoreSkuDto>(`${this.base}/skus/${storeSkuId}`, request);
	}

	/** Delete one SKU. Refused server-side if it appears in any cart or purchase. */
	deleteSku(storeSkuId: number): Observable<void> {
		return this.http.delete<void>(`${this.base}/skus/${storeSkuId}`);
	}

	/** Delete an item and all of its SKUs. Refused if any SKU has been sold or is in a cart. */
	deleteItem(storeItemId: number): Observable<void> {
		return this.http.delete<void>(`${this.base}/items/${storeItemId}`);
	}

	// ── Sales operations ──

	/**
	 * Every purchased line. `walkUpOnly` is legacy's separate walk-up sales screen — the same
	 * grid narrowed to counter sales.
	 */
	getSaleLines(walkUpOnly = false): Observable<StoreSaleLineDto[]> {
		return this.http.get<StoreSaleLineDto[]>(
			`${this.base}/sales/lines?walkUpOnly=${walkUpOnly}`,
		);
	}

	/** Variants this line could be exchanged for — same item, active, in stock. */
	getSwapOptions(storeCartBatchSkuId: number): Observable<StoreSwapOptionDto[]> {
		return this.http.get<StoreSwapOptionDto[]>(
			`${this.base}/sales/lines/${storeCartBatchSkuId}/swap-options`,
		);
	}

	/**
	 * Whether the purchase's card charge has settled. Ask BEFORE opening the refund dialog: an
	 * unsettled charge can only be voided in full, so offering an amount box would mislead.
	 */
	getBatchSettledStatus(storeCartBatchId: number): Observable<StoreBatchSettledStatusDto> {
		return this.http.get<StoreBatchSettledStatusDto>(
			`${this.base}/sales/batches/${storeCartBatchId}/settled-status`,
		);
	}

	swapCartSku(request: StoreSwapRequest): Observable<void> {
		return this.http.post<void>(`${this.base}/sales/swap`, request);
	}

	/**
	 * Refund a line or void the whole purchase. Resolves with `success: false` for a gateway
	 * refusal — that is an answer to show the director, not an error to swallow.
	 */
	refundSale(request: StoreRefundRequest): Observable<StoreRefundResponse> {
		return this.http.post<StoreRefundResponse>(`${this.base}/sales/refund`, request);
	}

	// ── Email campaigns ──
	// Legacy's three store email screens. One endpoint family, `kind` selects the audience.

	/**
	 * Opens a campaign: audience size, seeded subject/body, token palette, and — for abandoned
	 * carts — the cart grid and its age window.
	 */
	getCampaignSetup(
		kind: StoreCampaignKind,
		minAgeHours?: number,
		maxAgeHours?: number,
	): Observable<StoreCampaignSetupDto> {
		const params = new URLSearchParams();
		if (minAgeHours != null) params.set('minAgeHours', String(minAgeHours));
		if (maxAgeHours != null) params.set('maxAgeHours', String(maxAgeHours));
		const query = params.toString();
		return this.http.get<StoreCampaignSetupDto>(
			`${this.base}/campaigns/${kind}${query ? `?${query}` : ''}`,
		);
	}

	startCampaign(
		kind: StoreCampaignKind,
		request: StoreCampaignSendRequest,
	): Observable<StoreCampaignSendResponse> {
		return this.http.post<StoreCampaignSendResponse>(
			`${this.base}/campaigns/${kind}/send`,
			request,
		);
	}

	getCampaignStatus(batchJobId: string): Observable<EmailBatchJobStatus> {
		return this.http.get<EmailBatchJobStatus>(`${this.base}/campaigns/status/${batchJobId}`);
	}

	/**
	 * Fires the campaign and polls until the background batch drains, emitting the final summary.
	 * Same shape as the roster and registration-search blasts — sends are never synchronous.
	 */
	sendCampaignAndAwait(
		kind: StoreCampaignKind,
		request: StoreCampaignSendRequest,
	): Observable<EmailBatchJobStatus> {
		return this.startCampaign(kind, request).pipe(
			switchMap(handle =>
				timer(0, 1000).pipe(
					switchMap(() => this.getCampaignStatus(handle.batchJobId)),
					takeWhile(s => !s.done, true),
					last(),
				),
			),
		);
	}

	// ── Store administrators ──
	// Legacy StoreAdminAdd. Every write answers with the whole refreshed roster, so the grid
	// never reconciles a partial row against a list the server may have re-sorted.

	getStoreAdmins(): Observable<StoreAdminRosterRowDto[]> {
		return this.http.get<StoreAdminRosterRowDto[]>(`${this.base}/admins`);
	}

	addStoreAdmin(request: StoreAdminAddRequest): Observable<StoreAdminRosterRowDto[]> {
		return this.http.post<StoreAdminRosterRowDto[]>(`${this.base}/admins`, request);
	}

	updateStoreAdmin(
		registrationId: string,
		request: StoreAdminUpdateRequest,
	): Observable<StoreAdminRosterRowDto[]> {
		return this.http.put<StoreAdminRosterRowDto[]>(
			`${this.base}/admins/${registrationId}`,
			request,
		);
	}

	searchStoreAdminCandidates(query: string): Observable<UserSearchResponseDto> {
		return this.http.get<UserSearchResponseDto>(
			`${this.base}/admins/candidates?q=${encodeURIComponent(query)}`,
		);
	}

	// ── Images ──
	// Files on the statics share are the source of truth, as in legacy; the server re-syncs its
	// index on every call, so a fresh GET after any mutation is always authoritative.

	/**
	 * Every image in the job's store. Items with no photo come back as one placeholder row
	 * (`isPlaceholder`), which is how the grid shows what still needs a picture.
	 */
	getStoreImages(): Observable<StoreItemImageDto[]> {
		return this.http.get<StoreItemImageDto[]>(`${this.base}/images`);
	}

	getItemImages(storeItemId: number): Observable<StoreItemImageDto[]> {
		return this.http.get<StoreItemImageDto[]>(`${this.base}/items/${storeItemId}/images`);
	}

	/** Add a photo. Server caps an item at 10, matching legacy. */
	addItemImage(storeItemId: number, file: File): Observable<StoreItemImageDto> {
		const form = new FormData();
		form.append('file', file, file.name);
		return this.http.post<StoreItemImageDto>(`${this.base}/items/${storeItemId}/images`, form);
	}

	/** Replace one photo in place, keeping its position in the item's image order. */
	replaceItemImage(
		storeItemId: number,
		instance: number,
		file: File,
	): Observable<StoreItemImageDto> {
		const form = new FormData();
		form.append('file', file, file.name);
		return this.http.put<StoreItemImageDto>(
			`${this.base}/items/${storeItemId}/images/${instance}`,
			form,
		);
	}

	/** Delete a photo. Remaining photos are renumbered server-side, so re-fetch after this. */
	deleteItemImage(storeItemId: number, instance: number): Observable<void> {
		return this.http.delete<void>(`${this.base}/items/${storeItemId}/images/${instance}`);
	}

	// ── Colors ──

	getColors(): Observable<StoreColorDto[]> {
		return this.http.get<StoreColorDto[]>(`${this.base}/colors`);
	}

	createColor(request: CreateStoreColorRequest): Observable<StoreColorDto> {
		return this.http.post<StoreColorDto>(`${this.base}/colors`, request);
	}

	updateColor(storeColorId: number, request: UpdateStoreColorRequest): Observable<StoreColorDto> {
		return this.http.put<StoreColorDto>(`${this.base}/colors/${storeColorId}`, request);
	}

	deleteColor(storeColorId: number): Observable<void> {
		return this.http.delete<void>(`${this.base}/colors/${storeColorId}`);
	}

	// ── Sizes ──

	getSizes(): Observable<StoreSizeDto[]> {
		return this.http.get<StoreSizeDto[]>(`${this.base}/sizes`);
	}

	createSize(request: CreateStoreSizeRequest): Observable<StoreSizeDto> {
		return this.http.post<StoreSizeDto>(`${this.base}/sizes`, request);
	}

	updateSize(storeSizeId: number, request: UpdateStoreSizeRequest): Observable<StoreSizeDto> {
		return this.http.put<StoreSizeDto>(`${this.base}/sizes/${storeSizeId}`, request);
	}

	deleteSize(storeSizeId: number): Observable<void> {
		return this.http.delete<void>(`${this.base}/sizes/${storeSizeId}`);
	}

	// ═══════════════════════════════════════
	//  CART — Customer
	// ═══════════════════════════════════════

	loadCart(): Observable<StoreCartBatchDto> {
		this.isCartLoading.set(true);
		return this.http.get<StoreCartBatchDto>(`${this.base}/cart`).pipe(
			tap({
				next: cart => {
					this.cart.set(cart);
					this.isCartLoading.set(false);
				},
				error: () => this.isCartLoading.set(false)
			})
		);
	}

	addToCart(request: AddToCartRequest): Observable<StoreCartBatchDto> {
		return this.http.post<StoreCartBatchDto>(`${this.base}/cart/items`, request).pipe(
			tap(cart => this.cart.set(cart))
		);
	}

	updateQuantity(storeCartBatchSkuId: number, request: UpdateCartQuantityRequest): Observable<StoreCartBatchDto> {
		return this.http.put<StoreCartBatchDto>(
			`${this.base}/cart/items/${storeCartBatchSkuId}/quantity`, request
		).pipe(
			tap(cart => this.cart.set(cart))
		);
	}

	removeFromCart(storeCartBatchSkuId: number): Observable<StoreCartBatchDto> {
		return this.http.delete<StoreCartBatchDto>(
			`${this.base}/cart/items/${storeCartBatchSkuId}`
		).pipe(
			tap(cart => this.cart.set(cart))
		);
	}

	checkAvailability(storeSkuId: number): Observable<SkuAvailabilityDto> {
		return this.http.get<SkuAvailabilityDto>(`${this.base}/skus/${storeSkuId}/availability`);
	}

	checkAvailabilityBatch(storeSkuIds: number[]): Observable<SkuAvailabilityDto[]> {
		const ids = storeSkuIds.join(',');
		return this.http.get<SkuAvailabilityDto[]>(`${this.base}/skus/availability?skuIds=${ids}`);
	}

	/**
	 * Loads the cart for the checkout page. A POST, not a GET, because it also trims any line
	 * whose stock has gone since it was added — legacy's Checkout GET did the same before
	 * rendering. The trimmed cart replaces the cached one so the page and the badge agree.
	 */
	prepareCheckout(): Observable<StoreCheckoutPrepareDto> {
		return this.http.post<StoreCheckoutPrepareDto>(`${this.base}/checkout/prepare`, {}).pipe(
			tap(result => this.cart.set(result.cart))
		);
	}

	checkout(request: StoreCheckoutRequest): Observable<StoreCheckoutResultDto> {
		return this.http.post<StoreCheckoutResultDto>(`${this.base}/checkout`, request).pipe(
			tap(result => {
				if (result.success) this.cart.set(null);
			})
		);
	}

	loadFamilyPlayers(): Observable<StoreFamilyPlayerDto[]> {
		return this.http.get<StoreFamilyPlayerDto[]>(`${this.base}/family-players`).pipe(
			tap(players => this.familyPlayers.set(players))
		);
	}

	getPaymentMethods(): Observable<PaymentMethodOptionDto[]> {
		return this.http.get<PaymentMethodOptionDto[]>(`${this.base}/payment-methods`);
	}

	downloadReceipt(storeCartBatchId: number): void {
		this.http.get(`${this.base}/receipt/${storeCartBatchId}`, { responseType: 'blob' }).subscribe({
			next: blob => {
				const url = URL.createObjectURL(blob);
				const a = document.createElement('a');
				a.href = url;
				a.download = `receipt-${storeCartBatchId}.pdf`;
				a.click();
				URL.revokeObjectURL(url);
			}
		});
	}

	// ═══════════════════════════════════════
	//  ANALYTICS — Admin
	// ═══════════════════════════════════════

	getSalesPivot(): Observable<StoreSalesPivotDto[]> {
		return this.http.get<StoreSalesPivotDto[]>(`${this.base}/analytics/sales-pivot`);
	}

	/** Every checkout auto-trim recorded for this job, newest first. */
	getQuantityAdjustments(): Observable<StoreQuantityAdjustmentDto[]> {
		return this.http.get<StoreQuantityAdjustmentDto[]>(
			`${this.base}/analytics/quantity-adjustments`);
	}

	getSalesByItem(): Observable<StoreSalesByItemDto[]> {
		return this.http.get<StoreSalesByItemDto[]>(`${this.base}/analytics/sales-by-item`);
	}

	getPaymentDetails(walkUpOnly = false): Observable<StorePaymentDetailDto[]> {
		const params = walkUpOnly ? '?walkUpOnly=true' : '';
		return this.http.get<StorePaymentDetailDto[]>(`${this.base}/analytics/payments${params}`);
	}

	getFamilyPurchases(): Observable<StoreFamilyPurchaseDto[]> {
		return this.http.get<StoreFamilyPurchaseDto[]>(`${this.base}/analytics/family-purchases`);
	}

	getFamilyPurchaseHistory(familyUserId: string): Observable<StoreFamilyPurchaseDto> {
		return this.http.get<StoreFamilyPurchaseDto>(
			`${this.base}/analytics/family-purchases/${familyUserId}`
		);
	}

	getRefundedItems(): Observable<StoreRefundedItemDto[]> {
		return this.http.get<StoreRefundedItemDto[]>(`${this.base}/analytics/refunded`);
	}

	getRestockedItems(): Observable<StoreRestockedItemDto[]> {
		return this.http.get<StoreRestockedItemDto[]>(`${this.base}/analytics/restocked`);
	}

	logRestock(request: LogRestockRequest): Observable<void> {
		return this.http.post<void>(`${this.base}/admin/restock`, request);
	}

	signForPickup(request: SignForPickupRequest): Observable<void> {
		return this.http.post<void>(`${this.base}/admin/sign-for-pickup`, request);
	}

	// ═══════════════════════════════════════
	//  WALK-UP — Anonymous Registration
	// ═══════════════════════════════════════

	walkUpRegister(request: StoreWalkUpRegisterRequest): Observable<StoreWalkUpRegisterResponse> {
		return this.http.post<StoreWalkUpRegisterResponse>(`${this.base}/walk-up-register`, request);
	}
}
