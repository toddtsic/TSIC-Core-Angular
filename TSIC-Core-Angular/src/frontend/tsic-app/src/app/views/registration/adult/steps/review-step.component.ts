import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';

/**
 * Review step — summary of identity, address, role, teams, form fields, waivers.
 * Matches the layout pattern used by the Player wizard's ReviewStepComponent:
 * welcome-hero + review-sections with icon headers and fields-grid rows.
 */
@Component({
    selector: 'app-adult-review-step',
    standalone: true,
    imports: [CurrencyPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="review-shell">
            <!-- Centered hero -->
            <div class="welcome-hero">
                <h4 class="welcome-title">
                    <i class="bi bi-clipboard-check welcome-icon" style="color: var(--bs-success)"></i>
                    Almost There!
                </h4>
                <p class="welcome-desc">
                    <i class="bi bi-eye me-1"></i>Review your details
                    <span class="desc-dot"></span>
                    @if (state.hasFees()) {
                        <i class="bi bi-arrow-right me-1"></i>Then proceed to payment
                    } @else {
                        <i class="bi bi-arrow-right me-1"></i>Then submit your registration
                    }
                </p>
            </div>

            <!-- Validation errors from preSubmit -->
            @if (state.validationErrors().length > 0) {
                <div class="review-alert">
                    <i class="bi bi-exclamation-triangle-fill"></i>
                    <div>
                        <div class="fw-semibold mb-1">Validation Errors</div>
                        <ul class="mb-0 ps-3">
                            @for (err of state.validationErrors(); track err.field) {
                                <li>{{ err.message || err.field }}</li>
                            }
                        </ul>
                    </div>
                </div>
            }

            @if (state.preSubmitError()) {
                <div class="review-alert">
                    <i class="bi bi-exclamation-triangle-fill"></i>
                    <div>{{ state.preSubmitError() }}</div>
                </div>
            }

            @if (state.submitError()) {
                <div class="review-alert">
                    <i class="bi bi-exclamation-triangle-fill"></i>
                    <div>{{ state.submitError() }}</div>
                </div>
            }

            <!-- Loading -->
            @if (state.preSubmitting() || state.submitting()) {
                <div class="d-flex justify-content-center py-3">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Validating...</span>
                    </div>
                </div>
            }

            <!-- Role -->
            <div class="review-section">
                <div class="review-section-header">
                    <i class="bi bi-person-badge"></i>
                    <span>Registering As</span>
                </div>
                <div class="review-section-body">
                    <div class="review-fields-grid single-col">
                        <div class="review-field">
                            <span class="review-field-label">Role</span>
                            <span class="review-field-value"
                                [class.text-muted]="!state.roleDisplayName()">
                                {{ state.roleDisplayName() || '—' }}
                            </span>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Identity -->
            <div class="review-section">
                <div class="review-section-header">
                    <i class="bi bi-person-lines-fill"></i>
                    <span>Identity</span>
                </div>
                <div class="review-section-body">
                    <div class="review-fields-grid">
                        <div class="review-field">
                            <span class="review-field-label">Name</span>
                            <span class="review-field-value">{{ state.firstName() }} {{ state.lastName() }}</span>
                        </div>
                        <div class="review-field">
                            <span class="review-field-label">Gender</span>
                            <span class="review-field-value"
                                [class.text-muted]="!genderLabel()">
                                {{ genderLabel() || '—' }}
                            </span>
                        </div>
                        <div class="review-field">
                            <span class="review-field-label">Email</span>
                            <span class="review-field-value">{{ state.email() }}</span>
                        </div>
                        <div class="review-field">
                            <span class="review-field-label">Phone</span>
                            <span class="review-field-value">{{ state.phone() }}</span>
                        </div>
                        <div class="review-field">
                            <span class="review-field-label">
                                {{ state.mode() === 'login' ? 'Signed in as' : 'Username' }}
                            </span>
                            <span class="review-field-value">{{ state.username() }}</span>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Address -->
            <div class="review-section">
                <div class="review-section-header">
                    <i class="bi bi-geo-alt-fill"></i>
                    <span>Address</span>
                </div>
                <div class="review-section-body">
                    <div class="review-fields-grid">
                        <div class="review-field">
                            <span class="review-field-label">Street</span>
                            <span class="review-field-value">{{ state.streetAddress() }}</span>
                        </div>
                        <div class="review-field">
                            <span class="review-field-label">City / State / ZIP</span>
                            <span class="review-field-value">
                                {{ state.city() }}, {{ state.state() }} {{ state.postalCode() }}
                            </span>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Teams Coaching (only when role config requires it — coach in tournament) -->
            @if (state.needsTeamSelection()) {
                <div class="review-section">
                    <div class="review-section-header">
                        <i class="bi bi-people-fill"></i>
                        <span>Teams Coaching</span>
                    </div>
                    <div class="review-section-body">
                        @if (state.teamIdsCoaching().length > 0) {
                            <div class="teams-wrap">
                                @for (id of state.teamIdsCoaching(); track id) {
                                    <span class="review-team-pill">{{ teamLabel(id) }}</span>
                                }
                            </div>
                        } @else {
                            <div class="empty-note">
                                <i class="bi bi-info-circle me-1"></i>
                                No teams selected — a director will assign you after club registrations complete.
                            </div>
                        }
                    </div>
                </div>
            }

            <!-- Additional Profile Fields -->
            @if (hasFormValues()) {
                <div class="review-section">
                    <div class="review-section-header">
                        <i class="bi bi-clipboard"></i>
                        <span>Additional Details</span>
                    </div>
                    <div class="review-section-body">
                        <div class="review-fields-grid">
                            @for (field of state.profileFields(); track field.name) {
                                @if (getDisplayValue(field.name)) {
                                    <div class="review-field">
                                        <span class="review-field-label">{{ field.displayName }}</span>
                                        <span class="review-field-value">{{ getDisplayValue(field.name) }}</span>
                                    </div>
                                }
                            }
                        </div>
                    </div>
                </div>
            }

            <!-- Waivers -->
            @if (state.waivers().length > 0) {
                <div class="review-section">
                    <div class="review-section-header">
                        <i class="bi bi-file-earmark-check"></i>
                        <span>Waivers</span>
                    </div>
                    <div class="review-section-body">
                        <div class="review-fields-grid">
                            @for (waiver of state.waivers(); track waiver.key) {
                                <div class="review-field">
                                    <span class="review-field-label">{{ waiver.title }}</span>
                                    <span class="review-field-value">
                                        @if (state.waiverAcceptance()[waiver.key]) {
                                            <i class="bi bi-check-circle-fill text-success me-1"></i>Accepted
                                        } @else {
                                            <i class="bi bi-x-circle-fill text-danger me-1"></i>Not accepted
                                        }
                                    </span>
                                </div>
                            }
                        </div>
                    </div>
                </div>
            }

            <!-- Fees preview -->
            @if (state.fees(); as fees) {
                @if (fees.owedTotal > 0) {
                    <div class="review-section">
                        <div class="review-section-header">
                            <i class="bi bi-receipt"></i>
                            <span>Fees</span>
                        </div>
                        <div class="review-section-body">
                            <div class="review-fields-grid">
                                <div class="review-field">
                                    <span class="review-field-label">Registration Fee</span>
                                    <span class="review-field-value">{{ fees.feeBase | currency }}</span>
                                </div>
                                @if (fees.feeProcessing > 0) {
                                    <div class="review-field">
                                        <span class="review-field-label">Processing Fee</span>
                                        <span class="review-field-value">{{ fees.feeProcessing | currency }}</span>
                                    </div>
                                }
                            </div>
                            <div class="review-total-row">
                                <span>Total Due</span>
                                <span class="review-total-amount">{{ fees.owedTotal | currency }}</span>
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
        }
        .review-alert i {
            font-size: var(--font-size-lg);
            flex-shrink: 0;
            margin-top: 2px;
        }

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
        }
        .review-section-header i {
            color: var(--bs-primary);
            font-size: var(--font-size-base);
        }

        .review-section-body { padding: 0; }

        .review-fields-grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 0;
        }
        .review-fields-grid.single-col {
            grid-template-columns: 1fr;
        }

        .review-field {
            display: flex;
            flex-direction: column;
            gap: 1px;
            padding: var(--space-2) var(--space-3);
            border-bottom: 1px solid var(--border-color);
        }
        .review-fields-grid:not(.single-col) .review-field:nth-child(odd) {
            border-right: 1px solid var(--border-color);
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

        .teams-wrap {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-1);
            padding: var(--space-3);
        }

        .review-team-pill {
            font-size: 11px;
            font-weight: var(--font-weight-medium);
            padding: 2px var(--space-2);
            border-radius: var(--radius-full);
            background: rgba(var(--bs-primary-rgb), 0.1);
            color: var(--bs-primary);
            border: 1px solid rgba(var(--bs-primary-rgb), 0.2);
        }

        .empty-note {
            padding: var(--space-3);
            font-size: var(--font-size-sm);
            color: var(--brand-text-muted);
        }
        .empty-note i { color: var(--bs-warning); }

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

        @media (max-width: 575.98px) {
            .review-fields-grid { grid-template-columns: 1fr; }
            .review-field { border-right: none !important; }
        }
    `],
})
export class ReviewStepComponent {
    readonly state = inject(AdultWizardStateService);

    readonly genderLabel = computed(() => {
        switch (this.state.gender()) {
            case 'F': return 'Female';
            case 'M': return 'Male';
            case 'U': return 'Prefer not to say';
            default: return '';
        }
    });

    hasFormValues(): boolean {
        return Object.keys(this.state.formValues()).length > 0;
    }

    getDisplayValue(fieldName: string): string {
        const val = this.state.formValues()[fieldName];
        if (val === null || val === undefined || val === '') return '';
        if (typeof val === 'boolean') return val ? 'Yes' : 'No';
        return String(val);
    }

    teamLabel(teamId: string): string {
        const t = this.state.availableTeams().find(x => x.teamId === teamId);
        return t?.displayText ?? teamId;
    }
}
