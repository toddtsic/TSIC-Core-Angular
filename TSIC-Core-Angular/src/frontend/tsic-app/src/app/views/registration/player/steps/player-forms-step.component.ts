import { ChangeDetectionStrategy, Component, inject, signal, output, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, mergeMap, switchMap, takeUntil, filter } from 'rxjs/operators';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { TeamService } from '@views/registration/player/services/team.service';
import { UsLaxValidationService } from '@infrastructure/services/uslax-validation.service';
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
      <div class="card-header card-header-subtle border-0 py-2 d-flex align-items-center">
        <h5 class="mb-0 fw-semibold" style="font-size: var(--font-size-base)">Player Information</h5>
        <span class="wizard-tip-inline">Complete the required fields for each player.</span>
      </div>
      <div class="card-body pt-2">
        <!-- Server validation errors -->
        @if (state.jobCtx.hasServerValidationErrors()) {
          <div class="alert alert-danger mb-2">
            <div class="fw-semibold mb-1">Server Validation Errors</div>
            <ul class="mb-0">
              @for (err of state.jobCtx.getServerValidationErrors(); track err.field) {
                <li>{{ err.message || err.field }}</li>
              }
            </ul>
          </div>
        }

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
                      @if (field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)) {
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
                               [attr.placeholder]="field.placeholder"
                               [class.is-required]="field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)">
                      }
                      @case ('email') {
                        <input type="email" class="field-input"
                               [id]="'field-' + pid + '-' + field.name"
                               [ngModel]="getFieldValue(pid, field.name)"
                               (ngModelChange)="setFieldValue(pid, field.name, $event)"
                               [disabled]="isPlayerLocked(pid)"
                               [attr.placeholder]="field.placeholder"
                               [class.is-required]="field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)">
                      }
                      @default {
                        <input type="text" class="field-input"
                               [id]="'field-' + pid + '-' + field.name"
                               [ngModel]="getFieldValue(pid, field.name)"
                               (ngModelChange)="setFieldValue(pid, field.name, $event)"
                               [disabled]="isPlayerLocked(pid)"
                               [attr.placeholder]="field.placeholder"
                               [class.is-required]="field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)">
                      }
                    }

                    @if (isValidating(pid, field)) {
                      <div class="field-validating">
                        <span class="spinner-border spinner-border-sm"></span>
                        Validating membership…
                      </div>
                    } @else if (getFieldError(pid, field); as error) {
                      @if (isHtmlError(error)) {
                        <div class="field-error field-error-link">
                          <i class="bi bi-exclamation-triangle-fill"></i>
                          Validation failed —
                          <a href="javascript:void(0)" (click)="openErrorPopup(pid, field.name, error)">see details</a>
                        </div>
                      } @else {
                        <div class="field-error">{{ error }}</div>
                      }
                    }

                    @if (errorPopupKey() === pid + ':' + field.name) {
                      <div class="error-popup-overlay" (click)="closeErrorPopup()"></div>
                      <div class="error-popup">
                        <div class="error-popup-header">
                          <span class="fw-semibold">{{ field.label }}</span>
                          <button type="button" class="error-popup-close" (click)="closeErrorPopup()"
                                  aria-label="Close">&times;</button>
                        </div>
                        <div class="error-popup-body" [innerHTML]="errorPopupContent()"></div>
                      </div>
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
      .wizard-tip-inline {
        margin-left: auto;
        font-size: var(--font-size-xs);
        font-style: italic;
        color: var(--brand-text-muted);
      }

      .player-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
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
        padding: var(--space-1) var(--space-3);
        background: rgba(var(--bs-body-color-rgb), 0.025);
        border-bottom: 1px solid var(--border-color);
        flex-wrap: wrap;
      }

      .player-icon {
        font-size: var(--font-size-base);
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

      /* Field grid — 2-column on desktop, 1-column on mobile */
      .field-grid {
        padding: var(--space-2) var(--space-3);
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-1) var(--space-4);
      }

      .field-row {
        display: flex;
        flex-direction: column;
        gap: 1px;
      }

      /* field-label, req-star, field-input, field-select,
         field-error, field-help — defined globally in _forms.scss */

      .field-validating {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-xs);
        color: var(--bs-primary);
        font-weight: var(--font-weight-medium);
        padding-top: 2px;
      }

      .field-error-link {
        display: flex;
        align-items: center;
        gap: var(--space-1);

        a {
          color: var(--bs-primary);
          text-decoration: underline;
          cursor: pointer;
          font-weight: var(--font-weight-medium);
        }
      }

      .error-popup-overlay {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.3);
        z-index: 1050;
      }

      .error-popup {
        position: fixed;
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        z-index: 1051;
        background: var(--brand-surface);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-xl);
        max-width: 520px;
        width: 90vw;
        max-height: 80vh;
        overflow-y: auto;
      }

      .error-popup-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-3) var(--space-4);
        border-bottom: 1px solid var(--border-color);
        color: var(--bs-danger);
        font-size: var(--font-size-sm);
      }

      .error-popup-close {
        background: none;
        border: none;
        font-size: var(--font-size-xl);
        color: var(--brand-text-muted);
        cursor: pointer;
        line-height: 1;
        padding: 0;

        &:hover { color: var(--brand-text); }
      }

      .error-popup-body {
        padding: var(--space-3) var(--space-4);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        line-height: var(--line-height-normal);

        :host ::ng-deep {
          strong { color: var(--bs-danger); }
          ol, ul { padding-left: var(--space-4); margin: var(--space-2) 0; }
          li { margin-bottom: var(--space-1); }
          a { color: var(--bs-primary); text-decoration: underline; }
        }
      }

      @media (max-width: 575.98px) {
        .field-grid {
          grid-template-columns: 1fr;
          padding: var(--space-1) var(--space-2);
        }

        .player-header {
          padding: var(--space-1) var(--space-2);
        }

        .wizard-tip-inline {
          display: none;
        }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerFormsStepComponent implements OnDestroy {
    readonly advance = output<void>();
    readonly state = inject(PlayerWizardStateService);
    private readonly usLaxService = inject(UsLaxValidationService);
    private readonly usLaxTrigger$ = new Subject<{ playerId: string; value: string; field: PlayerProfileFieldSchema }>();
    private readonly destroy$ = new Subject<void>();
    private _autoAdvanceTimer: ReturnType<typeof setTimeout> | null = null;

    constructor() {
        // Debounced US Lax API validation stream
        this.usLaxTrigger$.pipe(
            debounceTime(800),
            filter(({ value }) => value.length >= 6),
            mergeMap(({ playerId, value, field }) => {
                this.state.playerForms.setUsLaxValidating(playerId);
                return this.usLaxService.verify(value).pipe(
                    takeUntil(this.destroy$),
                    switchMap(member => {
                        if (!member) {
                            this.state.playerForms.setUsLaxResult(playerId, false, field.errorMessage || 'Member not found');
                            return [];
                        }
                        // Check expiration against job's validThroughDate
                        const validThrough = this.state.jobCtx.getUsLaxValidThroughDate();
                        if (validThrough && member.exp_date) {
                            const expDate = new Date(member.exp_date);
                            if (expDate < validThrough) {
                                this.state.playerForms.setUsLaxResult(playerId, false,
                                    `Membership expires ${expDate.toLocaleDateString()} — must be valid through ${validThrough.toLocaleDateString()}`);
                                return [];
                            }
                        }
                        // Check membership status
                        const status = (member.mem_status || '').toLowerCase();
                        if (status !== 'active' && status !== 'valid') {
                            this.state.playerForms.setUsLaxResult(playerId, false,
                                field.errorMessage || `Membership status: ${member.mem_status}`);
                            return [];
                        }
                        this.state.playerForms.setUsLaxResult(playerId, true, undefined, member as unknown as Record<string, unknown>);
                        return [];
                    }),
                );
            }),
            takeUntil(this.destroy$),
        ).subscribe({
            error: (err) => console.warn('[USLax] Validation stream error', err),
        });

        // Auto-validate prefilled US Lax numbers — retry briefly if schemas not ready yet
        this.validatePrefilled();
        setTimeout(() => this.validatePrefilled(), 2000);
    }

    /** Kick off API validation for any prefilled SportAssnId values that haven't been validated yet. */
    private validatePrefilled(): void {
        const schemas = this.state.jobCtx.profileFieldSchemas();
        const usLaxField = schemas.find(f => this.state.playerForms.isUsLaxSchemaField(f) && f.remoteUrl);
        if (!usLaxField) return;
        for (const pid of this.state.familyPlayers.selectedPlayerIds()) {
            if (this.state.familyPlayers.isPlayerLocked(pid)) continue;
            const status = this.state.playerForms.usLaxStatus()[pid]?.status;
            if (status === 'valid' || status === 'validating') continue;
            const val = String(this.state.playerForms.getPlayerFieldValue(pid, usLaxField.name) ?? '').trim();
            if (val.length >= 6 && val !== '424242424242') {
                this.usLaxTrigger$.next({ playerId: pid, value: val, field: usLaxField });
            }
        }
    }

    ngOnDestroy(): void {
        if (this._autoAdvanceTimer) clearTimeout(this._autoAdvanceTimer);
        this.destroy$.next();
        this.destroy$.complete();
    }

    /** Auto-advance after 500ms if all visible fields are valued across all unlocked players. */
    private checkAutoAdvance(): void {
        if (this._autoAdvanceTimer) clearTimeout(this._autoAdvanceTimer);
        const allComplete = this.selectedPlayerIds()
            .filter(pid => !this.isPlayerLocked(pid))
            .every(pid => this.visibleFields(pid).every(f => this.hasValue(pid, f.name)));
        if (allComplete) {
            this._autoAdvanceTimer = setTimeout(() => this.advance.emit(), 500);
        }
    }

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
        if (t === 'text') {
            const n = field.name.toLowerCase();
            const l = field.label.toLowerCase();
            if (n.includes('email') || l.includes('email')) return 'email';
        }
        return 'text';
    }

    isValidating(playerId: string, field: PlayerProfileFieldSchema): boolean {
        if (!this.state.playerForms.isUsLaxSchemaField(field) || !field.remoteUrl) return false;
        return this.state.playerForms.usLaxStatus()[playerId]?.status === 'validating';
    }

    getFieldValue(playerId: string, fieldName: string): unknown {
        return this.state.playerForms.getPlayerFieldValue(playerId, fieldName) ?? '';
    }

    setFieldValue(playerId: string, fieldName: string, value: unknown): void {
        this.state.playerForms.setPlayerFieldValue(playerId, fieldName, value as PlayerFormFieldValue);
        // Trigger debounced US Lax API validation if this field has a remoteUrl
        const field = this.state.jobCtx.profileFieldSchemas().find(f => f.name === fieldName);
        if (field?.remoteUrl && this.state.playerForms.isUsLaxSchemaField(field)) {
            const str = String(value ?? '').trim();
            if (str.length >= 6) {
                this.usLaxTrigger$.next({ playerId, value: str, field });
            }
        }
        this.checkAutoAdvance();
    }

    onCheckboxChange(playerId: string, fieldName: string, event: Event): void {
        const checked = (event.target as HTMLInputElement).checked;
        this.state.playerForms.setPlayerFieldValue(playerId, fieldName, checked);
        this.checkAutoAdvance();
    }

    hasValue(playerId: string, fieldName: string): boolean {
        const v = this.state.playerForms.getPlayerFieldValue(playerId, fieldName);
        if (v === null || v === undefined) return false;
        if (typeof v === 'string') return v.trim().length > 0;
        if (typeof v === 'boolean') return v;
        if (Array.isArray(v)) return v.length > 0;
        return true;
    }

    getFieldError(playerId: string, field: PlayerProfileFieldSchema): string | null {
        if (this.isPlayerLocked(playerId)) return null;
        // Only show errors once the field has been touched
        const raw = this.state.playerForms.getPlayerFieldValue(playerId, field.name);
        if (raw === null || raw === undefined) return null;
        const wfn = this.state.jobCtx.waiverFieldNames();
        const tct = this.state.eligibility.teamConstraintType();
        return this.state.playerForms.getFieldError(
            playerId, field,
            pid => this.state.familyPlayers.isPlayerLocked(pid),
            (pid, f) => this.state.playerForms.isFieldVisibleForPlayer(pid, f, wfn, tct),
        );
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
        this.checkAutoAdvance();
    }

    isHtmlError(error: string): boolean {
        return error.includes('<') && error.includes('>');
    }

    // ── Error popup ──────────────────────────────────────────────────
    readonly errorPopupKey = signal('');
    readonly errorPopupContent = signal('');

    openErrorPopup(playerId: string, fieldName: string, html: string): void {
        this.errorPopupKey.set(`${playerId}:${fieldName}`);
        this.errorPopupContent.set(html);
    }

    closeErrorPopup(): void {
        this.errorPopupKey.set('');
        this.errorPopupContent.set('');
    }

    private readonly teamService = inject(TeamService);
}
