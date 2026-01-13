import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TeamPaymentService } from '../services/team-payment.service';

/**
 * Team payment summary table - displays registered teams with fees and balances.
 * Simpler than player version (no ARB, no deposits, just PIF).
 */
@Component({
    selector: 'app-team-payment-summary-table',
    standalone: true,
    imports: [CommonModule],
    template: `
    <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="team-pay-summary-title"
             style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
      <h6 id="team-pay-summary-title" class="fw-semibold mb-2">Team Payment Summary</h6>
      
      <!-- Payment Method Selector (show when both CC and Check are allowed) -->
      @if (svc.showPaymentMethodSelector()) {
        <div class="mb-3">
          <label for="paymentMethod" class="form-label fw-semibold">How will you be paying?</label>
          <select id="paymentMethod" class="form-select" 
                  [value]="svc.selectedPaymentMethod()"
                  (change)="svc.selectedPaymentMethod.set($any($event.target).value)">
            <option value="CC">Credit Card</option>
            <option value="Check">Check</option>
          </select>
          @if (svc.selectedPaymentMethod() === 'Check') {
            <div class="form-text">
              <i class="bi bi-info-circle me-1"></i>
              Processing fees are waived for check payments. 
              You save {{ svc.processingFeeSavings() | currency }}!
            </div>
          }
        </div>
      }

      <div class="table-responsive">
        <table class="table table-sm mb-0">
          <thead>
            <tr>
              <th>Team Name</th>
              <th>Age Group</th>
              <th class="text-end">Fee Base</th>
              @if (svc.showFeeProcessingColumn()) {
                <th class="text-end">Fee-Processing</th>
              }
              <th class="text-end">Already Paid</th>
              @if (svc.showCcOwedColumn()) {
                <th class="text-end">CC Owed Total</th>
              }
              @if (svc.showCkOwedColumn()) {
                <th class="text-end">Ck Owed Total</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (team of svc.lineItems(); track team.teamId) {
              <tr>
                <td>{{ team.teamName }}</td>
                <td>{{ team.ageGroup }}</td>
                <td class="text-end">{{ team.feeBase | currency }}</td>
                @if (svc.showFeeProcessingColumn()) {
                  <td class="text-end text-muted">{{ team.feeProcessing | currency }}</td>
                }
                <td class="text-end text-success">{{ team.paidTotal | currency }}</td>
                @if (svc.showCcOwedColumn()) {
                  <td class="text-end fw-semibold" [class.text-warning]="team.ccOwedTotal > 0">
                    {{ team.ccOwedTotal | currency }}
                  </td>
                }
                @if (svc.showCkOwedColumn()) {
                  <td class="text-end fw-semibold" [class.text-warning]="team.ckOwedTotal > 0">
                    {{ team.ckOwedTotal | currency }}
                  </td>
                }
              </tr>
            }
          </tbody>
          <tfoot>
            <tr class="table-light fw-bold">
              <th colspan="2" class="text-end">Totals</th>
              <th class="text-end">{{ svc.totalFeeBase() | currency }}</th>
              @if (svc.showFeeProcessingColumn()) {
                <th class="text-end">{{ svc.totalFeeProcessing() | currency }}</th>
              }
              <th class="text-end">{{ svc.totalPaid() | currency }}</th>
              @if (svc.showCcOwedColumn()) {
                <th class="text-end">{{ svc.totalCcOwed() | currency }}</th>
              }
              @if (svc.showCkOwedColumn()) {
                <th class="text-end">{{ svc.totalCkOwed() | currency }}</th>
              }
            </tr>
            <tr class="table-primary">
              <th [attr.colspan]="svc.getColspan()" class="text-end">Amount to Charge</th>
              <th class="text-end text-primary">{{ svc.amountToCharge() | currency }}</th>
            </tr>
          </tfoot>
        </table>
      </div>
      
      @if (!svc.hasBalance()) {
        <div class="alert alert-success border-0 mt-3 mb-0" role="status">
          <i class="bi bi-check-circle-fill me-2"></i>
          All teams are fully paid. No payment is required.
        </div>
      }
    </section>
  `
})
export class TeamPaymentSummaryTableComponent {
    readonly svc = inject(TeamPaymentService);
}
