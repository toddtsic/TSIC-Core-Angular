import { AfterViewInit, ChangeDetectionStrategy, Component, QueryList, ViewChildren, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MultiSelectModule, MultiSelectComponent, CheckBoxSelectionService } from '@syncfusion/ej2-angular-dropdowns';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';
import type { JobRegFieldDto } from '@core/api';

/**
 * Profile step — role-config-driven.
 *
 * Reads fields from <c>state.roleConfig().profileFields</c>. Shows the teams
 * multi-select whenever the coach may make team REQUESTS — i.e. when
 * <c>needsTeamSelection</c> (Tournament: request required) OR
 * <c>allowTeamRequests</c> (Club/League: request optional). Every coach is an
 * UnassignedAdult; selections are non-binding requests the director approves —
 * never an assignment or roster/PII access.
 */
@Component({
    selector: 'app-adult-profile-step',
    standalone: true,
    imports: [FormsModule, MultiSelectModule],
    providers: [CheckBoxSelectionService],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <!-- Centered hero -->
        <div class="welcome-hero">
            <h4 class="welcome-title">
                <i class="bi bi-person-lines-fill welcome-icon" style="color: var(--bs-primary)"></i>
                {{ titleLabel() }}
            </h4>
            <p class="welcome-desc">
                <i class="bi bi-pencil-square me-1"></i>Tell us a bit about you
                <span class="desc-dot"></span>
                <i class="bi bi-check-circle me-1"></i>Required fields marked with <span class="req">*</span>
            </p>
        </div>

        <div class="card shadow border-0 card-rounded">
          <div class="card-body">
            @if (state.showTeamPicker()) {
                <section class="profile-section"
                    [class.is-required-section]="state.needsTeamSelection() && state.teamIdsCoaching().length === 0">
                    @if (state.needsTeamSelection()) {
                        <label class="field-label">
                            Teams you'd like to coach <span class="req">*</span>
                        </label>
                        <small class="wizard-tip mb-2 d-block">
                            Required — select every team you'd like to coach. Type a club or team name to
                            filter. The director reviews your request and assigns you; choosing teams here
                            does <strong>not</strong> add you to a roster or grant access to any roster.
                        </small>
                    } @else {
                        <label class="field-label">
                            Teams you'd like to coach
                        </label>
                        <small class="wizard-tip mb-2 d-block">
                            Optional — select any teams you're interested in. The director reviews every
                            request and assigns you. Choosing teams here does <strong>not</strong> add you
                            to a roster or grant access to any roster.
                        </small>
                    }

                    @if (state.teamsLoading()) {
                        <div class="text-center py-3">
                            <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                            <span class="ms-2 text-muted">Loading teams...</span>
                        </div>
                    } @else if (state.teamsError()) {
                        <div class="alert alert-danger" role="alert">{{ state.teamsError() }}</div>
                    } @else if (state.availableTeams().length === 0) {
                        <div class="alert alert-warning" role="note">
                            @if (state.needsTeamSelection()) {
                                No teams are registered yet for this event. Contact the tournament director.
                            } @else {
                                No teams are registered yet for this event. You can still complete your
                                registration — the director will review and assign you.
                            }
                        </div>
                    } @else {
                        <ejs-multiselect
                            [dataSource]="teamsDataSource()"
                            [fields]="teamFields"
                            [value]="state.teamPickerSeed()"
                            (change)="onTeamsChange($event.value)"
                            [mode]="'CheckBox'"
                            [allowFiltering]="true"
                            [filterBarPlaceholder]="'Search by club or team...'"
                            [showSelectAll]="false"
                            [closePopupOnSelect]="false"
                            [changeOnBlur]="false"
                            [showDropDownIcon]="true"
                            [placeholder]="'Select teams you\\'d like to coach'"
                            [popupHeight]="'320px'">
                        </ejs-multiselect>

                        @if (state.teamIdsCoaching().length > 0) {
                            <div class="selected-teams mt-2">
                                <strong class="small">Selected ({{ state.teamIdsCoaching().length }}):</strong>
                                <ul class="small mb-0">
                                    @for (t of selectedTeamsSorted(); track t.id) {
                                        <li>{{ t.label }}</li>
                                    }
                                </ul>
                            </div>
                        }
                    }
                </section>
            }

            <section class="profile-section">
                <div class="row g-3">
                    @for (field of state.profileFields(); track field.name) {
                        @if (shouldShowField(field)) {
                            <div [class]="getFieldColClass(field)">
                                <label class="field-label">
                                    {{ field.displayName }}
                                    @if (field.validation?.required) {
                                        <span class="req">*</span>
                                    }
                                </label>

                                @switch (normalizeInputType(field.inputType)) {
                                    @case ('textarea') {
                                        <textarea class="field-input" rows="3"
                                            [class.is-required]="isFieldEmpty(field)"
                                            [ngModel]="getFieldValue(field.name)"
                                            (ngModelChange)="onFieldChange(field.name, $event)"
                                            [placeholder]="field.validation?.message ?? ''"
                                            [maxlength]="field.validation?.maxLength ?? null"></textarea>
                                    }
                                    @case ('checkbox') {
                                        <div class="form-check">
                                            <input type="checkbox" class="form-check-input"
                                                [id]="'field-' + field.name"
                                                [ngModel]="getFieldValue(field.name)"
                                                (ngModelChange)="onFieldChange(field.name, $event)" />
                                            <label class="form-check-label" [for]="'field-' + field.name">
                                                {{ field.displayName }}
                                            </label>
                                        </div>
                                    }
                                    @case ('date') {
                                        <input type="date" class="field-input"
                                            [class.is-required]="isFieldEmpty(field)"
                                            [ngModel]="getFieldValue(field.name)"
                                            (ngModelChange)="onFieldChange(field.name, $event)" />
                                    }
                                    @case ('select') {
                                        <select class="field-select"
                                            [class.is-required]="isFieldEmpty(field)"
                                            [ngModel]="getFieldValue(field.name)"
                                            (ngModelChange)="onFieldChange(field.name, $event)">
                                            <option value="">-- Select --</option>
                                            @for (opt of field.options; track opt.value) {
                                                <option [value]="opt.value">{{ opt.label }}</option>
                                            }
                                        </select>
                                    }
                                    @default {
                                        <input type="text" class="field-input"
                                            [class.is-required]="isFieldEmpty(field)"
                                            [ngModel]="getFieldValue(field.name)"
                                            (ngModelChange)="onFieldChange(field.name, $event)"
                                            [placeholder]="field.validation?.message ?? ''"
                                            [maxlength]="field.validation?.maxLength ?? null" />
                                    }
                                }

                                @if (isUsLaxField(field)) {
                                    @let st = state.usLaxStatus();
                                    <div class="uslax-verify">
                                        @switch (st) {
                                            @case ('verified') {
                                                <div class="uslax-badge uslax-badge--ok">
                                                    <i class="bi bi-patch-check-fill"></i>
                                                    Identity verified via USA&nbsp;Lacrosse email
                                                </div>
                                            }
                                            @case ('unverified') {
                                                <div class="uslax-notice uslax-notice--warn">
                                                    <i class="bi bi-exclamation-triangle-fill"></i>
                                                    <div>
                                                        We couldn't reach your USA&nbsp;Lacrosse email to verify you.
                                                        You can continue — a director will verify you before you're assigned.
                                                        @if (state.usLaxMessage()) {
                                                            <div class="uslax-msg">{{ state.usLaxMessage() }}</div>
                                                        }
                                                    </div>
                                                </div>
                                            }
                                            @case ('sent') {
                                                <div class="uslax-code">
                                                    <small class="uslax-hint">
                                                        <i class="bi bi-envelope-check me-1"></i>
                                                        We sent a 6-digit code to your USA&nbsp;Lacrosse email
                                                        @if (state.usLaxMaskedEmail()) {
                                                            (<strong>{{ state.usLaxMaskedEmail() }}</strong>)
                                                        }. Enter it below to verify your identity.
                                                    </small>
                                                    <div class="uslax-code-row">
                                                        <input type="text" inputmode="numeric" autocomplete="one-time-code"
                                                            class="field-input uslax-code-input" maxlength="6"
                                                            placeholder="000000"
                                                            [(ngModel)]="usLaxCode"
                                                            (ngModelChange)="onCodeInput($event)" />
                                                        <button type="button" class="btn btn-primary btn-sm"
                                                            [disabled]="usLaxCode.length < 6"
                                                            (click)="confirmCode()">
                                                            Confirm
                                                        </button>
                                                    </div>
                                                    @if (state.usLaxMessage()) {
                                                        <div class="uslax-msg uslax-msg--err">{{ state.usLaxMessage() }}</div>
                                                    }
                                                    <button type="button" class="btn btn-link btn-sm uslax-skip"
                                                        (click)="state.markUsLaxUnverified()">
                                                        I can't access that email — continue without verifying
                                                    </button>
                                                </div>
                                            }
                                            @case ('verifying') {
                                                <div class="uslax-busy">
                                                    <span class="spinner-border spinner-border-sm me-2"></span>
                                                    Verifying code…
                                                </div>
                                            }
                                            @case ('sending') {
                                                <div class="uslax-busy">
                                                    <span class="spinner-border spinner-border-sm me-2"></span>
                                                    Checking membership & sending code…
                                                </div>
                                            }
                                            @default {
                                                <button type="button" class="btn btn-outline-primary btn-sm"
                                                    [disabled]="!usLaxValue(field)"
                                                    (click)="beginVerify(field)">
                                                    <i class="bi bi-shield-check me-1"></i>
                                                    Verify my USA&nbsp;Lacrosse membership
                                                </button>
                                                @if (st === 'error' && state.usLaxMessage()) {
                                                    <div class="uslax-msg uslax-msg--err">{{ state.usLaxMessage() }}</div>
                                                }
                                                <small class="uslax-hint d-block mt-1">
                                                    We'll email a one-time code to the address USA&nbsp;Lacrosse has on file to confirm it's you.
                                                </small>
                                            }
                                        }
                                    </div>
                                }
                            </div>
                        }
                    }
                </div>
            </section>
          </div>
        </div>
    `,
    styles: [`
        .step-title { font-weight: var(--font-weight-semibold); }
        .profile-section {
            padding: var(--space-4);
            background: var(--brand-surface);
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
            margin-bottom: var(--space-4);
        }
        /* Red-accent signal when a required section is empty — matches .is-required pattern. */
        .profile-section.is-required-section {
            border-left: 3px solid var(--bs-danger);
        }
        .req { color: var(--bs-danger); }
        .selected-teams {
            padding: var(--space-2) var(--space-3);
            background: rgba(var(--bs-success-rgb), 0.08);
            border-left: 3px solid var(--bs-success);
            border-radius: var(--radius-sm);
        }
        /* USLax identity verification */
        .uslax-verify { margin-top: var(--space-2); }
        .uslax-hint { color: var(--text-muted); }
        .uslax-code-row {
            display: flex;
            gap: var(--space-2);
            align-items: center;
            margin-top: var(--space-2);
        }
        .uslax-code-input {
            max-width: 8rem;
            letter-spacing: 0.3em;
            text-align: center;
            font-variant-numeric: tabular-nums;
        }
        .uslax-skip {
            display: block;
            padding-left: 0;
            margin-top: var(--space-1);
        }
        .uslax-badge {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-2) var(--space-3);
            border-radius: var(--radius-sm);
            font-weight: var(--font-weight-semibold);
        }
        .uslax-badge--ok {
            background: rgba(var(--bs-success-rgb), 0.1);
            color: var(--bs-success);
            border-left: 3px solid var(--bs-success);
        }
        .uslax-notice {
            display: flex;
            gap: var(--space-2);
            padding: var(--space-2) var(--space-3);
            border-radius: var(--radius-sm);
        }
        .uslax-notice--warn {
            background: rgba(var(--bs-warning-rgb), 0.12);
            border-left: 3px solid var(--bs-warning);
        }
        .uslax-busy { color: var(--text-muted); }
        .uslax-msg { font-size: 0.85rem; margin-top: var(--space-1); }
        .uslax-msg--err { color: var(--bs-danger); }
    `],
})
export class ProfileStepComponent implements AfterViewInit {
    readonly state = inject(AdultWizardStateService);

    @ViewChildren(MultiSelectComponent) readonly teamPickers!: QueryList<MultiSelectComponent>;
    private _openedOnce = false;

    /**
     * Auto-open the team picker when it lands in the DOM. Mirrors the pattern
     * used in RoleSelectionComponent: subscribe to QueryList.changes so we catch
     * the component whether it's present immediately or arrives after async
     * team loading. Skip on mobile — Syncfusion's full-screen overlay is poor
     * UX on narrow viewports.
     */
    ngAfterViewInit(): void {
        this.tryOpenTeamPicker();
        this.teamPickers.changes.subscribe(() => this.tryOpenTeamPicker());
    }

    private tryOpenTeamPicker(): void {
        if (this._openedOnce) return;
        if (!this.state.needsTeamSelection()) return;
        if (typeof window !== 'undefined' && window.innerWidth < 768) return;

        const first = this.teamPickers?.first;
        if (first) {
            this._openedOnce = true;
            setTimeout(() => { try { first.showPopup(); } catch { /* no-op */ } }, 0);
        }
    }

    readonly titleLabel = computed(() => {
        const name = this.state.roleDisplayName();
        return name ? `${name} Details` : 'Profile Information';
    });

    /** Syncfusion MultiSelect config — groupBy renders collapsible club headers. */
    readonly teamFields = {
        text: 'label',
        value: 'teamId',
        groupBy: 'clubName',
    };

    /**
     * Flat list shaped for Syncfusion MultiSelect with groupBy.
     *
     * The <c>label</c> intentionally includes the club name so the default
     * filter (which matches against the text field) works for BOTH club-name
     * and team-name typeahead — a coach typing "Aces" finds all Aces Elite
     * teams, typing "2027" finds all 2027 teams across clubs, etc.
     * The groupBy header still renders the club name above the group so the
     * visual hierarchy is preserved when not filtering.
     */
    readonly teamsDataSource = computed(() =>
        this.state.availableTeams()
            .map(t => ({
                teamId: t.teamId,
                clubName: t.clubName,
                label: `${t.clubName} — ${t.agegroupName}:${t.divName}:${t.teamName}`,
            }))
            .sort((a, b) => a.label.localeCompare(b.label)),
    );

    teamLabel(teamId: string): string {
        const t = this.state.availableTeams().find(x => x.teamId === teamId);
        return t?.displayText ?? teamId;
    }

    /** Selected teams resolved to labels and sorted alphabetically for the summary list. */
    readonly selectedTeamsSorted = computed(() =>
        this.state.teamIdsCoaching()
            .map(id => ({ id, label: this.teamLabel(id) }))
            .sort((a, b) => a.label.localeCompare(b.label)),
    );

    onTeamsChange(value: string[] | string | null | undefined): void {
        const ids = Array.isArray(value) ? value : (value ? [value] : []);
        this.state.setTeamIdsCoaching(ids);
    }

    normalizeInputType(inputType: string): string {
        return (inputType ?? 'text').toLowerCase();
    }

    getFieldColClass(field: JobRegFieldDto): string {
        const type = this.normalizeInputType(field.inputType);
        return type === 'textarea' ? 'col-12' : 'col-md-6';
    }

    getFieldValue(fieldName: string): string | number | boolean | null {
        return this.state.formValues()[fieldName] ?? null;
    }

    /** True when a required field has no value — drives the .is-required red accent. */
    isFieldEmpty(field: JobRegFieldDto): boolean {
        if (!field.validation?.required) return false;
        const v = this.state.formValues()[field.name];
        if (v === null || v === undefined) return true;
        if (typeof v === 'string') return v.trim().length === 0;
        if (typeof v === 'boolean') return !v;
        return false;
    }

    onFieldChange(fieldName: string, value: string | number | boolean | null): void {
        this.state.setFieldValue(fieldName, value);
    }

    // ── USLax identity verification ───────────────────────────────

    /** Local code-entry buffer for the OTP input. */
    usLaxCode = '';

    /** The USA Lacrosse number field — drives the inline verify UI. */
    isUsLaxField(field: JobRegFieldDto): boolean {
        return (field.name ?? '').toLowerCase() === 'sportassnid';
    }

    /** Trimmed current value of the USLax number field. */
    usLaxValue(field: JobRegFieldDto): string {
        return String(this.getFieldValue(field.name) ?? '').trim();
    }

    beginVerify(field: JobRegFieldDto): void {
        const num = this.usLaxValue(field);
        if (!num) return;
        this.usLaxCode = '';
        void this.state.beginUsLaxVerify(num);
    }

    onCodeInput(value: string): void {
        this.usLaxCode = (value ?? '').replace(/\D/g, '').slice(0, 6);
    }

    confirmCode(): void {
        if (this.usLaxCode.length < 6) return;
        void this.state.confirmUsLaxCode(this.usLaxCode);
    }

    shouldShowField(field: JobRegFieldDto): boolean {
        if (field.visibility === 'hidden' || field.visibility === 'adminOnly') return false;
        const cond = field.conditionalOn;
        if (!cond?.field) return true;

        const depValue = this.state.formValues()[cond.field];
        const expected = cond.value;
        const op = cond.operator ?? 'equals';

        if (op === 'equals') return depValue == expected;
        if (op === 'notEquals') return depValue != expected;
        return true;
    }
}
