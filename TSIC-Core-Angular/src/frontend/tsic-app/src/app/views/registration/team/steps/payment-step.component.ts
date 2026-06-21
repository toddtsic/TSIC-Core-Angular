import {
  AfterViewInit, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnDestroy,
  inject, signal, computed, effect, output,
  viewChild
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { TeamInsuranceService } from '../services/team-insurance.service';
import { TeamInsuranceStateService } from '../services/team-insurance-state.service';
import { IdempotencyService } from '@views/registration/shared/services/idempotency.service';
import { CreditCardFormComponent } from '@views/registration/shared/components/credit-card-form.component';
import { BankAccountFormComponent } from '@views/registration/shared/components/bank-account-form.component';
import { ViChargeConfirmModalComponent } from '@views/registration/shared/components/vi-charge-confirm-modal.component';
import { ToastService } from '@shared-ui/toast.service';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/shared/services/credit-card-utils';
import { sanitizeRouting, sanitizeAccount, sanitizeNameOnAccount } from '@views/registration/shared/services/bank-account-utils';
import { scrollWizardToTop } from '@views/registration/shared/services/vi-scroll.util';
import { DatePipe } from '@angular/common';
import type {
    BankAccountInfo, CreditCardInfo,
    TeamEcheckPaymentRequestDto,
    TeamArbTrialPaymentRequestDto, TeamArbTrialPaymentResponseDto,
    TeamPaymentResponseDto,
} from '@core/api';
import type { CreditCardFormValue, VIOfferData } from '@views/registration/shared/types/wizard.types';
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
    imports: [CurrencyPipe, DatePipe, FormsModule, CreditCardFormComponent, BankAccountFormComponent, ViChargeConfirmModalComponent, RegisteredTeamsGridComponent],
    template: `
    <div class="step-card step-card-registered">
      <div class="section-titlebar section-titlebar-registered">
        <i class="bi bi-trophy-fill section-titlebar-icon" aria-hidden="true"></i>
        <h3 class="section-titlebar-title">
          <span class="section-titlebar-tail">Payment</span>
        </h3>
        <span class="phase-badge">
          <span class="phase-badge__label">Payment Phase</span>
          <span class="phase-badge__value">{{ phaseBadgeLabel() }}</span>
        </span>
      </div>
      <div class="step-card-body">
        @if (lastError()) {
          <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
            <div class="flex-grow-1">
              <div class="fw-semibold mb-1">Payment Error</div>
              <div class="small">{{ lastError() }}</div>
            </div>
          </div>
        }

        <!-- Per-team results panel — shown after a CC/eCheck partial-success or all-failed
             submit. The summary table + Pay button above have already been refreshed to the
             real remaining balance; this tells the rep exactly which teams charged and which
             declined (and why), so they retry only the declined teams. -->
        @if (chargeResult(); as cr) {
          <section class="charge-results alert alert-warning border-0 mb-3" role="status">
            <div class="fw-semibold mb-2">Results by team</div>
            <table class="table table-sm mb-0 bg-transparent">
              <thead>
                <tr><th>Team</th><th>Status</th><th>Detail</th></tr>
              </thead>
              <tbody>
                @for (t of cr.teams ?? []; track t.teamId) {
                  <tr [class.table-success]="t.charged" [class.table-danger]="!t.charged">
                    <td>{{ t.teamName || teamName(t.teamId) }}</td>
                    <td>
                      @if (t.charged) {
                        <span class="badge bg-success">Charged</span>
                      } @else {
                        <span class="badge bg-danger">Declined</span>
                      }
                    </td>
                    <td class="small">
                      @if (t.charged) {
                        {{ t.chargedAmount | currency }}
                      } @else {
                        {{ t.failureReason }}
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
            <div class="small text-muted mt-2">
              <i class="bi bi-info-circle me-1"></i>The teams that charged are now paid. Retry the declined team(s) — the amount due above reflects the remaining balance.
            </div>
          </section>
        }

        @if (!hasBalance()) {
          <div class="alert alert-success border-0 mb-3" role="status">
            <div class="d-flex align-items-center gap-2">
              <span class="badge bg-success">No Balance</span>
              @if (showViStandaloneSection()) {
                <div>All team registration fees have been paid. Optional team registration insurance is available below.</div>
              } @else {
                <div>No payment is required at this time. You may proceed to the review step.</div>
              }
            </div>
          </div>

          <!-- ═══ STANDALONE VI PURCHASE (returning rep, PIF) ═══
               Surfaces when TSIC is paid but uncovered teams remain and the offer
               is still within VI's 14-day window. Independent CC entry — does not
               touch TSIC at all. Same charge-confirm modal as the bundled flow. -->
          @if (showViStandaloneSection()) {
            <div class="insurance-wrapper mb-4">
              <header class="insurance-card-title">
                <i class="bi bi-shield-check me-2"></i>Team Registration Insurance
              </header>
              @if (insuranceSvc.widgetError(); as viErr) {
                <div class="alert alert-warning border-0 mb-0 small" role="alert">
                  <strong>Insurance is unavailable for this session.</strong>
                  You can still proceed to review.
                  <span class="text-muted">({{ viErr }})</span>
                </div>
              } @else {
                <div #viOffer id="dVITeamOffer" class="text-center vi-container">
                  @if (!insuranceSvc.widgetInitialized()) {
                    <div class="py-4">
                      <div class="spinner-border spinner-border-sm text-primary" role="status">
                        <span class="visually-hidden">Loading...</span>
                      </div>
                      <p class="text-muted mt-2 small mb-0">Getting Team Registration Insurance Quote...</p>
                    </div>
                  }
                </div>

                @if (insuranceSvc.widgetInitialized() && insuranceSvc.hasUserResponse() && insuranceSvc.quotes().length > 0) {
                  <!-- Rep accepted — collect a fresh card for the VI charge. -->
                  <section class="payment-form-panel mt-3">
                    <h6 class="fw-semibold mb-3">Payment for Insurance</h6>
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

                  <button type="button" class="btn btn-primary mt-2"
                          (click)="submitViOnly()"
                          [disabled]="!canSubmitViOnly()">
                    {{ submitting() ? 'Processing...' : 'Purchase Insurance ' + (viPremiumTotal() | currency) }}
                  </button>
                  @if (viOnlyPayHintVisible()) {
                    <div class="vi-pay-hint mt-2 small d-flex align-items-center gap-1" role="status">
                      <i class="bi bi-arrow-up-short"></i>
                      <span>Please respond to the insurance offer above to enable purchase.</span>
                    </div>
                  }
                } @else if (insuranceSvc.widgetInitialized() && insuranceSvc.hasUserResponse()) {
                  <!-- Rep declined coverage. No charge to make — wizard footer handles advance. -->
                  <div class="alert alert-secondary border-0 small mb-0 mt-2" role="status">
                    No coverage selected. Use <strong>Proceed to Review</strong> below to continue.
                  </div>
                }
              }
            </div>
          }
        } @else {
          <!-- Registered-teams ledger. Aggregate footer carries running totals;
               the old summary-pill row above used to duplicate the same numbers. -->
          <section class="mb-3">
            <app-registered-teams-grid
              [teams]="registeredTeams()"
              [showProcessing]="showProcessing()"
              [showCcOwed]="allowsCc()"
              [showCkOwed]="showProcessing()"
              [showAgeGroup]="false"
              [showTotalFee]="false"
              [showDeposit]="true"
              [showBalance]="showBalanceColumn()"
              [procFeeHeader]="procFeeHeaderLabel()"
              [frozenTeamCol]="true"
              [teamColWidth]="70"
              [gridHeight]="'auto'" />
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

          <!-- ═══ OPTIONAL DONATION ═══ -->
          @if (showDonationInput()) {
            <div class="donation-block mb-3">
              <label for="teamDonationAmount" class="field-label donation-label">
                <i class="bi bi-heart-fill me-1"></i>Add a Donation
                <span class="donation-optional">optional</span>
              </label>
              <div class="donation-input-row">
                <span class="donation-currency" aria-hidden="true">$</span>
                <input type="number" min="0" step="1" inputmode="decimal"
                       class="field-input donation-input" id="teamDonationAmount"
                       [ngModel]="donation() || null"
                       (ngModelChange)="setDonation($event)"
                       placeholder="0.00"
                       aria-describedby="teamDonationHelp">
              </div>
              <div id="teamDonationHelp" class="field-help donation-help">
                @if (donation() > 0) {
                  <i class="bi bi-check-circle-fill text-success me-1"></i>Thank you! Your
                  {{ donation() | currency }} gift is added to this payment@if (donationProcessing() > 0) { (plus {{ donationProcessing() | currency }} processing) }.
                } @else {
                  Your optional gift is charged in full with this payment.
                }
              </div>
            </div>
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

          <!-- ═══ VERTICAL INSURE / REGSAVER (CC-bearing methods only) ═══
               Per legacy: VI premium charged via the same CC the rep enters for TSIC,
               so the widget is visible only when a CC form is on screen (CC method
               or ARB-Trial with CC source). Widget targets #dVITeamOffer by id —
               do not introduce clipping/positioning ancestors between it and body. -->
          @if (showViSection()) {
            <div class="insurance-wrapper mb-4">
              <header class="insurance-card-title">
                <i class="bi bi-shield-check me-2"></i>Team Registration Insurance
              </header>
              @if (insuranceSvc.widgetError(); as viErr) {
                <!-- VI SDK reported an error (or the script never reached the
                     onError callback and we surfaced a synchronous throw). The
                     base service forces hasUserResponse=true on error so the
                     submit gate auto-unlocks; this banner just tells the rep
                     why the offer didn't appear. -->
                <div class="alert alert-warning border-0 mb-0 small" role="alert">
                  <strong>Insurance is unavailable for this session.</strong>
                  You can still complete your team payment.
                  <span class="text-muted">({{ viErr }})</span>
                </div>
              } @else {
                <div #viOffer id="dVITeamOffer" class="text-center vi-container">
                  @if (!insuranceSvc.widgetInitialized()) {
                    <div class="py-4">
                      <div class="spinner-border spinner-border-sm text-primary" role="status">
                        <span class="visually-hidden">Loading...</span>
                      </div>
                      <p class="text-muted mt-2 small mb-0">Getting Team Registration Insurance Quote...</p>
                    </div>
                  }
                </div>
                @if (insuranceSvc.widgetInitialized() && !insuranceSvc.hasUserResponse()) {
                  <div class="alert alert-secondary border-0 py-2 small mb-0 mt-2" role="alert">
                    Insurance is optional. Please indicate your interest in registration insurance for each team listed.
                  </div>
                }
              }
            </div>
          }

          <!-- ═══ CREDIT CARD FORM ═══ -->
          @if (isCc()) {
            <section class="payment-form-panel">
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
              {{ submitting() ? 'Processing...' : 'Pay ' + (ccPayTotal() | currency) + ' Now' }}
            </button>
            @if (viPayHintVisible()) {
              <div class="vi-pay-hint mt-2 small d-flex align-items-center gap-1" role="status">
                <i class="bi bi-arrow-up-short"></i>
                <span>Please respond to the insurance offer above to enable payment.</span>
              </div>
            }
          }

          <!-- ═══ BANK ACCOUNT (eCheck) FORM ═══ -->
          @if (isEcheck()) {
            <section class="payment-form-panel">
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
              {{ submitting() ? 'Processing...' : 'Pay ' + (echeckAmount() | currency) + ' by eCheck' }}
            </button>
          }

          <!-- ═══ ARB-TRIAL (Deposit tomorrow + Balance on configured date) ═══ -->
          @if (isArbTrial()) {
            <section class="arb-trial-panel payment-form-panel">
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
            @if (viPayHintVisible()) {
              <div class="vi-pay-hint mt-2 small d-flex align-items-center gap-1" role="status">
                <i class="bi bi-arrow-up-short"></i>
                <span>Please respond to the insurance offer above to enable payment.</span>
              </div>
            }

            <!-- Per-team results panel — shown only after a partial-success or all-failed submit. -->
            @if (arbTrialResult(); as r) {
              @if (!r.success) {
                <section class="arb-trial-results mt-3">
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
            <section class="check-instructions">
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

        <!-- VI charge confirmation modal — outside the balance/no-balance split
             so it renders for both bundled (CC submit) and standalone (VI-only) flows. -->
        @if (showViChargeConfirm()) {
          <app-vi-charge-confirm-modal
            [quotedPlayers]="viQuotedTeams()"
            [premiumTotal]="viPremiumTotal()"
            [email]="viCcEmail()"
            [viCcOnlyFlow]="false"
            (cancelled)="cancelViConfirm()"
            (confirmed)="confirmViAndContinue()" />
        }
      </div>
    </div>
  `,
    styles: [`
      :host { display: flex; flex-direction: column; gap: var(--space-4); }

      /* .step-card / .section-titlebar / .phase-badge live in
         styles/_wizard-globals.scss — shared with teams-step. */

      /* Insurance card — card chrome lives on the same div that holds
         #dVITeamOffer so we don't introduce any clipping/positioning ancestor
         between the widget mount and the page. NEVER add overflow:hidden — the
         VI widget renders absolutely-positioned popups/tooltips that must escape.
         Palette steps back from primary-blue to success-keyed neutral so the
         card sits inside the green-stripe step shell without competing. */
      .insurance-wrapper {
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        box-shadow: var(--shadow-sm);
        padding: 0 var(--space-4) var(--space-3);
      }

      .insurance-card-title {
        display: flex;
        align-items: center;
        margin: 0 calc(var(--space-4) * -1) var(--space-3);
        padding: var(--space-2) var(--space-3);
        background: color-mix(in srgb, var(--bs-success) 12%, transparent);
        color: var(--brand-text);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-bold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        border-top-left-radius: calc(var(--radius-md) - 1px);
        border-top-right-radius: calc(var(--radius-md) - 1px);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-success) 22%, transparent);

        i { color: var(--bs-success); }
      }

      .vi-container { min-height: 280px; }

      /* Validation message under a disabled Pay button that points back up to
         the insurance widget — same red form-validation convention as VI's own
         "You must either accept or decline." Spatial separation (top of card vs
         bottom) means the two reinforce rather than clash. */
      .vi-pay-hint {
        color: var(--bs-danger);
        font-weight: var(--font-weight-semibold);
      }
      .vi-pay-hint i { font-size: 1.1rem; }

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

        /* Hover keeps the primary cue (action affordance), but rest+active
           sit on the success-keyed palette so the page reads green-dominant. */
        &:hover { border-color: var(--bs-primary); }

        &.active {
          border-color: var(--bs-success);
          background: color-mix(in srgb, var(--bs-success) 10%, transparent);
          color: var(--bs-success);
          font-weight: var(--font-weight-semibold);
          box-shadow: 0 0 0 1px color-mix(in srgb, var(--bs-success) 18%, transparent);
        }
      }

      /* Shared chrome for the panel that wraps each payment-method form
         (CC / eCheck / ARB-Trial / Check). Replaces inline style attrs that
         were repeated on each section. */
      .payment-form-panel {
        background: var(--bs-secondary-bg);
        border: 1px solid var(--bs-border-color-translucent);
        border-radius: var(--radius-md);
        padding: var(--space-4);
        margin-bottom: var(--space-3);
      }

      .check-instructions {
        background: rgba(var(--bs-info-rgb), 0.04);
        border: 1px solid rgba(var(--bs-info-rgb), 0.15);
        border-radius: var(--radius-md);
        padding: var(--space-4);
        margin-bottom: var(--space-3);
      }

      /* .arb-trial-panel chrome is provided by .payment-form-panel; this
         class is kept as a marker for any future ARB-specific overrides. */

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
        border-radius: var(--radius-md);
        padding: var(--space-3);
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

      /* Optional donation — bordered + subtle primary wash so it reads as part of
         the charge. Heart accent (danger) is the recognizable donation cue. */
      .donation-block {
        padding: var(--space-3);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.25);
        border-radius: var(--radius-md);
        background: rgba(var(--bs-primary-rgb), 0.03);
      }
      .donation-label {
        display: flex;
        align-items: center;
        margin-bottom: var(--space-2);
        color: var(--bs-primary);
        font-weight: var(--font-weight-bold);
        font-size: var(--font-size-base);

        i { color: var(--bs-danger); }
      }
      .donation-optional {
        margin-left: var(--space-2);
        padding: 1px var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-normal);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--brand-text-muted);
        background: rgba(var(--bs-primary-rgb), 0.1);
        border-radius: var(--radius-sm);
      }
      .donation-input-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        max-width: 220px;
      }
      .donation-currency {
        font-size: var(--font-size-lg);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text-muted);
      }
      .donation-input {
        background-color: var(--neutral-0);
      }
      .donation-input:focus-visible {
        outline: none;
        box-shadow: var(--shadow-focus);
      }
      .donation-help {
        margin-top: var(--space-2);
      }

    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamPaymentStepV2Component implements AfterViewInit, OnDestroy {
    readonly viOfferElement = viewChild<ElementRef<HTMLDivElement>>('viOffer');

    readonly submitted = output<void>();
    readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    readonly insuranceState = inject(TeamInsuranceStateService);
    readonly insuranceSvc = inject(TeamInsuranceService);
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
    // Per-team CC/eCheck charge outcomes — set on a partial-success or all-failed submit so
    // the rep sees which teams charged and which declined (and why). Null until then.
    readonly chargeResult = signal<TeamPaymentResponseDto | null>(null);
    private lastIdemKey: string | null = null;

    // ── VI state ─────────────────────────────────────────────────────────
    readonly showViChargeConfirm = signal(false);
    private readonly insuranceOfferLoaded = signal(false);
    private viInitTimeout?: ReturnType<typeof setTimeout>;
    private viInitRetries = 0;
    /** Which submit path triggered the modal — needed so confirmViAndContinue routes back correctly. */
    private pendingViSubmitFlow: 'cc' | 'arbTrial' | 'viOnly' | null = null;
    /** True once we've restored the scroll position after the VI embed's first mount.
     *  Guards against re-scrolling on the remount that a payment-method toggle triggers
     *  (scheduleViWidgetSync → reset → re-init → widget re-ready). */
    private viScrollRestored = false;
    private readonly viScrollTimers: ReturnType<typeof setTimeout>[] = [];

    constructor() {
        // The VI embed steals scroll on mount (its iframe autofocuses), dropping the
        // rep partway down the Payment page. When the widget first reports ready,
        // restore the top — but only on the first arrival, never on a toggle-driven
        // remount (which would yank a scrolled-down rep back up).
        effect(() => {
            if (!this.insuranceSvc.widgetInitialized() || this.viScrollRestored) return;
            this.viScrollRestored = true;
            // Fire just after ready and once more shortly after, to land past the
            // embed's own delayed focus/scroll.
            this.viScrollTimers.push(setTimeout(() => scrollWizardToTop(), 50));
            this.viScrollTimers.push(setTimeout(() => scrollWizardToTop(), 300));
        });
    }

    readonly clubRepContact = computed(() => this.state.clubRepContact());
    readonly hasBalance = computed(() => this.state.teamPayment.hasBalance());
    readonly balanceDue = computed(() => this.state.teamPayment.balanceDue());
    readonly registeredTeams = computed(() => this.state.teamPayment.teams());
    /**
     * Cart phase derived PER-ROW from the teams' server-resolved fullPaymentRequired, NOT the
     * single job-level flag — a club-rep cart can span scopes that differ in phase. 'mixed' when
     * rows disagree. Drives the honest phase badge and the Balance-Due column visibility.
     */
    readonly cartPhase = computed<'deposit' | 'full' | 'mixed' | 'none'>(() => {
        const teams = this.registeredTeams();
        if (!teams.length) return 'none';
        const full = teams.filter(t => t.fullPaymentRequired).length;
        if (full === 0) return 'deposit';
        if (full === teams.length) return 'full';
        return 'mixed';
    });
    readonly phaseBadgeLabel = computed(() => {
        switch (this.cartPhase()) {
            case 'full': return 'Final Balance Due';
            case 'mixed': return 'Mixed';
            default: return 'Deposit Only';
        }
    });
    /** Show the Balance-Due column whenever ANY row is full-payment (its balance is active);
     *  each row's cell still renders its own additionalDue. An all-deposit cart hides it. */
    readonly showBalanceColumn = computed(() => this.cartPhase() === 'full' || this.cartPhase() === 'mixed');
    readonly procFeeHeaderLabel = computed(() => this.showBalanceColumn() ? 'ProcFee Due' : 'Proc Fee');
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
    readonly checkAmount = computed(() => this.state.teamPayment.totalCkOwed());
    // eCheck total (lower than CC by the proc-rate spread) — what the rep is shown AND
    // submits. Must equal the engine's debit, so it comes from the server-computed total.
    // Includes any optional donation at the eCheck rate (the server re-levies the same).
    readonly echeckAmount = computed(() => this.state.teamPayment.totalEkOwed() + this.state.teamPayment.donationEcheck());

    // ── Optional donation (CC / eCheck immediate charge only) ─────────────
    readonly donation = computed(() => this.state.teamPayment.donation());
    readonly donationProcessing = computed(() => this.state.teamPayment.donationProcessing());
    setDonation(v: number | string): void { this.state.teamPayment.setDonation(v); }
    /** CC charge total INCLUDING any donation — the CC button + submit + receipt use this. */
    readonly ccPayTotal = computed(() => this.balanceDue() + this.state.teamPayment.donationCc());
    /** Donation input shows only for the immediate-charge online methods (CC / eCheck). ARB-Trial
     *  (deposit/balance split) and mail-in check can't carry a charged-in-full gift in v1. */
    readonly showDonationInput = computed(() =>
        this.state.teamPayment.bIncludeTeamDonation()
        && this.hasBalance()
        && (this.isCc() || this.isEcheck())
    );
    readonly payTo = computed(() => this.state.teamPayment.payTo());
    readonly mailTo = computed(() => this.state.teamPayment.mailTo());
    readonly mailinPaymentWarning = computed(() => this.state.teamPayment.mailinPaymentWarning());

    /** VI is offered only when a CC form is on screen (CC method or ARB-Trial w/ CC source).
     *  VI premium is charged via the same CC the rep enters for TSIC, so we must
     *  not present the offer on eCheck or mail-in check methods.
     *
     *  Also requires an actual offer payload for this submission. The backend returns
     *  `Available=false` / data=null when there is nothing to insure — all teams free
     *  (the repo excludes FeeTotal=0 rows), within VI's 14-day cutoff, or no event date.
     *  Without the data check the section renders a perpetual "Getting Quote..." spinner
     *  the widget can never mount (the standalone section below already guards on data). */
    readonly showViSection = computed(() =>
        this.insuranceState.offerTeamRegSaver()
        && this.insuranceState.verticalInsureOffer().data !== null
        && this.hasBalance()
        && (this.isCc() || (this.isArbTrial() && this.arbTrialSource() === 'CC'))
    );

    /** Standalone VI surface for returning reps who are paid in full but still have
     *  uncovered teams within VI's 14-day window. Backend `BuildTeamOfferAsync`
     *  returns `Available=true` only when uncovered teams + 14-day window both hold,
     *  so checking `verticalInsureOffer().data` covers both conditions. */
    readonly showViStandaloneSection = computed(() =>
        !this.hasBalance()
        && this.insuranceState.offerTeamRegSaver()
        && this.insuranceState.verticalInsureOffer().data !== null
    );

    readonly viQuotedTeams = computed(() => this.insuranceSvc.quotedTeams());
    readonly viPremiumTotal = computed(() => this.insuranceSvc.premiumTotal());
    readonly viCcEmail = computed(() => this.clubRepContact()?.email ?? '');

    /** True when the VI widget is showing AND the rep has responded (chose teams or declined all). */
    private readonly viDecisionMade = computed(() =>
        !this.showViSection() || this.insuranceSvc.hasUserResponse()
    );

    /** Hint shown directly under the disabled Pay button when the CC form is valid
     *  but the rep hasn't responded to the insurance widget yet. The widget itself
     *  flags the missing response in red ("You must either accept or decline"); this
     *  hint exists so reps who have scrolled past the widget see WHY Pay is disabled
     *  at the place they're actually looking. */
    readonly viPayHintVisible = computed(() =>
        !this.viDecisionMade() && this.ccValid() && !this.submitting()
    );

    /** VI-only purchase gate (standalone section): widget responded with quotes,
     *  CC valid, not already submitting. Decline path uses the wizard footer instead. */
    readonly canSubmitViOnly = computed(() =>
        this.showViStandaloneSection()
        && this.insuranceSvc.hasUserResponse()
        && this.insuranceSvc.quotes().length > 0
        && this.ccValid()
        && !this.submitting()
    );

    /** Same idea as viPayHintVisible but for the standalone VI-only Purchase button. */
    readonly viOnlyPayHintVisible = computed(() =>
        this.showViStandaloneSection()
        && this.insuranceSvc.quotes().length > 0
        && this.ccValid()
        && !this.insuranceSvc.hasUserResponse()
        && !this.submitting()
    );

    readonly canSubmitCc = computed(() =>
        this.hasBalance() && this.ccValid() && !this.submitting() && this.viDecisionMade(),
    );

    readonly canSubmitEcheck = computed(() =>
        this.hasBalance() && this.bankAccountValid() && !this.submitting(),
    );

    /** ARB-Trial submit gate — needs balance + the form valid for the chosen sub-source.
     *  When the sub-source is CC and VI is offered, also require a VI response. */
    readonly canSubmitArbTrial = computed(() => {
        if (!this.hasBalance() || this.submitting()) return false;
        if (this.arbTrialSource() === 'CC') {
            return this.ccValid() && this.viDecisionMade();
        }
        return this.bankAccountValid();
    });

    selectMethod(method: 'CC' | 'Echeck' | 'ArbTrial' | 'Check'): void {
        this.state.teamPayment.selectPaymentMethod(method);
        this.lastError.set(null);
        this.arbTrialResult.set(null);
        this.scheduleViWidgetSync();
    }

    selectArbTrialSource(src: 'CC' | 'Echeck'): void {
        this.state.teamPayment.selectArbTrialSource(src);
        this.lastError.set(null);
        this.scheduleViWidgetSync();
    }

    /** Re-mount the VI widget when method/source changes flip showViSection().
     *  Full `reset()` (not just `resetWidgetInit`) so any quotes/response from a
     *  prior CC visit are cleared — otherwise flipping CC → eCheck → CC would
     *  re-trigger the charge-confirm modal with stale quotes the rep hasn't
     *  re-confirmed. The cached `verticalInsureOffer.data` lives on the state
     *  service and survives, so we don't re-fetch the offer on each toggle. */
    private scheduleViWidgetSync(): void {
        if (!this.insuranceState.offerTeamRegSaver()) return;
        clearTimeout(this.viInitTimeout);
        this.insuranceSvc.reset();
        this.viInitTimeout = setTimeout(() => this.tryInitViWidget(), 0);
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

    // ── Lifecycle ────────────────────────────────────────────────────────
    ngAfterViewInit(): void {
        this.state.teamPayment.resetDonation();
        if (this.insuranceState.offerTeamRegSaver()) {
            this.viInitTimeout = setTimeout(() => this.loadAndInitVi(), 0);
        }
    }

    ngOnDestroy(): void {
        clearTimeout(this.viInitTimeout);
        this.viScrollTimers.forEach(clearTimeout);
        this.insuranceSvc.reset();
    }

    // ── VI offer load + widget init ──────────────────────────────────────
    private async loadAndInitVi(): Promise<void> {
        if (!this.insuranceOfferLoaded()) {
            await this.insuranceSvc.fetchTeamInsuranceOffer();
            this.insuranceOfferLoaded.set(true);
        }
        this.tryInitViWidget();
    }

    /** Mount the widget on #dVITeamOffer when either VI section is visible.
     *  Retries with backoff when the DOM node hasn't rendered yet — happens on
     *  first paint and immediately after a method toggle that flips visibility. */
    private tryInitViWidget(): void {
        const offerData = this.insuranceState.verticalInsureOffer().data;
        if (!offerData) return;
        const visible = this.showViSection() || this.showViStandaloneSection();
        if (this.viOfferElement()?.nativeElement && visible) {
            this.viInitRetries = 0;
            this.insuranceSvc.initWidget('#dVITeamOffer', offerData as VIOfferData);
            return;
        }
        if (this.viInitRetries++ < 20) {
            this.viInitTimeout = setTimeout(() => this.tryInitViWidget(), 150);
        }
    }

    // ── VI charge-confirm modal handlers ─────────────────────────────────
    cancelViConfirm(): void {
        this.showViChargeConfirm.set(false);
        this.pendingViSubmitFlow = null;
        this.submitting.set(false);
    }

    confirmViAndContinue(): void {
        this.showViChargeConfirm.set(false);
        const flow = this.pendingViSubmitFlow;
        this.pendingViSubmitFlow = null;
        if (flow === 'cc') this.continueCcSubmit();
        else if (flow === 'arbTrial') this.continueArbTrialSubmit();
        else if (flow === 'viOnly') this.continueViOnlySubmit();
    }

    /** Standalone VI purchase entry — TSIC is already paid; we only charge VI. */
    submitViOnly(): void {
        if (this.submitting() || !this.canSubmitViOnly()) return;
        this.captureViDecision();
        if (this.insuranceState.verticalInsureConfirmed() && this.insuranceSvc.quotes().length > 0) {
            this.pendingViSubmitFlow = 'viOnly';
            this.showViChargeConfirm.set(true);
        }
    }

    private async continueViOnlySubmit(): Promise<void> {
        this.submitting.set(true);
        this.lastError.set(null);
        const ccInfo = this.buildCreditCardInfo();
        try {
            const result = await this.insuranceSvc.purchaseTeamInsurance(ccInfo);
            if (result.success && result.policies) {
                this.insuranceState.updatePolicyNumbers(result.policies);
                // Clear the offer so the standalone section hides — purchased teams
                // are now covered, no remaining uncovered teams to surface. Rep uses
                // the wizard footer's "Proceed to Review" (now enabled because policy
                // numbers are recorded, clearing the canContinue gate).
                this.insuranceState.setVerticalInsureOffer({ loading: false, data: null, error: null });
                this.insuranceSvc.reset();
                this.toast.show('Insurance purchased successfully', 'success', 3000);
            } else {
                this.toast.show(result.error || 'Insurance purchase failed.', 'danger', 4000);
            }
        } catch (e: unknown) {
            console.warn('[Team Payment] VI-only purchase threw', e);
            this.toast.show('Insurance purchase failed.', 'danger', 4000);
        } finally {
            this.submitting.set(false);
        }
    }

    /** Snapshot current widget state into the consent store. Quotes>0 ⇒ confirmed; 0 ⇒ declined. */
    private captureViDecision(): void {
        const widgetVisible = this.showViSection() || this.showViStandaloneSection();
        if (!widgetVisible || !this.insuranceSvc.hasUserResponse()) return;
        const quotes = this.insuranceSvc.quotes();
        if (quotes.length > 0) {
            this.insuranceState.confirmVerticalInsurePurchase(quotes as unknown as Record<string, unknown>[]);
        } else {
            this.insuranceState.declineVerticalInsurePurchase();
        }
    }

    /** Build a CreditCardInfo from the current CC form signal — shared by TSIC + VI. */
    private buildCreditCardInfo(): CreditCardInfo {
        const cc = this._creditCard();
        return {
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
                                next: meta => {
                                    this.state.applyTeamsMetadata(meta);
                                    this.refreshViAfterDiscount();
                                },
                            });
                    }
                },
            });
    }

    /** Re-fetch the VI offer (rebuilt server-side off the now-stamped discount) and remount the
     *  widget so it re-quotes at the corrected insurable amount. Conditional: only when VI is
     *  offered and not declined — a declined offer needs no fresh premium. When the rep HAD an
     *  active selection, the remount clears it, so warn them to re-confirm at the new price. */
    private refreshViAfterDiscount(): void {
        if (!this.insuranceState.offerTeamRegSaver()) return;
        const hadSelection = this.insuranceSvc.quotes().length > 0;
        // A discount that zeroes the registration leaves nothing to insure — the rebuilt offer
        // comes back unavailable (data=null). Clear any prior selection so a stale pre-discount
        // premium is never charged for a now-free registration — clearing _quotes makes the
        // insurance-charge path unreachable. Nothing to remount.
        if (!this.insuranceState.verticalInsureOffer().data) {
            this.insuranceSvc.reset();
            this.insuranceOfferLoaded.set(false);
            if (hadSelection) {
                this.toast.show('Your discount made this registration free — the insurance you selected no longer applies and will not be charged.', 'info', 7000);
            }
            return;
        }
        // Live widget state, captured BEFORE reset() wipes it: "declined" = responded with no quotes.
        const declined = this.insuranceSvc.hasUserResponse() && this.insuranceSvc.quotes().length === 0;
        if (declined) return;
        this.insuranceSvc.reset();
        this.insuranceOfferLoaded.set(false);
        this.viInitRetries = 0;
        clearTimeout(this.viInitTimeout);
        this.viInitTimeout = setTimeout(() => this.loadAndInitVi(), 0);
        if (hadSelection) {
            this.toast.show('Your discount changed the insurance price — please re-confirm your insurance selection below.', 'info', 6000);
        }
    }

    submit(): void {
        if (this.submitting() || !this.canSubmitCc()) return;

        // Capture the rep's VI decision from the widget. If they chose insurance,
        // pop the charge-confirm modal first; otherwise proceed straight to TSIC.
        this.captureViDecision();
        if (this.insuranceState.verticalInsureConfirmed() && this.insuranceSvc.quotes().length > 0) {
            this.pendingViSubmitFlow = 'cc';
            this.showViChargeConfirm.set(true);
            return;
        }
        this.continueCcSubmit();
    }

    private continueCcSubmit(): void {
        this.submitting.set(true);
        this.lastError.set(null);
        this.chargeResult.set(null);

        if (!this.lastIdemKey) {
            this.lastIdemKey = crypto?.randomUUID
                ? crypto.randomUUID()
                : (Date.now().toString(36) + Math.random().toString(36).slice(2));
        }

        const ccInfo = this.buildCreditCardInfo();
        const teamIds = this.state.teamPayment.teamIdsWithBalance();
        const request = {
            teamIds,
            totalAmount: this.ccPayTotal(),
            jobPath: this.state.jobPath(),
            creditCard: ccInfo,
            idempotencyKey: this.lastIdemKey,
            donation: this.state.teamPayment.donation() || undefined,
        };

        this.state.teamPayment.submitPayment(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: resp => {
                    if (resp?.success) {
                        this.lastIdemKey = null;
                        // TSIC payment succeeded — chain VI purchase if rep confirmed.
                        // Mirrors player ordering: registration is the source of truth;
                        // VI is best-effort. A failed VI charge does NOT roll back the
                        // already-successful registration payment.
                        this.chainViPurchaseAndAdvance(teamIds, ccInfo, {
                            transactionId: resp.transactionId || undefined,
                            amount: this.ccPayTotal(),
                            message: resp.message || 'Payment successful',
                        });
                    } else {
                        // Partial-success or all-failed: surface per-team results + refresh totals.
                        this.handlePartialOrFailedTeamCharge(resp, 'Payment failed.');
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
     * CC/eCheck submit that did NOT fully succeed (partial-success or all-failed). The backend
     * has already persisted the teams that charged; surface the per-team results and refresh the
     * ledger so the summary table + "Pay $X Now" button reflect the real remaining balance. Stays
     * on the payment step so the rep retries ONLY the declined team(s) against the correct total.
     */
    private handlePartialOrFailedTeamCharge(resp: TeamPaymentResponseDto | null, fallbackMsg: string): void {
        const msg = resp?.message || fallbackMsg;
        this.lastError.set(msg);
        this.toast.show(msg, 'danger', 6000);
        // Only surface the per-team panel when there are real charge outcomes — pre-charge
        // validation failures (AMOUNT_MISMATCH, NOTHING_DUE) carry an empty Teams list and are
        // already covered by the error banner above.
        this.chargeResult.set(resp && resp.teams && resp.teams.length > 0 ? resp : null);
        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: meta => { this.state.applyTeamsMetadata(meta); this.submitting.set(false); },
                error: () => this.submitting.set(false),
            });
    }

    /** After a successful TSIC charge: optionally purchase VI policies, stash
     *  whatever we have on lastPayment, then advance the wizard. VI purchase
     *  failure surfaces as a toast but never blocks the wizard advance. */
    private async chainViPurchaseAndAdvance(
        _teamIds: string[],
        ccInfo: CreditCardInfo,
        baseSummary: { transactionId?: string; amount: number; message: string; paymentMethod?: 'CC' | 'Echeck' | 'Check' },
    ): Promise<void> {
        let viPolicyNumbers: Record<string, string> | undefined;
        if (this.insuranceState.verticalInsureConfirmed() && this.insuranceSvc.quotes().length > 0) {
            try {
                // Service derives teamIds + quoteIds from its own quotes — keeps
                // the two arrays aligned (rep may have accepted coverage on a
                // subset of paid teams; widget quotes are the source of truth).
                const result = await this.insuranceSvc.purchaseTeamInsurance(ccInfo);
                if (result.success && result.policies) {
                    viPolicyNumbers = result.policies;
                    this.insuranceState.updatePolicyNumbers(result.policies);
                }
            } catch (e) {
                console.warn('[Team Payment] VI purchase chain threw', e);
            }
        }
        this.state.teamPaymentState.setLastPayment({ ...baseSummary, viPolicyNumbers });
        // Refresh the team ledger so a back-nav to Payment reflects the post-charge
        // balances (mirrors submitArbTrial). Failure still advances — the charge
        // succeeded; a stale ledger is recoverable, blocking the wizard is not.
        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: meta => {
                    this.state.applyTeamsMetadata(meta);
                    this.submitting.set(false);
                    this.submitted.emit();
                },
                error: () => {
                    this.submitting.set(false);
                    this.submitted.emit();
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
        this.chargeResult.set(null);

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
            totalAmount: this.echeckAmount(),
            bankAccount,
            donation: this.state.teamPayment.donation() || undefined,
        };

        this.state.teamPayment.submitEcheckPayment(request as unknown as Record<string, unknown>)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: resp => {
                    if (resp?.success) {
                        this.lastIdemKey = null;
                        this.state.teamPaymentState.setLastPayment({
                            transactionId: resp.transactionId || undefined,
                            amount: this.echeckAmount(),
                            message: resp.message || 'eCheck submitted — pending settlement',
                            paymentMethod: 'Echeck',
                        });
                        // Refresh ledger so back-nav to Payment reflects post-submit state.
                        this.teamReg.getTeamsMetadata()
                            .pipe(takeUntilDestroyed(this.destroyRef))
                            .subscribe({
                                next: meta => {
                                    this.state.applyTeamsMetadata(meta);
                                    this.submitting.set(false);
                                    this.submitted.emit();
                                },
                                error: () => {
                                    this.submitting.set(false);
                                    this.submitted.emit();
                                },
                            });
                    } else {
                        // Partial-success or all-failed: surface per-team results + refresh totals.
                        this.handlePartialOrFailedTeamCharge(resp, 'eCheck submission failed.');
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

        // VI is only available on the CC sub-source. Capture decision and gate
        // through the charge-confirm modal when the rep elected coverage.
        if (this.arbTrialSource() === 'CC') {
            this.captureViDecision();
            if (this.insuranceState.verticalInsureConfirmed() && this.insuranceSvc.quotes().length > 0) {
                this.pendingViSubmitFlow = 'arbTrial';
                this.showViChargeConfirm.set(true);
                return;
            }
        }
        this.continueArbTrialSubmit();
    }

    private continueArbTrialSubmit(): void {
        this.submitting.set(true);
        this.lastError.set(null);
        this.arbTrialResult.set(null);

        const teamIds = this.state.teamPayment.teamIdsWithBalance();
        let creditCard: CreditCardInfo | null = null;
        let bankAccount: BankAccountInfo | null = null;

        if (this.arbTrialSource() === 'CC') {
            creditCard = this.buildCreditCardInfo();
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
                    this.arbTrialResult.set(resp);
                    if (resp.success) {
                        const baseMessage = resp.message ?? (resp.mode === 'FALLBACK_FULL_CHARGE'
                            ? 'Payment processed (fallback full charge — balance date had passed)'
                            : 'Payment plan scheduled — deposit charges tomorrow');
                        const paymentMethod: 'CC' | 'Echeck' = this.arbTrialSource() === 'Echeck' ? 'Echeck' : 'CC';

                        // Chain VI on success when the sub-source is CC and the rep confirmed.
                        // Refresh metadata afterward so AdnSubscription* lands on the team rows.
                        const finishAndAdvance = (viPolicyNumbers?: Record<string, string>) => {
                            this.state.teamPaymentState.setLastPayment({
                                amount: this.balanceDue(),
                                message: baseMessage,
                                paymentMethod,
                                viPolicyNumbers,
                            });
                            this.teamReg.getTeamsMetadata()
                                .pipe(takeUntilDestroyed(this.destroyRef))
                                .subscribe({
                                    next: meta => {
                                        this.state.applyTeamsMetadata(meta);
                                        this.submitting.set(false);
                                        this.submitted.emit();
                                    },
                                    error: () => {
                                        this.submitting.set(false);
                                        this.submitted.emit();
                                    },
                                });
                        };

                        if (
                            this.arbTrialSource() === 'CC'
                            && creditCard
                            && this.insuranceState.verticalInsureConfirmed()
                            && this.insuranceSvc.quotes().length > 0
                        ) {
                            // Service derives teamIds + quoteIds from its own
                            // quotes (rep may have insured a subset of paid teams).
                            this.insuranceSvc.purchaseTeamInsurance(creditCard)
                                .then(result => {
                                    if (result.success && result.policies) {
                                        this.insuranceState.updatePolicyNumbers(result.policies);
                                        finishAndAdvance(result.policies);
                                    } else {
                                        finishAndAdvance();
                                    }
                                })
                                .catch(e => {
                                    console.warn('[Team Payment] VI purchase chain (ARB-Trial) threw', e);
                                    finishAndAdvance();
                                });
                        } else {
                            finishAndAdvance();
                        }
                    } else {
                        this.submitting.set(false);
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
