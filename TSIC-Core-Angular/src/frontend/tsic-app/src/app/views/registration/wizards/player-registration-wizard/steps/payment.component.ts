import { ChangeDetectionStrategy, Component, DestroyRef, EventEmitter, Output, inject, AfterViewInit, OnDestroy, ViewChild, ElementRef, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgClass, CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PaymentSummaryComponent } from './payment-summary.component';
import { PaymentOptionSelectorComponent } from './payment-option-selector.component';
import { CreditCardFormComponent } from './credit-card-form.component';
import { PaymentService } from '../services/payment.service';
import { IdempotencyService } from '../../shared/services/idempotency.service';
import { HttpErrorResponse } from '@angular/common/http';
import { RegistrationWizardService } from '../registration-wizard.service';
import { InsuranceStateService } from '../services/insurance-state.service';
import { PaymentStateService } from '../services/payment-state.service';
import { ViChargeConfirmModalComponent } from '../verticalinsure/vi-charge-confirm-modal.component';
import type { PaymentResponseDto, PaymentRequestDto, RegSaverDetailsDto } from '@core/api';
import { TeamService } from '../team.service';
import { ToastService } from '@shared-ui/toast.service';
import { InsuranceService } from '../services/insurance.service';
import { sanitizeExpiry, sanitizePhone } from '../../shared/services/credit-card-utils';
import type { VIOfferData, CreditCardFormValue } from '../../shared/types/wizard.types';

import type { LineItem } from '../services/payment.service';

// Local aliases to make types obvious without duplicating backend DTOs
// Types now come from shared core models (see import above).

