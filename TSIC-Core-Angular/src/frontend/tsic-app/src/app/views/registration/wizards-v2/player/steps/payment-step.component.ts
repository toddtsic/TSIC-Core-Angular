import {
    ChangeDetectionStrategy, Component, DestroyRef, ElementRef, ViewChild,
    AfterViewInit, OnDestroy, inject, signal, computed, output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgClass, CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { PaymentV2Service } from '../state/payment-v2.service';
import { PaymentStateV2Service } from '../state/payment-state-v2.service';
import { InsuranceStateV2Service } from '../state/insurance-state-v2.service';
import { InsuranceV2Service } from '../state/insurance-v2.service';
import { IdempotencyService } from '@views/registration/wizards/shared/services/idempotency.service';
import { CreditCardFormComponent } from '@views/registration/wizards/player-registration-wizard/steps/credit-card-form.component';
import { ViChargeConfirmModalComponent } from '@views/registration/wizards/player-registration-wizard/verticalinsure/vi-charge-confirm-modal.component';
import { ToastService } from '@shared-ui/toast.service';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/wizards/shared/services/credit-card-utils';
import type { PaymentResponseDto, PaymentRequestDto } from '@core/api';
import type { VIOfferData, CreditCardFormValue } from '@views/registration/wizards/shared/types/wizard.types';
import type { LineItem } from '../state/payment-v2.service';

/**
 * Payment step — credit card form, payment option selection, discount codes,
 * insurance (RegSaver/VerticalInsure) integration, and payment submission.
 */
@Component({
    selector: 'app-prw-payment-step',
    standalone: true,
    imports: [NgClass, CurrencyPipe, DatePipe, FormsModule, CreditCardFormComponent, ViChargeConfirmModalComponent],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">
          {{ insuranceState.offerPlayerRegSaver() ? 'Payment / Insurance' : 'Payment' }}
        </h5>
      </div>
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

        <!-- Balance due banner -->
        @if (currentTotal() > 0) {
          <div class="d-flex align-items-center justify-content-between p-3 mb-3 rounded-3 bg-primary text-white">
            <span class="fw-semibold">Balance Due</span>
            <span class="fs-4 fw-bold">{{ currentTotal() | currency }}</span>
          </div>
        }

        <!-- Inline payment summary table -->
        <section class="mb-3">
          <h6 class="fw-semibold mb-2">Summary</h6>
          <div class="table-responsive">
            <table class="table table-sm align-middle mb-0">
              <thead class="table-light">
                <tr>
                  <th>Player</th>
                  <th>Team</th>
                  <th class="text-end">Amount</th>
                </tr>
              </thead>
              <tbody>
                @for (li of paySvc.lineItems(); track li.playerId) {
                  <tr>
                    <td>{{ li.playerName }}</td>
                    <td>{{ li.teamName }}</td>
                    <td class="text-end">{{ li.amount | currency }}</td>
                  </tr>
                }
              </tbody>
              <tfoot>
                @if (paySvc.appliedDiscount() > 0) {
                  <tr>
                    <td colspan="2" class="text-end text-success">Discount</td>
                    <td class="text-end text-success">-{{ paySvc.appliedDiscount() | currency }}</td>
                  </tr>
                }
                <tr>
                  <th colspan="2" class="text-end">Due Now</th>
                  <th class="text-end">{{ currentTotal() | currency }}</th>
                </tr>
              </tfoot>
            </table>
          </div>
        </section>

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
              } @else {
                <div class="form-check mb-2">
                  <input class="form-check-input" type="radio" name="payOpt" id="optPifOnly" value="PIF"
                         checked disabled>
                  <label class="form-check-label" for="optPifOnly">Pay In Full</label>
                </div>
              }

              <!-- Discount code input -->
              @if (state.jobCtx.jobHasActiveDiscountCodes()) {
                <div class="d-flex gap-2 mt-3 align-items-end">
                  <div class="flex-grow-1">
                    <label for="discountCode" class="form-label small mb-1">Discount Code</label>
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
                  <div class="small mt-1"
                       [class.text-success]="paySvc.appliedDiscount() > 0"
                       [class.text-danger]="paySvc.appliedDiscount() === 0">
                    {{ paySvc.discountMessage() }}
                  </div>
                }
              }
            </section>
          }
        }

        <!-- VerticalInsure / RegSaver region -->
        @if (insuranceState.offerPlayerRegSaver() && !state.familyPlayers.regSaverDetails()) {
          <div class="mb-3">
            <div #viOffer id="dVIOffer" class="text-center"></div>
            @if (!insuranceState.hasVerticalInsureDecision()) {
              <div class="alert alert-secondary border-0 py-2 small" role="alert">
                Insurance is optional. Choose <strong>Confirm Purchase</strong> or <strong>Decline Insurance</strong> to continue.
              </div>
            }
            @if (insuranceState.hasVerticalInsureDecision()) {
              <div class="mt-2">
                <div class="alert" [ngClass]="insuranceState.verticalInsureConfirmed() ? 'alert-success' : 'alert-secondary'" role="status">
                  <div class="d-flex align-items-center gap-2">
                    <span class="badge" [ngClass]="insuranceState.verticalInsureConfirmed() ? 'bg-success' : 'bg-secondary'">RegSaver</span>
                    <div>
                      @if (insuranceState.verticalInsureConfirmed()) {
                        <div class="fw-semibold mb-0">Insurance Selected</div>
                        @if (insuranceState.viConsent()?.policyNumber) {
                          <div class="small text-muted">Policy #: {{ insuranceState.viConsent()?.policyNumber }}</div>
                        }
                      } @else {
                        <div class="fw-semibold mb-0">Insurance Declined</div>
                        <div class="small text-muted">You chose not to purchase coverage.</div>
                      }
                    </div>
                  </div>
                </div>
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
            [viCcOnlyFlow]="isViCcOnlyFlow()"
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

        <!-- Existing RegSaver policy -->
        @if (state.familyPlayers.regSaverDetails()) {
          <div class="alert alert-info border-0 mb-3" role="status">
            <div class="d-flex align-items-center gap-2">
              <span class="badge bg-info-subtle text-info-emphasis border">RegSaver</span>
              <div>
                <div class="fw-semibold">RegSaver policy on file</div>
                <div class="small text-muted">
                  Policy #: {{ state.familyPlayers.regSaverDetails()!.policyNumber }}
                  &bull; Created: {{ state.familyPlayers.regSaverDetails()!.policyCreateDate | date:'mediumDate' }}
                </div>
              </div>
            </div>
          </div>
        }

        <!-- Credit card form -->
        @if (showCcSection()) {
          <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="cc-title"
                   style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
            <h6 id="cc-title" class="fw-semibold mb-2">Credit Card Information</h6>
            @if (isViCcOnlyFlow()) {
              <div class="alert alert-warning border-0" role="status">
                <span class="badge bg-warning-subtle text-warning-emphasis border me-1">Insurance Premium</span>
                A registration balance is not due, but an insurance premium is. Enter card details and click
                <strong>Proceed with Insurance Processing</strong>.
              </div>
            }
            <app-credit-card-form
              (validChange)="onCcValidChange($event)"
              (valueChange)="onCcValueChange($event)"
              [viOnly]="isViCcOnlyFlow()"
              [defaultFirstName]="familyUser()?.firstName ?? familyUser()?.ccInfo?.firstName ?? null"
              [defaultLastName]="familyUser()?.lastName ?? familyUser()?.ccInfo?.lastName ?? null"
              [defaultAddress]="familyUser()?.address ?? familyUser()?.ccInfo?.streetAddress ?? null"
              [defaultZip]="familyUser()?.zipCode ?? familyUser()?.zip ?? familyUser()?.ccInfo?.zip ?? null"
              [defaultEmail]="familyUser()?.ccInfo?.email ?? familyUser()?.email ?? (familyUser()?.userName?.includes('@') ? familyUser()!.userName : null)"
              [defaultPhone]="familyUser()?.ccInfo?.phone ?? familyUser()?.phone ?? null" />
          </section>
        }

        <!-- Submit buttons -->
        @if (isViCcOnlyFlow()) {
          <button type="button" class="btn btn-primary me-2"
                  (click)="submitInsuranceOnly()"
                  [disabled]="!canInsuranceOnlySubmit()">
            Proceed with Insurance Processing
          </button>
        }
        @if (showPayNowButton()) {
          <button type="button" class="btn btn-primary"
                  (click)="submit()"
                  [disabled]="!canSubmit()">
            Pay {{ currentTotal() | currency }} Now
          </button>
        }
        <!-- Zero-balance continue: handled by shell action bar via arbHideAllOptions path -->
        @if (arbHideAllOptions() && !isViCcOnlyFlow()) {
          <button type="button" class="btn btn-primary" (click)="continueArbOrZero()">
            Continue
          </button>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentStepComponent implements AfterViewInit, OnDestroy {
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
    private viInitRetries = 0;
    private hydrateTimeout?: ReturnType<typeof setTimeout>;
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

    readonly showPayNowButton = computed(() => this.tsicChargeDueNow());
    readonly showCcSection = computed(() =>
        this.tsicChargeDueNow() || (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0),
    );
    readonly showNoPaymentInfo = computed(() => !this.tsicChargeDueNow() && !this.isViCcOnlyFlow());

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
    ngAfterViewInit(): void {
        this.loadStoredIdem();
        this.simpleHydrateFromCc(this.familyUser()?.ccInfo);
        this.hydrateTimeout = setTimeout(() => this.simpleHydrateFromCc(this.familyUser()?.ccInfo), 300);

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
        clearTimeout(this.hydrateTimeout);
        clearTimeout(this.viInitTimeout);
    }

    // ── CC form callbacks ────────────────────────────────────────────────
    onCcValidChange(valid: boolean): void { this.ccValid.set(!!valid); }
    onCcValueChange(val: Partial<CreditCardFormValue>): void {
        this._creditCard.update(c => ({ ...c, ...val }));
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

        // Gate: insurance decision required
        const needInsuranceDecision = this.insuranceState.offerPlayerRegSaver()
            && !this.insuranceState.verticalInsureConfirmed()
            && !this.insuranceState.verticalInsureDeclined()
            && !this.state.familyPlayers.regSaverDetails()
            && this.tsicChargeDueNow();
        if (needInsuranceDecision && this.isViOfferVisible()) {
            this.submitting.set(false);
            this.toast.show('Insurance is optional. Please Confirm Purchase or Decline to continue.', 'danger', 4000);
            return;
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
            if (!this.insuranceState.hasVerticalInsureDecision()) {
                this.insuranceState.openVerticalInsureModal();
                return;
            }
            if (!this.insuranceState.verticalInsureConfirmed()) {
                this.advance.emit();
                return;
            }
            this.submit();
            return;
        }
        this.advance.emit();
    }

    // ── VI confirmation modal ───────────────────────────────────────────
    cancelViConfirm(): void {
        this.showViChargeConfirm.set(false);
        this.pendingSubmitAfterViConfirm = false;
    }

    confirmViAndContinue(): void {
        this.showViChargeConfirm.set(false);
        if (!this.pendingSubmitAfterViConfirm) return;
        this.pendingSubmitAfterViConfirm = false;

        if (this.isViCcOnlyFlow()) {
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
                } catch { /* ignore */ }
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

        const rs = this.state.familyPlayers.regSaverDetails();
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
            } catch { /* ignore */ }
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
            } catch { /* ignore */ }
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
        } catch { return false; }
    }
}
