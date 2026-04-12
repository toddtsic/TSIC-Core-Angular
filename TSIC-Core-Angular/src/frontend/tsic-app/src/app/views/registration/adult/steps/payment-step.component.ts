import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';
import { CreditCardFormComponent } from '@views/registration/shared/components/credit-card-form.component';
import type { CreditCardValues } from '@infrastructure/services/adult-registration.service';

/**
 * Payment step — matches the player wizard's pattern:
 * welcome-hero + card wrapper + Bootstrap-style fee table + method selector
 * (CC / Check) + submit area.
 */
@Component({
    selector: 'app-adult-payment-step',
    standalone: true,
    imports: [CurrencyPipe, CreditCardFormComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <!-- Centered hero -->
        <div class="welcome-hero">
            <h4 class="welcome-title">
                <i class="bi bi-credit-card-fill welcome-icon" style="color: var(--bs-success)"></i>
                Complete Payment
            </h4>
            <p class="welcome-desc">
                <i class="bi bi-lock me-1"></i>Secure checkout
                <span class="desc-dot"></span>
                <i class="bi bi-receipt me-1"></i>Review fees below
                <span class="desc-dot"></span>
                <i class="bi bi-check-circle me-1"></i>Almost done!
            </p>
        </div>

        <div class="card shadow border-0 card-rounded">
            <div class="card-body">
                <!-- Error banner -->
                @if (state.paymentError()) {
                    <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
                        <i class="bi bi-exclamation-triangle-fill mt-1"></i>
                        <div>{{ state.paymentError() }}</div>
                    </div>
                }

                <!-- Fee summary — Bootstrap table -->
                @if (state.fees(); as fees) {
                    <section class="payment-summary mb-4">
                        <div class="table-responsive">
                            <table class="table table-sm align-middle mb-0">
                                <thead class="table-light">
                                    <tr>
                                        <th scope="col">Item</th>
                                        <th scope="col" class="text-end">Amount</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <tr>
                                        <td>Registration Fee</td>
                                        <td class="text-end">{{ fees.feeBase | currency }}</td>
                                    </tr>
                                    @if (fees.feeProcessing > 0) {
                                        <tr>
                                            <td>Processing Fee</td>
                                            <td class="text-end">{{ fees.feeProcessing | currency }}</td>
                                        </tr>
                                    }
                                    @if (fees.feeDiscount > 0) {
                                        <tr class="text-success">
                                            <td>Discount</td>
                                            <td class="text-end">&ndash;{{ fees.feeDiscount | currency }}</td>
                                        </tr>
                                    }
                                    @if (fees.feeLateFee > 0) {
                                        <tr class="text-warning">
                                            <td>Late Fee</td>
                                            <td class="text-end">{{ fees.feeLateFee | currency }}</td>
                                        </tr>
                                    }
                                </tbody>
                                <tfoot class="table-group-divider">
                                    <tr class="fw-bold">
                                        <td>Total Due</td>
                                        <td class="text-end text-success">{{ fees.owedTotal | currency }}</td>
                                    </tr>
                                </tfoot>
                            </table>
                        </div>
                    </section>
                }

                <!-- Payment method selector -->
                <section class="mb-4">
                    <h6 class="text-muted mb-2">Payment Method</h6>
                    <div class="btn-group w-100" role="group" aria-label="Payment method">
                        <input type="radio" class="btn-check" name="payMethod" id="payMethod-cc"
                            [checked]="state.paymentMethod() === 'CC'"
                            (change)="state.setPaymentMethod('CC')" />
                        <label class="btn btn-outline-primary" for="payMethod-cc">
                            <i class="bi bi-credit-card me-1"></i>Credit Card
                        </label>

                        <input type="radio" class="btn-check" name="payMethod" id="payMethod-check"
                            [checked]="state.paymentMethod() === 'Check'"
                            (change)="state.setPaymentMethod('Check')" />
                        <label class="btn btn-outline-primary" for="payMethod-check">
                            <i class="bi bi-envelope-paper me-1"></i>Pay by Check
                        </label>
                    </div>
                </section>

                <!-- CC form (only when CC selected) -->
                @if (state.paymentMethod() === 'CC') {
                    <app-credit-card-form
                        [defaultFirstName]="state.firstName()"
                        [defaultLastName]="state.lastName()"
                        [defaultEmail]="state.email()"
                        [defaultPhone]="state.phone()"
                        (validChange)="onCcValidChange($event)"
                        (valueChange)="onCcValueChange($event)" />
                } @else {
                    <div class="wizard-callout wizard-callout-info">
                        <i class="bi bi-info-circle"></i>
                        <span>
                            Your registration will be recorded now. Please mail a check for the total
                            amount above. Your participation will be confirmed once payment is received.
                        </span>
                    </div>
                }

                <!-- Submit -->
                <div class="payment-actions d-flex justify-content-end mt-4">
                    <button class="btn btn-success btn-lg"
                        [disabled]="!canSubmit() || state.paymentSubmitting()"
                        (click)="onSubmitPayment()">
                        @if (state.paymentSubmitting()) {
                            <span class="spinner-border spinner-border-sm me-2" role="status"></span>
                            Processing...
                        } @else if (state.paymentMethod() === 'CC') {
                            <i class="bi bi-lock-fill me-1"></i>
                            Pay {{ state.fees()?.owedTotal | currency }}
                        } @else {
                            <i class="bi bi-check-circle me-1"></i>
                            Confirm &amp; Submit
                        }
                    </button>
                </div>
            </div>
        </div>
    `,
    styles: [],
})
export class PaymentStepComponent {
    readonly state = inject(AdultWizardStateService);

    readonly ccValid = signal(false);
    private ccValues: CreditCardValues | null = null;

    /** Submit enabled when payment method's prerequisites are met. */
    readonly canSubmit = computed(() =>
        this.state.paymentMethod() === 'Check' || this.ccValid(),
    );

    onCcValidChange(valid: boolean): void {
        this.ccValid.set(valid);
    }

    onCcValueChange(values: Record<string, string>): void {
        this.ccValues = {
            number: values['number'] ?? '',
            expiry: values['expiry'] ?? '',
            code: values['code'] ?? '',
            firstName: values['firstName'] ?? '',
            lastName: values['lastName'] ?? '',
            address: values['address'] ?? '',
            zip: values['zip'] ?? '',
            email: values['email'] ?? '',
            phone: values['phone'] ?? '',
        };
    }

    async onSubmitPayment(): Promise<void> {
        if (!this.canSubmit()) return;
        if (this.state.paymentMethod() === 'CC' && !this.ccValues) return;
        await this.state.submitPayment(
            this.state.paymentMethod() === 'CC' ? this.ccValues! : undefined,
        );
    }
}
