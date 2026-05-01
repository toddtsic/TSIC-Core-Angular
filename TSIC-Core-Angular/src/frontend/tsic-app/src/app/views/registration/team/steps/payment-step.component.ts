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
import { BankAccountFormComponent } from '@views/registration/shared/components/bank-account-form.component';
import { ToastService } from '@shared-ui/toast.service';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/shared/services/credit-card-utils';
import { sanitizeRouting, sanitizeAccount, sanitizeNameOnAccount } from '@views/registration/shared/services/bank-account-utils';
import { DatePipe } from '@angular/common';
import type {
    BankAccountInfo, CreditCardInfo,
    TeamEcheckPaymentRequestDto,
    TeamArbTrialPaymentRequestDto, TeamArbTrialPaymentResponseDto,
} from '@core/api';
import type { CreditCardFormValue } from '@views/registration/shared/types/wizard.types';
import { RegisteredTeamsGridComponent } from '../components/registered-teams-grid.component';

/**
 * Team Payment step — supports CC, eCheck (ACH), ARB-Trial scheduled payments,
 * and mail-in check. Per-job availability:
 *   • PaymentMethodsAllowedCode: 1=CC only, 2=CC or Check, 3=Check only
 *   • bEnableEcheck: per-job opt-in
 *   • adnArbTrial + adnStartDateAfterTrial: per-job opt-in for the deposit/balance schedule
 */
