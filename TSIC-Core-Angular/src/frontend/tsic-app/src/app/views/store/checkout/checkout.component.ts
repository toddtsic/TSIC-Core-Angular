import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { CreditCardFormComponent } from '@views/registration/shared/components/credit-card-form.component';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/shared/services/credit-card-utils';
import type { StoreCheckoutResultDto, StoreCartTrimAdjustmentDto } from '@core/api';
import type { CreditCardFormValue } from '@views/registration/shared/types/wizard.types';

@Component({
	selector: 'app-checkout',
	standalone: true,
	imports: [CommonModule, RouterLink, CreditCardFormComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './checkout.component.html',
	styleUrl: './checkout.component.scss',
})
export class StoreCheckoutComponent {
	private readonly auth = inject(AuthService);
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	readonly cart = this.store.cart;
	readonly isLoading = signal(true);
	readonly isSubmitting = signal(false);
	readonly errorMessage = signal<string | null>(null);

	// Payment method (auto-resolved to CC)
	private readonly ccPaymentMethodId = signal('');

	// Credit card state
	private readonly _creditCard = signal<CreditCardFormValue>({
		type: '', number: '', expiry: '', code: '',
		firstName: '', lastName: '', address: '', zip: '', email: '', phone: '',
	});
	readonly ccValid = signal(false);

	// Confirmation state (after successful checkout)
	readonly confirmation = signal<StoreCheckoutResultDto | null>(null);

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
						if (cc) this.ccPaymentMethodId.set(cc.paymentMethodId);
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
