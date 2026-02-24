import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';

@Component({
    selector: 'app-adult-account-step',
    standalone: true,
    imports: [FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="step-content">
            <h3 class="step-title">Create Your Account</h3>

            <div class="alert alert-info mb-3" role="note">
                <i class="bi bi-info-circle me-2"></i>
                <strong>Important:</strong> If you already have a family account, you must create a
                <em>separate</em> account for adult roles. Family accounts are shared with children
                and cannot be used for staff, referee, or recruiter access.
            </div>

            <div class="row g-3">
                <div class="col-md-6">
                    <label class="form-label fw-medium">First Name</label>
                    <input type="text" class="form-control"
                        [ngModel]="state.firstName()"
                        (ngModelChange)="onFieldChange('firstName', $event)"
                        placeholder="First name" required />
                </div>
                <div class="col-md-6">
                    <label class="form-label fw-medium">Last Name</label>
                    <input type="text" class="form-control"
                        [ngModel]="state.lastName()"
                        (ngModelChange)="onFieldChange('lastName', $event)"
                        placeholder="Last name" required />
                </div>
                <div class="col-md-6">
                    <label class="form-label fw-medium">Email</label>
                    <input type="email" class="form-control"
                        [ngModel]="state.email()"
                        (ngModelChange)="onFieldChange('email', $event)"
                        placeholder="you@example.com" required />
                </div>
                <div class="col-md-6">
                    <label class="form-label fw-medium">Phone</label>
                    <input type="tel" class="form-control"
                        [ngModel]="state.phone()"
                        (ngModelChange)="onFieldChange('phone', $event)"
                        placeholder="(555) 123-4567" required />
                </div>
                <div class="col-md-6">
                    <label class="form-label fw-medium">Username</label>
                    <input type="text" class="form-control"
                        [ngModel]="state.username()"
                        (ngModelChange)="onFieldChange('username', $event)"
                        placeholder="At least 6 characters" required />
                    <small class="form-text text-muted">Minimum 6 characters</small>
                </div>
                <div class="col-md-6">
                    <label class="form-label fw-medium">Password</label>
                    <input type="password" class="form-control"
                        [ngModel]="state.password()"
                        (ngModelChange)="onFieldChange('password', $event)"
                        placeholder="At least 6 characters" required />
                    <small class="form-text text-muted">Minimum 6 characters</small>
                </div>
            </div>
        </div>
    `,
})
export class AccountStepComponent {
    readonly state = inject(AdultWizardStateService);

    private draft = {
        username: '', password: '',
        firstName: '', lastName: '',
        email: '', phone: '',
    };

    constructor() {
        // Initialize draft from current state
        this.draft = {
            username: this.state.username(),
            password: this.state.password(),
            firstName: this.state.firstName(),
            lastName: this.state.lastName(),
            email: this.state.email(),
            phone: this.state.phone(),
        };
    }

    onFieldChange(field: keyof typeof this.draft, value: string): void {
        this.draft[field] = value;
        this.state.setCredentials({ ...this.draft });
    }
}