@Component({
    selector: 'app-trw-payment-step',
    standalone: true,
    imports: [CurrencyPipe, DatePipe, FormsModule, CreditCardFormComponent, BankAccountFormComponent, RegisteredTeamsGridComponent],
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

          <!-- ═══ PAYMENT METHOD SELECTOR (CC, eCheck, Check) ═══ -->
          @if (showMethodSelector()) {
            <div class="method-selector mb-3">
              <label class="form-label fw-semibold mb-2">Payment Method</label>
              <div class="d-flex gap-2 flex-wrap">
                @if (showCcButton()) {
                  <button type="button" class="method-btn"
                          [class.active]="isCc()"
                          (click)="selectMethod('CC')">
                    <i class="bi bi-credit-card me-2"></i>Credit Card
                  </button>
                }
                @if (showEcheckButton()) {
                  <button type="button" class="method-btn"
                          [class.active]="isEcheck()"
                          (click)="selectMethod('Echeck')">
                    <i class="bi bi-bank me-2"></i>eCheck (ACH)
                  </button>
                }
                @if (showArbTrialButton()) {
                  <button type="button" class="method-btn"
                          [class.active]="isArbTrial()"
                          (click)="selectMethod('ArbTrial')">
                    <i class="bi bi-calendar2-range me-2"></i>Pay in 2 (Deposit + Balance)
                  </button>
                }
                @if (showCheckButton()) {
                  <button type="button" class="method-btn"
                          [class.active]="isCheck()"
                          (click)="selectMethod('Check')">
                    <i class="bi bi-envelope-paper me-2"></i>Pay by Check
                  </button>
                }
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

          <!-- ═══ BANK ACCOUNT (eCheck) FORM ═══ -->
          @if (isEcheck()) {
            <section class="p-3 p-sm-4 mb-3 rounded-3"
                     style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
              <app-bank-account-form
                [defaultFirstName]="clubRepContact()?.firstName ?? null"
                [defaultLastName]="clubRepContact()?.lastName ?? null"
                [defaultAddress]="clubRepContact()?.streetAddress ?? null"
                [defaultZip]="clubRepContact()?.postalCode ?? null"
                [defaultEmail]="clubRepContact()?.email ?? null"
                [defaultPhone]="clubRepContact()?.cellphone ?? clubRepContact()?.phone ?? null"
                (validChange)="onBaValidChange($event)"
                (valueChange)="onBaValueChange($event)" />
            </section>

            <button type="button" class="btn btn-primary"
                    (click)="submitEcheck()"
                    [disabled]="!canSubmitEcheck()">
              {{ submitting() ? 'Processing...' : 'Pay ' + (balanceDue() | currency) + ' by eCheck' }}
            </button>
          }

          <!-- ═══ ARB-TRIAL (Deposit tomorrow + Balance on configured date) ═══ -->
          @if (isArbTrial()) {
            <section class="arb-trial-panel p-3 p-sm-4 mb-3 rounded-3">
              @if (arbTrialIsFallback()) {
                <div class="alert alert-warning border-0 d-flex align-items-start gap-2 mb-3">
                  <i class="bi bi-exclamation-triangle fs-5"></i>
                  <div>
                    <div class="fw-semibold mb-1">Balance date already passed</div>
                    <div class="small">
                      Today is on or after the configured balance date
                      ({{ arbTrialBalanceDate() | date:'mediumDate' }}). Submitting will charge the
                      full amount now as a single transaction — no payment plan will be created.
                    </div>
                  </div>
                </div>
              } @else {
                <div class="schedule-banner mb-3">
                  <div class="schedule-row">
                    <span class="schedule-label">
                      <i class="bi bi-1-circle me-2"></i>Deposit (charged tomorrow)
                    </span>
                    <span class="schedule-value">{{ arbTrialDepositDate() | date:'mediumDate' }}</span>
                  </div>
                  <div class="schedule-row">
                    <span class="schedule-label">
                      <i class="bi bi-2-circle me-2"></i>Balance
                    </span>
                    <span class="schedule-value">{{ arbTrialBalanceDate() | date:'mediumDate' }}</span>
                  </div>
                  <div class="schedule-row total">
                    <span class="schedule-label">Total across {{ arbTrialTeamCount() }} team(s)</span>
                    <span class="schedule-value fw-bold">{{ balanceDue() | currency }}</span>
                  </div>
                </div>
                <div class="text-muted small mb-3">
                  <i class="bi bi-info-circle me-1"></i>
                  One subscription per team — refunds and cancellations are handled team-by-team.
                </div>
              }

              <!-- Sub-source picker shown only when both CC and eCheck are available for this job. -->
              @if (showArbTrialSourcePicker()) {
                <div class="mb-3">
                  <label class="form-label fw-semibold mb-2">Fund this with:</label>
                  <div class="d-flex gap-2 flex-wrap">
                    <button type="button" class="method-btn"
                            [class.active]="arbTrialSource() === 'CC'"
                            (click)="selectArbTrialSource('CC')">
                      <i class="bi bi-credit-card me-2"></i>Credit Card
                    </button>
                    <button type="button" class="method-btn"
                            [class.active]="arbTrialSource() === 'Echeck'"
                            (click)="selectArbTrialSource('Echeck')">
                      <i class="bi bi-bank me-2"></i>eCheck (ACH)
                    </button>
                  </div>
                </div>
              }

              @if (arbTrialSource() === 'CC') {
                <app-credit-card-form
                  [defaultFirstName]="clubRepContact()?.firstName ?? null"
                  [defaultLastName]="clubRepContact()?.lastName ?? null"
                  [defaultAddress]="clubRepContact()?.streetAddress ?? null"
                  [defaultZip]="clubRepContact()?.postalCode ?? null"
                  [defaultEmail]="clubRepContact()?.email ?? null"
                  [defaultPhone]="clubRepContact()?.cellphone ?? clubRepContact()?.phone ?? null"
                  (validChange)="onCcValidChange($event)"
                  (valueChange)="onCcValueChange($event)" />
              } @else {
                <app-bank-account-form
                  [defaultFirstName]="clubRepContact()?.firstName ?? null"
                  [defaultLastName]="clubRepContact()?.lastName ?? null"
                  [defaultAddress]="clubRepContact()?.streetAddress ?? null"
                  [defaultZip]="clubRepContact()?.postalCode ?? null"
                  [defaultEmail]="clubRepContact()?.email ?? null"
                  [defaultPhone]="clubRepContact()?.cellphone ?? clubRepContact()?.phone ?? null"
                  (validChange)="onBaValidChange($event)"
                  (valueChange)="onBaValueChange($event)" />
              }
            </section>

            <button type="button" class="btn btn-primary"
                    (click)="submitArbTrial()"
                    [disabled]="!canSubmitArbTrial()">
              {{ submitting()
                  ? 'Processing...'
                  : (arbTrialIsFallback()
                      ? 'Charge ' + (balanceDue() | currency) + ' Now'
                      : 'Schedule ' + (balanceDue() | currency) + ' (Deposit + Balance)') }}
            </button>

            <!-- Per-team results panel — shown only after a partial-success or all-failed submit. -->
            @if (arbTrialResult(); as r) {
              @if (!r.success) {
                <section class="arb-trial-results mt-3 p-3 rounded-3">
                  <h6 class="fw-semibold mb-2">Results by team</h6>
                  <table class="table table-sm mb-0">
                    <thead>
                      <tr>
                        <th>Team</th>
                        <th>Status</th>
                        <th>Detail</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (team of r.teams; track team.teamId) {
                        <tr [class.table-success]="team.registered" [class.table-danger]="!team.registered">
                          <td>{{ teamName(team.teamId) }}</td>
                          <td>
                            @if (team.registered) {
                              <span class="badge bg-success">Registered</span>
                            } @else {
                              <span class="badge bg-danger">Failed</span>
                            }
                          </td>
                          <td class="small">
                            @if (team.registered) {
                              Deposit {{ team.depositCharge | currency }} on {{ team.depositDate | date:'mediumDate' }};
                              Balance {{ team.balanceCharge | currency }} on {{ team.balanceDate | date:'mediumDate' }}
                            } @else {
                              {{ team.failureReason }}
                            }
                          </td>
                        </tr>
                      }
                      @for (notId of r.notAttempted; track notId) {
                        <tr class="table-secondary">
                          <td>{{ teamName(notId) }}</td>
                          <td><span class="badge bg-secondary">Not attempted</span></td>
                          <td class="small text-muted">Batch stopped at first failure — earlier successes were kept.</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </section>
              }
            }
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

      .arb-trial-panel {
        background: var(--bs-secondary-bg);
        border: 1px solid var(--bs-border-color-translucent);
      }

      .schedule-banner {
        background: rgba(var(--bs-primary-rgb), 0.05);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.2);
        border-radius: var(--radius-md);
        padding: var(--space-3);
      }

      .schedule-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: var(--space-2) 0;
        border-bottom: 1px solid rgba(var(--bs-primary-rgb), 0.1);

        &:last-of-type { border-bottom: none; }
        &.total {
          margin-top: var(--space-2);
          padding-top: var(--space-3);
          border-top: 2px solid rgba(var(--bs-primary-rgb), 0.2);
          border-bottom: none;
        }
      }

      .schedule-label {
        font-size: var(--font-size-sm);
        color: var(--brand-text);
      }

      .schedule-value {
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        font-weight: var(--font-weight-medium);
      }

      .arb-trial-results {
        background: var(--bs-secondary-bg);
        border: 1px solid var(--bs-border-color-translucent);
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
    private readonly _bankAccount = signal<Record<string, string>>({
        accountType: '', routingNumber: '', accountNumber: '', nameOnAccount: '',
        firstName: '', lastName: '', address: '', zip: '', email: '', phone: '',
    });
    readonly bankAccountValid = signal(false);
    readonly submitting = signal(false);
    readonly lastError = signal<string | null>(null);
    readonly discountCode = signal('');
    readonly arbTrialResult = signal<TeamArbTrialPaymentResponseDto | null>(null);
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
    readonly showCcButton = computed(() => this.state.teamPayment.showCcButton());
    readonly showEcheckButton = computed(() => this.state.teamPayment.showEcheckButton());
    readonly showArbTrialButton = computed(() => this.state.teamPayment.showArbTrialButton());
    readonly showCheckButton = computed(() => this.state.teamPayment.showCheckButton());
    readonly isCc = computed(() => this.state.teamPayment.isCcPayment());
    readonly isEcheck = computed(() => this.state.teamPayment.isEcheckPayment());
    readonly isArbTrial = computed(() => this.state.teamPayment.isArbTrialPayment());
    readonly isCheck = computed(() => this.state.teamPayment.isCheckPayment());
    readonly arbTrialIsFallback = computed(() => this.state.teamPayment.arbTrialIsFallback());
    readonly arbTrialDepositDate = computed(() => this.state.teamPayment.arbTrialDepositDate());
    readonly arbTrialBalanceDate = computed(() => this.state.teamPayment.adnStartDateAfterTrial());
    readonly arbTrialTeamCount = computed(() => this.state.teamPayment.teamIdsWithBalance().length);
    readonly arbTrialSource = computed(() => this.state.teamPayment.arbTrialSource());
    /** Show the CC/eCheck sub-picker only when both sources are available on this job. */
    readonly showArbTrialSourcePicker = computed(() => this.showCcButton() && this.showEcheckButton());
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

    readonly canSubmitEcheck = computed(() =>
        this.hasBalance() && this.bankAccountValid() && !this.submitting(),
    );

    /** ARB-Trial submit gate — needs balance + the form valid for the chosen sub-source. */
    readonly canSubmitArbTrial = computed(() => {
        if (!this.hasBalance() || this.submitting()) return false;
        return this.arbTrialSource() === 'CC' ? this.ccValid() : this.bankAccountValid();
    });

    selectMethod(method: 'CC' | 'Echeck' | 'ArbTrial' | 'Check'): void {
        this.state.teamPayment.selectPaymentMethod(method);
        this.lastError.set(null);
        this.arbTrialResult.set(null);
    }

    selectArbTrialSource(src: 'CC' | 'Echeck'): void {
        this.state.teamPayment.selectArbTrialSource(src);
        this.lastError.set(null);
    }

    /** Lookup helper for the per-team result table — falls back to short id when team rolls off the list. */
    teamName(teamId: string): string {
        const t = this.registeredTeams().find(x => x.teamId === teamId);
        return t?.teamName ?? teamId.slice(0, 8);
    }

    onCcValidChange(valid: boolean): void { this.ccValid.set(!!valid); }
    onCcValueChange(val: Partial<CreditCardFormValue>): void {
        this._creditCard.update(c => ({ ...c, ...val }));
    }

    onBaValidChange(valid: boolean): void { this.bankAccountValid.set(!!valid); }
    onBaValueChange(val: Record<string, string>): void {
        this._bankAccount.update(b => ({ ...b, ...val }));
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

    submitEcheck(): void {
        if (this.submitting()) return;
        if (!this.canSubmitEcheck()) {
            this.toast.show('Bank account form is invalid.', 'danger', 3000);
            return;
        }
        this.submitting.set(true);
        this.lastError.set(null);

        if (!this.lastIdemKey) {
            this.lastIdemKey = crypto?.randomUUID
                ? crypto.randomUUID()
                : (Date.now().toString(36) + Math.random().toString(36).slice(2));
        }

        const ba = this._bankAccount();
        const bankAccount: BankAccountInfo = {
            accountType: (ba['accountType'] || '').trim() || null,
            routingNumber: sanitizeRouting(ba['routingNumber']),
            accountNumber: sanitizeAccount(ba['accountNumber']),
            nameOnAccount: sanitizeNameOnAccount(ba['nameOnAccount']) || null,
            firstName: ba['firstName']?.trim() || null,
            lastName: ba['lastName']?.trim() || null,
            address: ba['address']?.trim() || null,
            zip: ba['zip']?.trim() || null,
            email: ba['email']?.trim() || null,
            phone: sanitizePhone(ba['phone']),
        };
        const teamIds = this.state.teamPayment.teamIdsWithBalance();
        const request: TeamEcheckPaymentRequestDto = {
            teamIds,
            totalAmount: this.balanceDue(),
            bankAccount,
        };

        this.state.teamPayment.submitEcheckPayment(request as unknown as Record<string, unknown>)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: resp => {
                    this.submitting.set(false);
                    if (resp?.success) {
                        this.lastIdemKey = null;
                        this.state.teamPaymentState.setLastPayment({
                            transactionId: resp.transactionId || undefined,
                            amount: this.balanceDue(),
                            message: resp.message || 'eCheck submitted — pending settlement',
                            paymentMethod: 'Echeck',
                        });
                        this.submitted.emit();
                    } else {
                        this.lastError.set(resp?.message || 'eCheck submission failed.');
                        this.toast.show(resp?.message || 'eCheck submission failed.', 'danger', 6000);
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

    /**
     * ARB-Trial submission. Posts to /team-payment/process-arb-trial; capture-what-you-can
     * means partial-success responses still carry per-team rows we render to the rep.
     * On full success we refresh metadata so AdnSubscription* fields land on the team rows.
     */
    submitArbTrial(): void {
        if (this.submitting() || !this.canSubmitArbTrial()) return;
        this.submitting.set(true);
        this.lastError.set(null);
        this.arbTrialResult.set(null);

        const teamIds = this.state.teamPayment.teamIdsWithBalance();
        let creditCard: CreditCardInfo | null = null;
        let bankAccount: BankAccountInfo | null = null;

        if (this.arbTrialSource() === 'CC') {
            const cc = this._creditCard();
            creditCard = {
                number: cc.number?.trim() || null,
                expiry: sanitizeExpiry(cc.expiry),
                code: cc.code?.trim() || null,
                firstName: cc.firstName?.trim() || null,
                lastName: cc.lastName?.trim() || null,
                address: cc.address?.trim() || null,
                zip: cc.zip?.trim() || null,
                email: cc.email?.trim() || null,
                phone: sanitizePhone(cc.phone),
            };
        } else {
            const ba = this._bankAccount();
            bankAccount = {
                accountType: (ba['accountType'] || '').trim() || null,
                routingNumber: sanitizeRouting(ba['routingNumber']),
                accountNumber: sanitizeAccount(ba['accountNumber']),
                nameOnAccount: sanitizeNameOnAccount(ba['nameOnAccount']) || null,
                firstName: ba['firstName']?.trim() || null,
                lastName: ba['lastName']?.trim() || null,
                address: ba['address']?.trim() || null,
                zip: ba['zip']?.trim() || null,
                email: ba['email']?.trim() || null,
                phone: sanitizePhone(ba['phone']),
            };
        }

        const request: TeamArbTrialPaymentRequestDto = { teamIds, creditCard, bankAccount };

        this.state.teamPayment.submitArbTrialPayment(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp: TeamArbTrialPaymentResponseDto) => {
                    this.submitting.set(false);
                    this.arbTrialResult.set(resp);
                    if (resp.success) {
                        this.state.teamPaymentState.setLastPayment({
                            amount: this.balanceDue(),
                            message: resp.message ?? (resp.mode === 'FALLBACK_FULL_CHARGE'
                                ? 'Payment processed (fallback full charge — balance date had passed)'
                                : 'Payment plan scheduled — deposit charges tomorrow'),
                            paymentMethod: this.arbTrialSource() === 'Echeck' ? 'Echeck' : 'CC',
                        });
                        // Refresh teams metadata so AdnSubscription* fields appear on the team rows.
                        this.teamReg.getTeamsMetadata()
                            .pipe(takeUntilDestroyed(this.destroyRef))
                            .subscribe({
                                next: meta => {
                                    this.state.applyTeamsMetadata(meta);
                                    this.submitted.emit();
                                },
                                error: () => this.submitted.emit(),
                            });
                    } else {
                        const msg = resp.message ?? 'ARB-Trial submission failed.';
                        this.lastError.set(msg);
                        this.toast.show(msg, 'danger', 6000);
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
