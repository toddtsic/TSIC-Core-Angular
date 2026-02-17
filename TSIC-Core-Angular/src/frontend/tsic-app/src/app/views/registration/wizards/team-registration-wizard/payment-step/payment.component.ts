import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  signal,
  computed,
  AfterViewInit,
  ViewChild,
  ElementRef,
  OnDestroy,
  OnInit,
  Output,
  EventEmitter,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { firstValueFrom, switchMap } from 'rxjs';
import { TeamPaymentSummaryTableComponent } from './team-payment-summary-table.component';
import { CreditCardFormComponent } from '../../player-registration-wizard/steps/credit-card-form.component';
import { TeamPaymentService } from '../services/team-payment.service';
import { TeamPaymentStateService } from '../services/team-payment-state.service';
import { TeamInsuranceService } from '../services/team-insurance.service';
import { TeamInsuranceStateService } from '../services/team-insurance-state.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import type { CreditCardInfo, TeamsMetadataResponse, TeamPaymentRequestDto } from '@core/api';
import { IdempotencyService } from '../../shared/services/idempotency.service';

// Extend generated request DTO with fields derived from client context (not in OpenAPI spec)
type TeamPaymentRequest = TeamPaymentRequestDto & {
  jobPath: string;
  clubRepRegId: string;
  idempotencyKey?: string | null;
};

