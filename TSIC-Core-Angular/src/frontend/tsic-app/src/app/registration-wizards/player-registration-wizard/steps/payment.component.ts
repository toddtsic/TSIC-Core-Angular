import { Component, EventEmitter, Output, inject, AfterViewInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PaymentSummaryComponent } from './payment-summary.component';
import { PaymentOptionSelectorComponent } from './payment-option-selector.component';
import { CreditCardFormComponent } from './credit-card-form.component';
import { PaymentService } from '../services/payment.service';
import { IdempotencyService } from '../services/idempotency.service';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { RegistrationWizardService } from '../registration-wizard.service';
import { InsuranceStateService } from '../services/insurance-state.service';
import { PaymentStateService } from '../services/payment-state.service';
import { ViChargeConfirmModalComponent } from '../verticalinsure/vi-charge-confirm-modal.component';
import type { PaymentResponseDto } from '../../../core/api/models/PaymentResponseDto';
import { environment } from '../../../../environments/environment';
import { TeamService } from '../team.service';
import { ToastService } from '../../../shared/toast.service';
import { InsuranceService } from '../services/insurance.service';

declare global {
  // Allow TypeScript to acknowledge the VerticalInsure constructor on window
  interface Window { VerticalInsure?: any; }
}

import type { LineItem } from '../services/payment.service';

// Local aliases to make types obvious without duplicating backend DTOs
// Types now come from shared core models (see import above).

