import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import type { RegisteredTeamDto } from '@core/api';

/**
 * Compact summary pills for a registered-teams set. Mirrors the bottom
 * aggregates on `app-registered-teams-grid`. Designed to sit flush-right
 * in a section title bar so totals stay glanceable without a separate band.
 */
@Component({
    selector: 'app-registered-teams-summary',
    standalone: true,
    imports: [CurrencyPipe],
    template: `
        <span class="summary-pill">
          <strong class="summary-value">{{ teams().length }}</strong>
          <span class="summary-text">{{ teams().length === 1 ? 'team' : 'teams' }}</span>
        </span>
        <span class="summary-pill">
          <span class="summary-text">Total Fee</span>
          <strong class="summary-value">{{ sumFee() | currency }}</strong>
        </span>
        @if (showDeposit()) {
          <span class="summary-pill">
            <span class="summary-text">Deposit Due</span>
            <strong class="summary-value">{{ sumDepositDue() | currency }}</strong>
          </span>
        }
        @if (showBalance()) {
          <span class="summary-pill">
            <span class="summary-text">Bal Due</span>
            <strong class="summary-value">{{ sumAdditionalDue() | currency }}</strong>
          </span>
        }
        @if (showOwed()) {
          <span class="summary-pill" [class.summary-pill-danger]="sumOwed() > 0">
            <span class="summary-text">Owed</span>
            <strong class="summary-value">{{ sumOwed() | currency }}</strong>
          </span>
        }
        @if (showProcessing()) {
          <span class="summary-pill">
            <span class="summary-text">Proc Fee</span>
            <strong class="summary-value">{{ sumProcessing() | currency }}</strong>
          </span>
        }
        @if (showDiscount()) {
          <span class="summary-pill summary-pill-success">
            <span class="summary-text">Discount</span>
            <strong class="summary-value">-{{ sumDiscount() | currency }}</strong>
          </span>
        }
        @if (showFeeAdj()) {
          <span class="summary-pill">
            <span class="summary-text">Fee-Adj</span>
            <strong class="summary-value">{{ sumFeeAdj() | currency }}</strong>
          </span>
        }
        @if (showPaid()) {
          <span class="summary-pill" [class.summary-pill-success]="sumPaid() > 0">
            <span class="summary-text">Paid</span>
            <strong class="summary-value">{{ sumPaid() | currency }}</strong>
          </span>
        }
        @if (showCcOwed()) {
          <span class="summary-pill"
                [class.summary-pill-danger]="sumCcOwed() > 0"
                [class.summary-pill-success]="sumCcOwed() === 0">
            <span class="summary-text">CC Owed</span>
            <strong class="summary-value">{{ sumCcOwed() | currency }}</strong>
          </span>
        }
        @if (showCkOwed()) {
          <span class="summary-pill"
                [class.summary-pill-danger]="sumCkOwed() > 0"
                [class.summary-pill-success]="sumCkOwed() === 0">
            <span class="summary-text">Check Owed</span>
            <strong class="summary-value">{{ sumCkOwed() | currency }}</strong>
          </span>
        }
    `,
    host: { 'role': 'group', 'aria-label': 'Totals summary' },
    styles: [`
      :host {
        display: inline-flex;
        flex-wrap: wrap;
        gap: var(--space-2);
        align-items: center;
      }

      .summary-pill {
        display: inline-flex;
        align-items: baseline;
        gap: var(--space-1);
        padding: 2px var(--space-2);
        font-size: var(--font-size-xs);
        color: var(--bs-primary);
        background: var(--brand-surface);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 15%, transparent);
        border-radius: var(--radius-sm);
        white-space: nowrap;
      }

      .summary-pill .summary-value { font-weight: var(--font-weight-bold); }
      .summary-pill .summary-text { color: var(--brand-text-muted); }

      .summary-pill.summary-pill-danger,
      .summary-pill.summary-pill-danger .summary-text { color: var(--bs-danger); }

      .summary-pill.summary-pill-success,
      .summary-pill.summary-pill-success .summary-text { color: var(--bs-success); }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisteredTeamsSummaryComponent {
    readonly teams = input.required<RegisteredTeamDto[]>();
    readonly showDeposit = input(false);
    readonly showBalance = input(false);
    readonly showOwed = input(false);
    readonly showProcessing = input(false);
    readonly showPaid = input(true);
    readonly showCcOwed = input(true);
    readonly showCkOwed = input(true);

    readonly showDiscount = computed(() => this.teams().some(t => (t.feeDiscount ?? 0) > 0));
    readonly showFeeAdj = computed(() => this.teams().some(t => (t.feeLatefee ?? 0) > 0));

    readonly sumFee = computed(() => this.teams().reduce((s, t) => s + t.feeBase, 0));
    readonly sumPaid = computed(() => this.teams().reduce((s, t) => s + t.paidTotal, 0));
    readonly sumDepositDue = computed(() => this.teams().reduce((s, t) => s + t.depositDue, 0));
    readonly sumAdditionalDue = computed(() => this.teams().reduce((s, t) => s + t.additionalDue, 0));
    readonly sumOwed = computed(() => this.teams().reduce((s, t) => s + t.owedTotal, 0));
    readonly sumProcessing = computed(() => this.teams().reduce((s, t) => s + (t.feeProcessing ?? 0), 0));
    readonly sumDiscount = computed(() => this.teams().reduce((s, t) => s + (t.feeDiscount ?? 0), 0));
    readonly sumFeeAdj = computed(() => this.teams().reduce((s, t) => s + (t.feeLatefee ?? 0), 0));
    readonly sumCcOwed = computed(() => this.teams().reduce((s, t) => s + t.ccOwedTotal, 0));
    readonly sumCkOwed = computed(() => this.teams().reduce((s, t) => s + t.ckOwedTotal, 0));
}
