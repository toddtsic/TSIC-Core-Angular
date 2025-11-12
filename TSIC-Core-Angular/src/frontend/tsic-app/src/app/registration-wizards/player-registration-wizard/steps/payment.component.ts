import { Component, EventEmitter, Output, computed, inject, AfterViewInit, OnChanges, DoCheck } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RegistrationWizardService } from '../registration-wizard.service';
import { TeamService } from '../team.service';

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

@Component({
  selector: 'app-rw-payment',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Payment</h5>
      </div>
      <div class="card-body">
        <!-- RegSaver / VerticalInsure offer region (hidden when no offer object and no policy) -->
        <div class="mb-3" id="vi-offer-region" *ngIf="showInsuranceRegion()">
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
          } @else if (state.offerPlayerRegSaver() && viOffer().loading) {
            <div class="alert alert-secondary border-0" role="status">
              <div class="d-flex align-items-center gap-2">
                <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
                <span class="small">Loading insurance offer…</span>
              </div>
            </div>
          } @else if (state.offerPlayerRegSaver() && viOffer().error) {
            <div class="alert alert-danger border-0" role="alert">
              Failed to load insurance offer. <button class="btn btn-sm btn-outline-light ms-2" type="button" (click)="reloadVi()">Retry</button>
            </div>
          } @else if (state.offerPlayerRegSaver() && viProductCount() > 0) {
            <div class="border rounded p-3 bg-light-subtle">
              <div class="fw-semibold mb-1">Optional Registration Insurance</div>
              <div class="small text-muted mb-2">Protect your registration investment. Select quotes below (powered by VerticalInsure).</div>
              <!-- Container target for VerticalInsure dynamic widget -->
              <!-- VerticalInsure mounts itself into this container -->
              <div id="dVIOffer" class="vi-offer"></div>
            </div>
          }
        </div>

        <div class="mb-3">
          <label class="form-label fw-semibold">Payment Option</label>
          <div class="form-check">
            <input class="form-check-input" type="radio" id="pif" name="paymentOption" [checked]="state.paymentOption() === 'PIF'" (change)="state.paymentOption.set('PIF')">
            <label class="form-check-label" for="pif">
              Pay in Full (PIF) - {{ totalAmount() | currency }}
            </label>
          </div>
          <div class="form-check">
            <input class="form-check-input" type="radio" id="deposit" name="paymentOption" [checked]="state.paymentOption() === 'Deposit'" (change)="state.paymentOption.set('Deposit')">
            <label class="form-check-label" for="deposit">
              Deposit Only - {{ depositTotal() | currency }}
            </label>
          </div>
          <div class="form-check">
            <input class="form-check-input" type="radio" id="arb" name="paymentOption" [checked]="state.paymentOption() === 'ARB'" (change)="state.paymentOption.set('ARB')">
            <label class="form-check-label" for="arb">
              Automated Recurring Billing (ARB) - {{ totalAmount() | currency }}
            </label>
          </div>
        </div>

        <div class="mb-3">
          <h6>Payment Summary</h6>
          <table class="table table-sm">
            <thead>
              <tr>
                <th>Player</th>
                <th>Team</th>
                <th>Amount</th>
              </tr>
            </thead>
            <tbody>
              @for (item of lineItems(); track item.playerId) {
                <tr>
                  <td>{{ item.playerName }}</td>
                  <td>{{ item.teamName }}</td>
                  <td>{{ item.amount | currency }}</td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <th colspan="2">Total</th>
                <th>{{ currentTotal() | currency }}</th>
              </tr>
            </tfoot>
          </table>
        <div class="mb-3">
          <h6>Credit Card Information</h6>
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
        </div>
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-primary" (click)="submit()" [disabled]="!canSubmit()">Pay Now</button>
        </div>
      </div>
    </div>
  `
})
export class PaymentComponent implements AfterViewInit, OnChanges, DoCheck {
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

  constructor(public state: RegistrationWizardService, private readonly http: HttpClient) { }

  // VerticalInsure integration state
  viOffer = computed(() => this.state.verticalInsureOffer());
  viProductCount = computed(() => {
    const data: any = this.viOffer().data;
    const list = data?.['product_config']?.['registration_cancellation'] || [];
    return Array.isArray(list) ? list.length : 0;
  });
  // Whether to render insurance region: show when policy exists OR loading/error/product data present
  showInsuranceRegion = () => {
    if (this.state.regSaverDetails()) return true; // existing policy always shown
    if (!this.state.offerPlayerRegSaver()) return false; // feature off
    const offer = this.viOffer();
    if (offer.loading) return true; // show spinner while loading
    if (offer.error) return true; // show error block
    const products = this.viProductCount();
    if (products > 0) return true; // show widget container
    // Otherwise hide entirely (no products, not loading, no error)
    return false;
  };
  private viInitialized = false;
  private verticalInsureInstance: any;
  viHasUserResponse = false;
  quotes: any[] = [];

  ngAfterViewInit(): void {
    this.tryInitVerticalInsure();
  }
  ngOnChanges(): void { this.tryInitVerticalInsure(); }
  ngDoCheck(): void { this.tryInitVerticalInsure(); }

  reloadVi(): void { this.state.fetchVerticalInsureObject(false); this.viInitialized = false; this.tryInitVerticalInsure(true); }

  private tryInitVerticalInsure(force: boolean = false): void {
    // Conditions: feature offered, data loaded, products > 0, widget not yet initialized or forced
    const offerEnabled = this.state.offerPlayerRegSaver();
    const offerDataReady = !this.viOffer().loading && !this.viOffer().error && !!this.viOffer().data;
    const hasProducts = this.viProductCount() > 0;
    if (!offerEnabled || !offerDataReady || !hasProducts) return;
    if (this.viInitialized && !force) return;

    const offerObj = this.viOffer().data;
    if (!offerObj) return;

    const init = () => {
      try {
        // Instantiate using provided simplified pattern
        this.verticalInsureInstance = new (globalThis as any).VerticalInsure(
          '#dVIOffer',
          offerObj,
          (offerState: any) => {
            this.verticalInsureInstance.validate().then((isValid: boolean) => {
              this.viHasUserResponse = isValid;
              this.quotes = offerState?.quotes || [];
              console.log('[VI] change viHasUserResponse:', this.viHasUserResponse, ' quotes:', this.quotes, ' isValid:', isValid);
            });
          },
          () => {
            this.verticalInsureInstance.validate().then((isValid: boolean) => {
              this.viHasUserResponse = isValid;
              console.log('[VI] ready isValid:', this.viHasUserResponse);
            });
          }
        );
        this.viInitialized = true;
      } catch (err) {
        console.error('[VI] instantiation failed', err);
      }
    };

    if ((globalThis as any).VerticalInsure) {
      init();
      return;
    }

    // Inject script with single @ path if not loaded
    const script = document.createElement('script');
    script.src = 'https://cdn.jsdelivr.net/npm/@vertical-insure/embedded-offer';
    script.async = true;
    script.onload = () => init();
    script.onerror = () => console.error('[VI] script load failed');
    document.head.appendChild(script);
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
    return this.lineItems().reduce((sum, item) => sum + (item.amount * 0.5), 0); // assume 50% deposit
  });

  currentTotal = computed(() => {
    const option = this.state.paymentOption();
    if (option === 'Deposit') {
      return this.depositTotal();
    }
    return this.totalAmount();
  });

  canSubmit = computed(() => {
    return this.lineItems().length > 0 && this.currentTotal() > 0;
  });

  private getAmountForTeam(team: any): number {
    // Use perRegistrantFee or fallback
    return team.perRegistrantFee || 100; // default
  }

  submit(): void {
    const request = {
      jobId: this.state.jobId(),
      familyUserId: this.state.familyUser()?.familyUserId,
      paymentOption: this.state.paymentOption(),
      creditCard: this.creditCard
    };

    this.http.post('/api/registration/submit-payment', request).subscribe({
      next: (response: any) => {
        if (response.success) {
          // Handle success, perhaps navigate to next step
          console.log('Payment successful', response);
          this.submitted.emit();
        } else {
          // Handle error
          console.error('Payment failed', response.message);
        }
      },
      error: (error: any) => {
        console.error('Payment error', error);
      }
    });
  }
}
