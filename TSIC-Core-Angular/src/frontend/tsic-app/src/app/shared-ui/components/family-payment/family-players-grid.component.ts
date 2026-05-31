import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import type { FamilyPlayerAccountingDto } from '@core/api';

/**
 * Per-child summary grid for the family-payment view — the parent-side analog of
 * app-registered-teams-grid. Lightweight table (the team grid is bound to RegisteredTeamDto
 * and team-specific columns, so it can't be reused). Renders Player / Fee / Paid / Owed /
 * status with an active-only totals footer.
 */
@Component({
  selector: 'app-family-players-grid',
  standalone: true,
  imports: [CurrencyPipe],
  template: `
    <table class="players-table">
      <thead>
        <tr>
          <th class="col-player">Player</th>
          <th class="text-end">Total Fee</th>
          <th class="text-end">Paid</th>
          <th class="text-end">Owed</th>
        </tr>
      </thead>
      <tbody>
        @for (p of players(); track p.registrationId) {
          <tr [class.row-inactive]="!p.active">
            <td class="col-player">
              <span class="player-name">{{ p.playerName }}</span>
              @if (!p.active) { <span class="status-badge">inactive</span> }
            </td>
            <td class="text-end">{{ p.feeTotal | currency }}</td>
            <td class="text-end" [class.text-success]="p.paidTotal > 0">{{ p.paidTotal | currency }}</td>
            <td class="text-end" [class.owed]="p.owedTotal > 0">{{ p.owedTotal | currency }}</td>
          </tr>
        }
      </tbody>
      <tfoot>
        <tr>
          <td class="col-player"><strong>{{ players().length }} {{ players().length === 1 ? 'player' : 'players' }}</strong></td>
          <td class="text-end"><strong>{{ sumFee() | currency }}</strong></td>
          <td class="text-end"><strong>{{ sumPaid() | currency }}</strong></td>
          <td class="text-end"><strong [class.owed]="sumOwed() > 0">{{ sumOwed() | currency }}</strong></td>
        </tr>
      </tfoot>
    </table>
  `,
  styles: [`
    .players-table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--font-size-sm);
    }
    .players-table th,
    .players-table td {
      padding: var(--space-2) var(--space-3);
      border-bottom: 1px solid var(--bs-border-color);
    }
    .players-table thead th {
      font-weight: var(--font-weight-semibold);
      color: var(--text-muted);
      text-align: left;
      border-bottom: 2px solid var(--bs-border-color);
    }
    .players-table tfoot td {
      border-top: 2px solid var(--bs-border-color);
      border-bottom: none;
    }
    .text-end { text-align: right; }
    .text-success { color: var(--bs-success); }
    .owed { color: var(--bs-danger); font-weight: var(--font-weight-semibold); }
    .player-name { font-weight: var(--font-weight-medium); }
    .row-inactive { opacity: 0.65; }
    .status-badge {
      margin-left: var(--space-2);
      padding: 0 var(--space-2);
      font-size: var(--font-size-xs);
      color: var(--text-muted);
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm);
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FamilyPlayersGridComponent {
  readonly players = input.required<FamilyPlayerAccountingDto[]>();

  readonly sumFee = computed(() => this.players().reduce((s, p) => s + p.feeTotal, 0));
  readonly sumPaid = computed(() => this.players().reduce((s, p) => s + p.paidTotal, 0));
  readonly sumOwed = computed(() => this.players().reduce((s, p) => s + p.owedTotal, 0));
}
