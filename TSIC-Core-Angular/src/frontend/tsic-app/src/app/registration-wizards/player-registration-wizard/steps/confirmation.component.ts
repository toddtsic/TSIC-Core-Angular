import { Component, EventEmitter, Output, computed, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';
import { TeamService } from '../team.service';

@Component({
  selector: 'app-rw-confirmation',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Confirmation</h5>
      </div>
      <div class="card-body">
        @if (!confirmationLoaded()) {
          <p class="text-muted">Loading confirmation summary…</p>
        } @else {
          <ng-container [ngSwitch]="bannerType()">
            <div *ngSwitchCase="'payment-success'" class="alert alert-success border-0 py-2 mb-3">
              Payment processed: {{ conf()!.tsic.amountCharged | currency:conf()!.tsic.currency }}
              <span *ngIf="conf()!.tsic.transactionId"> (Txn: {{ conf()!.tsic.transactionId }})</span>
            </div>
            <div *ngSwitchCase="'insurance-only'" class="alert alert-info border-0 py-2 mb-3">
              Insurance purchase completed; no TSIC payment today.
            </div>
            <div *ngSwitchCase="'arb-active'" class="alert alert-info border-0 py-2 mb-3">
              Subscription active. Next billing: {{ conf()!.tsic.nextArbBillDate || 'TBD' }}
            </div>
            <div *ngSwitchDefault class="alert alert-secondary border-0 py-2 mb-3">
              Registration completed.
            </div>
          </ng-container>

          <h6 class="fw-semibold">Registrations</h6>
          <table class="table table-sm align-middle mb-3" *ngIf="conf()!.tsic.lines.length; else noRegs">
            <thead>
              <tr>
                <th>Player</th>
                <th>Team</th>
                <th class="text-end">Fee</th>
                <th>Policy</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of conf()!.tsic.lines">
                <td>{{ r.playerName }}</td>
                <td>{{ r.teamName || '-' }}</td>
                <td class="text-end">{{ r.feeTotal | currency:conf()!.tsic.currency }}</td>
                <td>{{ policyFor(r.registrationId) || '-' }}</td>
              </tr>
            </tbody>
            <tfoot>
              <tr class="table-light">
                <td colspan="2" class="text-end">Total:</td>
                <td class="text-end">{{ conf()!.tsic.totalNet | currency:conf()!.tsic.currency }}</td>
                <td></td>
              </tr>
            </tfoot>
          </table>
          <ng-template #noRegs><p class="text-muted">No registrations found.</p></ng-template>

          <div *ngIf="conf()!.insurance.purchaseSucceeded" class="mb-3">
            <h6 class="fw-semibold">Insurance Policies</h6>
            <ul class="mb-0">
              <li *ngFor="let p of conf()!.insurance.policies">{{ p.policyNumber }} (Issued {{ p.issuedUtc | date:'yyyy-MM-dd' }})</li>
            </ul>
          </div>
          <p *ngIf="conf()!.insurance.declined" class="text-muted">Insurance declined.</p>

          <div class="mt-3" [innerHTML]="conf()!.confirmationHtml"></div>

          <button type="button" class="btn btn-primary mt-3" (click)="completed.emit()">Finish</button>
        }
      </div>
    </div>
  `
})
export class ConfirmationComponent implements OnInit {
  @Output() completed = new EventEmitter<void>();
  readonly state = inject(RegistrationWizardService);
  readonly teamService = inject(TeamService);

  ngOnInit(): void {
    // Trigger load once jobId & familyUser are known.
    // Simple polling for initial presence; avoids race without extra subscriptions.
    const tryLoad = () => {
      if (this.state.jobId() && this.state.familyUser()?.familyUserId) {
        this.state.loadConfirmation();
        return true;
      }
      return false;
    };
    if (!tryLoad()) {
      const timer = setInterval(() => { if (tryLoad()) clearInterval(timer); }, 250);
      setTimeout(() => clearInterval(timer), 4000); // safety stop
    }
  }

  conf = computed(() => this.state.confirmation());
  confirmationLoaded = computed(() => !!this.conf());
  bannerType = computed(() => {
    const c = this.conf();
    if (!c) return 'loading';
    const t = c.tsic; const i = c.insurance;
    if (t.wasImmediateCharge && t.amountCharged > 0) return 'payment-success';
    if (i.purchaseSucceeded && !t.wasImmediateCharge && t.amountCharged === 0) return 'insurance-only';
    if (t.wasArb && t.amountCharged === 0) return 'arb-active';
    return 'completed';
  });
  policyFor(regId: string): string | undefined {
    const c = this.conf(); if (!c) return undefined;
    return c.insurance.policies.find(p => p.registrationId === regId)?.policyNumber;
  }
}
