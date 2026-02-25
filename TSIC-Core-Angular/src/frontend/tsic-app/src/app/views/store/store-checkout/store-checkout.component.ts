import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { CreditCardFormComponent } from '@views/registration/wizards/player-registration-wizard/steps/credit-card-form.component';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/wizards/shared/services/credit-card-utils';
import type { StoreCheckoutResultDto } from '@core/api';
import type { CreditCardFormValue } from '@views/registration/wizards/shared/types/wizard.types';

@Component({
	selector: 'app-store-checkout',
	standalone: true,
	imports: [CommonModule, RouterLink, CreditCardFormComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-checkout.component.html',
	styleUrl: './store-checkout.component.scss',
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

	readonly defaultEmail = computed(() => {
		const user = this.auth.currentUser();
		return user?.username?.includes('@') ? user.username : null;
	});

	readonly canSubmit = computed(() => {
		return this.ccPaymentMethodId() !== '' && this.lineItems().length > 0 && this.ccValid();
	});

	constructor() {
		this.store.loadCart().subscribe({
			next: () => {
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
				} else {
					this.errorMessage.set(result.message || 'Payment failed. Please try again.');
					this.isSubmitting.set(false);
				}
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
