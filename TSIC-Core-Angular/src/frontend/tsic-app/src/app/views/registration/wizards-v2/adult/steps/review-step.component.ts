import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';

@Component({
    selector: 'app-adult-review-step',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="step-content">
            @if (state.submitSuccess()) {
                <!-- Confirmation view -->
                <div class="text-center py-4">
                    <i class="bi bi-check-circle-fill text-success" style="font-size: 3rem;"></i>
                    <h3 class="mt-3">Registration Complete!</h3>
                    <p class="text-muted">You have been registered as <strong>{{ state.selectedRole()?.displayName }}</strong>.</p>

                    @if (state.confirmationHtml()) {
                        <div class="confirmation-html mt-4 text-start" [innerHTML]="state.confirmationHtml()"></div>
                    }

                    <div class="mt-4">
                        <button class="btn btn-primary" (click)="completed.emit()">Done</button>
                    </div>
                </div>
            } @else {
                <!-- Review summary -->
                <h3 class="step-title">Review & Submit</h3>

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

                @if (state.submitError()) {
                    <div class="alert alert-danger" role="alert">{{ state.submitError() }}</div>
                }

                <div class="d-flex justify-content-end mt-4">
                    <button class="btn btn-primary btn-lg"
                        [disabled]="state.submitting()"
                        (click)="onSubmit()">
                        @if (state.submitting()) {
                            <span class="spinner-border spinner-border-sm me-2" role="status"></span>
                            Submitting...
                        } @else {
                            Submit Registration
                        }
                    </button>
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
        .confirmation-html {
            padding: var(--space-4);
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
            background: var(--brand-surface);
        }
    `],
})
export class ReviewStepComponent {
    readonly state = inject(AdultWizardStateService);
    readonly completed = output<void>();

    private jobPath = '';

    constructor() {
        // jobPath will be set from the parent via the state's job info
        const ji = this.state.jobInfo();
        // We don't have direct jobPath in state, parent passes it through submit
    }

    hasFormValues(): boolean {
        return Object.keys(this.state.formValues()).length > 0;
    }

    getDisplayValue(fieldName: string): string {
        const val = this.state.formValues()[fieldName];
        if (val === null || val === undefined || val === '') return '';
        if (typeof val === 'boolean') return val ? 'Yes' : 'No';
        return String(val);
    }

    onSubmit(): void {
        // The jobPath is extracted from the URL in the parent wizard component
        // and passed to the state service via the submit method.
        // We emit an event so the parent handles the submit call.
        this.completed.emit();
    }
}
