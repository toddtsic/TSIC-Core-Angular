import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';

/**
 * Eligibility step — shows a per-player dropdown for the constraint field
 * (grad year, age group, age range, or club name).
 */
@Component({
    selector: 'app-prw-eligibility-step',
    standalone: true,
    imports: [FormsModule],
    template: `
    <!-- Centered hero -->
    <div class="welcome-hero">
      <h4 class="welcome-title"><i class="bi bi-bullseye welcome-icon"></i> Set Player {{ cardTitle() }}</h4>
      <p class="welcome-desc">
        <i class="bi bi-person-check me-1"></i>Choose {{ constraintLabel() }} per player
        <span class="desc-dot"></span>
        <i class="bi bi-diagram-3 me-1"></i>Determines team placement
      </p>
    </div>

    <div class="card shadow border-0 card-rounded">
      <div class="card-body pt-3">
        <div class="player-list">
          @for (pid of selectedPlayerIds(); track pid) {
            <div class="player-row" [class.is-set]="!!state.eligibility.getEligibilityForPlayer(pid)">
              <i class="bi bi-person-fill player-icon"></i>
              <div class="player-info">
                <label class="player-name" [for]="'elig-' + pid">
                  {{ getPlayerName(pid) }}
                </label>
                <select
                  class="elig-select"
                  [id]="'elig-' + pid"
                  [ngModel]="state.eligibility.getEligibilityForPlayer(pid) || ''"
                  (ngModelChange)="onEligibilityChange(pid, $event)">
                  <option value="">— Select —</option>
                  @for (opt of eligibilityOptions(); track opt) {
                    <option [value]="opt">{{ opt }}</option>
                  }
                </select>
              </div>
              @if (state.eligibility.getEligibilityForPlayer(pid)) {
                <i class="bi bi-check-circle-fill set-icon"></i>
              }
            </div>
          }
        </div>
      </div>
    </div>
  `,
    styles: [`
      .player-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      .player-row {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        border-radius: var(--radius-md);
        border: 1px solid var(--border-color);
        background: var(--brand-surface);
        transition: border-color 0.15s ease, background-color 0.15s ease;

        &.is-set {
          border-color: rgba(var(--bs-primary-rgb), 0.3);
          background: rgba(var(--bs-primary-rgb), 0.03);
        }
      }

      .player-icon {
        font-size: var(--font-size-xl);
        color: var(--neutral-400);

        .is-set & { color: var(--bs-primary); }
      }

      .player-info {
        flex: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
      }

      .player-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        cursor: pointer;
        margin-bottom: 0;
      }

      .elig-select {
        appearance: none;
        width: 100%;
        padding: var(--space-1) var(--space-3);
        padding-right: var(--space-8);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        background-color: var(--neutral-50);
        background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3e%3cpath fill='none' stroke='%2378716c' stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='m2 5 6 6 6-6'/%3e%3c/svg%3e");
        background-repeat: no-repeat;
        background-position: right var(--space-2) center;
        background-size: 14px 10px;
        border: 1px solid var(--border-color);
        border-radius: var(--radius-sm);
        transition: border-color 0.15s ease, box-shadow 0.15s ease, background-color 0.15s ease;

        &:hover:not(:focus) {
          border-color: var(--neutral-400);
        }

        &:focus {
          outline: none;
          border-color: var(--bs-primary);
          background-color: var(--brand-surface);
          box-shadow: var(--shadow-focus);
        }
      }

      .set-icon {
        font-size: var(--font-size-lg);
        color: var(--bs-primary);
        flex-shrink: 0;
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EligibilityStepComponent {
    readonly state = inject(PlayerWizardStateService);
    readonly advance = output<void>();

    readonly cardTitle = computed(() => {
        const ct = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        if (ct === 'BYGRADYEAR') return 'Graduation Year';
        if (ct === 'BYAGEGROUP') return 'Age Group';
        if (ct === 'BYAGERANGE') return 'Age Range';
        if (ct === 'BYCLUBNAME') return 'Club';
        return 'Eligibility';
    });

    selectedPlayerIds(): string[] {
        return this.state.familyPlayers.selectedPlayerIds();
    }

    getPlayerName(playerId: string): string {
        const player = this.state.familyPlayers.familyPlayers().find(p => p.playerId === playerId);
        return player ? `${player.firstName} ${player.lastName}`.trim() : playerId;
    }

    constraintLabel(): string {
        const ct = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        if (ct === 'BYGRADYEAR') return 'graduation year';
        if (ct === 'BYAGEGROUP') return 'age group';
        if (ct === 'BYAGERANGE') return 'age range';
        if (ct === 'BYCLUBNAME') return 'club';
        return 'eligibility';
    }

    eligibilityOptions(): string[] {
        const schemas = this.state.jobCtx.profileFieldSchemas();
        const eligField = this.state.eligibility.determineEligibilityField(schemas);
        if (!eligField) return [];
        const field = schemas.find(s => s.name === eligField);
        return field?.options || [];
    }

    onEligibilityChange(playerId: string, value: string): void {
        this.state.eligibility.setEligibilityForPlayer(playerId, value || null);
        this.state.eligibility.updateUnifiedConstraintValue(this.selectedPlayerIds());

        // Auto-advance when every player has a value
        if (value) {
            const allSet = this.selectedPlayerIds()
                .every(id => !!this.state.eligibility.getEligibilityForPlayer(id));
            if (allSet) this.advance.emit();
        }
    }
}
