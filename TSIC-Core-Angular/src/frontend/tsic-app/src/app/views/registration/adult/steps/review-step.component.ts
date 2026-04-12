import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';

@Component({
    selector: 'app-adult-review-step',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="step-content">
            <h3 class="step-title">Review Your Information</h3>
            <p class="text-muted mb-4">Please verify all details before continuing.</p>

            <!-- PreSubmit validation errors -->
            @if (state.validationErrors().length > 0) {
                <div class="alert alert-danger" role="alert">
                    <div class="fw-semibold mb-1">Please fix the following:</div>
                    <ul class="mb-0 ps-3">
                        @for (err of state.validationErrors(); track err.field) {
                            <li>{{ err.message }}</li>
                        }
                    </ul>
                </div>
            }

            @if (state.preSubmitError()) {
                <div class="alert alert-danger" role="alert">{{ state.preSubmitError() }}</div>
            }

            <!-- PreSubmit spinner -->
            @if (state.preSubmitting()) {
                <div class="d-flex justify-content-center my-4">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Validating...</span>
                    </div>
                </div>
            }

            <!-- Account info (create mode) -->
            @if (state.mode() === 'create') {
                <div class="review-section mb-3">
                    <h6 class="text-muted mb-2">Account</h6>
                    <div class="review-row">
                        <span class="review-label">Name</span>
                        <span>{{ state.firstName() }} {{ state.lastName() }}</span>
                    </div>
                    <div class="review-row">
                        <span class="review-label">Email</span>
                        <span>{{ state.email() }}</span>
                    </div>
                    <div class="review-row">
                        <span class="review-label">Phone</span>
                        <span>{{ state.phone() }}</span>
                    </div>
                    <div class="review-row">
                        <span class="review-label">Username</span>
                        <span>{{ state.username() }}</span>
                    </div>
                </div>
            } @else {
                <div class="review-section mb-3">
                    <h6 class="text-muted mb-2">Account</h6>
                    <div class="review-row">
                        <span class="review-label">Signed in as</span>
                        <span>{{ state.username() }}</span>
                    </div>
                </div>
            }

            <div class="review-section mb-3">
                <h6 class="text-muted mb-2">Role</h6>
                <div class="review-row">
                    <span class="review-label">Selected Role</span>
                    <span>{{ state.selectedRole()?.displayName ?? 'None' }}</span>
                </div>
            </div>

            @if (hasFormValues()) {
                <div class="review-section mb-3">
                    <h6 class="text-muted mb-2">Profile</h6>
                    @for (field of state.formFields(); track field.name) {
                        @if (getDisplayValue(field.name)) {
                            <div class="review-row">
                                <span class="review-label">{{ field.displayName }}</span>
                                <span>{{ getDisplayValue(field.name) }}</span>
                            </div>
                        }
                    }
                </div>
            }

            @if (state.waivers().length > 0) {
                <div class="review-section mb-3">
                    <h6 class="text-muted mb-2">Waivers</h6>
                    @for (waiver of state.waivers(); track waiver.key) {
                        <div class="review-row">
                            <span class="review-label">{{ waiver.title }}</span>
                            <span>
                                @if (state.waiverAcceptance()[waiver.key]) {
                                    <i class="bi bi-check-circle text-success me-1"></i> Accepted
                                } @else {
                                    <i class="bi bi-x-circle text-danger me-1"></i> Not accepted
                                }
                            </span>
                        </div>
                    }
                </div>
            }
        </div>
    `,
    styles: [`
        .review-section {
            padding: var(--space-3) var(--space-4);
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
        }
        .review-row {
            display: flex;
            justify-content: space-between;
            padding: var(--space-1) 0;
        }
        .review-row + .review-row {
            border-top: 1px solid rgba(var(--bs-dark-rgb), 0.05);
        }
        .review-label {
            font-weight: var(--font-weight-medium);
            color: var(--text-secondary);
        }
    `],
})
export class ReviewStepComponent {
    readonly state = inject(AdultWizardStateService);

    hasFormValues(): boolean {
        return Object.keys(this.state.formValues()).length > 0;
    }

    getDisplayValue(fieldName: string): string {
        const val = this.state.formValues()[fieldName];
        if (val === null || val === undefined || val === '') return '';
        if (typeof val === 'boolean') return val ? 'Yes' : 'No';
        return String(val);
    }
}
