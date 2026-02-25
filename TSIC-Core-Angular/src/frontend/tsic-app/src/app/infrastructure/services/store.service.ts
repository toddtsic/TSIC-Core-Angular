import { inject, Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
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
