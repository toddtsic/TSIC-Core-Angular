import { Component, ChangeDetectionStrategy, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { CreditCardFormComponent } from '@views/registration/shared/components/credit-card-form.component';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/shared/services/credit-card-utils';
import type { StoreCheckoutResultDto, StoreCartTrimAdjustmentDto } from '@core/api';
import type { CreditCardFormValue } from '@views/registration/shared/types/wizard.types';
import { StoreFrontInfoComponent } from '../store-front-info.component';

@Component({
	selector: 'app-checkout',
	standalone: true,
	imports: [CommonModule, RouterLink, CreditCardFormComponent, StoreFrontInfoComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './checkout.component.html',
	styleUrl: './checkout.component.scss',
})
export class StoreCheckoutComponent implements OnDestroy {
	private readonly auth = inject(AuthService);
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);
	private readonly sanitizer = inject(DomSanitizer);

	readonly cart = this.store.cart;
	readonly isLoading = signal(true);
	readonly isSubmitting = signal(false);
	readonly errorMessage = signal<string | null>(null);

	// Payment method (auto-resolved to CC). The NAME is kept alongside the id purely so the
	// confirmation can say "Your Credit Card of $x was successful", as legacy's does — that
	// sentence is the only place it is used, and it costs no extra round trip.
	private readonly ccPaymentMethodId = signal('');
	readonly paymentMethodName = signal('');

	// Credit card state
	private readonly _creditCard = signal<CreditCardFormValue>({
		type: '', number: '', expiry: '', code: '',
		firstName: '', lastName: '', address: '', zip: '', email: '', phone: '',
	});
	readonly ccValid = signal(false);

	// Confirmation state (after successful checkout)
	readonly confirmation = signal<StoreCheckoutResultDto | null>(null);

	/**
	 * The receipt, shown inline under the confirmation as legacy did (A-23). Legacy inlined the
	 * whole PDF as a base64 `data:` URI in the rendered HTML; this fetches the same bytes and
	 * hands the iframe a blob URL, which keeps a multi-hundred-KB document out of the DOM.
	 */
	readonly receiptUrl = signal<SafeResourceUrl | null>(null);
	private receiptObjectUrl: string | null = null;
	readonly isResendingReceipt = signal(false);

	readonly lineItems = computed(() => this.cart()?.lineItems ?? []);
	readonly grandTotal = computed(() => this.cart()?.grandTotal ?? 0);
	readonly subtotal = computed(() => this.cart()?.subtotal ?? 0);
	readonly totalFees = computed(() => this.cart()?.totalFees ?? 0);
	readonly totalTax = computed(() => this.cart()?.totalTax ?? 0);
	// AMEX offered only when this job's merchant account accepts it (fail-closed false).
	readonly jobUsesAmex = computed(() => this.cart()?.jobUsesAmex ?? false);

	readonly defaultEmail = computed(() => {
		const user = this.auth.currentUser();
		return user?.username?.includes('@') ? user.username : null;
	});

	readonly canSubmit = computed(() => {
		return this.ccPaymentMethodId() !== '' && this.lineItems().length > 0 && this.ccValid();
	});

	/**
	 * Lines the server trimmed on the way in — a SKU sold out while this cart sat open. Legacy
	 * showed a bare "Your Cart Has Been Updated" warning and left the shopper to work out what
	 * changed; this names the items.
	 */
	readonly trimmedLines = signal<StoreCartTrimAdjustmentDto[]>([]);

	constructor() {
		// prepareCheckout, not loadCart: entering checkout re-checks availability and reduces
		// anything no longer in stock, which is what legacy's Checkout GET did before rendering.
		// Doing it here means the shopper always reviews a cart that can actually be filled.
		this.store.prepareCheckout().subscribe({
			next: prepared => {
				this.trimmedLines.set(prepared.adjustments);
				this.store.getPaymentMethods().subscribe({
					next: methods => {
						const cc = methods.find(m => m.paymentMethod.toLowerCase().includes('credit'));
						if (cc) {
							this.ccPaymentMethodId.set(cc.paymentMethodId);
							this.paymentMethodName.set(cc.paymentMethod);
						}
						this.isLoading.set(false);
					},
					error: () => {
						this.errorMessage.set('Failed to load payment configuration');
						this.isLoading.set(false);
					}
				});
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load cart');
				this.isLoading.set(false);
			}
		});
	}

	onCcValidChange(valid: boolean): void {
		this.ccValid.set(!!valid);
	}

	onCcValueChange(val: Partial<CreditCardFormValue>): void {
		this._creditCard.update(c => ({ ...c, ...val }));
	}

	submitCheckout(): void {
		if (!this.canSubmit() || this.isSubmitting()) return;

		this.isSubmitting.set(true);
		this.errorMessage.set(null);

		const cc = this._creditCard();
		this.store.checkout({
			paymentMethodId: this.ccPaymentMethodId(),
			creditCard: {
				number: cc.number?.trim() || null,
				expiry: sanitizeExpiry(cc.expiry),
				code: cc.code?.trim() || null,
				firstName: cc.firstName?.trim() || null,
				lastName: cc.lastName?.trim() || null,
				address: cc.address?.trim() || null,
				zip: cc.zip?.trim() || null,
				email: cc.email?.trim() || null,
				phone: sanitizePhone(cc.phone),
			},
			comment: null,
		}).subscribe({
			next: result => {
				if (result.success) {
					this.confirmation.set(result);
					this.isSubmitting.set(false);
					this.toast.show('Order placed successfully!', 'success');
					this.loadInlineReceipt(result.storeCartBatchId, !!result.isWalkUp);
					return;
				}

				// Someone else bought the last one between loading this page and pressing pay.
				// Nothing was charged; the server has already trimmed the cart. Re-read it so the
				// totals on screen are the ones they would actually be paying.
				if (result.errorCode === 'CART_AUTO_UPDATED') {
					this.store.prepareCheckout().subscribe({
						next: prepared => {
							this.trimmedLines.set(prepared.adjustments);
							this.isSubmitting.set(false);
						},
						error: () => {
							this.errorMessage.set(result.message ?? 'Your cart has been updated.');
							this.isSubmitting.set(false);
						},
					});
					return;
				}

				this.errorMessage.set(result.message || 'Payment failed. Please try again.');
				this.isSubmitting.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Checkout failed. Please try again.');
				this.isSubmitting.set(false);
			}
		});
	}

	downloadReceipt(): void {
		const conf = this.confirmation();
		if (conf) this.store.downloadReceipt(conf.storeCartBatchId);
	}

	/** Legacy's Resend Receipt button (A-27). The receipt already went out on checkout (A-26). */
	resendReceipt(): void {
		const conf = this.confirmation();
		if (!conf || this.isResendingReceipt()) return;

		this.isResendingReceipt.set(true);
		this.store.emailReceipt(conf.storeCartBatchId).subscribe({
			next: result => {
				this.isResendingReceipt.set(false);
				// The server reports WHY nothing was sent — no address on file is the common
				// case, and legacy's blanket "sent SUCCESSFULLY" toast lied about it.
				if (result.sent) {
					this.toast.show(`Receipt emailed to ${result.recipients.join(', ')}`, 'success');
				} else {
					this.toast.show(result.reason || 'No email address on file for this purchase.', 'warning');
				}
			},
			error: () => {
				this.isResendingReceipt.set(false);
				this.toast.show('Could not send the receipt. Please try again.', 'danger');
			},
		});
	}

	/**
	 * Fetch the receipt for the inline frame, and — for a walk-up — end the session once it is on
	 * screen. Legacy signed the walk-up customer out while RENDERING this page
	 * (`CheckoutConfirmation` → `SignoutCustomAsync`), which is the point: the counter tablet is
	 * shared, and the next customer must not inherit this one's account. Order matters — the PDF
	 * request carries the token, so the sign-out waits until the bytes are in hand. Clearing auth
	 * does not disturb the page (route guards run on navigation), so the customer keeps reading
	 * their receipt and the next click lands on the store login, exactly as legacy behaved.
	 */
	private loadInlineReceipt(storeCartBatchId: number, isWalkUp: boolean): void {
		this.store.getReceiptBlob(storeCartBatchId).subscribe({
			next: blob => {
				this.revokeReceiptUrl();
				this.receiptObjectUrl = URL.createObjectURL(blob);
				this.receiptUrl.set(
					this.sanitizer.bypassSecurityTrustResourceUrl(this.receiptObjectUrl)
				);
				if (isWalkUp) this.auth.logoutLocal();
			},
			// Supporting detail: the money moved and the confirmation above stands on its own.
			error: () => { if (isWalkUp) this.auth.logoutLocal(); },
		});
	}

	private revokeReceiptUrl(): void {
		if (this.receiptObjectUrl) {
			URL.revokeObjectURL(this.receiptObjectUrl);
			this.receiptObjectUrl = null;
		}
	}

	ngOnDestroy(): void {
		this.revokeReceiptUrl();
	}

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}

	variantLabel(item: { colorName?: string | null; sizeName?: string | null }): string {
		const parts: string[] = [];
		if (item.colorName) parts.push(item.colorName);
		if (item.sizeName) parts.push(item.sizeName);
		return parts.join(' / ');
	}
}
