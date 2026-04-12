import { AfterViewInit, ChangeDetectionStrategy, Component, QueryList, ViewChildren, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MultiSelectModule, MultiSelectComponent, CheckBoxSelectionService } from '@syncfusion/ej2-angular-dropdowns';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';
import type { AdultRegField } from '@infrastructure/services/adult-registration.service';

/**
 * Profile step — role-config-driven.
 *
 * Reads fields from <c>state.roleConfig().profileFields</c>. Shows the teams
 * multi-select only when <c>roleConfig.needsTeamSelection</c> is true — which
 * the backend resolver sets only for coach in Tournament job. Club/League
 * coaches get UnassignedAdult status and no team selection (director assigns).
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
            @if (state.needsTeamSelection()) {
                <section class="profile-section"
                    [class.is-required-section]="state.teamIdsCoaching().length === 0">
                    <label class="field-label">
                        Teams Coaching <span class="req">*</span>
                    </label>
                    <small class="wizard-tip mb-2 d-block">
                        Check every team you're coaching. Type a club or team name to filter. First selection is your primary team assignment.
                    </small>

                    @if (state.teamsLoading()) {
                        <div class="text-center py-3">
                            <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                            <span class="ms-2 text-muted">Loading teams...</span>
                        </div>
                    } @else if (state.teamsError()) {
                        <div class="alert alert-danger" role="alert">{{ state.teamsError() }}</div>
                    } @else if (state.availableTeams().length === 0) {
                        <div class="alert alert-warning" role="note">
                            No teams are registered yet for this event. Contact the tournament director.
                        </div>
                    } @else {
                        <ejs-multiselect
                            [dataSource]="teamsDataSource()"
                            [fields]="teamFields"
                            [value]="state.teamIdsCoaching()"
                            (change)="onTeamsChange($event.value)"
                            [mode]="'CheckBox'"
                            [allowFiltering]="true"
                            [filterBarPlaceholder]="'Search by club or team...'"
                            [showSelectAll]="false"
                            [closePopupOnSelect]="false"
                            [changeOnBlur]="false"
                            [showDropDownIcon]="true"
                            placeholder="Select teams you're coaching"
                            [popupHeight]="'320px'">
                        </ejs-multiselect>

                        @if (state.teamIdsCoaching().length > 0) {
                            <div class="selected-teams mt-2">
                                <strong class="small">Selected ({{ state.teamIdsCoaching().length }}):</strong>
                                <ul class="small mb-0">
                                    @for (id of state.teamIdsCoaching(); track id) {
                                        <li>{{ teamLabel(id) }}</li>
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

    onTeamsChange(value: string[] | string | null | undefined): void {
        const ids = Array.isArray(value) ? value : (value ? [value] : []);
        this.state.setTeamIdsCoaching(ids);
    }

    normalizeInputType(inputType: string): string {
        return (inputType ?? 'text').toLowerCase();
    }

    getFieldColClass(field: AdultRegField): string {
        const type = this.normalizeInputType(field.inputType);
        return type === 'textarea' ? 'col-12' : 'col-md-6';
    }

    getFieldValue(fieldName: string): string | number | boolean | null {
        return this.state.formValues()[fieldName] ?? null;
    }

    /** True when a required field has no value — drives the .is-required red accent. */
    isFieldEmpty(field: AdultRegField): boolean {
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

    shouldShowField(field: AdultRegField): boolean {
        if (field.visibility === 'hidden' || field.visibility === 'adminOnly') return false;
        if (!field.conditionalOn) return true;

        const depValue = this.state.formValues()[field.conditionalOn.field];
        const expected = field.conditionalOn.value;
        const op = field.conditionalOn.operator ?? 'equals';

        if (op === 'equals') return depValue == expected;
        if (op === 'notEquals') return depValue != expected;
        return true;
    }
}
