import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import type { PaymentMethodOptionDto, StoreCheckoutResultDto } from '@core/api';

@Component({
	selector: 'app-store-checkout',
	standalone: true,
	imports: [CommonModule, FormsModule, RouterLink],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-checkout.component.html',
})
export class StoreCheckoutComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	readonly cart = this.store.cart;
	readonly isLoading = signal(true);
	readonly isSubmitting = signal(false);
	readonly errorMessage = signal<string | null>(null);

	// Payment methods
	readonly paymentMethods = signal<PaymentMethodOptionDto[]>([]);
	readonly selectedPaymentMethodId = signal('');
	readonly ccLast4 = signal('');
	readonly ccExpDate = signal('');
	readonly comment = signal('');

	// Confirmation state (after successful checkout)
	readonly confirmation = signal<StoreCheckoutResultDto | null>(null);

	readonly lineItems = computed(() => this.cart()?.lineItems ?? []);
	readonly grandTotal = computed(() => this.cart()?.grandTotal ?? 0);
	readonly subtotal = computed(() => this.cart()?.subtotal ?? 0);
	readonly totalFees = computed(() => this.cart()?.totalFees ?? 0);
	readonly totalTax = computed(() => this.cart()?.totalTax ?? 0);

	readonly canSubmit = computed(() => {
		return this.selectedPaymentMethodId() !== '' && this.lineItems().length > 0;
	});

	constructor() {
		// Load cart + payment methods
		this.store.loadCart().subscribe({
			next: () => {
				this.store.getPaymentMethods().subscribe({
					next: methods => {
						this.paymentMethods.set(methods);
						this.isLoading.set(false);
					},
					error: () => {
						this.errorMessage.set('Failed to load payment methods');
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

	submitCheckout(): void {
		if (!this.canSubmit() || this.isSubmitting()) return;

		this.isSubmitting.set(true);
		this.errorMessage.set(null);

		this.store.checkout({
			paymentMethodId: this.selectedPaymentMethodId(),
			cclast4: this.ccLast4().trim() || null,
			ccexpDate: this.ccExpDate().trim() || null,
			comment: this.comment().trim() || null,
		}).subscribe({
			next: result => {
				this.confirmation.set(result);
				this.isSubmitting.set(false);
				this.toast.show('Order placed successfully!', 'success');
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Checkout failed. Please try again.');
				this.isSubmitting.set(false);
			}
		});
	}

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}
}
