import { Component, inject, signal, computed, AfterViewInit, ViewChild, ElementRef, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { TeamPaymentSummaryTableComponent } from './team-payment-summary-table.component';
import { CreditCardFormComponent } from '../../player-registration-wizard/steps/credit-card-form.component';
import { TeamPaymentService } from '../services/team-payment.service';
import { TeamPaymentStateService } from '../services/team-payment-state.service';
import { TeamInsuranceService } from '../services/team-insurance.service';
import { TeamInsuranceStateService } from '../services/team-insurance-state.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { JobService } from '@infrastructure/services/job.service';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { environment } from '@environments/environment';
import type { CreditCardInfo } from '@core/api';

// TODO: Generate these types when backend controller is complete
interface TeamPaymentRequestDto {
    jobPath: string;
    clubRepRegId: string;
    teamIds: string[];
    totalAmount: number;
    creditCard: CreditCardInfo;
}

interface TeamPaymentResponseDto {
    success: boolean;
    transactionId?: string;
    error?: string;
    message?: string;
}

declare global {
    interface Window { VerticalInsure?: any; }
}

/**
 * Team payment step component.
 * Displays payment summary table, optional VI insurance widget, and credit card form.
 * Teams only support Pay-In-Full (no ARB/deposits).
 */
@Component({
    selector: 'app-team-payment-step',
    standalone: true,
    imports: [
        CommonModule,
        FormsModule,
        TeamPaymentSummaryTableComponent,
        CreditCardFormComponent
    ],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">
          {{ insuranceState.offerTeamRegSaver() ? 'Payment/Insurance' : 'Payment' }}
        </h5>
      </div>
      
      <div class="card-body">
        <!-- Error display -->
        @if (lastError()) {
          <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
            <div class="flex-grow-1">
              <div class="fw-semibold mb-1">Payment Error</div>
              <div class="small">{{ lastError() }}</div>
            </div>
          </div>
        }

        <!-- Payment summary table -->
        <app-team-payment-summary-table></app-team-payment-summary-table>

        <!-- Vertical Insure widget section -->
        @if (insuranceState.offerTeamRegSaver() && paymentSvc.hasBalance()) {
          <div class="mb-3">
            <h6 class="fw-semibold mb-2">Team Insurance (Optional)</h6>
            <div #viOffer id="dVITeamOffer" class="text-center"></div>
            
            @if (!insuranceState.hasVerticalInsureDecision()) {
              <div class="alert alert-secondary border-0 py-2 small mt-2" role="alert">
                Insurance is optional. Choose <strong>Confirm Purchase</strong> or <strong>Decline Insurance</strong> to continue.
              </div>
            }
            
            @if (insuranceState.hasVerticalInsureDecision()) {
              <div class="mt-2">
                <div class="alert" 
                     [ngClass]="insuranceState.verticalInsureConfirmed() ? 'alert-success' : 'alert-secondary'" 
                     role="status">
                  <div class="d-flex align-items-center gap-2">
                    <span class="badge" 
                          [ngClass]="insuranceState.verticalInsureConfirmed() ? 'bg-success' : 'bg-secondary'">
                      RegSaver
                    </span>
                    <div>
                      @if (insuranceState.verticalInsureConfirmed()) {
                        <div class="fw-semibold mb-0">Insurance Selected</div>
                        <div class="small text-muted">Coverage for {{ insuranceSvc.quotedTeams().length }} team(s)</div>
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

        <!-- Credit card form (only if balance due) -->
        @if (paymentSvc.hasBalance()) {
          <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="cc-title"
                   style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
            <h6 id="cc-title" class="fw-semibold mb-2">Credit Card Information</h6>
            
            <app-credit-card-form
              (validChange)="onCcValidChange($event)"
              (valueChange)="onCcValueChange($event)"
              [viOnly]="false"
              [defaultEmail]="auth.currentUser()?.username || null"
            ></app-credit-card-form>
          </section>
        }

        <!-- Submit button -->
        @if (paymentSvc.hasBalance()) {
          <div class="d-grid gap-2">
            <button 
              type="button"
              class="btn btn-primary btn-lg"
              [disabled]="!canSubmit() || submitting()"
              (click)="submitPayment()">
              @if (submitting()) {
                <span class="spinner-border spinner-border-sm me-2"></span>
                Processing...
              } @else {
                Submit Payment ({{ paymentSvc.balanceDue() | currency }})
              }
            </button>
          </div>
        }
      </div>
    </div>
  `,
    styles: [`
    .card-header-subtle {
      background: linear-gradient(135deg, var(--bs-primary-bg-subtle) 0%, var(--bs-secondary-bg-subtle) 100%);
    }
  `]
})
export class TeamPaymentStepComponent implements AfterViewInit, OnDestroy {
    readonly paymentSvc = inject(TeamPaymentService);
    readonly paymentState = inject(TeamPaymentStateService);
    readonly insuranceSvc = inject(TeamInsuranceService);
    readonly insuranceState = inject(TeamInsuranceStateService);
    readonly teamReg = inject(TeamRegistrationService);
    readonly auth = inject(AuthService);
    readonly http = inject(HttpClient);
    readonly toast = inject(ToastService);
    readonly jobService = inject(JobService);

    @ViewChild('viOffer') viOfferElement?: ElementRef<HTMLDivElement>;

    // Component state
    ccValid = signal(false);
    ccData = signal<CreditCardInfo | null>(null);
    submitting = signal(false);
    lastError = signal<string | null>(null);

    // Insurance offer loaded
    private insuranceOfferLoaded = signal(false);

    canSubmit = computed(() => {
        if (!this.paymentSvc.hasBalance()) return false;
        if (!this.ccValid() || !this.ccData()) return false;

        // If insurance is offered, require decision
        if (this.insuranceState.offerTeamRegSaver()) {
            return this.insuranceState.hasVerticalInsureDecision();
        }

        return true;
    });

    ngAfterViewInit(): void {
        // Load insurance offer if enabled and not already loaded
        if (this.insuranceState.offerTeamRegSaver() && !this.insuranceOfferLoaded()) {
            this.loadInsuranceOffer();
        }
    }

    ngOnDestroy(): void {
        // Reset insurance state when leaving payment step
        this.insuranceSvc.widgetInitialized.set(false);
    }

    private async loadInsuranceOffer(): Promise<void> {
        const regId = this.auth.currentUser()?.regId;
        const jobId = this.jobService.currentJob()?.jobId;

        if (!regId || !jobId) {
            console.warn('Cannot load insurance offer: missing regId or jobId');
            return;
        }

        try {
            const offer = await this.insuranceSvc.fetchTeamInsuranceOffer(jobId, regId);
            this.insuranceOfferLoaded.set(true);

            if (offer?.available && offer.teamObject) {
                // Initialize VI widget
                setTimeout(() => {
                    if (this.viOfferElement?.nativeElement) {
                        this.insuranceSvc.initWidget('#dVITeamOffer', offer.teamObject);
                    }
                }, 100);
            }
        } catch (error) {
            console.error('Failed to load insurance offer:', error);
        }
    }

    onCcValidChange(valid: boolean): void {
        this.ccValid.set(valid);
    }

    onCcValueChange(data: CreditCardInfo): void {
        this.ccData.set(data);
    }

    async submitPayment(): Promise<void> {
        if (!this.canSubmit() || this.submitting()) return;

        const ccData = this.ccData();
        if (!ccData) return;

        this.submitting.set(true);
        this.lastError.set(null);

        try {
            const user = this.auth.currentUser();
            const jobId = this.jobService.currentJob()?.jobId;
            if (!user?.regId || !user?.jobPath || !jobId) {
                throw new Error('Missing required authentication data');
            }
            const regId = user.regId;
            const jobPath = user.jobPath;

            // Step 1: Purchase insurance if selected
            let viPolicyNumbers: Record<string, string> | undefined;
            if (this.insuranceState.verticalInsureConfirmed()) {
                const teamIds = this.paymentSvc.teamIdsWithBalance();
                const quotes = this.insuranceSvc.quotes();
                const quoteIds = quotes.map(q => q.id || q.quote_id).filter(Boolean);

                const viResult = await this.insuranceSvc.purchaseTeamInsurance(
                    jobId,
                    regId,
                    teamIds,
                    quoteIds,
                    ccData
                );

                if (!viResult.success) {
                    throw new Error(viResult.error || 'Insurance purchase failed');
                }

                viPolicyNumbers = viResult.policies;
                this.insuranceState.updatePolicyNumbers(viResult.policies || {});
            }

            // Step 2: Process TSIC payment
            const request: TeamPaymentRequestDto = {
                jobPath,
                clubRepRegId: regId,
                teamIds: this.paymentSvc.teamIdsWithBalance(),
                totalAmount: this.paymentSvc.balanceDue(),
                creditCard: ccData
            };

            const url = `${environment.apiUrl}/team-payment/process`;
            const response = await firstValueFrom(
                this.http.post<TeamPaymentResponseDto>(url, request)
            );

            if (response.success) {
                this.toast.show('Payment processed successfully', 'success');
                this.paymentState.setLastPayment({
                    amount: this.paymentSvc.balanceDue(),
                    transactionId: response.transactionId,
                    viPolicyNumbers,
                    message: response.message || null
                });
            } else {
                throw new Error(response.error || 'Payment processing failed');
            }
        } catch (error) {
            const message = error instanceof HttpErrorResponse
                ? error.error?.message || error.message
                : (error as Error).message || 'Payment failed';

            this.lastError.set(message);
            this.toast.show(message, 'danger');
        } finally {
            this.submitting.set(false);
        }
    }
}