@Component({
  selector: 'app-rw-payment',
  standalone: true,
  imports: [NgClass, CurrencyPipe, DatePipe, FormsModule, ViChargeConfirmModalComponent, PaymentSummaryComponent, PaymentOptionSelectorComponent, CreditCardFormComponent],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">{{ insuranceState.offerPlayerRegSaver() ? 'Payment/Insurance' : 'Payment' }}</h5>
      </div>
      <div class="card-body">
        @if (lastError()) {
          <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
            <div class="grow">
              <div class="fw-semibold mb-1">Payment Error</div>
              <div class="small">{{ lastError()!.message || 'An error occurred.' }}</div>
            </div>
            @if (lastError()!.errorCode) { <span class="badge bg-danger-subtle text-danger-emphasis border">{{ lastError()!.errorCode }}</span> }
          </div>
        }

        <!-- Prominent balance due banner -->
        @if (currentTotal() > 0) {
          <div class="d-flex align-items-center justify-content-between p-3 mb-3 rounded-3"
               class="bg-primary text-white">
            <span class="fw-semibold">Balance Due</span>
            <span class="fs-4 fw-bold">{{ currentTotal() | currency }}</span>
          </div>
        }

        <app-payment-summary></app-payment-summary>
        <!-- Payment Option section - only shown when amount is due -->
        @if (currentTotal() > 0) {
          <!-- ARB subscription state messaging / option gating -->
          @if (arbHideAllOptions()) {
            <div class="alert alert-success border-0" role="status">
              <div class="d-flex align-items-center gap-2">
                <span class="badge bg-success">✓ Paid in Full</span>
                <div>All selected registrations have an active Automated Recurring Billing subscription. No payment action is required at this time.</div>
              </div>
            </div>
          } @else if (arbProblemAny()) {
            <div class="alert alert-danger border-0" role="alert">
              There is a problem with your Automated Recurring Billing. Please contact your club immediately.
            </div>
            <app-payment-option-selector></app-payment-option-selector>
          } @else {
            <app-payment-option-selector></app-payment-option-selector>
          }
        }
        <!-- RegSaver / VerticalInsure region (render only if offer is active and policy not already on file to avoid blank spacing) -->
        @if (insuranceState.offerPlayerRegSaver() && !state.regSaverDetails()) {
          <div class="mb-3">
            <div #viOffer id="dVIOffer" class="text-center"></div>
            @if (!insuranceState.hasVerticalInsureDecision()) {
              <div class="alert alert-secondary border-0 py-2 small" role="alert">
                Insurance is optional. Choose <strong>Confirm Purchase</strong> or <strong>Decline Insurance</strong> to continue.
              </div>
            }
            @if (insuranceState.hasVerticalInsureDecision()) {
              <div class="mt-2 d-flex flex-column gap-2">
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
    
        <!-- RegSaver charge confirmation modal (Bootstrap-style) -->
        @if (showViChargeConfirm()) {
          <app-vi-charge-confirm-modal
            [quotedPlayers]="viQuotedPlayers()"
            [premiumTotal]="viPremiumTotal()"
            [email]="viCcEmail()"
            [viCcOnlyFlow]="isViCcOnlyFlow()"
            (cancelled)="cancelViConfirm()"
            (confirmed)="confirmViAndContinue()" />
          }
    
          <!-- No-payment-due info panel when no TSIC balance and no VI-only flow -->
          @if (showNoPaymentInfo()) {
            <div class="alert alert-success border-0 mb-3" role="status">
              <div class="d-flex align-items-center gap-2">
                <span class="badge bg-success">✓ Paid in Full</span>
                <div>No payment due at this time. You can proceed to confirmation.</div>
              </div>
            </div>
          }
          @if (state.regSaverDetails()) {
            <div class="alert alert-info border-0 mb-3" role="status">
              <div class="d-flex align-items-center gap-2">
                <span class="badge bg-info-subtle text-info-emphasis border">RegSaver</span>
                <div>
                  <div class="fw-semibold">RegSaver policy on file</div>
                  <div class="small text-muted">Policy #: {{ state.regSaverDetails()!.policyNumber }} • Created: {{ state.regSaverDetails()!.policyCreateDate | date:'mediumDate' }}</div>
                </div>
              </div>
            </div>
          }
    
          @if (showCcSection()) {
            <section #ccSection class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="cc-title" style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
              <h6 id="cc-title" class="fw-semibold mb-2">Credit Card Information</h6>
              @if (isViCcOnlyFlow()) {
                <div class="alert alert-warning border-0" role="status">
                  <span class="badge bg-warning-subtle text-warning-emphasis border me-1">Insurance Premium</span>
                  A registration balance is not due, but an insurance premium is. Enter card details and click <strong>Proceed with Insurance Processing</strong>.
                </div>
              }
              <app-credit-card-form
                (validChange)="onCcValidChange($event)"
                (valueChange)="onCcValueChange($event)"
                [viOnly]="isViCcOnlyFlow()"
                [defaultFirstName]="state.familyUser()?.firstName || state.familyUser()?.ccInfo?.firstName || null"
                [defaultLastName]="state.familyUser()?.lastName || state.familyUser()?.ccInfo?.lastName || null"
                [defaultAddress]="state.familyUser()?.address || state.familyUser()?.ccInfo?.streetAddress || null"
                [defaultZip]="state.familyUser()?.zipCode || state.familyUser()?.zip || state.familyUser()?.ccInfo?.zip || null"
                [defaultEmail]="state.familyUser()?.ccInfo?.email || state.familyUser()?.email || (state.familyUser()?.userName?.includes('@') ? (state.familyUser()?.userName || null) : null)"
                [defaultPhone]="state.familyUser()?.ccInfo?.phone || state.familyUser()?.phone || null"
              ></app-credit-card-form>
            </section>
          }
          <!-- Zero-balance Continue button: always shown when there is no TSIC balance and we are NOT in an insurance-only (VI CC) flow.
          If insurance decision missing, clicking will open the insurance modal; if declined, it advances; if confirmed & still zero (edge), falls back to submit logic. -->
          @if (arbHideAllOptions() && !isViCcOnlyFlow()) {
            <!-- Continue button removed; logic relocated to global action bar -->
          }
          @if (isViCcOnlyFlow()) {
            <button type="button" class="btn btn-primary me-2" (click)="submitInsuranceOnly()" [disabled]="!canInsuranceOnlySubmit()">
              Proceed with Insurance Processing
            </button>
          }
          @if (showPayNowButton()) {
            <button type="button" class="btn btn-primary" (click)="submit()" [disabled]="!canSubmit()">
              Pay {{ currentTotal() | currency }} Now
            </button>
          }
        </div>
      </div>
    `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PaymentComponent implements AfterViewInit, OnDestroy {
  @ViewChild('viOffer') viOffer?: ElementRef<HTMLDivElement>;
  @ViewChild('ccSection') ccSection?: ElementRef<HTMLElement>;
  @Output() back = new EventEmitter<void>();
  @Output() submitted = new EventEmitter<void>();

  readonly teamService = inject(TeamService);

  private readonly _creditCard = signal<CreditCardFormValue>({
    type: '',
    number: '',
    expiry: '',
    code: '',
    firstName: '',
    lastName: '',
    address: '',
    zip: '',
    email: '',
    phone: ''
  });
  readonly ccValid = signal(false);

  public readonly state = inject(RegistrationWizardService);
  private readonly toast = inject(ToastService);

  constructor() { }

  // VerticalInsure legacy instance removed; handled by InsuranceService
  // quotes & user response now supplied by insuranceSvc signals
  // Flag retained for potential future auto-selection logic; currently unused.
  private readonly userChangedOption = false;
  readonly submitting = signal(false);
  readonly lastError = signal<{ message: string | null; errorCode: string | null } | null>(null);
  private lastIdemKey: string | null = null;
  private readonly idemSvc = inject(IdempotencyService);
  verticalInsureError: string | null = null;
  private viInitRetries = 0;
  private hydrateTimeout?: ReturnType<typeof setTimeout>;
  private viInitTimeout?: ReturnType<typeof setTimeout>;
  // Discount handled by PaymentService now; local UI state removed
  // VI confirmation modal state
  readonly showViChargeConfirm = signal(false);
  private pendingSubmitAfterViConfirm = false;
  private loadStoredIdem(): void {
    this.lastIdemKey = this.idemSvc.load(this.state.jobId(), this.state.familyUser()?.familyUserId) || null;
  }
  private persistIdem(key: string): void {
    this.idemSvc.persist(this.state.jobId(), this.state.familyUser()?.familyUserId, key);
  }
  private clearStoredIdem(): void {
    this.idemSvc.clear(this.state.jobId(), this.state.familyUser()?.familyUserId);
  }
  // Legacy checkbox removed; consent now tracked via wizard service signals.
  viConfirmChecked = false; // retained for backward compatibility (unused gating replaced)

  // Effect to auto-select a valid payment option based on current scenario.
  // Moved from ngAfterViewInit to a field initializer to ensure a proper injection context (fixes NG0203).
  private readonly paySvc = inject(PaymentService);
  private readonly destroyRef = inject(DestroyRef);
  // Added InsuranceService delegation property
  readonly insuranceSvc = inject(InsuranceService);
  readonly insuranceState = inject(InsuranceStateService);
  readonly paymentState = inject(PaymentStateService);


  ngAfterViewInit(): void {
    // Attempt to hydrate any existing idempotency key (previous attempt that failed or was retried)
    this.loadStoredIdem();
    // Simpler: one-shot hydrate + short delayed retry (in case data arrives slightly later)
    this.simpleHydrateFromCc(this.state.familyUser()?.ccInfo);
    this.hydrateTimeout = setTimeout(() => this.simpleHydrateFromCc(this.state.familyUser()?.ccInfo), 300);
    // Hydrate email/phone from familyUser if blank
    const fu = this.state.familyUser();
    if (fu) {
      const cc = this._creditCard();
      const patch: Partial<CreditCardFormValue> = {};
      if (!cc.email && fu.email?.includes('@')) patch.email = fu.email;
      if (!cc.email && !patch.email && fu.userName?.includes('@')) patch.email = fu.userName;
      if (!cc.phone && fu.phone) patch.phone = fu.phone;
      if (Object.keys(patch).length) this._creditCard.update(c => ({ ...c, ...patch }));
    }
    // Initialize VerticalInsure (simple retry if offer data not yet present)
    this.viInitTimeout = setTimeout(() => this.tryInitVerticalInsure(), 0);
  }

  ngOnDestroy(): void {
    clearTimeout(this.hydrateTimeout);
    clearTimeout(this.viInitTimeout);
  }

  // Payment option selection now delegated to PaymentOptionSelectorComponent

  private tryInitVerticalInsure(): void {
    if (!this.insuranceState.offerPlayerRegSaver()) return;
    const offerObj = this.insuranceState.verticalInsureOffer().data;
    if (!offerObj) {
      if (this.viInitRetries++ < 20) { // Max ~3s (20 × 150ms)
        this.viInitTimeout = setTimeout(() => this.tryInitVerticalInsure(), 150);
      } else {
        console.warn('[Payment] VerticalInsure offer data not available after 20 retries');
        this.verticalInsureError = 'Insurance widget could not be loaded. You may still proceed without insurance.';
      }
      return;
    }
    this.viInitRetries = 0;
    this.insuranceSvc.initWidget('#dVIOffer', offerObj as VIOfferData);
  }


  // --- Delegated getters wrapping PaymentService signals ---
  lineItems(): LineItem[] { return this.paySvc.lineItems(); }
  readonly currentTotal = computed(() => this.paySvc.currentTotal());
  readonly arbHideAllOptions = computed(() => {
    const regs = this.relevantRegs();
    if (!regs.length) return false;
    return regs.every(r => !!r.adnSubscriptionId && (r.adnSubscriptionStatus || '').toLowerCase() === 'active');
  });
  readonly tsicChargeDueNow = computed(() => {
    // Immediate TSIC payment is due only when balance > 0 and not fully covered by active ARB subscriptions.
    if (this.arbHideAllOptions()) return false;
    return this.currentTotal() > 0;
  });
  readonly isViCcOnlyFlow = computed(() => {
    // Insurance premium-only flow: no immediate TSIC charge due (either zero balance OR all ARB active), insurance confirmed, and quotes (premium) present.
    return !this.tsicChargeDueNow() && this.insuranceState.offerPlayerRegSaver() && this.insuranceState.verticalInsureConfirmed() && this.insuranceSvc.quotes().length > 0;
  });
  readonly showPayNowButton = computed(() => {
    // Pay Now should only be visible if a TSIC charge is actually due.
    return this.tsicChargeDueNow();
  });
  readonly showCcSection = computed(() => {
    // Show CC when TSIC charge due OR insurance quotes (premium) present (even before confirmation) to allow early form completion.
    return this.tsicChargeDueNow() || (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0);
  });
  readonly showNoPaymentInfo = computed(() => !this.tsicChargeDueNow() && !this.isViCcOnlyFlow());
  readonly canSubmit = computed(() => {
    // Hide submit when all ARB subs active (nothing to do)
    if (this.arbHideAllOptions()) return false;
    const tsicCharge = this.tsicChargeDueNow();
    const ccNeeded = this.showCcSection();
    const ccOk = !ccNeeded || this.ccValid();
    // For TSIC payment only (insurance-only handled by separate button)
    return tsicCharge && ccOk && !this.submitting();
  });

  readonly canInsuranceOnlySubmit = computed(() => {
    if (!this.isViCcOnlyFlow()) return false;
    const ccNeeded = this.showCcSection();
    const ccOk = !ccNeeded || this.ccValid();
    return ccOk && !this.submitting();
  });

  // Progression handler for ARB-covered or no-payment scenarios.
  continueArbOrZero(): void {
    // Only valid when all registration payments are handled by ARB (no TSIC payment action) and not VI-only flow.
    if (!this.arbHideAllOptions()) return;
    if (this.isViCcOnlyFlow()) { this.submit(); return; }
    if (this.insuranceState.offerPlayerRegSaver()) {
      if (!this.insuranceState.hasVerticalInsureDecision()) { this.openViModal(); return; }
      if (!this.insuranceState.verticalInsureConfirmed()) { this.submitted.emit(); return; }
      // Confirmed with quotes -> defer to submit flow (will show charge confirm modal if needed)
      this.submit();
      return;
    }
    this.submitted.emit();
  }

  submit(): void {
    if (this.submitting()) return;
    this.submitting.set(true); // Set immediately to prevent double-click race condition
    // Gate: require insurance decision ONLY if offered AND user has not confirmed nor declined AND no existing stored policy AND payment is actually due.
    const needInsuranceDecision = this.insuranceState.offerPlayerRegSaver()
      && !this.insuranceState.verticalInsureConfirmed()
      && !this.insuranceState.verticalInsureDeclined()
      && !this.state.regSaverDetails()
      && this.tsicChargeDueNow();
    if (needInsuranceDecision && this.isViOfferVisible()) {
      this.submitting.set(false);
      this.toast.show('Insurance is optional. Please Confirm Purchase or Decline to continue.', 'danger', 4000);
      return;
    }
    // Frontend CC validation hard stop (defensive against accidental blank submits)
    if (this.showCcSection() && !this.ccValid()) {
      this.submitting.set(false);
      this.toast.show('Credit card form is invalid.', 'danger', 3000);
      return;
    }
    // If VI quotes exist (insurance selected), show charge confirmation modal before proceeding (TSIC+VI or VI-only).
    if (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0) {
      this.submitting.set(false); // Re-enable after modal interaction
      this.pendingSubmitAfterViConfirm = true;
      this.showViChargeConfirm.set(true);
      return;
    }
    this.continueSubmit();
  }

  // Insurance-only submission path (no TSIC payment). Performs CC validation and processes VI purchase directly.
  submitInsuranceOnly(): void {
    if (!this.canInsuranceOnlySubmit()) return;
    if (this.submitting()) return;
    // Gate: insurance decision must be confirmed (isViCcOnlyFlow implies confirmed + offer)
    if (!this.insuranceState.verticalInsureConfirmed()) return;
    // CC validation defensive check
    if (this.showCcSection() && !this.ccValid()) {
      this.toast.show('Credit card form is invalid.', 'danger', 3000);
      return;
    }
    // If quotes require confirmation, show modal (reusing existing confirmation flow)
    if (this.insuranceSvc.quotes().length > 0) {
      this.pendingSubmitAfterViConfirm = true;
      this.showViChargeConfirm.set(true);
      return;
    }
    // No quotes/premium -> finalize immediately
    this.processInsuranceOnlyFinish('Insurance request submitted.');
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
          message: doneMsg || msg
        });
      } catch { }
      this.submitting.set(false);
      this.submitted.emit();
    });
  }

  private continueSubmit(): void {
    this.submitting.set(true); // Ensure set (may already be true from submit())
    // Reuse existing idempotency key if present; generate and persist if absent
    if (!this.lastIdemKey) {
      const newKey = crypto?.randomUUID ? crypto.randomUUID() : (Date.now().toString(36) + Math.random().toString(36).slice(2));
      this.lastIdemKey = newKey;
      this.persistIdem(newKey);
    }
    const rs = this.insuranceState.regSaverDetails();
    // Map client PaymentOption string ('PIF' | 'Deposit' | 'ARB') to backend enum numeric (0=PIF,1=Deposit,2=ARB)
    const mapPaymentOption = (opt: string): number => {
      switch (opt) {
        case 'Deposit': return 1;
        case 'ARB': return 2;
        case 'PIF':
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
      email: (cc.email || this.state.familyUser()?.userName || '').trim() || null,
      phone: sanitizePhone(cc.phone)
    } : null;
    const request: PaymentRequestDto = {
      jobPath: this.state.jobPath(),
      paymentOption: mapPaymentOption(this.paymentState.paymentOption()),
      creditCard: creditCardPayload,
      idempotencyKey: this.lastIdemKey,
      viConfirmed: this.insuranceState.offerPlayerRegSaver() ? this.insuranceState.verticalInsureConfirmed() : undefined,
      viPolicyNumber: (this.insuranceState.verticalInsureConfirmed() ? (rs?.policyNumber || this.insuranceState.viConsent()?.policyNumber) : undefined) || undefined,
      viPolicyCreateDate: (this.insuranceState.verticalInsureConfirmed() ? (rs?.policyCreateDate || this.insuranceState.viConsent()?.policyCreateDate) : undefined) || undefined
    };
    this.paySvc.submitPayment(request).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => this.handlePaymentResponse(response, rs),
      error: (error: HttpErrorResponse) => this.handlePaymentHttpError(error)
    });
  }

  // --- Extracted handlers to reduce cognitive complexity ---
  private handlePaymentResponse(response: PaymentResponseDto, rs: RegSaverDetailsDto | null): void {
    if (response.success) {
      this.handlePaymentSuccess(response, rs);
    } else {
      this.handlePaymentFailure(response);
    }
  }

  private handlePaymentSuccess(response: PaymentResponseDto, rs: RegSaverDetailsDto | null): void {
    this.lastError.set(null);
    console.log('Payment successful', response);
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
        message: response.message ?? null
      });
    } catch { /* ignore */ }
    this.submitted.emit();
    this.submitting.set(false);
    if (this.paymentState.paymentOption() === 'ARB') {
      this.patchArbSubscriptions(response);
    }
    if (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0) {
      this.insuranceSvc.purchaseInsurance(this._creditCard());
    }
  }

  private handlePaymentFailure(response: PaymentResponseDto): void {
    console.error('Payment failed', response.message);
    this.submitting.set(false);
    this.lastError.set({ message: response.message || null, errorCode: response.errorCode || null });
    const msg = `[${response.errorCode || 'ERROR'}] ${response.message || 'Payment failed.'}`;
    this.toast.show(msg, 'danger', 6000);
  }

  private handlePaymentHttpError(error: HttpErrorResponse): void {
    console.error('Payment error', error?.error?.message || error.message || error);
    this.submitting.set(false);
    const apiMsg = (error.error && typeof error.error === 'object') ? (error.error.message || JSON.stringify(error.error)) : (error.message || 'Network error');
    const apiCode = (error.error && typeof error.error === 'object') ? (error.error.errorCode || null) : null;
    this.lastError.set({ message: apiMsg, errorCode: apiCode });
    this.toast.show(`[${apiCode || 'NETWORK'}] ${apiMsg}`, 'danger', 6000);
  }

  private patchArbSubscriptions(response: PaymentResponseDto): void {
    if (!response.subscriptionIds && !response.subscriptionId) return;
    try {
      const famPlayers = this.state.familyPlayers();
      const map = response.subscriptionIds;
      const updated = famPlayers.map(fp => {
        if (!fp.priorRegistrations?.length) return fp;
        const prior = fp.priorRegistrations.map(r => {
          const mappedId = map?.[r.registrationId];
          if (mappedId) {
            return { ...r, adnSubscriptionId: mappedId, adnSubscriptionStatus: 'active' };
          }
          if (!map && response.subscriptionId && !r.adnSubscriptionId) {
            return { ...r, adnSubscriptionId: response.subscriptionId, adnSubscriptionStatus: 'active' };
          }
          return r;
        });
        return { ...fp, priorRegistrations: prior };
      });
      this.state.updateFamilyPlayers(updated);
    } catch { /* ignore */ }
  }

  // Launch insurance modal
  openViModal(): void {
    this.insuranceState.openVerticalInsureModal();
  }


  // Discount application now performed via PaymentOptionSelectorComponent + PaymentService.
  // Deposit helper provided directly by PaymentService; wrapper retained if template needs it later.
  getDepositForPlayer(playerId: string): number { return this.paySvc.getDepositForPlayer(playerId); }

  // --- VI Confirmation helpers (computed signals for template efficiency) ---
  readonly viQuotedPlayers = computed(() => this.insuranceSvc.quotedPlayers());
  readonly viPremiumTotal = computed(() => this.insuranceSvc.premiumTotal());
  readonly viCcEmail = computed(() => this.state.familyUser()?.userName || '');
  cancelViConfirm(): void {
    this.showViChargeConfirm.set(false);
    this.pendingSubmitAfterViConfirm = false;
  }
  confirmViAndContinue(): void {
    this.showViChargeConfirm.set(false);
    if (this.pendingSubmitAfterViConfirm) {
      this.pendingSubmitAfterViConfirm = false;
      // Reuse the same modal: if VI-only flow (no TSIC balance), purchase insurance directly.
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
              message: msg
            });
          } catch { }
          this.submitted.emit();
        });
      } else {
        this.continueSubmit();
      }
    }
  }

  // Removed client-side heuristic CC prefill; data now supplied by server via ccInfo.
  private simpleHydrateFromCc(cc?: { firstName?: string; lastName?: string; streetAddress?: string; zip?: string; email?: string; phone?: string; }): void {
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

  // (Legacy insurance purchase methods removed; handled by InsuranceService.)

  private isViOfferVisible(): boolean {
    try {
      const el = document.getElementById('dVIOffer');
      if (!el) return false;
      const style = getComputedStyle(el);
      const hasSize = (el.offsetWidth + el.offsetHeight) > 0;
      return style.display !== 'none' && style.visibility !== 'hidden' && hasSize;
    } catch { return false; }
  }
  // Receive credit card form changes from child component
  onCcValueChange(val: Partial<CreditCardFormValue>): void {
    this._creditCard.update(c => ({ ...c, ...val }));
  }
  // Removed original simple onCcValidChange; replaced with version that also prompts insurance decision.
  onCcValidChange(valid: boolean): void { this.ccValid.set(!!valid); }
  // Autofocus logic removed per request
  monthLabel(): string { return this.paySvc.monthLabel(); }
  // --- ARB subscription helpers (computed signals for template efficiency) ---
  private readonly relevantRegs = computed(() => {
    const playerIds = new Set(this.state.familyPlayers().filter(p => p.selected || p.registered).map(p => p.playerId));
    return this.state.familyPlayers().filter(p => playerIds.has(p.playerId)).flatMap(p => p.priorRegistrations || []);
  });
  readonly arbProblemAny = computed(() => {
    const regs = this.relevantRegs();
    return regs.some(r => !!r.adnSubscriptionId && (r.adnSubscriptionStatus || '').toLowerCase() !== 'active');
  });
}
