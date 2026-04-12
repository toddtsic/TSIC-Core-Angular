import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';
import { CreditCardFormComponent } from '@views/registration/shared/components/credit-card-form.component';
import type { CreditCardValues } from '@infrastructure/services/adult-registration.service';

@Component({
    selector: 'app-adult-payment-step',
    standalone: true,
    imports: [CurrencyPipe, CreditCardFormComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <!-- Hero -->
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

        <div class="card shadow-sm border-0" style="border-radius: var(--radius-md);">
            <div class="card-body">
                <!-- Error banner -->
                @if (state.paymentError()) {
                    <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
                        <i class="bi bi-exclamation-triangle-fill"></i>
                        <div>{{ state.paymentError() }}</div>
                    </div>
                }

                <!-- Fee summary -->
                @if (state.fees(); as fees) {
                    <section class="fee-summary mb-4">
                        <h6 class="text-muted mb-2">Fee Summary</h6>
                        <div class="fee-table">
                            <div class="fee-row">
                                <span>Registration Fee</span>
                                <span>{{ fees.feeBase | currency }}</span>
                            </div>
                            @if (fees.feeProcessing > 0) {
                                <div class="fee-row">
                                    <span>Processing Fee</span>
                                    <span>{{ fees.feeProcessing | currency }}</span>
                                </div>
                            }
                            @if (fees.feeDiscount > 0) {
                                <div class="fee-row text-success">
                                    <span>Discount</span>
                                    <span>-{{ fees.feeDiscount | currency }}</span>
                                </div>
                            }
                            @if (fees.feeLateFee > 0) {
                                <div class="fee-row text-warning">
                                    <span>Late Fee</span>
                                    <span>{{ fees.feeLateFee | currency }}</span>
                                </div>
                            }
                            <div class="fee-row fee-total">
                                <span>Total Due</span>
                                <span>{{ fees.owedTotal | currency }}</span>
                            </div>
                        </div>
                    </section>
                }

                <!-- Credit Card Form -->
                <app-credit-card-form
                    [defaultFirstName]="state.firstName()"
                    [defaultLastName]="state.lastName()"
                    [defaultEmail]="state.email()"
                    [defaultPhone]="state.phone()"
                    (validChange)="onCcValidChange($event)"
                    (valueChange)="onCcValueChange($event)" />

                <!-- Submit button -->
                <div class="d-flex justify-content-end mt-4">
                    <button class="btn btn-success btn-lg"
                        [disabled]="!ccValid() || state.paymentSubmitting()"
                        (click)="onSubmitPayment()">
                        @if (state.paymentSubmitting()) {
                            <span class="spinner-border spinner-border-sm me-2" role="status"></span>
                            Processing...
                        } @else {
                            <i class="bi bi-lock-fill me-1"></i>
                            Pay {{ state.fees()?.owedTotal | currency }}
                        }
                    </button>
                </div>
            </div>
        </div>
    `,
    styles: [`
        .welcome-hero {
            text-align: center;
            margin-bottom: var(--space-6);
        }
        .welcome-title {
            font-size: var(--font-size-xl);
            font-weight: var(--font-weight-semibold);
        }
        .welcome-icon {
            margin-right: var(--space-2);
        }
        .welcome-desc {
            color: var(--text-secondary);
            font-size: var(--font-size-sm);
        }
        .desc-dot {
            display: inline-block;
            width: 4px;
            height: 4px;
            border-radius: 50%;
            background: var(--neutral-400);
            vertical-align: middle;
            margin: 0 var(--space-2);
        }
        .fee-table {
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
            overflow: hidden;
        }
        .fee-row {
            display: flex;
            justify-content: space-between;
            padding: var(--space-2) var(--space-3);
        }
        .fee-row + .fee-row {
            border-top: 1px solid rgba(var(--bs-dark-rgb), 0.05);
        }
        .fee-total {
            font-weight: var(--font-weight-bold);
            font-size: var(--font-size-lg);
            background: rgba(var(--bs-primary-rgb), 0.05);
        }
    `],
})
export class PaymentStepComponent {
    readonly state = inject(AdultWizardStateService);

    readonly ccValid = signal(false);
    private ccValues: CreditCardValues | null = null;

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
        if (!this.ccValid() || !this.ccValues) return;
        await this.state.submitPayment(this.ccValues);
    }
}
