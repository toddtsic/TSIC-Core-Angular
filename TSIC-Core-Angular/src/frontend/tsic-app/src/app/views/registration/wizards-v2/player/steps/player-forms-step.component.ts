import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { TeamService } from '@views/registration/wizards/player-registration-wizard/team.service';
import { colorClassForIndex } from '@views/registration/wizards/shared/utils/color-class.util';
import type { PlayerProfileFieldSchema, PlayerFormFieldValue } from '../types/player-wizard.types';

/**
 * Player Forms step — renders dynamic form fields per player
 * based on the profile field schemas from job metadata.
 */
@Component({
    selector: 'app-prw-player-forms-step',
    standalone: true,
    imports: [FormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Player Information</h5>
      </div>
      <div class="card-body">
        <!-- Server validation errors -->
        @if (state.jobCtx.hasServerValidationErrors()) {
          <div class="alert alert-danger mb-3">
            <div class="fw-semibold mb-1">Server Validation Errors</div>
            <ul class="mb-0">
              @for (err of state.jobCtx.getServerValidationErrors(); track err.field) {
                <li>{{ err.message || err.field }}</li>
              }
            </ul>
          </div>
        }

        @for (pid of selectedPlayerIds(); track pid; let i = $index) {
          <div class="mb-4 p-3 rounded-3 border">
            <div class="d-flex align-items-center gap-2 mb-3">
              <span class="badge" [class]="getPlayerBadgeClass(i)">
                {{ getPlayerName(pid) }}
              </span>
              @if (isRegistered(pid)) {
                <span class="badge bg-success">Registered</span>
              }
              <!-- Team pills (read-only display) -->
              @for (tid of getTeamIds(pid); track tid) {
                <span class="badge bg-primary-subtle text-primary-emphasis border border-primary-subtle small">
                  {{ getTeamName(tid) }}
                </span>
              }
            </div>

            <!-- Dynamic form fields -->
            @for (field of visibleFields(pid); track field.name) {
              <div class="mb-3">
                <label class="form-label fw-semibold" [for]="'field-' + pid + '-' + field.name">
                  {{ field.label }}
                  @if (field.required && !isPlayerLocked(pid)) {
                    <span class="text-danger">*</span>
                  }
                </label>

                @switch (getFieldType(field)) {
                  @case ('select') {
                    <select class="form-select"
                            [id]="'field-' + pid + '-' + field.name"
                            [ngModel]="getFieldValue(pid, field.name)"
                            (ngModelChange)="setFieldValue(pid, field.name, $event)"
                            [disabled]="isPlayerLocked(pid)">
                      <option value="">— Select —</option>
                      @for (opt of field.options; track opt) {
                        <option [value]="opt">{{ opt }}</option>
                      }
                    </select>
                  }
                  @case ('checkbox') {
                    <div class="form-check">
                      <input class="form-check-input" type="checkbox"
                             [id]="'field-' + pid + '-' + field.name"
                             [checked]="getFieldValue(pid, field.name) === true || getFieldValue(pid, field.name) === 'true'"
                             (change)="onCheckboxChange(pid, field.name, $event)"
                             [disabled]="isPlayerLocked(pid)">
                      <label class="form-check-label" [for]="'field-' + pid + '-' + field.name">
                        {{ field.label }}
                      </label>
                    </div>
                  }
                  @case ('multiselect') {
                    <div class="d-flex flex-wrap gap-2">
                      @for (opt of field.options; track opt) {
                        <div class="form-check">
                          <input class="form-check-input" type="checkbox"
                                 [id]="'field-' + pid + '-' + field.name + '-' + opt"
                                 [checked]="isMultiOptionSelected(pid, field.name, opt)"
                                 (change)="toggleMultiOption(pid, field.name, opt, $event)"
                                 [disabled]="isPlayerLocked(pid)">
                          <label class="form-check-label" [for]="'field-' + pid + '-' + field.name + '-' + opt">
                            {{ opt }}
                          </label>
                        </div>
                      }
                    </div>
                  }
                  @case ('date') {
                    <input type="date" class="form-control"
                           [id]="'field-' + pid + '-' + field.name"
                           [ngModel]="getFieldValue(pid, field.name)"
                           (ngModelChange)="setFieldValue(pid, field.name, $event)"
                           [disabled]="isPlayerLocked(pid)">
                  }
                  @case ('number') {
                    <input type="number" class="form-control"
                           [id]="'field-' + pid + '-' + field.name"
                           [ngModel]="getFieldValue(pid, field.name)"
                           (ngModelChange)="setFieldValue(pid, field.name, $event)"
                           [disabled]="isPlayerLocked(pid)">
                  }
                  @default {
                    <input type="text" class="form-control"
                           [id]="'field-' + pid + '-' + field.name"
                           [ngModel]="getFieldValue(pid, field.name)"
                           (ngModelChange)="setFieldValue(pid, field.name, $event)"
                           [disabled]="isPlayerLocked(pid)">
                  }
                }

                <!-- Field validation badge -->
                @if (field.required && !isPlayerLocked(pid)) {
                  @if (hasValue(pid, field.name)) {
                    <span class="badge bg-success-subtle text-success-emphasis mt-1">&#10003;</span>
                  } @else {
                    <span class="badge bg-danger-subtle text-danger-emphasis mt-1">Required</span>
                  }
                }

                <!-- Field help text -->
                @if (field.helpText) {
                  <div class="form-text">{{ field.helpText }}</div>
                }
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerFormsStepComponent {
    readonly state = inject(PlayerWizardStateService);

    selectedPlayerIds(): string[] {
        return this.state.familyPlayers.selectedPlayerIds();
    }

    getPlayerName(playerId: string): string {
        const p = this.state.familyPlayers.familyPlayers().find(fp => fp.playerId === playerId);
        return p ? `${p.firstName} ${p.lastName}`.trim() : playerId;
    }

    getPlayerBadgeClass(index: number): string {
        return colorClassForIndex(index);
    }

    isRegistered(playerId: string): boolean {
        const p = this.state.familyPlayers.familyPlayers().find(fp => fp.playerId === playerId);
        return !!p?.registered;
    }

    isPlayerLocked(playerId: string): boolean {
        return this.state.familyPlayers.isPlayerLocked(playerId);
    }

    getTeamIds(playerId: string): string[] {
        const sel = this.state.eligibility.selectedTeams()[playerId];
        if (!sel) return [];
        return Array.isArray(sel) ? sel : [sel];
    }

    getTeamName(teamId: string): string {
        const team = this.teamService.getTeamById(teamId);
        return team?.teamName || teamId;
    }

    /** Returns visible profile fields for a given player. */
    visibleFields(playerId: string): PlayerProfileFieldSchema[] {
        const schemas = this.state.jobCtx.profileFieldSchemas();
        const wfn = this.state.jobCtx.waiverFieldNames();
        const tct = this.state.eligibility.teamConstraintType();
        return schemas.filter(f => this.state.playerForms.isFieldVisibleForPlayer(playerId, f, wfn, tct));
    }

    getFieldType(field: PlayerProfileFieldSchema): string {
        const t = (field.type || 'text').toLowerCase();
        if (t === 'select' || t === 'dropdown') return 'select';
        if (t === 'checkbox') return 'checkbox';
        if (t === 'multiselect' || t === 'multi-select') return 'multiselect';
        if (t === 'date') return 'date';
        if (t === 'number' || t === 'numeric') return 'number';
        return 'text';
    }

    getFieldValue(playerId: string, fieldName: string): unknown {
        return this.state.playerForms.getPlayerFieldValue(playerId, fieldName) ?? '';
    }

    setFieldValue(playerId: string, fieldName: string, value: unknown): void {
        this.state.playerForms.setPlayerFieldValue(playerId, fieldName, value as PlayerFormFieldValue);
    }

    onCheckboxChange(playerId: string, fieldName: string, event: Event): void {
        const checked = (event.target as HTMLInputElement).checked;
        this.state.playerForms.setPlayerFieldValue(playerId, fieldName, checked);
    }

    hasValue(playerId: string, fieldName: string): boolean {
        const v = this.state.playerForms.getPlayerFieldValue(playerId, fieldName);
        if (v === null || v === undefined) return false;
        if (typeof v === 'string') return v.trim().length > 0;
        if (typeof v === 'boolean') return v;
        if (Array.isArray(v)) return v.length > 0;
        return true;
    }

    isMultiOptionSelected(playerId: string, fieldName: string, option: string): boolean {
        const v = this.state.playerForms.getPlayerFieldValue(playerId, fieldName);
        if (Array.isArray(v)) return v.includes(option);
        if (typeof v === 'string') return v.split(',').map(s => s.trim()).includes(option);
        return false;
    }

    toggleMultiOption(playerId: string, fieldName: string, option: string, event: Event): void {
        const checked = (event.target as HTMLInputElement).checked;
        const current = this.state.playerForms.getPlayerFieldValue(playerId, fieldName);
        let arr: string[];
        if (Array.isArray(current)) arr = [...current];
        else if (typeof current === 'string' && current) arr = current.split(',').map(s => s.trim());
        else arr = [];

        if (checked && !arr.includes(option)) arr.push(option);
        else if (!checked) arr = arr.filter(v => v !== option);

        this.state.playerForms.setPlayerFieldValue(playerId, fieldName, arr);
    }

    private readonly teamService = inject(TeamService);
}
