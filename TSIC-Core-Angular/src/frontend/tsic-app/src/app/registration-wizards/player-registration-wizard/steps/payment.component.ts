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
import { ViConfirmModalComponent } from '../verticalinsure/vi-confirm-modal.component';
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
  imports: [CommonModule, FormsModule, ViConfirmModalComponent, ViChargeConfirmModalComponent, PaymentSummaryComponent, PaymentOptionSelectorComponent, CreditCardFormComponent],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Payment</h5>
      </div>
      <div class="card-body">
        <!-- RegSaver / VerticalInsure region with deferred offer only -->
        @if (insuranceState.showVerticalInsureModal()) {
          <app-vi-confirm-modal
            [quotes]="insuranceSvc.quotes()"
            [ready]="insuranceSvc.hasUserResponse()"
            [error]="insuranceSvc.error()"
            (confirmed)="onViConfirmed($event)"
            (declined)="onViDeclined()"
            (closed)="onViClosed()" />
        }
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

        <app-payment-summary></app-payment-summary>
        <app-payment-option-selector></app-payment-option-selector>
        
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
            <app-credit-card-form (validChange)="onCcValidChange($event)" (valueChange)="onCcValueChange($event)"></app-credit-card-form>
          </section>
        }
          <button type="button" class="btn btn-primary" (click)="submit()" [disabled]="!canSubmit()">{{ isViCcOnlyFlow() ? 'Send to Vertical Insure' : 'Pay Now' }}</button>
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
    zip: ''
  };
  ccValid = false;

  constructor(public state: RegistrationWizardService, private readonly http: HttpClient, private readonly toast: ToastService) { }

  // VerticalInsure legacy instance removed; handled by InsuranceService
  // quotes & user response now supplied by insuranceSvc signals
  // Flag retained for potential future auto-selection logic; currently unused.
  private readonly userChangedOption = false;
  submitting = false;
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
    return this.currentTotal() === 0 && this.insuranceState.offerPlayerRegSaver() && !this.insuranceState.verticalInsureConfirmed();
  }
  showCcSection(): boolean { return this.currentTotal() > 0 || this.isViCcOnlyFlow(); }
  showNoPaymentInfo(): boolean { return this.currentTotal() === 0 && !this.isViCcOnlyFlow(); }
  canSubmit(): boolean {
    const tsicCharge = this.lineItems().length > 0 && this.currentTotal() > 0;
    const viOnly = this.isViCcOnlyFlow();
    const ccNeeded = this.showCcSection();
    const ccOk = !ccNeeded || this.ccValid;
    return (tsicCharge || viOnly) && ccOk && !this.submitting;
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
    const request = {
      jobId: this.state.jobId(),
      familyUserId: this.state.familyUser()?.familyUserId,
      paymentOption: this.paymentState.paymentOption(),
      creditCard: this.creditCard,
      idempotencyKey: this.lastIdemKey,
      viConfirmed: this.insuranceState.offerPlayerRegSaver() ? this.insuranceState.verticalInsureConfirmed() : undefined,
      viDeclined: this.insuranceState.offerPlayerRegSaver() ? this.insuranceState.verticalInsureDeclined() : undefined,
      viPolicyNumber: (this.insuranceState.verticalInsureConfirmed() ? (rs?.policyNumber || this.insuranceState.viConsent()?.policyNumber) : undefined) || undefined,
      viPolicyCreateDate: (this.insuranceState.verticalInsureConfirmed() ? (rs?.policyCreateDate || this.insuranceState.viConsent()?.policyCreateDate) : undefined) || undefined
    };

    this.http.post<PaymentResponseDto>(`${environment.apiUrl}/registration/submit-payment`, request).subscribe({
      next: (response) => {
        if (response.success) {
          // Handle success, perhaps navigate to next step
          console.log('Payment successful', response);
          // Clear stored idempotency key on success; future payments should generate a new one
          this.clearStoredIdem();
          this.lastIdemKey = null;
          // Persist summary for Confirmation step
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
          // After successful TSIC charge, if insurance was offered and quotes exist, purchase RegSaver policies.
          if (this.insuranceState.offerPlayerRegSaver() && this.insuranceSvc.quotes().length > 0) {
            this.insuranceSvc.purchaseInsurance();
          }
        } else {
          // Handle error
          console.error('Payment failed', response.message);
          // Keep idempotency key so user can retry safely
          this.submitting = false;
        }
      },
      error: (error: HttpErrorResponse) => {
        console.error('Payment error', error?.error?.message || error.message || error);
        // Preserve idempotency key for retry
        this.submitting = false;
      }
    });
  }

  // Launch insurance modal
  openViModal(): void {
    this.insuranceState.openVerticalInsureModal();
  }

  onViConfirmed(evt: { policyNumber: string | null; policyCreateDate: string | null; quotes: any[] }): void {
    this.insuranceState.confirmVerticalInsurePurchase(evt.policyNumber, evt.policyCreateDate, evt.quotes);
  }
  onViDeclined(): void { this.insuranceState.declineVerticalInsurePurchase(); }
  onViClosed(): void { this.insuranceState.closeVerticalInsureModal(); }

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
  private simpleHydrateFromCc(cc?: { firstName?: string; lastName?: string; streetAddress?: string; zip?: string; }): void {
    if (!cc) return;
    if (!this.creditCard.firstName && cc.firstName) this.creditCard.firstName = cc.firstName.trim();
    if (!this.creditCard.lastName && cc.lastName) this.creditCard.lastName = cc.lastName.trim();
    if (!this.creditCard.address && cc.streetAddress) this.creditCard.address = cc.streetAddress.trim();
    if (!this.creditCard.zip && cc.zip) this.creditCard.zip = cc.zip.trim();
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
  onCcValueChange(val: { type?: string; number?: string; expiry?: string; code?: string; firstName?: string; lastName?: string; address?: string; zip?: string; }): void {
    this.creditCard = { ...this.creditCard, ...val };
  }
  onCcValidChange(valid: any): void { this.ccValid = !!valid; }
  monthLabel(): string { return this.paySvc.monthLabel(); }
}
