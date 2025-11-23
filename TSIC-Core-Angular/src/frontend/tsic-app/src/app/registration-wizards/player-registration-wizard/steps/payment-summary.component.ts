import { Component } from '@angular/core';
import { RegistrationWizardService } from '../registration-wizard.service';
import { CommonModule } from '@angular/common';
import { PaymentService } from '../services/payment.service';

@Component({
  selector: 'app-payment-summary',
  standalone: true,
  imports: [CommonModule],
  template: `
    <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="pay-summary-title"
             style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
      <h6 id="pay-summary-title" class="fw-semibold mb-2">Payment Summary</h6>
      <table class="table table-sm mb-0">
        <thead>
        <tr>
          <th>Player</th>
          <th>Team</th>
          @if (svc.isArbScenario()) { <th>Active ARB</th><th>Subscription ID</th><th>Next Bill (Progress)</th><th># Intervals</th><th>Per Interval</th><th>Total</th> }
          @else if (svc.isDepositScenario()) { <th>Deposit</th><th>Pay In Full</th> }
          @else { <th>Amount</th> }
        </tr>
        </thead>
        <tbody>
        @for (li of svc.lineItems(); track li.playerId) {
          <tr>
            <td>{{ li.playerName }}</td>
            <td>{{ li.teamName }}</td>
            @if (svc.isArbScenario()) {
              <td>
                @if (activeArb(li.playerId)) {
                  <span class="text-success" aria-label="Active subscription" title="Active subscription">&#10003;</span>
                } @else if (hasSub(li.playerId)) {
                  <span class="text-warning" aria-label="Subscription issue" title="Subscription issue">&#9888;</span>
                } @else {
                  <span class="text-muted" aria-label="No subscription" title="No subscription">&ndash;</span>
                }
              </td>
              <td>{{ subscriptionId(li.playerId) || '-' }}</td>
              <td [title]="scheduleTooltip(li.playerId)">
                <ng-container *ngIf="arbProgress(li.playerId) as prog">
                  <ng-container [ngSwitch]="prog.state">
                    <span *ngSwitchCase="'issue'" class="text-warning">Issue</span>
                    <span *ngSwitchCase="'pending'">{{ prog.nextDate | date:'MMM d, y'}} ({{ prog.nextIndex + 1 }}/{{ prog.total }})</span>
                    <span *ngSwitchCase="'completed'" class="badge bg-secondary-subtle text-dark border" title="No further scheduled billing dates">No more due</span>
                    <span *ngSwitchDefault>-</span>
                  </ng-container>
                </ng-container>
              </td>
              <td>
                <ng-container *ngIf="arbProgress(li.playerId) as prog">
                  <ng-container [ngSwitch]="prog.state">
                    <span *ngSwitchCase="'issue'" class="text-warning">Issue</span>
                    <span *ngSwitchDefault>{{ prog.total || '-' }}</span>
                  </ng-container>
                </ng-container>
              </td>
              <td>{{ (li.amount / svc.arbOccurrences()) | currency }}</td>
              <td>{{ li.amount | currency }}</td>
            } @else if (svc.isDepositScenario()) {
              <td>{{ svc.getDepositForPlayer(li.playerId) | currency }}</td>
              <td>{{ li.amount | currency }}</td>
            } @else {
              <td>{{ li.amount | currency }}</td>
            }
          </tr>
        }
        </tbody>
        <tfoot>
        @if (svc.isArbScenario()) {
              <tr><th colspan="6" class="text-end">Total</th>
                <th>{{ svc.arbPerOccurrence() | currency }}</th>
                <th>{{ svc.totalAmount() | currency }}</th></tr>
        } @else if (svc.isDepositScenario()) {
          <tr><th colspan="2" class="text-end">Deposit Total</th>
              <th>{{ svc.depositTotal() | currency }}</th>
              <th class="text-muted small">Pay In Full: {{ svc.totalAmount() | currency }}</th></tr>
        } @else {
          <tr><th colspan="2" class="text-end">Subtotal</th><th>{{ svc.totalAmount() | currency }}</th></tr>
          @if (svc.appliedDiscount() > 0) {
            <tr><th colspan="2" class="text-end">Discount</th><th>-{{ svc.appliedDiscount() | currency }}</th></tr>
          }
          <tr><th colspan="2" class="text-end">Due Now</th><th>{{ svc.currentTotal() | currency }}</th></tr>
        }
        </tfoot>
      </table>
    </section>
  `
})
export class PaymentSummaryComponent {
  constructor(public svc: PaymentService, private readonly wizard: RegistrationWizardService) { }
  activeArb(playerId: string): boolean {
    const p = this.wizard.familyPlayers().find(fp => fp.playerId === playerId);
    if (!p) return false;
    return p.priorRegistrations.some(r => !!r.adnSubscriptionId && (r.adnSubscriptionStatus || '').toLowerCase() === 'active');
  }
  hasSub(playerId: string): boolean {
    const p = this.wizard.familyPlayers().find(fp => fp.playerId === playerId);
    if (!p) return false;
    return p.priorRegistrations.some(r => !!r.adnSubscriptionId);
  }
  subscriptionId(playerId: string): string | null {
    const p = this.wizard.familyPlayers().find(fp => fp.playerId === playerId);
    if (!p) return null;
    const reg = p.priorRegistrations.find(r => !!r.adnSubscriptionId);
    return reg?.adnSubscriptionId ?? null;
  }
  arbProgress(playerId: string): { state: 'none' | 'issue' | 'pending' | 'completed'; nextDate?: Date; nextIndex: number; total: number; } {
    const p = this.wizard.familyPlayers().find(fp => fp.playerId === playerId);
    if (!p) return { state: 'none', nextIndex: -1, total: 0 };
    const reg = p.priorRegistrations.find(r => !!r.adnSubscriptionId);
    if (!reg) return { state: 'none', nextIndex: -1, total: 0 };
    const status = (reg.adnSubscriptionStatus || '').toLowerCase();
    if (status !== 'active') return { state: 'issue', nextIndex: -1, total: reg.adnSubscriptionBillingOccurences || 0 };
    const startRaw = reg.adnSubscriptionStartDate;
    const interval = reg.adnSubscriptionIntervalLength || 1;
    const occur = reg.adnSubscriptionBillingOccurences || 0;
    if (!startRaw || occur <= 0) return { state: 'none', nextIndex: -1, total: occur };
    const start = new Date(startRaw); if (Number.isNaN(start.getTime())) return { state: 'none', nextIndex: -1, total: occur };
    const today = new Date();
    let nextDate: Date | undefined;
    let nextIndex = -1;
    for (let i = 0; i < occur; i++) {
      const d = new Date(start);
      d.setMonth(d.getMonth() + interval * i);
      if (d >= today) { nextDate = d; nextIndex = i; break; }
    }
    if (!nextDate) return { state: 'completed', nextIndex: occur, total: occur };
    return { state: 'pending', nextDate, nextIndex, total: occur };
  }
  scheduleTooltip(playerId: string): string {
    const prog = this.arbProgress(playerId);
    if (prog.state === 'none') return 'No subscription';
    if (prog.state === 'issue') return 'Subscription issue – contact club';
    const p = this.wizard.familyPlayers().find(fp => fp.playerId === playerId);
    const reg = p?.priorRegistrations.find(r => !!r.adnSubscriptionId);
    if (!reg) return 'No subscription';
    const startRaw = reg.adnSubscriptionStartDate; const interval = reg.adnSubscriptionIntervalLength || 1; const occur = reg.adnSubscriptionBillingOccurences || 0;
    if (!startRaw || occur <= 0) return 'Subscription scheduled';
    const start = new Date(startRaw); if (Number.isNaN(start.getTime())) return 'Subscription scheduled';
    const end = new Date(start); end.setMonth(end.getMonth() + interval * (occur - 1));
    if (prog.state === 'completed') return `Schedule ended ${end.toLocaleDateString()} (${occur} intervals)`;
    const nextHuman = prog.nextDate ? prog.nextDate.toLocaleDateString() : 'Unknown';
    return `Billing ${occur} intervals starting ${start.toLocaleDateString()} ending ${end.toLocaleDateString()} • Next: ${nextHuman} (${prog.nextIndex + 1}/${occur})`;
  }
}
