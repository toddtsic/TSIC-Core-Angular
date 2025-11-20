import { Component } from '@angular/core';
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
          @if (svc.isArbScenario()) { <th>Per Interval</th><th>Total</th> }
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
          <tr><th colspan="2" class="text-end">Per Interval Total</th>
              <th>{{ svc.arbPerOccurrence() | currency }}</th>
              <th class="text-muted small">(of {{ svc.totalAmount() | currency }})</th></tr>
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
    constructor(public svc: PaymentService) { }
}
