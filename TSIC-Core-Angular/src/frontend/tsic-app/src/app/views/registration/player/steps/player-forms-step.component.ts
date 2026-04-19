import { ChangeDetectionStrategy, Component, computed, inject, signal, output, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { Subject } from 'rxjs';
import { debounceTime, mergeMap, switchMap, takeUntil, filter } from 'rxjs/operators';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { TeamService } from '@views/registration/player/services/team.service';
import { UsLaxValidationService } from '@infrastructure/services/uslax-validation.service';
import { colorClassForIndex } from '@views/registration/shared/utils/color-class.util';
import { JobService } from '@infrastructure/services/job.service';
import type { PlayerProfileFieldSchema, PlayerFormFieldValue } from '../types/player-wizard.types';

const JOB_TYPE_TOURNAMENT = 2;

// PP20 canonical recruiting field order (lowercase schema name).
// On tournament sites, these are hoisted into a single fieldset anchored at
// the position of the first canonical field present in the editor schema.
const RECRUITING_ORDER: readonly string[] = [
    'gpa', 'classrank', 'act',
    'satmath', 'satverbal', 'satwriting',
    'weightlbs', 'heightinches',
    'bcollegecommit', 'collegecommit',
];

type FieldGroup = { kind: 'plain' | 'recruiting'; fields: PlayerProfileFieldSchema[] };

/**
 * Player Forms step — renders dynamic form fields per player
 * based on the profile field schemas from job metadata.
 */
@Component({
    selector: 'app-prw-player-forms-step',
    standalone: true,
    imports: [FormsModule, NgTemplateOutlet],
    template: `
    <!-- Centered hero -->
    <div class="welcome-hero">
      <h4 class="welcome-title"><i class="bi bi-card-checklist welcome-icon"></i> Player Details</h4>
      <p class="welcome-desc">
        <i class="bi bi-pencil-square me-1"></i>Complete required fields
        <span class="desc-dot"></span>
        <i class="bi bi-save me-1"></i>Info saved to your account
        <span class="desc-dot"></span>
        <i class="bi bi-person me-1"></i>One form per player
      </p>
    </div>

    <div class="card shadow border-0 card-rounded">
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
                <div class="player-header-top">
                  <i class="bi player-icon"
                     [class.bi-person-fill]="!isRegistered(pid)"
                     [class.bi-person-check-fill]="isRegistered(pid)"></i>
                  <span class="player-name">{{ getPlayerName(pid) }}</span>
                  @if (isRegistered(pid)) {
                    <span class="reg-badge"><i class="bi bi-lock-fill me-1"></i>Registered</span>
                  }
                </div>
                @if (getTeamIds(pid).length) {
                  @if (state.jobCtx.isCacMode() && getTeamIds(pid).length > 1) {
                    <button type="button" class="events-summary"
                            (click)="toggleEventsList(pid)">
                      <i class="bi me-1"
                         [class.bi-chevron-right]="!isEventsExpanded(pid)"
                         [class.bi-chevron-down]="isEventsExpanded(pid)"></i>
                      <i class="bi bi-calendar-event me-1"></i>{{ getTeamIds(pid).length }} events selected
                    </button>
                    @if (isEventsExpanded(pid)) {
                      <ul class="events-list">
                        @for (tid of getTeamIds(pid); track tid) {
                          <li>{{ getTeamName(tid) }}</li>
                        }
                      </ul>
                    }
                  } @else {
                    <div class="team-pill-row">
                      @for (tid of getTeamIds(pid); track tid) {
                        <span class="badge bg-primary-subtle text-primary-emphasis border border-primary-subtle">{{ getTeamPillLabel(tid) }}</span>
                      }
                    </div>
                  }
                }
              </div>

              <!-- Dynamic form fields -->
              <div class="field-grid">
                @for (group of visibleFieldGroups(pid); track $index) {
                  @if (group.kind === 'recruiting') {
                    <fieldset class="recruiting-fieldset">
                      <legend>College Recruiting</legend>
                      <div class="recruiting-tip">Leave ANY field blank if unknown</div>
                      <div class="fieldset-grid">
                        @for (field of group.fields; track field.name) {
                          <ng-container *ngTemplateOutlet="fieldRowTpl; context: { $implicit: field, pid: pid }"></ng-container>
                        }
                      </div>
                    </fieldset>
                  } @else {
                    @for (field of group.fields; track field.name) {
                      <ng-container *ngTemplateOutlet="fieldRowTpl; context: { $implicit: field, pid: pid }"></ng-container>
                    }
                  }
                }
              </div>
            </div>
          }
        </div>
      </div>
    </div>

    <!-- Shared field-row template (used by both plain and recruiting groups) -->
    <ng-template #fieldRowTpl let-field let-pid="pid">
      <div class="field-row" [class.field-row--wide]="getFieldType(field) === 'textarea'">
        @if (getFieldType(field) !== 'checkbox') {
          <label class="field-label" [for]="'field-' + pid + '-' + field.name">
            {{ field.label }}
            @if (field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)) {
              <span class="req-star">*</span>
            }
          </label>
        }

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
                @if (field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)) {
                  <span class="req-star">*</span>
                }
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
          @case ('textarea') {
            <textarea class="field-input"
                   [id]="'field-' + pid + '-' + field.name"
                   [ngModel]="getFieldValue(pid, field.name)"
                   (ngModelChange)="setFieldValue(pid, field.name, $event)"
                   [disabled]="isPlayerLocked(pid)"
                   [attr.placeholder]="field.placeholder"
                   [class.is-required]="field.required && !isPlayerLocked(pid) && !hasValue(pid, field.name)"
                   rows="5" style="resize: vertical;"></textarea>
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
    </ng-template>
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
        flex-direction: column;
        gap: var(--space-1);
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-body-color-rgb), 0.025);
        border-bottom: 1px solid var(--border-color);
      }

      .player-header-top {
        display: flex;
        align-items: center;
        gap: var(--space-2);
      }

      .team-pill-row {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
        padding-left: calc(var(--font-size-base) + var(--space-2));
      }

      .events-summary {
        display: inline-flex;
        align-items: center;
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        color: var(--bs-primary);
        padding-left: calc(var(--font-size-base) + var(--space-2));
        background: none;
        border: none;
        cursor: pointer;
        padding-top: 0;
        padding-bottom: 0;

        &:hover { text-decoration: underline; }
      }

      .events-list {
        list-style: none;
        margin: var(--space-1) 0 0;
        padding-left: calc(var(--font-size-base) + var(--space-2) + var(--space-4));
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);

        li {
          padding: 2px 0;
          &::before {
            content: '•';
            color: var(--bs-primary);
            margin-right: var(--space-2);
          }
        }
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
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        padding: 2px var(--space-3);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.18);
        color: var(--bs-primary);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.5);
      }

      .reg-badge {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
        margin-left: auto;
      }

      /* Field grid — 2-column on desktop, 1-column on mobile */
      .field-grid {
        padding: var(--space-3);
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-1) var(--space-4);
        background: rgba(var(--bs-primary-rgb), 0.04);

        .field-input,
        .field-select {
          background-color: var(--neutral-0);
        }
      }

      .field-row {
        display: flex;
        flex-direction: column;
        gap: 1px;
      }
      .field-row--wide {
        grid-column: 1 / -1;
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

      /* Recruiting fieldset — spans both columns of .field-grid,
         contains its own 2-column sub-grid for the canonical PP20 fields. */
      .recruiting-fieldset {
        grid-column: 1 / -1;
        border: 1px solid var(--bs-primary);
        border-radius: var(--radius-sm);
        padding: var(--space-1) var(--space-3) var(--space-2);
        margin: var(--space-2) 0;
        background: var(--neutral-0);

        legend {
          float: none;
          width: auto;
          font-size: var(--font-size-sm);
          font-weight: var(--font-weight-semibold);
          color: var(--bs-primary);
          padding: 0 var(--space-2);
          margin-bottom: 0;
        }
      }

      .recruiting-tip {
        font-size: var(--font-size-xs);
        font-style: italic;
        color: var(--brand-text-muted);
        margin: 0 0 var(--space-2);
      }

      .fieldset-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-1) var(--space-4);
      }

      @media (max-width: 575.98px) {
        .field-grid {
          grid-template-columns: 1fr;
          padding: var(--space-1) var(--space-2);
        }

        .fieldset-grid {
          grid-template-columns: 1fr;
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
    private readonly jobService = inject(JobService);
    readonly isTournament = computed(() => this.jobService.currentJob()?.jobTypeId === JOB_TYPE_TOURNAMENT);
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

    isPlayerLocked(_playerId: string): boolean {
        // Forms are always editable — families can update profile fields
        // (jersey size, school, uniform#, etc.) even for registered players.
        return false;
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

    getTeamPillLabel(teamId: string): string {
        const team = this.teamService.getTeamById(teamId);
        if (!team) return teamId;
        const club = team.clubName?.trim();
        return club ? `${club}:${team.teamName}` : team.teamName;
    }

    // ── CAC events expand/collapse ───────────────────────────────────
    private readonly _expandedEvents = signal<Record<string, boolean>>({});

    isEventsExpanded(playerId: string): boolean {
        return !!this._expandedEvents()[playerId];
    }

    toggleEventsList(playerId: string): void {
        this._expandedEvents.set({
            ...this._expandedEvents(),
            [playerId]: !this._expandedEvents()[playerId],
        });
    }

    /** Returns visible profile fields for a given player. */
    visibleFields(playerId: string): PlayerProfileFieldSchema[] {
        const schemas = this.state.jobCtx.profileFieldSchemas();
        const wfn = this.state.jobCtx.waiverFieldNames();
        const tct = this.state.eligibility.teamConstraintType();
        const tournament = this.isTournament();
        const recruitingGradYears = tournament ? this.state.jobCtx.recruitingGradYears() : [];
        const eligValue = this.state.eligibility.getEligibilityForPlayer(playerId) ?? null;
        const playerGradYear = tournament
            ? this.state.playerForms.getPlayerGradYearFromState(playerId, schemas, tct, eligValue)
            : null;
        return schemas.filter(f => this.state.playerForms.isFieldVisibleForPlayer(
            playerId, f, wfn, tct, tournament, recruitingGradYears, playerGradYear,
        ));
    }

    /**
     * SP-040: On tournament sites, hoist recruiting fields into a single fieldset.
     * The fieldset is anchored at the position of the first canonical recruiting
     * field (per PP20 order) that appears in the editor schema; its contents are
     * rendered in canonical PP20 order regardless of editor order.
     */
    visibleFieldGroups(playerId: string): FieldGroup[] {
        const visible = this.visibleFields(playerId);
        if (!this.isTournament()) return [{ kind: 'plain', fields: visible }];

        const visibleByLName = new Map<string, PlayerProfileFieldSchema>();
        for (const f of visible) visibleByLName.set(f.name.toLowerCase(), f);

        const recruitingPresent = RECRUITING_ORDER.filter(n => visibleByLName.has(n));
        if (recruitingPresent.length === 0) return [{ kind: 'plain', fields: visible }];

        const recruitingSet = new Set(recruitingPresent);
        const anchorIndex = visible.findIndex(f => recruitingSet.has(f.name.toLowerCase()));
        if (anchorIndex < 0) return [{ kind: 'plain', fields: visible }];

        const recruitingFields = recruitingPresent.map(n => visibleByLName.get(n)!);
        const beforeAnchor = visible.slice(0, anchorIndex)
            .filter(f => !recruitingSet.has(f.name.toLowerCase()));
        const afterAnchor = visible.slice(anchorIndex + 1)
            .filter(f => !recruitingSet.has(f.name.toLowerCase()));

        const groups: FieldGroup[] = [];
        if (beforeAnchor.length) groups.push({ kind: 'plain', fields: beforeAnchor });
        groups.push({ kind: 'recruiting', fields: recruitingFields });
        if (afterAnchor.length) groups.push({ kind: 'plain', fields: afterAnchor });
        return groups;
    }

    getFieldType(field: PlayerProfileFieldSchema): string {
        const t = (field.type || 'text').toLowerCase();
        if (t === 'select' || t === 'dropdown') return 'select';
        if (t === 'checkbox') return 'checkbox';
        if (t === 'multiselect' || t === 'multi-select') return 'multiselect';
        if (t === 'textarea') return 'textarea';
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
