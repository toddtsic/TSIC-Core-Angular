import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { PaymentV2Service } from '../state/payment-v2.service';
import { TeamService } from '@views/registration/player/services/team.service';
import { JobService } from '@infrastructure/services/job.service';

/**
 * Review step — summary table of players, teams, and amounts.
 * Waiver summary and server validation errors display.
 */
@Component({
    selector: 'app-prw-review-step',
    standalone: true,
    imports: [CurrencyPipe, DatePipe],
    template: `
    <div class="review-shell">
      <!-- Centered hero -->
      <div class="welcome-hero">
        <h4 class="welcome-title"><i class="bi bi-clipboard-check welcome-icon" style="color: var(--bs-success)"></i> Almost There!</h4>
        <p class="welcome-desc">
          <i class="bi bi-eye me-1"></i>Review your details
          <span class="desc-dot"></span>
          <i class="bi bi-arrow-right me-1"></i>Then proceed to payment
        </p>
      </div>

      <!-- Server validation errors -->
      @if (state.jobCtx.hasServerValidationErrors()) {
        <div class="review-alert">
          <i class="bi bi-exclamation-triangle-fill"></i>
          <div>
            <div class="fw-semibold mb-1">Validation Errors</div>
            <ul class="mb-0 ps-3">
              @for (err of state.jobCtx.getServerValidationErrors(); track err.field) {
                <li>{{ err.message || err.field }}</li>
              }
            </ul>
          </div>
        </div>
      }

      <!-- Players & Teams -->
      <div class="review-section">
        <div class="review-section-header">
          <i class="bi bi-people-fill"></i>
          <span>Players &amp; Teams</span>
        </div>
        <div class="review-section-body">
          @for (player of selectedPlayers(); track player.userId; let last = $last) {
            <div class="review-player-row" [class.border-bottom]="!last">
              <div class="review-player-info">
                <span class="review-player-name">{{ player.name }}</span>
                @if (player.dob || player.gender) {
                  <span class="review-player-meta">
                    @if (player.gender) { {{ player.gender }} }
                    @if (player.gender && player.dob) { &middot; }
                    @if (player.dob) { DOB: {{ player.dob | date:'mediumDate' }} }
                  </span>
                }
              </div>
              <div class="review-player-teams">
                @for (t of getTeamsForPlayer(player.userId); track t) {
                  <span class="review-team-pill">{{ t }}</span>
                }
              </div>
              <div class="review-player-amount">
                @if (getAmountForPlayer(player.userId) !== null) {
                  {{ getAmountForPlayer(player.userId) | currency }}
                } @else {
                  <span class="text-muted">&ndash;</span>
                }
              </div>
            </div>
          }
          @if (paySvc.totalAmount() > 0) {
            <div class="review-total-row">
              <span>Total</span>
              <span class="review-total-amount">{{ paySvc.totalAmount() | currency }}</span>
            </div>
          }
        </div>
      </div>

      <!-- Form details per player -->
      @for (player of selectedPlayers(); track player.userId) {
        @if (getFormFields(player.userId).length > 0) {
          <div class="review-section">
            <div class="review-section-header">
              <i class="bi bi-person-lines-fill"></i>
              <span>{{ player.name }}</span>
            </div>
            <div class="review-section-body">
              <div class="review-fields-grid">
                @for (f of getFormFields(player.userId); track f.name) {
                  <div class="review-field">
                    <span class="review-field-label">{{ f.label }}</span>
                    <span class="review-field-value" [class.text-muted]="!f.value">{{ f.value || '—' }}</span>
                  </div>
                }
              </div>
            </div>
          </div>
        }
      }
    </div>
  `,
    styles: [`
      .review-shell {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
      }

      /* ── Alert ────────────────────────────────────────── */
      .review-alert {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-3);
        border-radius: var(--radius-md);
        background: rgba(var(--bs-danger-rgb), 0.08);
        border: 1px solid rgba(var(--bs-danger-rgb), 0.25);
        color: var(--bs-danger);
        font-size: var(--font-size-sm);

        i { font-size: var(--font-size-lg); flex-shrink: 0; margin-top: 2px; }
      }

      /* ── Section card ─────────────────────────────────── */
      .review-section {
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        overflow: hidden;
        box-shadow: var(--shadow-sm);
      }

      .review-section-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-body-color-rgb), 0.03);
        border-bottom: 1px solid var(--border-color);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        text-transform: uppercase;
        letter-spacing: 0.03em;

        i { color: var(--bs-primary); font-size: var(--font-size-base); }
      }

      .review-section-body {
        padding: 0;
      }

      /* ── Player rows ──────────────────────────────────── */
      .review-player-row {
        display: grid;
        grid-template-columns: 1fr auto auto;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-3);

        &.border-bottom { border-bottom: 1px solid var(--border-color); }
      }

      .review-player-info {
        display: flex;
        flex-direction: column;
        gap: 1px;
        min-width: 0;
      }

      .review-player-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .review-player-meta {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      .review-player-teams {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .review-team-pill {
        font-size: 11px;
        font-weight: var(--font-weight-medium);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.2);
        white-space: nowrap;
      }

      .review-player-amount {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
      }

      /* ── Total row ────────────────────────────────────── */
      .review-total-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-body-color-rgb), 0.03);
        border-top: 2px solid var(--border-color);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .review-total-amount {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-bold);
        color: var(--bs-success);
      }

      /* ── Form fields grid ────────────────────────────── */
      .review-fields-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 0;
      }

      .review-field {
        display: flex;
        flex-direction: column;
        gap: 1px;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--border-color);

        &:nth-child(odd) { border-right: 1px solid var(--border-color); }
      }

      .review-field-label {
        font-size: 10px;
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--brand-text-muted);
      }

      .review-field-value {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
      }

      /* ── Mobile ───────────────────────────────────────── */
      @media (max-width: 575.98px) {
        .review-hero { padding: var(--space-3); gap: var(--space-2); }
        .review-hero-icon { font-size: 1.5rem; }
        .review-hero-title { font-size: var(--font-size-base); }

        .review-player-row {
          grid-template-columns: 1fr;
          gap: var(--space-1);
          padding: var(--space-2) var(--space-3);
        }

        .review-player-amount { text-align: right; }

        .review-fields-grid { grid-template-columns: 1fr; }
        .review-field { border-right: none !important; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewStepComponent {
    readonly advance = output<void>();
    readonly state = inject(PlayerWizardStateService);
    readonly paySvc = inject(PaymentV2Service);
    private readonly teamService = inject(TeamService);
    private readonly jobService = inject(JobService);

    selectedPlayers() {
        return this.state.familyPlayers.familyPlayers()
            .filter(p => p.selected || p.registered)
            .map(p => ({
                userId: p.playerId,
                name: `${p.firstName} ${p.lastName}`.trim(),
                dob: p.dob || null,
                gender: p.gender || null,
            }));
    }

    getTeamsForPlayer(playerId: string): string[] {
        const teams = this.state.eligibility.selectedTeams()[playerId];
        if (!teams) return [];
        const allTeams = this.teamService.filterByEligibility(null);
        if (Array.isArray(teams)) {
            return teams.map((tid: string) => {
                const team = allTeams.find(t => t.teamId === tid);
                return team?.teamName || tid;
            });
        }
        const team = allTeams.find(t => t.teamId === teams);
        return [team?.teamName || teams];
    }

    getAmountForPlayer(playerId: string): number | null {
        const li = this.paySvc.lineItems().find(i => i.playerId === playerId);
        return li ? li.amount : null;
    }

    isMultiTeamMode(): boolean {
        const t = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        return !t || t === 'BYCLUBNAME';
    }

    getFormFields(playerId: string): { name: string; label: string; value: string }[] {
        const schemas = this.state.jobCtx.profileFieldSchemas();
        const wfn = this.state.jobCtx.waiverFieldNames();
        const tct = this.state.eligibility.teamConstraintType();
        return schemas
            .filter(f => this.state.playerForms.isFieldVisibleForPlayer(playerId, f, wfn, tct))
            .map(f => {
                const raw = this.state.playerForms.getPlayerFieldValue(playerId, f.name);
                let value = '';
                if (raw == null) value = '';
                else if (Array.isArray(raw)) value = raw.join(', ');
                else if (typeof raw === 'boolean') value = raw ? 'Yes' : 'No';
                else value = String(raw).trim();
                return { name: f.name, label: f.label, value };
            })
            .filter(f => f.value.length > 0);
    }
}