@Component({
  selector: 'app-rw-payment',
  standalone: true,
  imports: [CommonModule, FormsModule, ViChargeConfirmModalComponent, PaymentSummaryComponent, PaymentOptionSelectorComponent, CreditCardFormComponent],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">{{ insuranceState.offerPlayerRegSaver() ? 'Payment/Insurance' : 'Payment' }}</h5>
      </div>
      <div class="card-body">
        @if (lastError) {
          <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
            <div class="flex-grow-1">
              <div class="fw-semibold mb-1">Payment Error</div>
              <div class="small">{{ lastError.message || 'An error occurred.' }}</div>
            </div>
            @if (lastError.errorCode) { <span class="badge bg-danger-subtle text-dark border">{{ lastError.errorCode }}</span> }
          </div>
        }

        <app-payment-summary></app-payment-summary>
        <!-- ARB subscription state messaging / option gating -->
        @if (arbHideAllOptions()) {
          <div class="alert alert-success border-0" role="status">
            All selected registrations have an active Automated Recurring Billing subscription. No payment action is required.
          </div>
        } @else if (arbProblemAny()) {
          <div class="alert alert-danger border-0" role="alert">
            There is a problem with your Automated Recurring Billing. Please contact your club immediately.
          </div>
          <app-payment-option-selector></app-payment-option-selector>
        } @else {
          <app-payment-option-selector></app-payment-option-selector>
        }
        <!-- RegSaver / VerticalInsure region with deferred offer only -->
        <div class="mb-3">
          @if (state.regSaverDetails()) {
            <div class="alert alert-info border-0" role="status">
              <div class="d-flex align-items-center gap-2">
                <span class="badge bg-info-subtle text-dark border">RegSaver</span>
                <div>
                  <div class="fw-semibold">RegSaver policy on file</div>
                  <div class="small text-muted">Policy #: {{ state.regSaverDetails()!.policyNumber }} • Created: {{ state.regSaverDetails()!.policyCreateDate | date:'mediumDate' }}</div>
                </div>
              </div>
            </div>
          }
          <div #viOffer id="dVIOffer" class="pb-3 text-center"></div>
          @if (insuranceState.offerPlayerRegSaver() && !insuranceState.hasVerticalInsureDecision()) {
            <div class="alert alert-secondary border-0 py-2 small" role="alert">
              Please review the insurance details above and choose Confirm Purchase or Decline Insurance to continue.
            </div>
          }
          @if (insuranceState.offerPlayerRegSaver() && insuranceState.hasVerticalInsureDecision()) {
            <div class="mt-2 d-flex flex-column gap-2">
              <div class="alert" [ngClass]="insuranceState.verticalInsureConfirmed() ? 'alert-success' : 'alert-secondary'" role="status">
                <div class="d-flex align-items-center gap-2">
                  <span class="badge" [ngClass]="insuranceState.verticalInsureConfirmed() ? 'bg-success' : 'bg-secondary'">RegSaver</span>
                  <div>
                    @if (insuranceState.verticalInsureConfirmed()) {
                      <div class="fw-semibold mb-0">Insurance Selected</div>
                      <div class="small text-muted" *ngIf="insuranceState.viConsent()?.policyNumber">Policy #: {{ insuranceState.viConsent()?.policyNumber }}</div>
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

        <!-- RegSaver charge confirmation modal (Bootstrap-style) -->
        @if (showViChargeConfirm) {
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
          <div class="alert alert-info border-0" role="status">
            No payments are due at this time.
          </div>
        }

        @if (showCcSection()) {
          <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="cc-title" style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
            <h6 id="cc-title" class="fw-semibold mb-2">Credit Card Information</h6>
            @if (isViCcOnlyFlow()) {
              <div class="alert alert-secondary border-0" role="status">
                Your TSIC registration balance is $0. The credit card details below are for Vertical Insure only.
              </div>
            }
            <app-credit-card-form
              (validChange)="onCcValidChange($event)"
              (valueChange)="onCcValueChange($event)"
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
            } @else {
              <button type="button" class="btn btn-primary" (click)="submit()" [disabled]="!canSubmit()">
                Pay Now
              </button>
            }
      </div>
    </div>
  `
})
export class PaymentComponent implements AfterViewInit {
  @ViewChild('viOffer') viOffer?: ElementRef<HTMLDivElement>;
  @Output() back = new EventEmitter<void>();
  @Output() submitted = new EventEmitter<void>();

  readonly teamService = inject(TeamService);

  creditCard = {
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
  };
  ccValid = false;

  constructor(public state: RegistrationWizardService, private readonly http: HttpClient, private readonly toast: ToastService) { }

  // VerticalInsure legacy instance removed; handled by InsuranceService
  // quotes & user response now supplied by insuranceSvc signals
  // Flag retained for potential future auto-selection logic; currently unused.
  private readonly userChangedOption = false;
  submitting = false;
  lastError: { message: string | null; errorCode: string | null } | null = null;
  private lastIdemKey: string | null = null;
  private readonly idemSvc = inject(IdempotencyService);
  verticalInsureError: string | null = null;
  // Discount handled by PaymentService now; local UI state removed
  // VI confirmation modal state
  showViChargeConfirm = false;
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
  // Added InsuranceService delegation property
  readonly insuranceSvc = inject(InsuranceService);
  readonly insuranceState = inject(InsuranceStateService);
  readonly paymentState = inject(PaymentStateService);


  ngAfterViewInit(): void {
    // Attempt to hydrate any existing idempotency key (previous attempt that failed or was retried)
    this.loadStoredIdem();
    // Simpler: one-shot hydrate + short delayed retry (in case data arrives slightly later)
    this.simpleHydrateFromCc(this.state.familyUser()?.ccInfo);
    setTimeout(() => this.simpleHydrateFromCc(this.state.familyUser()?.ccInfo), 300);
    // Hydrate email/phone from familyUser if blank
    const fu = this.state.familyUser();
    if (fu) {
      if (!this.creditCard.email && fu.email?.includes('@')) this.creditCard.email = fu.email;
      if (!this.creditCard.email && fu.userName?.includes('@')) this.creditCard.email = fu.userName;
      if (!this.creditCard.phone && fu.phone) this.creditCard.phone = fu.phone;
    }
    // Initialize VerticalInsure (simple retry if offer data not yet present)
    setTimeout(() => this.tryInitVerticalInsure(), 0);
  }
  // Payment option selection now delegated to PaymentOptionSelectorComponent

  private tryInitVerticalInsure(): void {
    if (!this.insuranceState.offerPlayerRegSaver()) return;
    const offerObj = this.insuranceState.verticalInsureOffer().data;
    if (!offerObj) { setTimeout(() => this.tryInitVerticalInsure(), 150); return; }
    this.insuranceSvc.initWidget('#dVIOffer', offerObj);
  }


  // --- Delegated getters wrapping PaymentService signals ---
  lineItems(): LineItem[] { return this.paySvc.lineItems(); }
  currentTotal(): number { return this.paySvc.currentTotal(); }
  isViCcOnlyFlow(): boolean {
    // Insurance-only flow: no TSIC balance and insurance selected (confirmed)
    return this.currentTotal() === 0 && this.insuranceState.offerPlayerRegSaver() && this.insuranceState.verticalInsureConfirmed();
  }
  showCcSection(): boolean {
    // Hide CC when all registrations are covered by active ARB (no TSIC payment due) and not in VI-only flow.
    if (this.arbHideAllOptions() && !this.isViCcOnlyFlow()) return false;
    // Show CC when there is a TSIC balance or an insurance-only (VI) charge flow.
    return this.currentTotal() > 0 || this.isViCcOnlyFlow();
  }
  showNoPaymentInfo(): boolean { return this.currentTotal() === 0 && !this.isViCcOnlyFlow(); }
  canSubmit(): boolean {
    // Hide submit when all ARB subs active (nothing to do)
    if (this.arbHideAllOptions()) return false;
    const tsicCharge = this.lineItems().length > 0 && this.currentTotal() > 0;
    const ccNeeded = this.showCcSection();
    const ccOk = !ccNeeded || this.ccValid;
    // For TSIC payment only (insurance-only handled by separate button)
    return tsicCharge && ccOk && !this.submitting;
  }

  canInsuranceOnlySubmit(): boolean {
    if (!this.isViCcOnlyFlow()) return false;
    const ccNeeded = this.showCcSection();
    const ccOk = !ccNeeded || this.ccValid;
    return ccOk && !this.submitting;
  }

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
    if (this.submitting) return;
    // Gate: if RegSaver is offered but no user response yet, require a decision before continuing.
    if (this.insuranceState.offerPlayerRegSaver()) {
      const noResponse = !this.insuranceSvc.hasUserResponse() && this.isViOfferVisible();
      if (noResponse) {
        this.toast.show('Please indicate your interest in registration insurance for each player listed.', 'danger', 4000);
        return;
      }
    }
    // Frontend CC validation hard stop (defensive against accidental blank submits)
    if (this.showCcSection() && !this.ccValid) {
      this.toast.show('Credit card form is invalid.', 'danger', 3000);
      return;
    }
    // If VI quotes exist (insurance selected), show charge confirmation modal before proceeding (TSIC+VI or VI-only).
    if (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0) {
      this.pendingSubmitAfterViConfirm = true;
      this.showViChargeConfirm = true;
      return;
    }
    this.continueSubmit();
  }

  // Insurance-only submission path (no TSIC payment). Performs CC validation and processes VI purchase directly.
  submitInsuranceOnly(): void {
    if (!this.canInsuranceOnlySubmit()) return;
    if (this.submitting) return;
    // Gate: insurance decision must be confirmed (isViCcOnlyFlow implies confirmed + offer)
    if (!this.insuranceState.verticalInsureConfirmed()) return;
    // CC validation defensive check
    if (this.showCcSection() && !this.ccValid) {
      this.toast.show('Credit card form is invalid.', 'danger', 3000);
      return;
    }
    // If quotes require confirmation, show modal (reusing existing confirmation flow)
    if (this.insuranceSvc.quotes().length > 0) {
      this.pendingSubmitAfterViConfirm = true;
      this.showViChargeConfirm = true;
      return;
    }
    // No quotes/premium -> finalize immediately
    this.processInsuranceOnlyFinish('Insurance request submitted.');
  }

  private processInsuranceOnlyFinish(msg: string): void {
    this.submitting = true;
    this.insuranceSvc.purchaseInsuranceAndFinish(doneMsg => {
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
      this.submitting = false;
      this.submitted.emit();
    });
  }

  private continueSubmit(): void {
    if (this.submitting) return;
    this.submitting = true;
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
    // Sanitize expiry to MMYY (remove all non-digits and ensure length 4)
    const sanitizeExpiry = (raw: string): string => {
      const digits = String(raw || '').replaceAll(/\D+/g, '').slice(0, 4);
      if (digits.length === 3) { // if user somehow produced MYY -> pad
        return '0' + digits;
      }
      return digits;
    };
    const sanitizePhone = (raw: string): string => String(raw || '').replaceAll(/\D+/g, '').slice(0, 15);
    const creditCardPayload = this.showCcSection() ? {
      Number: this.creditCard.number?.trim() || null,
      Expiry: sanitizeExpiry(this.creditCard.expiry),
      Code: this.creditCard.code?.trim() || null,
      FirstName: this.creditCard.firstName?.trim() || null,
      LastName: this.creditCard.lastName?.trim() || null,
      Address: this.creditCard.address?.trim() || null,
      Zip: this.creditCard.zip?.trim() || null,
      Email: (this.creditCard.email || this.state.familyUser()?.userName || '').trim() || null,
      Phone: sanitizePhone(this.creditCard.phone)
    } : null;
    const request = {
      JobId: this.state.jobId(),
      FamilyUserId: this.state.familyUser()?.familyUserId,
      PaymentOption: mapPaymentOption(this.paymentState.paymentOption()),
      CreditCard: creditCardPayload,
      IdempotencyKey: this.lastIdemKey,
      ViConfirmed: this.insuranceState.offerPlayerRegSaver() ? this.insuranceState.verticalInsureConfirmed() : undefined,
      ViPolicyNumber: (this.insuranceState.verticalInsureConfirmed() ? (rs?.policyNumber || this.insuranceState.viConsent()?.policyNumber) : undefined) || undefined,
      ViPolicyCreateDate: (this.insuranceState.verticalInsureConfirmed() ? (rs?.policyCreateDate || this.insuranceState.viConsent()?.policyCreateDate) : undefined) || undefined
    };
    // POST using keys matching backend DTO property casing (case-insensitive but explicit for clarity)
    this.http.post<PaymentResponseDto>(`${environment.apiUrl}/registration/submit-payment`, request).subscribe({
      next: (response) => this.handlePaymentResponse(response, rs),
      error: (error: HttpErrorResponse) => this.handlePaymentHttpError(error)
    });
  }

  // --- Extracted handlers to reduce cognitive complexity ---
  private handlePaymentResponse(response: PaymentResponseDto, rs: any): void {
    if (response.success) {
      this.handlePaymentSuccess(response, rs);
    } else {
      this.handlePaymentFailure(response);
    }
  }

  private handlePaymentSuccess(response: PaymentResponseDto, rs: any): void {
    this.lastError = null;
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
    this.submitting = false;
    if (this.paymentState.paymentOption() === 'ARB') {
      this.patchArbSubscriptions(response);
    }
    if (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0) {
      this.insuranceSvc.purchaseInsurance();
    }
  }

  private handlePaymentFailure(response: PaymentResponseDto): void {
    console.error('Payment failed', response.message);
    this.submitting = false;
    this.lastError = { message: response.message || null, errorCode: response.errorCode || null };
    const msg = `[${response.errorCode || 'ERROR'}] ${response.message || 'Payment failed.'}`;
    this.toast.show(msg, 'danger', 6000);
  }

  private handlePaymentHttpError(error: HttpErrorResponse): void {
    console.error('Payment error', error?.error?.message || error.message || error);
    this.submitting = false;
    const apiMsg = (error.error && typeof error.error === 'object') ? (error.error.message || JSON.stringify(error.error)) : (error.message || 'Network error');
    const apiCode = (error.error && typeof error.error === 'object') ? (error.error.errorCode || null) : null;
    this.lastError = { message: apiMsg, errorCode: apiCode };
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
      this.state.familyPlayers.set(updated);
    } catch { /* ignore */ }
  }

  // Launch insurance modal
  openViModal(): void {
    this.insuranceState.openVerticalInsureModal();
  }


  // Discount application now performed via PaymentOptionSelectorComponent + PaymentService.
  // Deposit helper provided directly by PaymentService; wrapper retained if template needs it later.
  getDepositForPlayer(playerId: string): number { return this.paySvc.getDepositForPlayer(playerId); }

  // --- VI Confirmation helpers ---
  viQuotedPlayers(): string[] { return this.insuranceSvc.quotedPlayers(); }
  viPremiumTotal(): number { return this.insuranceSvc.premiumTotal(); }
  viCcEmail(): string {
    // Prefer family user username if that is an email
    const u = this.state.familyUser()?.userName || '';
    return u || '';
  }
  cancelViConfirm(): void {
    this.showViChargeConfirm = false;
    this.pendingSubmitAfterViConfirm = false;
  }
  confirmViAndContinue(): void {
    this.showViChargeConfirm = false;
    if (this.pendingSubmitAfterViConfirm) {
      this.pendingSubmitAfterViConfirm = false;
      // Reuse the same modal: if VI-only flow (no TSIC balance), purchase insurance directly.
      if (this.isViCcOnlyFlow()) {
        this.insuranceSvc.purchaseInsuranceAndFinish(msg => {
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
    if (!this.creditCard.firstName && cc.firstName) this.creditCard.firstName = cc.firstName.trim();
    if (!this.creditCard.lastName && cc.lastName) this.creditCard.lastName = cc.lastName.trim();
    if (!this.creditCard.address && cc.streetAddress) this.creditCard.address = cc.streetAddress.trim();
    if (!this.creditCard.zip && cc.zip) this.creditCard.zip = cc.zip.trim();
    if (!this.creditCard.email && cc.email?.includes('@')) this.creditCard.email = cc.email.trim();
    if (!this.creditCard.phone && cc.phone) this.creditCard.phone = cc.phone.replaceAll(/\D+/g, '');
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
  onCcValueChange(val: { type?: string; number?: string; expiry?: string; code?: string; firstName?: string; lastName?: string; address?: string; zip?: string; email?: string; phone?: string; }): void {
    this.creditCard = { ...this.creditCard, ...val };
  }
  // Removed original simple onCcValidChange; replaced with version that also prompts insurance decision.
  onCcValidChange(valid: any): void { this.ccValid = !!valid; }
  monthLabel(): string { return this.paySvc.monthLabel(); }
  // --- ARB subscription helpers ---
  private priorRegs() {
    return this.state.familyPlayers().flatMap(p => p.priorRegistrations || []);
  }
  private relevantRegs() {
    // Consider registrations tied to selected or already registered players
    const playerIds = new Set(this.state.familyPlayers().filter(p => p.selected || p.registered).map(p => p.playerId));
    return this.state.familyPlayers().filter(p => playerIds.has(p.playerId)).flatMap(p => p.priorRegistrations || []);
  }
  // Hide payment options when ALL relevant registrations have an active subscription (future billing handled automatically).
  arbHideAllOptions(): boolean {
    const regs = this.relevantRegs();
    if (!regs.length) return false;
    return regs.every(r => !!r.adnSubscriptionId && (r.adnSubscriptionStatus || '').toLowerCase() === 'active');
  }
  arbProblemAny(): boolean {
    const regs = this.relevantRegs();
    // Any registration with a subscription id but non-active status
    return regs.some(r => !!r.adnSubscriptionId && (r.adnSubscriptionStatus || '').toLowerCase() !== 'active');
  }
}
