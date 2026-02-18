import {
    ChangeDetectionStrategy, Component, DestroyRef,
    inject, signal, computed, output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { IdempotencyService } from '@views/registration/wizards/shared/services/idempotency.service';
import { CreditCardFormComponent } from '@views/registration/wizards/player-registration-wizard/steps/credit-card-form.component';
import { ToastService } from '@shared-ui/toast.service';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/wizards/shared/services/credit-card-utils';
import type { CreditCardFormValue } from '@views/registration/wizards/shared/types/wizard.types';

/**
 * Team Payment step — CC form, discount codes, optional VI insurance.
 * Teams only support Pay-In-Full (no ARB/Deposit).
 */
@Component({
    selector: 'app-trw-payment-step',
    standalone: true,
    imports: [CurrencyPipe, FormsModule, CreditCardFormComponent],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Payment</h5>
      </div>
      <div class="card-body">
        @if (lastError()) {
          <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
            <div class="flex-grow-1">
              <div class="fw-semibold mb-1">Payment Error</div>
              <div class="small">{{ lastError() }}</div>
            </div>
          </div>
        }

        @if (!hasBalance()) {
          <div class="alert alert-success border-0 mb-3" role="status">
            <div class="d-flex align-items-center gap-2">
              <span class="badge bg-success">No Balance</span>
              <div>No payment is required at this time. You may proceed to the review step.</div>
            </div>
          </div>
        } @else {
          <!-- Balance banner -->
          <div class="d-flex align-items-center justify-content-between p-3 mb-3 rounded-3 bg-primary text-white">
            <span class="fw-semibold">Balance Due</span>
            <span class="fs-4 fw-bold">{{ balanceDue() | currency }}</span>
          </div>

          <!-- Line items -->
          <section class="mb-3">
            <h6 class="fw-semibold mb-2">Summary</h6>
            <div class="table-responsive">
              <table class="table table-sm align-middle mb-0">
                <thead class="table-light">
                  <tr>
                    <th>Team</th>
                    <th>Age Group</th>
                    <th class="text-end">Amount</th>
                  </tr>
                </thead>
                <tbody>
                  @for (li of lineItems(); track li.teamId) {
                    <tr>
                      <td>{{ li.teamName }}</td>
                      <td>{{ li.ageGroup }}</td>
                      <td class="text-end">{{ li.owedTotal | currency }}</td>
                    </tr>
                  }
                </tbody>
                <tfoot>
                  <tr>
                    <th colspan="2" class="text-end">Total</th>
                    <th class="text-end">{{ balanceDue() | currency }}</th>
                  </tr>
                </tfoot>
              </table>
            </div>
          </section>

          <!-- Discount code -->
          @if (state.hasActiveDiscountCodes()) {
            <div class="d-flex gap-2 mb-3 align-items-end">
              <div class="flex-grow-1">
                <label for="discountCode" class="form-label small mb-1">Discount Code</label>
                <input type="text" class="form-control form-control-sm" id="discountCode"
                       [ngModel]="discountCode()"
                       (ngModelChange)="discountCode.set($event)"
                       placeholder="Enter code">
              </div>
              <button type="button" class="btn btn-sm btn-outline-primary" (click)="applyDiscount()">
                Apply
              </button>
            </div>
          }

          <!-- Credit card form -->
          <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="cc-title"
                   style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
            <h6 id="cc-title" class="fw-semibold mb-2">Credit Card Information</h6>
            <app-credit-card-form
              (validChange)="onCcValidChange($event)"
              (valueChange)="onCcValueChange($event)" />
          </section>

          <!-- Pay button -->
          <button type="button" class="btn btn-primary"
                  (click)="submit()"
                  [disabled]="!canSubmit()">
            {{ submitting() ? 'Processing...' : 'Pay ' + (balanceDue() | currency) + ' Now' }}
          </button>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamPaymentStepV2Component {
    readonly submitted = output<void>();
    readonly state = inject(TeamWizardStateService);
    private readonly idemSvc = inject(IdempotencyService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    private readonly _creditCard = signal<CreditCardFormValue>({
        type: '', number: '', expiry: '', code: '',
        firstName: '', lastName: '', address: '', zip: '', email: '', phone: '',
    });
    readonly ccValid = signal(false);
    readonly submitting = signal(false);
    readonly lastError = signal<string | null>(null);
    readonly discountCode = signal('');
    private lastIdemKey: string | null = null;

    readonly hasBalance = computed(() => this.state.teamPayment.hasBalance());
    readonly balanceDue = computed(() => this.state.teamPayment.balanceDue());
    readonly lineItems = computed(() => this.state.teamPayment.lineItems());

    readonly canSubmit = computed(() =>
        this.hasBalance() && this.ccValid() && !this.submitting(),
    );

    onCcValidChange(valid: boolean): void { this.ccValid.set(!!valid); }
    onCcValueChange(val: Partial<CreditCardFormValue>): void {
        this._creditCard.update(c => ({ ...c, ...val }));
    }

    applyDiscount(): void {
        const code = this.discountCode().trim();
        if (!code) return;
        const teamIds = this.state.teamPayment.teamIdsWithBalance();
        if (teamIds.length === 0) return;
        this.state.teamPayment.applyDiscount(code, teamIds)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                error: () => this.toast.show('Failed to apply discount code.', 'danger', 4000),
            });
    }

    submit(): void {
        if (this.submitting() || !this.canSubmit()) return;
        this.submitting.set(true);
        this.lastError.set(null);

        if (!this.lastIdemKey) {
            this.lastIdemKey = crypto?.randomUUID
                ? crypto.randomUUID()
                : (Date.now().toString(36) + Math.random().toString(36).slice(2));
        }

        const cc = this._creditCard();
        const request = {
            jobPath: this.state.jobPath(),
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
            idempotencyKey: this.lastIdemKey,
        };

        this.state.teamPayment.submitPayment(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: resp => {
                    this.submitting.set(false);
                    if (resp?.success) {
                        this.lastIdemKey = null;
                        this.state.teamPaymentState.setLastPayment({
                            transactionId: resp.transactionId || undefined,
                            amount: this.balanceDue(),
                            message: resp.message || 'Payment successful',
                        });
                        this.submitted.emit();
                    } else {
                        this.lastError.set(resp?.message || 'Payment failed.');
                        this.toast.show(resp?.message || 'Payment failed.', 'danger', 6000);
                    }
                },
                error: (err: HttpErrorResponse) => {
                    this.submitting.set(false);
                    const msg = (err.error && typeof err.error === 'object')
                        ? (err.error.message || JSON.stringify(err.error))
                        : (err.message || 'Network error');
                    this.lastError.set(msg);
                    this.toast.show(msg, 'danger', 6000);
                },
            });
    }
}
