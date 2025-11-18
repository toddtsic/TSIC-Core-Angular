import { Component, EventEmitter, Output, computed, inject, AfterViewInit, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { RegistrationWizardService } from '../registration-wizard.service';
import { ViConfirmModalComponent } from '../verticalinsure/vi-confirm-modal.component';
import type { VIPlayerObjectResponse } from '../../../core/api/models/VIPlayerObjectResponse';
import type { PaymentResponseDto } from '../../../core/api/models/PaymentResponseDto';
import { environment } from '../../../../environments/environment';
import type { Loadable } from '../../../core/models/state.models';
import { TeamService } from '../team.service';
import type { ApplyDiscountRequestDto } from '../../../core/api/models/ApplyDiscountRequestDto';
import type { ApplyDiscountResponseDto } from '../../../core/api/models/ApplyDiscountResponseDto';
import type { ApplyDiscountItemDto } from '../../../core/api/models/ApplyDiscountItemDto';
import type { InsurancePurchaseRequestDto } from '../../../core/api/models/InsurancePurchaseRequestDto';
import type { InsurancePurchaseResponseDto } from '../../../core/api/models/InsurancePurchaseResponseDto';
import { ToastService } from '../../../shared/toast.service';

declare global {
  // Allow TypeScript to acknowledge the VerticalInsure constructor on window
  interface Window { VerticalInsure?: any; }
}

interface LineItem {
  playerId: string;
  playerName: string;
  teamName: string;
  amount: number;
}

// Local aliases to make types obvious without duplicating backend DTOs
// Types now come from shared core models (see import above).

@Component({
  selector: 'app-rw-payment',
  standalone: true,
  imports: [CommonModule, FormsModule, ViConfirmModalComponent],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Payment</h5>
      </div>
      <div class="card-body">
        <!-- VerticalInsure Modal Host -->
        @if (state.showVerticalInsureModal()) {
          <app-vi-confirm-modal
            [quotes]="quotes"
            [ready]="viHasUserResponse"
            [error]="verticalInsureError"
            (confirmed)="onViConfirmed($event)"
            (declined)="onViDeclined()"
            (closed)="onViClosed()" />
        }
        <!-- RegSaver / VerticalInsure offer region (simple always-present container) -->
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
        <div id="dVIOffer" class="pb-3 text-center">
        </div>
        <div id="divVIPayment" class="text-center" style="display:none;">
            <button type="button" id="btnVIPayment" onclick="submitVIPayment()">Submit Payment</button>
        </div>
        @if (state.offerPlayerRegSaver() && state.hasVerticalInsureDecision()) {
          <!-- VerticalInsure decision status (no standalone review/change button) -->
          <div class="mt-2 d-flex flex-column gap-2">
            <div class="alert" [ngClass]="state.verticalInsureConfirmed() ? 'alert-success' : 'alert-secondary'" role="status">
              <div class="d-flex align-items-center gap-2">
                <span class="badge" [ngClass]="state.verticalInsureConfirmed() ? 'bg-success' : 'bg-secondary'">RegSaver</span>
                <div>
                  @if (state.verticalInsureConfirmed()) {
                    <div class="fw-semibold mb-0">Insurance Selected</div>
                    <div class="small text-muted" *ngIf="state.viConsent()?.policyNumber">Policy #: {{ state.viConsent()?.policyNumber }}</div>
                  } @else {
                    <div class="fw-semibold mb-0">Insurance Declined</div>
                    <div class="small text-muted">You chose not to purchase coverage.</div>
                  }
                </div>
              </div>
            </div>
          </div>
        }

        <!-- RegSaver charge confirmation modal (Bootstrap-style) -->
        @if (showViChargeConfirm) {
          <div class="modal fade show d-block" tabindex="-1" role="dialog" style="background: rgba(0,0,0,0.5)">
            <div class="modal-dialog" role="document">
              <div class="modal-content">
                <div class="modal-header">
                  <h5 class="modal-title">Confirm Registration Insurance Purchase</h5>
                  <button type="button" class="btn-close" aria-label="Close" (click)="cancelViConfirm()"></button>
                </div>
                <div class="modal-body">
                  <p>The premium(s) for {{ viQuotedPlayers().join(', ') }} will be charged by <strong>VERTICAL INSURANCE</strong> and not by <strong>TEAMSPORTSINFO.COM</strong>.</p>
                  <p>The credit card info here will be passed to <strong>VERTICAL INSURANCE</strong> for them to process.</p>
                  <p>You will receive an email at <strong>{{ viCcEmail() }}</strong> from <strong>VERTICAL INSURANCE</strong> immediately upon processing.</p>
                  <p class="mb-0"><strong>Total Insurance Premium:</strong> {{ viPremiumTotal() | currency }}</p>
                </div>
                <div class="modal-footer">
                  <button type="button" class="btn btn-secondary" (click)="cancelViConfirm()">CANCEL</button>
                  <button type="button" class="btn btn-primary" (click)="confirmViAndContinue()">OK</button>
                </div>
              </div>
            </div>
          </div>
        }        

        <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="pay-summary-title" style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
          <h6 id="pay-summary-title" class="fw-semibold mb-2">Payment Summary</h6>
          <table class="table table-sm mb-0">
            <thead>
              <tr>
                <th>Player</th>
                <th>Team</th>
                @if (isArbScenario()) {
                  <th>Per Interval</th>
                  <th>Total</th>
                } @else if (isDepositScenario()) {
                  <th>Deposit</th>
                  <th>Pay In Full</th>
                } @else {
                  <th>Amount</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (item of lineItems(); track item.playerId) {
                <tr>
                  <td>{{ item.playerName }}</td>
                  <td>{{ item.teamName }}</td>
                  @if (isArbScenario()) {
                    <td>{{ (item.amount / arbOccurrences()) | currency }}</td>
                    <td>{{ item.amount | currency }}</td>
                  } @else if (isDepositScenario()) {
                    <td>{{ getDepositForPlayer(item.playerId) | currency }}</td>
                    <td>{{ item.amount | currency }}</td>
                  } @else {
                    <td>{{ item.amount | currency }}</td>
                  }
                </tr>
              }
            </tbody>
            <tfoot>
              @if (isArbScenario()) {
                <tr>
                  <th colspan="2" class="text-end">Per Interval Total</th>
                  <th>{{ arbPerOccurrence() | currency }}</th>
                  <th class="text-muted small">(of {{ totalAmount() | currency }})</th>
                </tr>
              } @else if (isDepositScenario()) {
                <tr>
                  <th colspan="2" class="text-end">Deposit Total</th>
                  <th>{{ depositTotal() | currency }}</th>
                  <th class="text-muted small">Pay In Full: {{ totalAmount() | currency }}</th>
                </tr>
              } @else {
                <tr>
                  <th colspan="2" class="text-end">Subtotal</th>
                  <th>{{ totalAmount() | currency }}</th>
                </tr>
                @if (appliedDiscount > 0) {
                  <tr>
                    <th colspan="2" class="text-end">Discount</th>
                    <th>-{{ appliedDiscount | currency }}</th>
                  </tr>
                }
                <tr>
                  <th colspan="2" class="text-end">Due Now</th>
                  <th>{{ currentTotal() | currency }}</th>
                </tr>
              }
            </tfoot>
          </table>
        </section>
        <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="pay-option-title" style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
          <h6 id="pay-option-title" class="fw-semibold mb-3">Payment Option</h6>
          <div class="mb-3">
            <label for="discountCode" class="form-label fw-semibold me-2 d-block d-md-inline">Discount Code</label>
            <div class="input-group input-group-sm w-auto d-inline-flex align-items-center">
              <input id="discountCode" type="text" [(ngModel)]="discountCode" name="discountCode" class="form-control" placeholder="Enter code" [disabled]="discountApplying" style="min-width: 180px;" />
              <button type="button" class="btn btn-outline-primary" (click)="applyDiscount()" [disabled]="discountApplying || !discountCode">Apply</button>
            </div>
            @if (discountMessage) {
              <div class="form-text mt-1" [class.text-success]="appliedDiscount > 0" [class.text-danger]="appliedDiscount === 0">{{ discountMessage }}</div>
            }
          </div>
          @if (isArbScenario()) {
            <div class="form-check">
              <input class="form-check-input" type="radio" id="arb" name="paymentOption" [checked]="state.paymentOption() === 'ARB'" (change)="chooseOption('ARB')">
              <label class="form-check-label" for="arb">
                Automated Recurring Billing (ARB)
                <div class="small text-muted">
                  {{ arbOccurrences() }} payments of {{ arbPerOccurrence() | currency }} every {{ arbIntervalLength() }} month(s) starting {{ arbStartDate() | date:'mediumDate' }}
                </div>
              </label>
            </div>
            <div class="form-check">
              <input class="form-check-input" type="radio" id="pifArb" name="paymentOption" [checked]="state.paymentOption() === 'PIF'" (change)="chooseOption('PIF')">
              <label class="form-check-label" for="pifArb">Pay In Full - {{ totalAmount() | currency }}</label>
            </div>
          } @else if (isDepositScenario()) {
            <div class="form-check">
              <input class="form-check-input" type="radio" id="deposit" name="paymentOption" [checked]="state.paymentOption() === 'Deposit'" (change)="chooseOption('Deposit')">
              <label class="form-check-label" for="deposit">Deposit Only - {{ depositTotal() | currency }}</label>
            </div>
            <div class="form-check">
              <input class="form-check-input" type="radio" id="pifDep" name="paymentOption" [checked]="state.paymentOption() === 'PIF'" (change)="chooseOption('PIF')">
              <label class="form-check-label" for="pifDep">Pay In Full - {{ totalAmount() | currency }}</label>
            </div>
          } @else {
            <div class="form-check">
              <input class="form-check-input" type="radio" id="pifOnly" name="paymentOption" checked (change)="$event.preventDefault()">
              <label class="form-check-label" for="pifOnly">Pay In Full - {{ totalAmount() | currency }}</label>
            </div>
          }
        </section>

        <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="cc-title" style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
          <h6 id="cc-title" class="fw-semibold mb-2">Credit Card Information</h6>
          <div class="row g-2">
            <div class="col-md-6">
              <label for="ccNumber" class="form-label">Card Number</label>
              <input type="text" class="form-control" id="ccNumber" [(ngModel)]="creditCard.number" name="ccNumber" placeholder="1234567890123456">
            </div>
            <div class="col-md-3">
              <label for="ccExpiry" class="form-label">Expiry (MMYY)</label>
              <input type="text" class="form-control" id="ccExpiry" [(ngModel)]="creditCard.expiry" name="ccExpiry" placeholder="1225">
            </div>
            <div class="col-md-3">
              <label for="ccCode" class="form-label">CVV</label>
              <input type="text" class="form-control" id="ccCode" [(ngModel)]="creditCard.code" name="ccCode" placeholder="123">
            </div>
          </div>
          <div class="row g-2 mt-2">
            <div class="col-md-6">
              <label for="ccFirstName" class="form-label">First Name</label>
              <input type="text" class="form-control" id="ccFirstName" [(ngModel)]="creditCard.firstName" name="ccFirstName">
            </div>
            <div class="col-md-6">
              <label for="ccLastName" class="form-label">Last Name</label>
              <input type="text" class="form-control" id="ccLastName" [(ngModel)]="creditCard.lastName" name="ccLastName">
            </div>
          </div>
          <div class="row g-2 mt-2">
            <div class="col-md-8">
              <label for="ccAddress" class="form-label">Address</label>
              <input type="text" class="form-control" id="ccAddress" [(ngModel)]="creditCard.address" name="ccAddress">
            </div>
            <div class="col-md-4">
              <label for="ccZip" class="form-label">Zip Code</label>
              <input type="text" class="form-control" id="ccZip" [(ngModel)]="creditCard.zip" name="ccZip">
            </div>
          </div>
        </section>
          <button type="button" class="btn btn-primary" (click)="submit()" [disabled]="!canSubmit()">Pay Now</button>
        </div>
      </div>
    </div>
  `
})
export class PaymentComponent implements AfterViewInit {
  @Output() back = new EventEmitter<void>();
  @Output() submitted = new EventEmitter<void>();

  readonly teamService = inject(TeamService);

  creditCard = {
    number: '',
    expiry: '',
    code: '',
    firstName: '',
    lastName: '',
    address: '',
    zip: ''
  };

  constructor(public state: RegistrationWizardService, private readonly http: HttpClient, private readonly toast: ToastService) { }

  // VerticalInsure integration state
  private verticalInsureInstance: any;
  quotes: any[] = [];
  viHasUserResponse: boolean = false;
  private userChangedOption = false;
  submitting = false;
  private lastIdemKey: string | null = null;
  verticalInsureError: string | null = null;
  // Discount UI state
  discountCode: string = '';
  discountApplying: boolean = false;
  appliedDiscount: number = 0;
  discountMessage: string | null = null;
  // VI confirmation modal state
  showViChargeConfirm = false;
  private pendingSubmitAfterViConfirm = false;
  private idemStorageKey(): string {
    return `tsic:payidem:${this.state.jobId()}:${this.state.familyUser()?.familyUserId}`;
  }
  private loadStoredIdem(): void {
    try {
      const k = localStorage.getItem(this.idemStorageKey());
      if (k) {
        this.lastIdemKey = k;
      }
    } catch { /* ignore storage errors */ }
  }
  private persistIdem(key: string): void {
    try { localStorage.setItem(this.idemStorageKey(), key); } catch { /* ignore */ }
  }
  private clearStoredIdem(): void {
    try { localStorage.removeItem(this.idemStorageKey()); } catch { /* ignore */ }
  }
  // Legacy checkbox removed; consent now tracked via wizard service signals.
  viConfirmChecked = false; // retained for backward compatibility (unused gating replaced)


  ngAfterViewInit(): void {
    // Attempt to hydrate any existing idempotency key (previous attempt that failed or was retried)
    this.loadStoredIdem();
    // Simpler: one-shot hydrate + short delayed retry (in case data arrives slightly later)
    this.simpleHydrateFromCc(this.state.familyUser()?.ccInfo);
    setTimeout(() => this.simpleHydrateFromCc(this.state.familyUser()?.ccInfo), 300);
    this.tryInitVerticalInsure();
    // Auto-select within the scenario unless user has chosen manually
    effect(() => {
      if (this.userChangedOption) return;
      const opts: Array<'ARB' | 'Deposit' | 'PIF'> = [];
      if (this.isArbScenario()) {
        opts.push('ARB', 'PIF');
      } else if (this.isDepositScenario()) {
        opts.push('Deposit', 'PIF');
      } else {
        opts.push('PIF');
      }
      const current = this.state.paymentOption();
      if (!opts.includes(current as any)) {
        this.state.paymentOption.set(opts[0]);
      }
    });
  }
  chooseOption(opt: 'PIF' | 'Deposit' | 'ARB') {
    this.userChangedOption = true;
    this.state.paymentOption.set(opt);
    // Reset previously applied discount when payment option changes to avoid stale math
    this.appliedDiscount = 0;
    this.discountMessage = null;
  }

  private tryInitVerticalInsure(force: boolean = false): void {
    // Minimal gating: require a response object present
    const offerEnabled: boolean = this.state.offerPlayerRegSaver();
    const offer: Loadable<VIPlayerObjectResponse> = this.state.verticalInsureOffer();
    const offerObj: VIPlayerObjectResponse | null = offer?.data ?? null;

    if (!offerEnabled || !offerObj) { return };

    this.verticalInsureInstance = new (globalThis as any).VerticalInsure(
      '#dVIOffer',
      offerObj,
      (offerState: any) => {

        this.verticalInsureInstance.validate()
          .then((isValid: boolean) => {
            this.viHasUserResponse = isValid;

            // save quotes in global variable, will never be null hereafter, will be empty array [] if no quotes
            this.quotes = offerState?.quotes;

            console.log('viHasUserResponse:', this.viHasUserResponse, ' quotes:', this.quotes, 'isValid:', isValid);
            this.verticalInsureError = null;
          });
      },
      () => {
        this.verticalInsureInstance.validate()
          .then((isValid: boolean) => {
            this.viHasUserResponse = isValid;

            // a failed quotes requests returns isValid TRUE, a successful Request returns false (seems paradoxical, but necessary for prompting for user interaction on payment)
            // if (viHasUserResponse){
            //     $("#dVIOffer").hide();
            // } else {
            //     $("#dVIOffer").show();
            // }
            console.log('offer ready, isValid:', this.viHasUserResponse);
          });
      }
    );
  }


  lineItems = computed(() => {
    const items: LineItem[] = [];
    const selectedPlayers = this.state.familyPlayers()
      .filter(p => p.selected || p.registered)
      .map(p => ({ userId: p.playerId, name: `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim() }));
    const selectedTeams = this.state.selectedTeams();

    for (const player of selectedPlayers) {
      const teamId = selectedTeams[player.userId];
      if (typeof teamId === 'string') {
        const team = this.teamService.getTeamById(teamId);
        if (team) {
          const amount = this.getAmountForTeam(team);
          items.push({
            playerId: player.userId,
            playerName: player.name,
            teamName: team.teamName,
            amount
          });
        }
      }
    }
    return items;
  });

  totalAmount = computed(() => {
    return this.lineItems().reduce((sum, item) => sum + item.amount, 0);
  });

  depositTotal = computed(() => {
    // Sum per-registrant deposit for each selected player's team
    const selectedPlayers = this.state.familyPlayers()
      .filter(p => p.selected || p.registered)
      .map(p => p.playerId);
    let sum = 0;
    const map = this.state.selectedTeams();
    for (const pid of selectedPlayers) {
      const teamId = map[pid];
      if (typeof teamId === 'string') {
        const team = this.teamService.getTeamById(teamId);
        const dep = Number(team?.perRegistrantDeposit ?? 0);
        sum += Number.isNaN(dep) ? 0 : dep;
      }
    }
    return sum;
  });

  // Scenario helpers per new strategy
  isArbScenario = computed(() => !!this.state.adnArb());
  isDepositScenario = computed(() => {
    if (this.isArbScenario()) return false;
    const selectedPlayers = this.state.familyPlayers()
      .filter(p => p.selected || p.registered)
      .map(p => p.playerId);
    if (selectedPlayers.length === 0) return false;
    const map = this.state.selectedTeams();
    for (const pid of selectedPlayers) {
      const teamId = map[pid];
      if (typeof teamId !== 'string') return false;
      const team = this.teamService.getTeamById(teamId);
      const dep = Number(team?.perRegistrantDeposit ?? 0);
      const fee = Number(team?.perRegistrantFee ?? 0);
      if (!(dep > 0 && fee > 0)) return false;
    }
    return true;
  });

  currentTotal = computed(() => {
    const option = this.state.paymentOption();
    const base = option === 'Deposit' ? this.depositTotal() : this.totalAmount();
    const adjusted = Math.max(0, base - (this.appliedDiscount || 0));
    return adjusted;
  });

  canSubmit = computed(() => {
    const baseOk = this.lineItems().length > 0 && this.currentTotal() > 0 && !this.submitting;
    return baseOk;
  });

  private getAmountForTeam(team: any): number {
    // Use perRegistrantFee or fallback
    const v = Number(team?.perRegistrantFee ?? 0);
    return Number.isNaN(v) || v <= 0 ? 100 : v; // default fallback
  }

  // ARB helpers for schedule messaging
  arbOccurrences = computed(() => {
    return this.state.adnArbBillingOccurences() || 10;
  });
  arbIntervalLength = computed(() => {
    return this.state.adnArbIntervalLength() || 1;
  });
  arbStartDate = computed(() => {
    const raw = this.state.adnArbStartDate();
    const d = raw ? new Date(raw) : new Date(Date.now() + 24 * 60 * 60 * 1000);
    return d;
  });
  arbPerOccurrence = computed(() => {
    const occ = this.arbOccurrences();
    const total = this.totalAmount();
    return occ > 0 ? Math.round((total / occ) * 100) / 100 : total;
  });

  // shouldShowPif removed: PIF visibility now determined solely by scenarios

  submit(): void {
    if (this.submitting) return;
    // Gate: if RegSaver is offered but no user response yet, require a decision before continuing.
    if (this.state.offerPlayerRegSaver()) {
      const noResponse = !this.viHasUserResponse && this.isViOfferVisible();
      if (noResponse) {
        this.toast.show('Please indicate your interest in registration insurance for each player listed.', 'danger', 4000);
        return;
      }
    }
    // If VI quotes exist (insurance selected), show charge confirmation modal before proceeding.
    if (this.state.offerPlayerRegSaver() && Array.isArray(this.quotes) && this.quotes.length > 0) {
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
    const rs = this.state.regSaverDetails();
    const request = {
      jobId: this.state.jobId(),
      familyUserId: this.state.familyUser()?.familyUserId,
      paymentOption: this.state.paymentOption(),
      creditCard: this.creditCard,
      idempotencyKey: this.lastIdemKey,
      viConfirmed: this.state.offerPlayerRegSaver() ? this.state.verticalInsureConfirmed() : undefined,
      viDeclined: this.state.offerPlayerRegSaver() ? this.state.verticalInsureDeclined() : undefined,
      viPolicyNumber: (this.state.verticalInsureConfirmed() ? (rs?.policyNumber || this.state.viConsent()?.policyNumber) : undefined) || undefined,
      viPolicyCreateDate: (this.state.verticalInsureConfirmed() ? (rs?.policyCreateDate || this.state.viConsent()?.policyCreateDate) : undefined) || undefined
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
            this.state.lastPayment.set({
              option: this.state.paymentOption(),
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
          if (this.state.offerPlayerRegSaver() && Array.isArray(this.quotes) && this.quotes.length > 0) {
            this.purchaseRegsaverInsurance();
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
    this.state.openVerticalInsureModal();
  }

  onViConfirmed(evt: { policyNumber: string | null; policyCreateDate: string | null; quotes: any[] }): void {
    this.state.confirmVerticalInsurePurchase(evt.policyNumber, evt.policyCreateDate, evt.quotes);
  }
  onViDeclined(): void { this.state.declineVerticalInsurePurchase(); }
  onViClosed(): void { this.state.showVerticalInsureModal.set(false); }

  applyDiscount(): void {
    if (!this.discountCode || this.discountApplying) return;
    // Build per-line due-now amounts so server can compute fairly
    const option = this.state.paymentOption();
    const items: ApplyDiscountItemDto[] = this.lineItems().map(li => ({
      playerId: li.playerId,
      amount: option === 'Deposit' ? this.getDepositForPlayer(li.playerId) : li.amount
    }));
    this.discountApplying = true;
    this.discountMessage = null;
    const req: ApplyDiscountRequestDto = {
      jobId: this.state.jobId(),
      familyUserId: this.state.familyUser()?.familyUserId!,
      code: this.discountCode,
      items
    };
    this.http.post<ApplyDiscountResponseDto>(
      `${environment.apiUrl}/registration/apply-discount`,
      req
    ).subscribe({
      next: resp => {
        this.discountApplying = false;
        const total = resp?.totalDiscount ?? 0;
        if (resp?.success && total > 0) {
          this.appliedDiscount = Math.round((total + Number.EPSILON) * 100) / 100;
          this.discountMessage = `Discount applied: ${this.appliedDiscount.toLocaleString(undefined, { style: 'currency', currency: 'USD' })}`;
        } else {
          this.appliedDiscount = 0;
          this.discountMessage = resp?.message || 'Invalid or ineligible discount code';
        }
      },
      error: err => {
        this.discountApplying = false;
        this.appliedDiscount = 0;
        this.discountMessage = (err?.error?.message || err?.message || 'Failed to apply code');
      }
    });
  }

  getDepositForPlayer(playerId: string): number {
    const teamId = this.state.selectedTeams()[playerId];
    if (typeof teamId === 'string') {
      const team = this.teamService.getTeamById(teamId);
      return Number(team?.perRegistrantDeposit ?? 0) || 0;
    }
    return 0;
  }

  // --- VI Confirmation helpers ---
  viQuotedPlayers(): string[] {
    if (!Array.isArray(this.quotes)) return [];
    const names: string[] = [];
    for (const q of this.quotes) {
      const fn = q?.policy_attributes?.participant?.first_name?.trim?.() || '';
      const ln = q?.policy_attributes?.participant?.last_name?.trim?.() || '';
      const name = `${fn} ${ln}`.trim();
      if (name) names.push(name + (q?.total ? ` ($${(Number(q.total) / 100).toFixed(2)})` : ''));
    }
    return names;
  }
  viPremiumTotal(): number {
    if (!Array.isArray(this.quotes)) return 0;
    let cents = 0;
    for (const q of this.quotes) cents += Number(q?.total || 0);
    return Math.round((cents / 100) * 100) / 100;
  }
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
      this.continueSubmit();
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

  private purchaseRegsaverInsurance(): void {
    try {
      const quoteIds: string[] = Array.isArray(this.quotes)
        ? this.quotes.map((q: any) => String(q?.id ?? q?.quote_id ?? '')).filter((s: string) => !!s)
        : [];
      const registrationIds: string[] = Array.isArray(this.quotes)
        ? this.quotes.map((q: any) => String(q?.metadata?.TsicRegistrationId ?? q?.metadata?.tsicRegistrationId ?? '')).filter((s: string) => !!s)
        : [];
      if (quoteIds.length === 0) return; // nothing to purchase
      const req: InsurancePurchaseRequestDto = {
        jobId: this.state.jobId(),
        familyUserId: this.state.familyUser()?.familyUserId!,
        registrationIds,
        quoteIds
      };
      this.http.post<InsurancePurchaseResponseDto>(`${environment.apiUrl}/insurance/purchase`, req)
        .subscribe({
          next: (resp) => {
            if (resp?.success) {
              this.toast.show('Processing with Vertical Insurance was SUCCESSFUL', 'success', 3000);
            } else {
              console.warn('RegSaver purchase failed:', (resp as any)?.error || resp);
            }
          },
          error: (err) => {
            console.warn('RegSaver purchase error:', err?.error?.message || err?.message || err);
            this.toast.show('Processing with Vertical Insurance failed', 'danger', 4000);
          }
        });
    } catch (e) {
      console.warn('RegSaver purchase threw exception', e);
    }
  }

  private isViOfferVisible(): boolean {
    try {
      const el = document.getElementById('dVIOffer');
      if (!el) return false;
      const style = getComputedStyle(el);
      const hasSize = (el.offsetWidth + el.offsetHeight) > 0;
      return style.display !== 'none' && style.visibility !== 'hidden' && hasSize;
    } catch { return false; }
  }
}
