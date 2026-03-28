import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { TeamService } from '@views/registration/player/services/team.service';
import { colorClassForIndex } from '@views/registration/shared/utils/color-class.util';
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
      <div class="card-header card-header-subtle border-0 py-2">
        <h5 class="mb-0 fw-semibold" style="font-size: var(--font-size-base)">Player Information</h5>
      </div>
      <div class="card-body pt-3">
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

        <p class="wizard-tip">Complete the required fields for each player.</p>

        <div class="player-list">
          @for (pid of selectedPlayerIds(); track pid; let i = $index) {
            <div class="player-section" [class.is-locked]="isPlayerLocked(pid)">
              <!-- Player header -->
              <div class="player-header">
                <i class="bi player-icon"
                   [class.bi-person-fill]="!isRegistered(pid)"
                   [class.bi-person-check-fill]="isRegistered(pid)"></i>
                <span class="player-name">{{ getPlayerName(pid) }}</span>
                @for (tid of getTeamIds(pid); track tid) {
                  <span class="team-pill">{{ getTeamName(tid) }}</span>
                }
                @if (isRegistered(pid)) {
                  <span class="reg-badge"><i class="bi bi-lock-fill me-1"></i>Registered</span>
                }
              </div>

              <!-- Dynamic form fields -->
              <div class="field-grid">
                @for (field of visibleFields(pid); track field.name) {
                  <div class="field-row">
                    <label class="field-label" [for]="'field-' + pid + '-' + field.name">
                      {{ field.label }}
                      @if (field.required && !isPlayerLocked(pid)) {
                        <span class="req-star">*</span>
                      }
                    </label>

                    @switch (getFieldType(field)) {
                      @case ('select') {
                        <select class="field-input field-select"
                                [id]="'field-' + pid + '-' + field.name"
                                [ngModel]="getFieldValue(pid, field.name)"
                                (ngModelChange)="setFieldValue(pid, field.name, $event)"
                                [disabled]="isPlayerLocked(pid)"
                                [class.is-empty]="!hasValue(pid, field.name)"
                                [class.is-required]="field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)">
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
                        <input type="date" class="field-input"
                               [id]="'field-' + pid + '-' + field.name"
                               [ngModel]="getFieldValue(pid, field.name)"
                               (ngModelChange)="setFieldValue(pid, field.name, $event)"
                               [disabled]="isPlayerLocked(pid)"
                               [class.is-required]="field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)">
                      }
                      @case ('number') {
                        <input type="number" class="field-input"
                               [id]="'field-' + pid + '-' + field.name"
                               [ngModel]="getFieldValue(pid, field.name)"
                               (ngModelChange)="setFieldValue(pid, field.name, $event)"
                               [disabled]="isPlayerLocked(pid)"
                               [class.is-required]="field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)">
                      }
                      @default {
                        <input type="text" class="field-input"
                               [id]="'field-' + pid + '-' + field.name"
                               [ngModel]="getFieldValue(pid, field.name)"
                               (ngModelChange)="setFieldValue(pid, field.name, $event)"
                               [disabled]="isPlayerLocked(pid)"
                               [class.is-required]="field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)">
                      }
                    }

                    @if (field.helpText) {
                      <div class="field-help">{{ field.helpText }}</div>
                    }
                  </div>
                }
              </div>
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
        gap: var(--space-3);
      }

      .player-section {
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        overflow: hidden;

        &.is-locked {
          border-color: rgba(var(--bs-success-rgb), 0.25);
          background: rgba(var(--bs-success-rgb), 0.03);
          opacity: 0.75;
        }
      }

      .player-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-body-color-rgb), 0.025);
        border-bottom: 1px solid var(--border-color);
        flex-wrap: wrap;
      }

      .player-icon {
        font-size: var(--font-size-lg);
        color: var(--neutral-400);

        .is-locked & { color: var(--bs-success); }
      }

      .player-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .team-pill {
        font-size: 10px;
        font-weight: var(--font-weight-medium);
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.2);
      }

      .reg-badge {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
        margin-left: auto;
      }

      /* Field grid — compact label + input rows */
      .field-grid {
        padding: var(--space-2) var(--space-3);
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      .field-row {
        display: flex;
        flex-direction: column;
        gap: 2px;
      }

      .field-label {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text-muted);
        text-transform: uppercase;
        letter-spacing: 0.03em;
      }

      .req-star {
        color: var(--bs-danger);
        font-weight: var(--font-weight-bold);
      }

      .field-input {
        width: 100%;
        padding: var(--space-1) var(--space-2);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        background-color: var(--neutral-50);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-sm);
        transition: border-color 0.15s ease, box-shadow 0.15s ease, background-color 0.15s ease;

        &:hover:not(:focus):not(:disabled) {
          border-color: var(--neutral-400);
        }

        &:focus {
          outline: none;
          border-color: var(--bs-primary);
          background-color: var(--brand-surface);
          box-shadow: var(--shadow-focus);
        }

        &:disabled {
          opacity: 0.6;
          cursor: not-allowed;
        }

        /* Subtle left-border accent on unfilled required fields */
        &.is-required {
          border-left: 3px solid rgba(var(--bs-danger-rgb), 0.4);
        }
      }

      .field-select {
        appearance: none;
        padding-right: var(--space-6);
        background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3e%3cpath fill='none' stroke='%2378716c' stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='m2 5 6 6 6-6'/%3e%3c/svg%3e");
        background-repeat: no-repeat;
        background-position: right var(--space-2) center;
        background-size: 12px 8px;
      }

      .field-help {
        font-size: var(--font-size-xs);
        color: var(--neutral-400);
        font-style: italic;
      }

      /* Mobile: even tighter */
      @media (max-width: 575.98px) {
        .field-grid {
          padding: var(--space-1) var(--space-2);
        }

        .player-header {
          padding: var(--space-1) var(--space-2);
        }
      }
    `],
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
