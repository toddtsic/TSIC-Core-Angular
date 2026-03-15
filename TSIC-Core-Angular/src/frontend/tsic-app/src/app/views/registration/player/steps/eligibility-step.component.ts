import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
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
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Eligibility</h5>
      </div>
      <div class="card-body">
        <p class="text-muted small mb-3">
          Select the {{ constraintLabel() }} for each player.
        </p>
        @for (pid of selectedPlayerIds(); track pid) {
          <div class="mb-3">
            <label class="form-label fw-semibold" [for]="'elig-' + pid">
              {{ getPlayerName(pid) }}
            </label>
            <select
              class="form-select"
              [id]="'elig-' + pid"
              [ngModel]="state.eligibility.getEligibilityForPlayer(pid) || ''"
              (ngModelChange)="onEligibilityChange(pid, $event)">
              <option value="">— Select —</option>
              @for (opt of eligibilityOptions(); track opt) {
                <option [value]="opt">{{ opt }}</option>
              }
            </select>
          </div>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EligibilityStepComponent {
    readonly state = inject(PlayerWizardStateService);

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
    }
}