declare global {
  interface Window {
    VerticalInsure?: any;
  }
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
    CreditCardFormComponent,
  ],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <div class="d-flex align-items-center gap-2">
          <h5 class="mb-0 fw-semibold">
            {{
              insuranceState.offerTeamRegSaver() ? 'Payment/Insurance' : 'Payment'
            }}
          </h5>
          @if (paymentSvc.lineItems().length > 0) {
            <button
              type="button"
              class="btn btn-outline-success btn-sm export-btn"
              (click)="summaryTable.exportPaymentsToExcel()"
              aria-label="Export payment summary to Excel"
              title="Export to Excel"
            >
              <i class="bi bi-file-earmark-excel export-icon"></i>
            </button>
          }
        </div>
      </div>

      <div class="card-body">
        <!-- Error display -->
        @if (lastError()) {
          <div
            class="alert alert-danger d-flex align-items-start gap-2"
            role="alert"
          >
            <div class="grow">
              <div class="fw-semibold mb-1">Payment Error</div>
              <div class="small">{{ lastError() }}</div>
            </div>
          </div>
        }

        <!-- Prominent balance due banner -->
        @if (paymentSvc.hasBalance()) {
          <div class="d-flex align-items-center justify-content-between p-3 mb-3 rounded-3"
               class="bg-primary text-white">
            <span class="fw-semibold">Balance Due</span>
            <span class="fs-4 fw-bold">{{ paymentSvc.amountToCharge() | currency }}</span>
          </div>
        }

        <!-- Payment summary table -->
        <app-team-payment-summary-table #summaryTable></app-team-payment-summary-table>

        <!-- Discount code section -->
        @if (paymentSvc.hasBalance() && metadata()?.hasActiveDiscountCodes) {
          <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="discount-title" style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
            <h6 id="discount-title" class="fw-semibold mb-3">
              <i class="bi bi-tag me-2"></i>Discount Code (Optional)
            </h6>

            <div class="input-group input-group-sm w-auto d-inline-flex align-items-center gap-2">
              <input
                type="text"
                class="form-control form-control-sm"
                placeholder="Enter discount code"
                [(ngModel)]="discountCode"
                [disabled]="paymentSvc.discountApplying()"
                (keyup.enter)="applyDiscount()"
                style="min-width: 180px;"
              />
              <button
                type="button"
                class="btn btn-outline-primary btn-sm"
                [disabled]="!discountCode.trim() || paymentSvc.discountApplying()"
                (click)="applyDiscount()"
              >
                @if (paymentSvc.discountApplying()) {
                  <span class="spinner-border spinner-border-sm me-2"></span>
                  Applying...
                } @else {
                  Apply
                }
              </button>
            </div>

            @if (paymentSvc.discountMessage()) {
              <div
                class="form-text mt-2"
                [class.text-success]="paymentSvc.appliedDiscountResponse()?.success"
                [class.text-danger]="!paymentSvc.appliedDiscountResponse()?.success"
              >
                <div class="fw-semibold mb-1">{{ paymentSvc.discountMessage() }}</div>
                @if (paymentSvc.appliedDiscountResponse(); as response) {
                  @if (response.success && response.results.length) {
                    <div class="small mt-2">
                      <div class="fw-semibold mb-1">Team Results:</div>
                      <ul class="mb-0 ps-3">
                        @for (result of response.results; track result.teamId) {
                          <li [ngClass]="result.success ? 'text-success' : 'text-danger'">
                            <strong>{{ result.teamName }}</strong>: {{ result.message }}
                          </li>
                        }
                      </ul>
                    </div>
                  }
                }
              </div>
            }
          </section>
        }

        <!-- Vertical Insure widget section -->
        @if (insuranceState.offerTeamRegSaver() && paymentSvc.hasBalance()) {
          <div class="mb-3">
            <h6 class="fw-semibold mb-2">Team Insurance (Optional)</h6>
            <div #viOffer id="dVITeamOffer" class="text-center"></div>

            @if (!insuranceState.hasVerticalInsureDecision()) {
              <div
                class="alert alert-secondary border-0 py-2 small mt-2"
                role="alert"
              >
                Insurance is optional. Choose
                <strong>Confirm Purchase</strong> or
                <strong>Decline Insurance</strong> to continue.
              </div>
            }

            @if (insuranceState.hasVerticalInsureDecision()) {
              <div class="mt-2">
                <div
                  class="alert"
                  [ngClass]="
                    insuranceState.verticalInsureConfirmed()
                      ? 'alert-success'
                      : 'alert-secondary'
                  "
                  role="status"
                >
                  <div class="d-flex align-items-center gap-2">
                    <span
                      class="badge"
                      [ngClass]="
                        insuranceState.verticalInsureConfirmed()
                          ? 'bg-success'
                          : 'bg-secondary'
                      "
                    >
                      RegSaver
                    </span>
                    <div>
                      @if (insuranceState.verticalInsureConfirmed()) {
                        <div class="fw-semibold mb-0">Insurance Selected</div>
                        <div class="small text-muted">
                          Coverage for
                          {{ insuranceSvc.quotedTeams().length }} team(s)
                        </div>
                      } @else {
                        <div class="fw-semibold mb-0">Insurance Declined</div>
                        <div class="small text-muted">
                          You chose not to purchase coverage.
                        </div>
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
          <section
            class="p-3 p-sm-4 mb-3 rounded-3"
            aria-labelledby="cc-title"
            style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)"
          >
            <h6 id="cc-title" class="fw-semibold mb-2">
              Credit Card Information
            </h6>

            <app-credit-card-form
              (validChange)="onCcValidChange($event)"
              (valueChange)="onCcValueChange($event)"
              [viOnly]="false"
              [defaultFirstName]="
                metadata()?.clubRepContactInfo?.firstName || null
              "
              [defaultLastName]="
                metadata()?.clubRepContactInfo?.lastName || null
              "
              [defaultAddress]="
                metadata()?.clubRepContactInfo?.streetAddress || null
              "
              [defaultZip]="metadata()?.clubRepContactInfo?.postalCode || null"
              [defaultEmail]="metadata()?.clubRepContactInfo?.email || null"
              [defaultPhone]="
                metadata()?.clubRepContactInfo?.cellphone ||
                metadata()?.clubRepContactInfo?.phone ||
                null
              "
            ></app-credit-card-form>
          </section>

          <!-- Submit button (Payment Step) -->
          <div class="d-grid gap-2">
            <button
              type="button"
              class="btn btn-primary"
              [disabled]="!canSubmit() || submitting()"
              (click)="submitPayment()"
            >
              @if (submitting()) {
                <span class="spinner-border spinner-border-sm me-2"></span>
                Processing...
              } @else {
                Pay {{ paymentSvc.amountToCharge() | currency }} Now
              }
            </button>
          </div>
        }

        <!-- No payment due alert -->
        @if (!paymentSvc.hasBalance()) {
          <div
            class="alert alert-info d-flex align-items-center gap-2"
            role="alert"
          >
            <i class="bi bi-info-circle-fill shrink-0"></i>
            <div class="grow">
              <strong>No Payment Due At This Time</strong> - All team
              registrations are fully paid at this time.
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styles: [
    `
      .card-header-subtle {
        background: linear-gradient(
          135deg,
          var(--bs-primary-bg-subtle) 0%,
          var(--bs-secondary-bg-subtle) 100%
        );
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamPaymentStepComponent
  implements OnInit, AfterViewInit, OnDestroy {
  // Services
  readonly paymentSvc = inject(TeamPaymentService);
  readonly paymentState = inject(TeamPaymentStateService);
  readonly insuranceSvc = inject(TeamInsuranceService);
  readonly insuranceState = inject(TeamInsuranceStateService);
  readonly teamReg = inject(TeamRegistrationService);
  readonly auth = inject(AuthService);
  readonly toast = inject(ToastService);
  readonly jobService = inject(JobService);
  readonly route = inject(ActivatedRoute);
  readonly jobContext = inject(JobContextService);
  private readonly idemSvc = inject(IdempotencyService);
  private readonly destroyRef = inject(DestroyRef);

  @ViewChild('viOffer') viOfferElement?: ElementRef<HTMLDivElement>;

  // Event output
  @Output() proceed = new EventEmitter<void>();

  // Component state
  ccValid = signal(false);
  ccData = signal<CreditCardInfo | null>(null);
  submitting = signal(false);
  lastError = signal<string | null>(null);
  metadata = signal<TeamsMetadataResponse | null>(null);
  discountCode = '';
  private lastIdemKey: string | null = null;

  // Insurance offer loaded
  private readonly insuranceOfferLoaded = signal(false);

  canSubmit = computed(() => {
    if (!this.paymentSvc.hasBalance()) return false;
    if (!this.ccValid() || !this.ccData()) return false;

    // If insurance is offered, require decision
    if (this.insuranceState.offerTeamRegSaver()) {
      return this.insuranceState.hasVerticalInsureDecision();
    }

    return true;
  });

  ngOnInit(): void {
    // Hydrate any existing idempotency key (from a previous failed attempt)
    const jobId = this.jobService.currentJob()?.jobId;
    const regId = this.auth.currentUser()?.regId;
    this.lastIdemKey = this.idemSvc.load(jobId, regId) || null;

    // Fetch teams metadata to get club rep contact info for form prefill
    // Context (clubName, jobId) derived from regId token claim on backend
    this.teamReg.getTeamsMetadata(true).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => this.metadata.set(response),
      error: (err) =>
        console.error('[PaymentComponent] Failed to load metadata:', err),
    });
  }

  ngAfterViewInit(): void {
    // Load insurance offer if enabled and not already loaded
    if (
      this.insuranceState.offerTeamRegSaver() &&
      !this.insuranceOfferLoaded()
    ) {
      this.loadInsuranceOffer();
    }
  }

  ngOnDestroy(): void {
    // Reset insurance state when leaving payment step
    this.insuranceSvc.widgetInitialized.set(false);
  }

  private async loadInsuranceOffer(): Promise<void> {
    const regId = this.auth.currentUser()?.regId;

    if (!regId) {
      console.warn('Cannot load insurance offer: missing regId');
      return;
    }

    try {
      const offer = await this.insuranceSvc.fetchTeamInsuranceOffer();
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

  applyDiscount(): void {
    const code = this.discountCode.trim();
    if (!code) return;

    const teamIds = this.paymentSvc.teamIdsWithBalance();
    if (teamIds.length === 0) {
      this.toast.show('No teams available for discount', 'warning');
      return;
    }

    this.paymentSvc.applyDiscount(code, teamIds).pipe(
      switchMap(() => this.teamReg.getTeamsMetadata(true)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (response) => this.metadata.set(response),
      error: (err) => {
        console.error('[PaymentComponent] Discount application failed:', err);
      },
    });
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
        const quoteIds = quotes.map((q) => q.id || q.quote_id).filter(Boolean);

        const viResult = await this.insuranceSvc.purchaseTeamInsurance(
          teamIds,
          quoteIds,
          ccData,
        );

        if (!viResult.success) {
          throw new Error(viResult.error || 'Insurance purchase failed');
        }

        viPolicyNumbers = viResult.policies;
        this.insuranceState.updatePolicyNumbers(viResult.policies || {});
      }

      // Generate idempotency key if absent; persist for retry safety
      if (!this.lastIdemKey) {
        const newKey = crypto?.randomUUID ? crypto.randomUUID() : (Date.now().toString(36) + Math.random().toString(36).slice(2));
        this.lastIdemKey = newKey;
        this.idemSvc.persist(jobId, regId, newKey);
      }

      // Step 2: Process TSIC payment
      const request: TeamPaymentRequest = {
        jobPath,
        clubRepRegId: regId,
        teamIds: this.paymentSvc.teamIdsWithBalance(),
        totalAmount: this.paymentSvc.balanceDue(),
        creditCard: ccData,
        idempotencyKey: this.lastIdemKey,
      };

      const response = await firstValueFrom(
        this.paymentSvc.submitPayment(request),
      );

      if (response.success) {
        // Clear idempotency key on success - next payment gets a new key
        this.idemSvc.clear(jobId, regId);
        this.lastIdemKey = null;

        this.toast.show('Payment processed successfully', 'success');
        this.paymentState.setLastPayment({
          amount: this.paymentSvc.balanceDue(),
          transactionId: response.transactionId ?? undefined,
          viPolicyNumbers,
          message: response.message || null,
        });
        // Proceed to Review step after successful payment
        this.proceed.emit();
      } else {
        throw new Error(response.error || 'Payment processing failed');
      }
    } catch (error) {
      const message =
        error instanceof HttpErrorResponse
          ? error.error?.message || error.message
          : (error as Error).message || 'Payment failed';

      this.lastError.set(message);
      this.toast.show(message, 'danger');
    } finally {
      this.submitting.set(false);
    }
  }
}
