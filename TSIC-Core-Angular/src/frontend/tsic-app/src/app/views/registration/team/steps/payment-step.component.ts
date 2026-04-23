import {
    ChangeDetectionStrategy, Component, DestroyRef,
    inject, signal, computed, output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { IdempotencyService } from '@views/registration/shared/services/idempotency.service';
import { CreditCardFormComponent } from '@views/registration/shared/components/credit-card-form.component';
import { ToastService } from '@shared-ui/toast.service';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/shared/services/credit-card-utils';
import type { CreditCardFormValue } from '@views/registration/shared/types/wizard.types';
import { RegisteredTeamsGridComponent } from '../components/registered-teams-grid.component';

/**
 * Team Payment step — CC form, check payment, discount codes.
 * Teams only support Pay-In-Full (no ARB/Deposit).
 * Respects PaymentMethodsAllowedCode: 1=CC only, 2=CC or Check, 3=Check only.
 */
@Component({
    selector: 'app-trw-payment-step',
    standalone: true,
    imports: [CurrencyPipe, FormsModule, CreditCardFormComponent, RegisteredTeamsGridComponent],
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
            <div class="d-flex align-items-center gap-2">
              <span class="badge" [class]="paymentPhaseBadgeClass()">{{ paymentPhaseLabel() }}</span>
              <span class="fw-semibold">Balance Due</span>
            </div>
            <span class="fs-4 fw-bold">{{ balanceDue() | currency }}</span>
          </div>

          <!-- Line items -->
          <section class="mb-3">
            <h6 class="fw-semibold mb-2">Summary</h6>
            <app-registered-teams-grid
              [teams]="registeredTeams()"
              [showProcessing]="showProcessing()"
              [showCcOwed]="allowsCc()"
              [showCkOwed]="showProcessing()"
              [showLop]="true"
              [showDeposit]="true"
              [frozenTeamCol]="true"
              [teamColWidth]="120"
              [gridHeight]="180" />
          </section>

          <!-- Discount code -->
          @if (state.hasActiveDiscountCodes()) {
            <div class="d-flex gap-2 mb-3 align-items-end">
              <div class="flex-grow-1">
                <label for="discountCode" class="field-label discount-label">Discount Code</label>
                <input type="text" class="field-input discount-input" id="discountCode"
                       [ngModel]="discountCode()"
                       (ngModelChange)="discountCode.set($event)"
                       placeholder="Enter code">
              </div>
              <button type="button" class="btn btn-sm btn-outline-primary"
                      [disabled]="state.teamPayment.discountApplying()"
                      (click)="applyDiscount()">
                {{ state.teamPayment.discountApplying() ? 'Applying...' : 'Apply' }}
              </button>
            </div>
            @if (state.teamPayment.discountMessage()) {
              <div class="small mb-3"
                   [class.text-success]="state.teamPayment.discountAppliedOk()"
                   [class.text-danger]="!state.teamPayment.discountAppliedOk()">
                {{ state.teamPayment.discountMessage() }}
              </div>
            }
          }

          <!-- ═══ PAYMENT METHOD SELECTOR (CC or Check) ═══ -->
          @if (showMethodSelector()) {
            <div class="method-selector mb-3">
              <label class="form-label fw-semibold mb-2">Payment Method</label>
              <div class="d-flex gap-2">
                <button type="button" class="method-btn"
                        [class.active]="isCc()"
                        (click)="selectMethod('CC')">
                  <i class="bi bi-credit-card me-2"></i>Credit Card
                </button>
                <button type="button" class="method-btn"
                        [class.active]="isCheck()"
                        (click)="selectMethod('Check')">
                  <i class="bi bi-envelope-paper me-2"></i>Pay by Check
                </button>
              </div>
            </div>
          }

          <!-- Processing fee savings callout -->
          @if (isCheck() && processingFeeSavings() > 0) {
            <div class="alert alert-success border-0 d-flex align-items-center gap-2 mb-3">
              <i class="bi bi-piggy-bank fs-5"></i>
              <div>
                <strong>Save {{ processingFeeSavings() | currency }}</strong> in processing fees by paying with check.
              </div>
            </div>
          }

          <!-- ═══ CREDIT CARD FORM ═══ -->
          @if (isCc()) {
            <section class="p-3 p-sm-4 mb-3 rounded-3"
                     style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
              <app-credit-card-form
                [defaultFirstName]="clubRepContact()?.firstName ?? null"
                [defaultLastName]="clubRepContact()?.lastName ?? null"
                [defaultAddress]="clubRepContact()?.streetAddress ?? null"
                [defaultZip]="clubRepContact()?.postalCode ?? null"
                [defaultEmail]="clubRepContact()?.email ?? null"
                [defaultPhone]="clubRepContact()?.cellphone ?? clubRepContact()?.phone ?? null"
                (validChange)="onCcValidChange($event)"
                (valueChange)="onCcValueChange($event)" />
            </section>

            <button type="button" class="btn btn-primary"
                    (click)="submit()"
                    [disabled]="!canSubmitCc()">
              {{ submitting() ? 'Processing...' : 'Pay ' + (balanceDue() | currency) + ' Now' }}
            </button>
          }

          <!-- ═══ CHECK PAYMENT INSTRUCTIONS ═══ -->
          @if (isCheck()) {
            <section class="check-instructions p-3 p-sm-4 mb-3 rounded-3">
              <h6 class="fw-semibold mb-3"><i class="bi bi-envelope-paper me-2 text-primary"></i>Check Payment Instructions</h6>

              @if (payTo()) {
                <div class="check-field">
                  <span class="check-label">Make check payable to:</span>
                  <span class="check-value">{{ payTo() }}</span>
                </div>
              }

              @if (mailTo()) {
                <div class="check-field">
                  <span class="check-label">Mail to:</span>
                  <span class="check-value check-address">{{ mailTo() }}</span>
                </div>
              }

              <div class="check-field">
                <span class="check-label">Amount:</span>
                <span class="check-value fw-bold text-primary">{{ checkAmount() | currency }}</span>
              </div>

              @if (mailinPaymentWarning()) {
                <div class="alert alert-warning border-0 mt-3 mb-0 small">
                  <i class="bi bi-exclamation-triangle me-1"></i>{{ mailinPaymentWarning() }}
                </div>
              }

              <div class="text-muted small mt-3">
                <i class="bi bi-info-circle me-1"></i>Your teams are registered. Your registration will be held pending receipt of payment.
              </div>
            </section>

            <button type="button" class="btn btn-primary"
                    (click)="submitCheck()"
                    [disabled]="submitting()">
              {{ submitting() ? 'Processing...' : 'Complete Registration' }}
            </button>
          }
        }
      </div>
    </div>
  `,
    styles: [`
      .discount-label {
        color: var(--bs-danger);
        font-weight: var(--font-weight-bold);
        font-size: var(--font-size-base);
        text-transform: uppercase;
        letter-spacing: 0.03em;
      }
      .discount-input {
        background-color: var(--neutral-0);
      }

      .method-selector .method-btn {
        flex: 1;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: var(--space-2) var(--space-3);
        border: 2px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        color: var(--brand-text);
        font-weight: var(--font-weight-medium);
        font-size: var(--font-size-sm);
        cursor: pointer;
        transition: all 0.15s ease;

        &:hover { border-color: var(--bs-primary); }

        &.active {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.08);
          color: var(--bs-primary);
          font-weight: var(--font-weight-semibold);
          box-shadow: 0 0 0 1px rgba(var(--bs-primary-rgb), 0.15);
        }
      }

      .check-instructions {
        background: rgba(var(--bs-info-rgb), 0.04);
        border: 1px solid rgba(var(--bs-info-rgb), 0.15);
      }

      .check-field {
        display: flex;
        gap: var(--space-2);
        padding: var(--space-2) 0;
        border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.04);

        &:last-of-type { border-bottom: none; }
      }

      .check-label {
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
        white-space: nowrap;
        min-width: 140px;
      }

      .check-value {
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        font-weight: var(--font-weight-medium);
      }

      .check-address {
        white-space: pre-line;
      }

    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamPaymentStepV2Component {
    readonly submitted = output<void>();
    readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
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

    readonly clubRepContact = computed(() => this.state.clubRepContact());
    readonly hasBalance = computed(() => this.state.teamPayment.hasBalance());
    readonly balanceDue = computed(() => this.state.teamPayment.balanceDue());
    readonly registeredTeams = computed(() => this.state.teamPayment.teams());
    readonly showProcessing = computed(() => this.state.teamPayment.bAddProcessingFees());
    // paymentMethodsAllowedCode: 1=CC only, 2=CC or Check, 3=Check only
    readonly allowsCc = computed(() => this.state.teamPayment.paymentMethodsAllowedCode() !== 3);
    readonly allowsCheck = computed(() => this.state.teamPayment.paymentMethodsAllowedCode() >= 2);
    readonly showMethodSelector = computed(() => this.state.teamPayment.showPaymentMethodSelector());
    readonly isCc = computed(() => this.state.teamPayment.isCcPayment());
    readonly isCheck = computed(() => this.state.teamPayment.isCheckPayment());
    readonly processingFeeSavings = computed(() => this.state.teamPayment.processingFeeSavings());
    readonly checkAmount = computed(() => this.state.teamPayment.totalCkOwed());
    readonly payTo = computed(() => this.state.teamPayment.payTo());
    readonly mailTo = computed(() => this.state.teamPayment.mailTo());
    readonly mailinPaymentWarning = computed(() => this.state.teamPayment.mailinPaymentWarning());

    readonly paymentPhaseLabel = computed(() => {
        const totalPaid = this.state.teamPayment.totalPaid();
        const fullRequired = this.state.fullPaymentRequired();
        if (totalPaid > 0) return 'Remaining Balance';
        if (!fullRequired) return 'Deposit';
        return 'Full Payment';
    });

    readonly paymentPhaseBadgeClass = computed(() => {
        const label = this.paymentPhaseLabel();
        if (label === 'Deposit') return 'bg-warning text-dark';
        if (label === 'Remaining Balance') return 'bg-info text-dark';
        return 'bg-light text-dark';
    });

    readonly canSubmitCc = computed(() =>
        this.hasBalance() && this.ccValid() && !this.submitting(),
    );

    selectMethod(method: 'CC' | 'Check'): void {
        this.state.teamPayment.selectPaymentMethod(method);
        this.lastError.set(null);
    }

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
                next: resp => {
                    if (resp?.success && resp.successCount > 0) {
                        this.teamReg.getTeamsMetadata()
                            .pipe(takeUntilDestroyed(this.destroyRef))
                            .subscribe({
                                next: meta => this.state.applyTeamsMetadata(meta),
                            });
                    }
                },
            });
    }

    submit(): void {
        if (this.submitting() || !this.canSubmitCc()) return;
        this.submitting.set(true);
        this.lastError.set(null);

        if (!this.lastIdemKey) {
            this.lastIdemKey = crypto?.randomUUID
                ? crypto.randomUUID()
                : (Date.now().toString(36) + Math.random().toString(36).slice(2));
        }

        const cc = this._creditCard();
        const teamIds = this.state.teamPayment.teamIdsWithBalance();
        const request = {
            teamIds,
            totalAmount: this.balanceDue(),
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

    /** Check payment — no backend call, just record intent and move to review. */
    submitCheck(): void {
        if (this.submitting()) return;
        this.submitting.set(true);
        this.state.teamPaymentState.setLastPayment({
            amount: this.checkAmount(),
            message: 'Payment by check — pending receipt',
            paymentMethod: 'Check',
        });
        this.submitting.set(false);
        this.submitted.emit();
    }
}
