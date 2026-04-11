import {
    ChangeDetectionStrategy, Component, DestroyRef, ElementRef, ViewChild,
    AfterViewInit, OnInit, OnDestroy, inject, signal, computed, output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { PaymentV2Service } from '../state/payment-v2.service';
import { PaymentStateV2Service } from '../state/payment-state-v2.service';
import { InsuranceStateV2Service } from '../state/insurance-state-v2.service';
import { InsuranceV2Service } from '../state/insurance-v2.service';
import { IdempotencyService } from '@views/registration/shared/services/idempotency.service';
import { CreditCardFormComponent } from '@views/registration/shared/components/credit-card-form.component';
import { ViChargeConfirmModalComponent } from '@views/registration/shared/components/vi-charge-confirm-modal.component';
import { ToastService } from '@shared-ui/toast.service';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/shared/services/credit-card-utils';
import type { PaymentResponseDto, PaymentRequestDto } from '@core/api';
import type { VIOfferData, CreditCardFormValue } from '@views/registration/shared/types/wizard.types';
import type { LineItem } from '../state/payment-v2.service';

/**
 * Payment step — credit card form, payment option selection, discount codes,
 * insurance (RegSaver/VerticalInsure) integration, and payment submission.
 */
@Component({
    selector: 'app-prw-payment-step',
    standalone: true,
    imports: [CurrencyPipe, DatePipe, FormsModule, CreditCardFormComponent, ViChargeConfirmModalComponent],
    template: `
    <!-- Centered hero -->
    <div class="welcome-hero">
      <h4 class="welcome-title"><i class="bi bi-credit-card-fill welcome-icon" style="color: var(--bs-success)"></i> Complete Payment</h4>
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
        @if (lastError()) {
          <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
            <div class="flex-grow-1">
              <div class="fw-semibold mb-1">Payment Error</div>
              <div class="small">{{ lastError()!.message || 'An error occurred.' }}</div>
            </div>
            @if (lastError()!.errorCode) {
              <span class="badge bg-danger-subtle text-danger-emphasis border">{{ lastError()!.errorCode }}</span>
            }
          </div>
        }

        <!-- Balance due + summary -->
        @if (paySvc.lineItems().length > 0) {
          <section class="payment-summary mb-4">
            <div class="table-responsive">
              <table class="table table-sm align-middle mb-0">
                <thead class="table-light">
                  <tr>
                    <th>Player</th>
                    <th>Team</th>
                    <th class="text-end">Fee</th>
                    <th class="text-end">Paid</th>
                    <th class="text-end">Due</th>
                  </tr>
                </thead>
                <tbody>
                  @for (li of paySvc.lineItems(); track li.playerId) {
                    <tr>
                      <td>{{ li.playerName }}</td>
                      <td>{{ li.teamName }}</td>
                      <td class="text-end">{{ li.feeTotal | currency }}</td>
                      <td class="text-end">{{ li.paidTotal | currency }}</td>
                      <td class="text-end">{{ li.amount | currency }}</td>
                    </tr>
                  }
                </tbody>
                <tfoot>
                  @if (paySvc.appliedDiscount() > 0) {
                    <tr>
                      <td colspan="4" class="text-end text-success">Discount</td>
                      <td class="text-end text-success">-{{ paySvc.appliedDiscount() | currency }}</td>
                    </tr>
                  }
                  <tr class="table-primary due-now-row">
                    @if (!paySvc.isArbScenario() && !paySvc.isDepositScenario()) {
                      <th class="text-start fw-bold">To Pay in Full</th>
                      <th colspan="3" class="text-end">Due Now</th>
                    } @else {
                      <th colspan="4" class="text-end">Due Now</th>
                    }
                    <th class="text-end due-now-amount">{{ currentTotal() | currency }}</th>
                  </tr>
                </tfoot>
              </table>
            </div>
          </section>
        }

        <!-- Payment option selector -->
        @if (currentTotal() > 0) {
          @if (arbHideAllOptions()) {
            <div class="alert alert-success border-0" role="status">
              <div class="d-flex align-items-center gap-2">
                <span class="badge bg-success">Paid in Full</span>
                <div>All registrations have an active recurring billing subscription.</div>
              </div>
            </div>
          } @else {
            <section class="mb-3" role="radiogroup" aria-label="Payment Option">
              @if (paySvc.isArbScenario()) {
                <div class="form-check mb-2">
                  <input class="form-check-input" type="radio" name="payOpt" id="optArb" value="ARB"
                         [checked]="paymentState.paymentOption() === 'ARB'"
                         (change)="chooseOption('ARB')">
                  <label class="form-check-label" for="optArb">
                    Automated Recurring Billing ({{ paySvc.arbOccurrences() }} payments of {{ paySvc.arbPerOccurrence() | currency }})
                  </label>
                </div>
                <div class="form-check mb-2">
                  <input class="form-check-input" type="radio" name="payOpt" id="optPif" value="PIF"
                         [checked]="paymentState.paymentOption() === 'PIF'"
                         (change)="chooseOption('PIF')">
                  <label class="form-check-label" for="optPif">Pay In Full ({{ paySvc.totalAmount() | currency }})</label>
                </div>
              } @else if (paySvc.isDepositScenario()) {
                <div class="form-check mb-2">
                  <input class="form-check-input" type="radio" name="payOpt" id="optDep" value="Deposit"
                         [checked]="paymentState.paymentOption() === 'Deposit'"
                         (change)="chooseOption('Deposit')">
                  <label class="form-check-label" for="optDep">Deposit Only ({{ paySvc.depositTotal() | currency }})</label>
                </div>
                <div class="form-check mb-2">
                  <input class="form-check-input" type="radio" name="payOpt" id="optPif2" value="PIF"
                         [checked]="paymentState.paymentOption() === 'PIF'"
                         (change)="chooseOption('PIF')">
                  <label class="form-check-label" for="optPif2">Pay In Full ({{ paySvc.totalAmount() | currency }})</label>
                </div>
              }

            </section>
          }
        }

        <!-- VerticalInsure / RegSaver region — matches legacy: widget + prompt only -->
        @if (insuranceState.offerPlayerRegSaver() && insuranceState.verticalInsureOffer().data) {
          <div class="mb-3">
            <div #viOffer id="dVIOffer" class="text-center"></div>
            @if (insuranceSvc.widgetInitialized() && !insuranceSvc.hasUserResponse()) {
              <div class="alert alert-secondary border-0 py-2 small" role="alert">
                Insurance is optional. Please indicate your interest in registration insurance for each player listed.
              </div>
            }
          </div>
        }

        <!-- VI charge confirmation modal -->
        @if (showViChargeConfirm()) {
          <app-vi-charge-confirm-modal
            [quotedPlayers]="viQuotedPlayers()"
            [premiumTotal]="viPremiumTotal()"
            [email]="viCcEmail()"
            [viCcOnlyFlow]="isViCcOnlyFlow() || isViCheckHybridFlow()"
            (cancelled)="cancelViConfirm()"
            (confirmed)="confirmViAndContinue()" />
        }

        <!-- No-payment-due info -->
        @if (showNoPaymentInfo()) {
          <div class="alert alert-success border-0 mb-3" role="status">
            <div class="d-flex align-items-center gap-2">
              <span class="badge bg-success">Paid in Full</span>
              <div>No payment due at this time. You can proceed to confirmation.</div>
            </div>
          </div>
        }

        <!-- Existing RegSaver policies — always shown for covered registrations -->
        @if (state.familyPlayers.regSaverDetails()?.length) {
          @for (policy of state.familyPlayers.regSaverDetails()!; track policy.policyNumber) {
            <div class="alert alert-success border-0 mb-2" role="status">
              <div class="d-flex align-items-center gap-2">
                <span class="badge bg-success">RegSaver</span>
                <div>
                  <div class="fw-semibold">{{ policy.playerName || 'Player' }}@if (policy.teamName) { — {{ policy.teamName }} }</div>
                  <div class="small text-muted">
                    Policy #: {{ policy.policyNumber }}
                    &bull; Created: {{ policy.policyCreateDate | date:'mediumDate' }}
                  </div>
                </div>
              </div>
            </div>
          }
        }

        <!-- Discount code -->
        @if (state.jobCtx.jobHasActiveDiscountCodes() && currentTotal() > 0) {
          <div class="d-flex gap-2 mb-3 align-items-end">
            <div class="flex-grow-1">
              <label for="discountCode" class="form-label small mb-1 fw-medium">Discount Code</label>
              <input type="text" class="form-control form-control-sm" id="discountCode"
                     [ngModel]="discountCode()"
                     (ngModelChange)="discountCode.set($event)"
                     placeholder="Enter code">
            </div>
            <button type="button" class="btn btn-sm btn-outline-primary"
                    [disabled]="paySvc.discountApplying()"
                    (click)="applyDiscount()">
              {{ paySvc.discountApplying() ? 'Applying...' : 'Apply' }}
            </button>
          </div>
          @if (paySvc.discountMessage()) {
            <div class="small mb-3"
                 [class.text-success]="paySvc.appliedDiscount() > 0"
                 [class.text-danger]="paySvc.appliedDiscount() === 0">
              {{ paySvc.discountMessage() }}
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
        @if (showCcSection()) {
          @if (isViCcOnlyFlow()) {
            <div class="alert alert-warning border-0 mb-3" role="status">
              <span class="badge bg-warning-subtle text-warning-emphasis border me-1">Insurance Premium</span>
              A registration balance is not due, but an insurance premium is. Enter card details and click
              <strong>Proceed with Insurance Processing</strong>.
            </div>
          }
          @if (isViCheckHybridFlow()) {
            <div class="alert alert-info border-0 mb-3" role="status">
              <span class="badge bg-info-subtle text-info-emphasis border me-1">Insurance Premium</span>
              Your registration will be paid by check. The credit card below is <strong>only</strong> for your
              insurance premium ({{ viPremiumTotal() | currency }}) charged by Vertical Insure.
            </div>
          }
          <app-credit-card-form
            (validChange)="onCcValidChange($event)"
            (valueChange)="onCcValueChange($event)"
            [viOnly]="isViCcOnlyFlow() || isViCheckHybridFlow()"
            [defaultFirstName]="familyUser()?.firstName ?? familyUser()?.ccInfo?.firstName ?? null"
            [defaultLastName]="familyUser()?.lastName ?? familyUser()?.ccInfo?.lastName ?? null"
            [defaultAddress]="familyUser()?.address ?? familyUser()?.ccInfo?.streetAddress ?? null"
            [defaultZip]="familyUser()?.zipCode ?? familyUser()?.zip ?? familyUser()?.ccInfo?.zip ?? null"
            [defaultEmail]="familyUser()?.ccInfo?.email ?? familyUser()?.email ?? (familyUser()?.userName?.includes('@') ? familyUser()!.userName : null)"
            [defaultPhone]="familyUser()?.ccInfo?.phone ?? familyUser()?.phone ?? null" />
        }

        <!-- ═══ CHECK PAYMENT INSTRUCTIONS ═══ -->
        @if (showCheckSection()) {
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
              <span class="check-value fw-bold text-primary">{{ checkTotal() | currency }}</span>
            </div>

            @if (mailinPaymentWarning()) {
              <div class="alert alert-warning border-0 mt-3 mb-0 small">
                <i class="bi bi-exclamation-triangle me-1"></i>{{ mailinPaymentWarning() }}
              </div>
            }

            <div class="text-muted small mt-3">
              <i class="bi bi-info-circle me-1"></i>Your registration will be held pending receipt of payment.
            </div>
          </section>
        }

        <!-- Submit buttons -->
        <div class="payment-actions">
          @if (isViCcOnlyFlow()) {
            <button type="button" class="btn btn-primary"
                    (click)="submitInsuranceOnly()"
                    [disabled]="!canInsuranceOnlySubmit() || submitting()">
              @if (submitting()) {
                <span class="spinner-border spinner-border-sm me-2"></span>Processing...
              } @else {
                <i class="bi bi-shield-lock me-2"></i>Proceed with Insurance Processing
              }
            </button>
          }
          @if (showPayNowButton()) {
            <div class="text-center">
            <button type="button" class="btn btn-primary"
                    (click)="submit()"
                    [disabled]="!canSubmit() || submitting()">
              @if (submitting()) {
                <span class="spinner-border spinner-border-sm me-2"></span>Processing...
              } @else {
                <i class="bi bi-lock-fill me-2"></i>Pay {{ currentTotal() | currency }} Now
              }
            </button>
            </div>
          }
          @if (showCheckSection()) {
            <button type="button" class="btn btn-primary"
                    (click)="submitCheck()"
                    [disabled]="submitting() || (isViCheckHybridFlow() && !ccValid())">
              @if (submitting()) {
                <span class="spinner-border spinner-border-sm me-2"></span>Processing...
              } @else if (isViCheckHybridFlow()) {
                <i class="bi bi-envelope-paper me-2"></i>Complete Registration &amp; Process Insurance
              } @else {
                <i class="bi bi-envelope-paper me-2"></i>Complete Registration
              }
            </button>
          }
          <!-- Zero-balance / ARB Continue handled by shell top-right button -->
        </div>
      </div>
    </div>
  `,
    styles: [`
      :host { display: block; }

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
export class PaymentStepComponent implements OnInit, AfterViewInit, OnDestroy {
    @ViewChild('viOffer') viOffer?: ElementRef<HTMLDivElement>;
    readonly advance = output<void>();

    readonly state = inject(PlayerWizardStateService);
    readonly paySvc = inject(PaymentV2Service);
    readonly paymentState = inject(PaymentStateV2Service);
    readonly insuranceState = inject(InsuranceStateV2Service);
    readonly insuranceSvc = inject(InsuranceV2Service);
    private readonly idemSvc = inject(IdempotencyService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    private readonly _creditCard = signal<CreditCardFormValue>({
        type: '', number: '', expiry: '', code: '',
        firstName: '', lastName: '', address: '', zip: '', email: '', phone: '',
    });
    readonly creditCard = this._creditCard.asReadonly();
    readonly ccValid = signal(false);
    readonly submitting = signal(false);
    readonly lastError = signal<{ message: string | null; errorCode: string | null } | null>(null);
    readonly showViChargeConfirm = signal(false);
    readonly discountCode = signal('');

    private lastIdemKey: string | null = null;
    private pendingSubmitAfterViConfirm = false;
    private pendingCheckSubmit = false;
    private viInitRetries = 0;
    private viInitTimeout?: ReturnType<typeof setTimeout>;

    // ── Computed helpers ────────────────────────────────────────────────
    readonly familyUser = computed(() => this.state.familyPlayers.familyUser());
    readonly currentTotal = computed(() => this.paySvc.currentTotal());

    private readonly relevantRegs = computed(() => {
        const playerIds = new Set(this.state.familyPlayers.familyPlayers().filter(p => p.selected || p.registered).map(p => p.playerId));
        return this.state.familyPlayers.familyPlayers().filter(p => playerIds.has(p.playerId)).flatMap(p => p.priorRegistrations || []);
    });

    readonly arbHideAllOptions = computed(() => {
        const regs = this.relevantRegs();
        if (!regs.length) return false;
        return regs.every(r => !!r.adnSubscriptionId && (r.adnSubscriptionStatus || '').toLowerCase() === 'active');
    });

    readonly tsicChargeDueNow = computed(() => {
        if (this.arbHideAllOptions()) return false;
        return this.currentTotal() > 0;
    });

    readonly isViCcOnlyFlow = computed(() =>
        !this.tsicChargeDueNow()
        && this.insuranceState.offerPlayerRegSaver()
        && this.insuranceState.verticalInsureConfirmed()
        && this.insuranceSvc.quotes().length > 0,
    );

    /** Check payment selected + VI insurance confirmed — hybrid: check for reg, CC for insurance. */
    readonly isViCheckHybridFlow = computed(() =>
        this.tsicChargeDueNow()
        && this.paySvc.isCheckPayment()
        && this.insuranceState.offerPlayerRegSaver()
        && this.insuranceState.verticalInsureConfirmed()
        && this.insuranceSvc.quotes().length > 0,
    );

    readonly showPayNowButton = computed(() => this.tsicChargeDueNow() && this.paySvc.isCcPayment());
    readonly showCcSection = computed(() =>
        (this.tsicChargeDueNow() && this.paySvc.isCcPayment())
        || (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0),
    );
    readonly showCheckSection = computed(() => this.tsicChargeDueNow() && this.paySvc.isCheckPayment());
    readonly showNoPaymentInfo = computed(() => !this.tsicChargeDueNow() && !this.isViCcOnlyFlow());
    readonly showMethodSelector = computed(() => this.paySvc.showPaymentMethodSelector() && this.tsicChargeDueNow());
    readonly isCc = computed(() => this.paySvc.isCcPayment());
    readonly isCheck = computed(() => this.paySvc.isCheckPayment());
    readonly processingFeeSavings = computed(() => this.paySvc.processingFeeSavings());
    readonly checkTotal = computed(() => this.paySvc.checkTotal());
    readonly payTo = computed(() => this.paySvc.payTo());
    readonly mailTo = computed(() => this.paySvc.mailTo());
    readonly mailinPaymentWarning = computed(() => this.paySvc.mailinPaymentWarning());

    readonly canSubmit = computed(() => {
        if (this.arbHideAllOptions()) return false;
        const ccNeeded = this.showCcSection();
        const ccOk = !ccNeeded || this.ccValid();
        return this.tsicChargeDueNow() && ccOk && !this.submitting();
    });

    readonly canInsuranceOnlySubmit = computed(() => {
        if (!this.isViCcOnlyFlow()) return false;
        const ccOk = !this.showCcSection() || this.ccValid();
        return ccOk && !this.submitting();
    });

    readonly viQuotedPlayers = computed(() => this.insuranceSvc.quotedPlayers());
    readonly viPremiumTotal = computed(() => this.insuranceSvc.premiumTotal());
    readonly viCcEmail = computed(() => this.familyUser()?.userName || '');

    // ── Lifecycle ────────────────────────────────────────────────────────
    ngOnInit(): void {
        this.paySvc.initPaymentMethod();
    }

    ngAfterViewInit(): void {
        this.loadStoredIdem();
        this.simpleHydrateFromCc(this.familyUser()?.ccInfo);

        const fu = this.familyUser();
        if (fu) {
            const cc = this._creditCard();
            const patch: Partial<CreditCardFormValue> = {};
            if (!cc.email && fu.email?.includes('@')) patch.email = fu.email;
            if (!cc.email && !patch.email && fu.userName?.includes('@')) patch.email = fu.userName;
            if (!cc.phone && fu.phone) patch.phone = fu.phone;
            if (Object.keys(patch).length) this._creditCard.update(c => ({ ...c, ...patch }));
        }

        this.viInitTimeout = setTimeout(() => this.tryInitVerticalInsure(), 0);
    }

    ngOnDestroy(): void {
        clearTimeout(this.viInitTimeout);
        this.insuranceSvc.resetWidgetInit();
    }

    // ── CC form callbacks ────────────────────────────────────────────────
    onCcValidChange(valid: boolean): void { this.ccValid.set(!!valid); }
    onCcValueChange(val: Partial<CreditCardFormValue>): void {
        this._creditCard.update(c => ({ ...c, ...val }));
    }

    // ── Payment method (CC vs Check) ───────────────────────────────────
    selectMethod(method: 'CC' | 'Check'): void {
        this.paySvc.selectPaymentMethod(method);
        this.lastError.set(null);
    }

    /** Check payment — record intent (and process insurance if hybrid flow). */
    submitCheck(): void {
        if (this.submitting()) return;

        // Hybrid flow: check + VI insurance — show charge confirmation first
        if (this.isViCheckHybridFlow() && this.insuranceSvc.quotes().length > 0) {
            this.pendingSubmitAfterViConfirm = true;
            this.pendingCheckSubmit = true;
            this.showViChargeConfirm.set(true);
            return;
        }

        this.finalizeCheckSubmit();
    }

    private finalizeCheckSubmit(): void {
        this.submitting.set(true);

        if (this.isViCheckHybridFlow()) {
            // Check for registration + CC for insurance premium
            this.insuranceSvc.purchaseInsurance(this._creditCard(), (msg) => {
                try {
                    this.paymentState.setLastPayment({
                        option: this.paymentState.paymentOption(),
                        amount: this.checkTotal(),
                        message: 'Payment by check \u2014 pending receipt. ' + (msg || ''),
                        paymentMethod: 'Check',
                        viPolicyNumber: this.insuranceState.viConsent()?.policyNumber ?? null,
                        viPolicyCreateDate: this.insuranceState.viConsent()?.policyCreateDate ?? null,
                    });
                } catch (e) { console.warn('[Payment] setLastPayment (check+VI) failed', e); }
                this.submitting.set(false);
                this.advance.emit();
            });
        } else {
            this.paymentState.setLastPayment({
                option: this.paymentState.paymentOption(),
                amount: this.checkTotal(),
                message: 'Payment by check \u2014 pending receipt',
                paymentMethod: 'Check',
            });
            this.submitting.set(false);
            this.advance.emit();
        }
    }

    // ── Payment option ──────────────────────────────────────────────────
    chooseOption(opt: 'PIF' | 'Deposit' | 'ARB'): void {
        this.paymentState.setPaymentOption(opt);
        this.paySvc.resetDiscount();
    }

    applyDiscount(): void {
        const code = this.discountCode().trim();
        if (code) this.paySvc.applyDiscount(code);
    }

    // ── VerticalInsure widget ───────────────────────────────────────────
    private tryInitVerticalInsure(): void {
        if (!this.insuranceState.offerPlayerRegSaver()) return;
        const offerObj = this.insuranceState.verticalInsureOffer().data;
        if (!offerObj) {
            if (this.viInitRetries++ < 20) {
                this.viInitTimeout = setTimeout(() => this.tryInitVerticalInsure(), 150);
            }
            return;
        }
        this.viInitRetries = 0;
        this.insuranceSvc.initWidget('#dVIOffer', offerObj as VIOfferData);
    }

    // ── Submit flow ─────────────────────────────────────────────────────
    submit(): void {
        if (this.submitting()) return;
        this.submitting.set(true);

        // Gate: insurance — block if widget is visible but user hasn't responded yet (matches legacy)
        if (this.insuranceState.offerPlayerRegSaver()
            && !this.insuranceSvc.hasUserResponse()
            && this.isViOfferVisible()
            && !!this.insuranceState.verticalInsureOffer().data
            && this.tsicChargeDueNow()) {
            this.submitting.set(false);
            this.toast.show('Please indicate your interest in registration insurance for each player listed.', 'danger', 4000);
            return;
        }

        // Set insurance decision from widget state (quotes determine confirm vs decline)
        if (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.hasUserResponse()) {
            if (this.insuranceSvc.quotes().length > 0) {
                this.insuranceState.confirmVerticalInsurePurchase(null, null, this.insuranceSvc.quotes() as unknown as Record<string, unknown>[]);
            } else {
                this.insuranceState.declineVerticalInsurePurchase();
            }
        }

        if (this.showCcSection() && !this.ccValid()) {
            this.submitting.set(false);
            this.toast.show('Credit card form is invalid.', 'danger', 3000);
            return;
        }

        // If VI quotes exist, show charge confirmation first
        if (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0) {
            this.submitting.set(false);
            this.pendingSubmitAfterViConfirm = true;
            this.showViChargeConfirm.set(true);
            return;
        }

        this.continueSubmit();
    }

    submitInsuranceOnly(): void {
        if (!this.canInsuranceOnlySubmit() || this.submitting()) return;
        if (!this.insuranceState.verticalInsureConfirmed()) return;
        if (this.showCcSection() && !this.ccValid()) {
            this.toast.show('Credit card form is invalid.', 'danger', 3000);
            return;
        }
        if (this.insuranceSvc.quotes().length > 0) {
            this.pendingSubmitAfterViConfirm = true;
            this.showViChargeConfirm.set(true);
            return;
        }
        this.processInsuranceOnlyFinish('Insurance request submitted.');
    }

    continueArbOrZero(): void {
        if (!this.arbHideAllOptions()) return;
        if (this.isViCcOnlyFlow()) { this.submit(); return; }
        if (this.insuranceState.offerPlayerRegSaver()) {
            if (!this.insuranceSvc.hasUserResponse() && this.isViOfferVisible()) {
                this.toast.show('Please indicate your interest in registration insurance for each player listed.', 'danger', 4000);
                return;
            }
            if (this.insuranceSvc.quotes().length > 0) {
                this.submit();
                return;
            }
            this.advance.emit();
            return;
        }
        this.advance.emit();
    }

    // ── VI confirmation modal ───────────────────────────────────────────
    cancelViConfirm(): void {
        this.showViChargeConfirm.set(false);
        this.pendingSubmitAfterViConfirm = false;
        this.pendingCheckSubmit = false;
    }

    confirmViAndContinue(): void {
        this.showViChargeConfirm.set(false);
        if (!this.pendingSubmitAfterViConfirm) return;
        this.pendingSubmitAfterViConfirm = false;

        if (this.pendingCheckSubmit) {
            // Hybrid flow: check registration + CC insurance
            this.pendingCheckSubmit = false;
            this.finalizeCheckSubmit();
        } else if (this.isViCcOnlyFlow()) {
            this.insuranceSvc.purchaseInsurance(this._creditCard(), msg => {
                try {
                    this.paymentState.setLastPayment({
                        option: this.paymentState.paymentOption(),
                        amount: 0,
                        transactionId: undefined,
                        subscriptionId: undefined,
                        viPolicyNumber: this.insuranceState.viConsent()?.policyNumber ?? null,
                        viPolicyCreateDate: this.insuranceState.viConsent()?.policyCreateDate ?? null,
                        message: msg,
                    });
                } catch (e) { console.warn('[Payment] setLastPayment (VI CC-only) failed', e); }
                this.advance.emit();
            });
        } else {
            this.continueSubmit();
        }
    }

    // ── Internal submit ─────────────────────────────────────────────────
    private continueSubmit(): void {
        this.submitting.set(true);

        if (!this.lastIdemKey) {
            const newKey = crypto?.randomUUID ? crypto.randomUUID() : (Date.now().toString(36) + Math.random().toString(36).slice(2));
            this.lastIdemKey = newKey;
            this.persistIdem(newKey);
        }

        const mapPaymentOption = (opt: string): number => {
            switch (opt) {
                case 'Deposit': return 1;
                case 'ARB': return 2;
                default: return 0;
            }
        };

        const cc = this._creditCard();
        const creditCardPayload = this.showCcSection() ? {
            number: cc.number?.trim() || null,
            expiry: sanitizeExpiry(cc.expiry),
            code: cc.code?.trim() || null,
            firstName: cc.firstName?.trim() || null,
            lastName: cc.lastName?.trim() || null,
            address: cc.address?.trim() || null,
            zip: cc.zip?.trim() || null,
            email: (cc.email || this.familyUser()?.userName || '').trim() || null,
            phone: sanitizePhone(cc.phone),
        } : null;

        const rsList = this.state.familyPlayers.regSaverDetails();
        const rs = rsList?.[0] ?? null;
        const request: PaymentRequestDto = {
            jobPath: this.state.jobCtx.jobPath(),
            paymentOption: mapPaymentOption(this.paymentState.paymentOption()),
            creditCard: creditCardPayload,
            idempotencyKey: this.lastIdemKey,
            viConfirmed: this.insuranceState.offerPlayerRegSaver() ? this.insuranceState.verticalInsureConfirmed() : undefined,
            viPolicyNumber: (this.insuranceState.verticalInsureConfirmed()
                ? (rs?.policyNumber || this.insuranceState.viConsent()?.policyNumber) : undefined) || undefined,
            viPolicyCreateDate: (this.insuranceState.verticalInsureConfirmed()
                ? (rs?.policyCreateDate || this.insuranceState.viConsent()?.policyCreateDate) : undefined) || undefined,
        };

        this.paySvc.submitPayment(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: resp => this.handlePaymentResponse(resp, rs),
                error: (err: HttpErrorResponse) => this.handlePaymentHttpError(err),
            });
    }

    private handlePaymentResponse(response: PaymentResponseDto, rs: { policyNumber?: string; policyCreateDate?: string } | null): void {
        if (response.success) {
            this.lastError.set(null);
            this.clearStoredIdem();
            this.lastIdemKey = null;
            try {
                this.paymentState.setLastPayment({
                    option: this.paymentState.paymentOption(),
                    amount: this.currentTotal(),
                    transactionId: response.transactionId || undefined,
                    subscriptionId: response.subscriptionId || undefined,
                    viPolicyNumber: rs?.policyNumber ?? null,
                    viPolicyCreateDate: rs?.policyCreateDate ?? null,
                    message: response.message ?? null,
                });
            } catch (e) { console.warn('[Payment] setLastPayment failed', e); }
            this.advance.emit();
            this.submitting.set(false);
            if (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0) {
                this.insuranceSvc.purchaseInsurance(this._creditCard());
            }
        } else {
            this.submitting.set(false);
            this.lastError.set({ message: response.message || null, errorCode: response.errorCode || null });
            this.toast.show(`[${response.errorCode || 'ERROR'}] ${response.message || 'Payment failed.'}`, 'danger', 6000);
        }
    }

    private handlePaymentHttpError(error: HttpErrorResponse): void {
        this.submitting.set(false);
        const apiMsg = (error.error && typeof error.error === 'object') ? (error.error.message || JSON.stringify(error.error)) : (error.message || 'Network error');
        const apiCode = (error.error && typeof error.error === 'object') ? (error.error.errorCode || null) : null;
        this.lastError.set({ message: apiMsg, errorCode: apiCode });
        this.toast.show(`[${apiCode || 'NETWORK'}] ${apiMsg}`, 'danger', 6000);
    }

    private processInsuranceOnlyFinish(msg: string): void {
        this.submitting.set(true);
        this.insuranceSvc.purchaseInsurance(this._creditCard(), doneMsg => {
            try {
                this.paymentState.setLastPayment({
                    option: this.paymentState.paymentOption(),
                    amount: 0,
                    transactionId: undefined,
                    subscriptionId: undefined,
                    viPolicyNumber: this.insuranceState.viConsent()?.policyNumber ?? null,
                    viPolicyCreateDate: this.insuranceState.viConsent()?.policyCreateDate ?? null,
                    message: doneMsg || msg,
                });
            } catch (e) { console.warn('[Payment] setLastPayment (insurance-only) failed', e); }
            this.submitting.set(false);
            this.advance.emit();
        });
    }

    // ── Idempotency helpers ─────────────────────────────────────────────
    private loadStoredIdem(): void {
        this.lastIdemKey = this.idemSvc.load(this.state.jobCtx.jobId(), this.familyUser()?.familyUserId) || null;
    }
    private persistIdem(key: string): void {
        this.idemSvc.persist(this.state.jobCtx.jobId(), this.familyUser()?.familyUserId, key);
    }
    private clearStoredIdem(): void {
        this.idemSvc.clear(this.state.jobCtx.jobId(), this.familyUser()?.familyUserId);
    }

    // ── CC hydration ────────────────────────────────────────────────────
    private simpleHydrateFromCc(cc?: {
        firstName?: string; lastName?: string; streetAddress?: string;
        zip?: string; email?: string; phone?: string;
    }): void {
        if (!cc) return;
        const cur = this._creditCard();
        const patch: Partial<CreditCardFormValue> = {};
        if (!cur.firstName && cc.firstName) patch.firstName = cc.firstName.trim();
        if (!cur.lastName && cc.lastName) patch.lastName = cc.lastName.trim();
        if (!cur.address && cc.streetAddress) patch.address = cc.streetAddress.trim();
        if (!cur.zip && cc.zip) patch.zip = cc.zip.trim();
        if (!cur.email && cc.email?.includes('@')) patch.email = cc.email.trim();
        if (!cur.phone && cc.phone) patch.phone = cc.phone.replaceAll(/\D+/g, '');
        if (Object.keys(patch).length) this._creditCard.update(c => ({ ...c, ...patch }));
    }

    private isViOfferVisible(): boolean {
        try {
            const el = document.getElementById('dVIOffer');
            if (!el) return false;
            const style = getComputedStyle(el);
            return style.display !== 'none' && style.visibility !== 'hidden' && (el.offsetWidth + el.offsetHeight) > 0;
        } catch (e) { console.warn('[Payment] isViOfferVisible check failed', e); return false; }
    }
}
