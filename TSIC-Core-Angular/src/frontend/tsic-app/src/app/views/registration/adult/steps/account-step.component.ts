import { ChangeDetectionStrategy, Component, inject, OnInit, signal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '@infrastructure/services/auth.service';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';

@Component({
    selector: 'app-adult-account-step',
    standalone: true,
    imports: [FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="step-content">
            <!-- Already authenticated → show signed-in banner -->
            @if (isAuthenticated()) {
                <div class="text-center py-4">
                    <i class="bi bi-person-check-fill text-success" style="font-size: 2.5rem;"></i>
                    <h3 class="mt-3">Welcome back!</h3>
                    <p class="text-muted">
                        You're signed in as <strong>{{ state.username() }}</strong>.
                        Click Continue to select your role.
                    </p>
                </div>
            } @else {
                <!-- Two-card layout: Create Account | Sign In -->
                <h3 class="step-title mb-4">Get Started</h3>

                <div class="row g-4">
                    <!-- Left card: Create Account -->
                    <div class="col-md-7">
                        <div class="card h-100 shadow-sm" style="border-radius: var(--radius-md);">
                            <div class="card-body">
                                <h5 class="card-title mb-3">
                                    <i class="bi bi-person-plus me-2 text-primary"></i>Create Account
                                </h5>

                                <div class="row g-3">
                                    <div class="col-md-6">
                                        <label class="field-label">First Name</label>
                                        <input type="text" class="field-input"
                                            [ngModel]="state.firstName()"
                                            (ngModelChange)="onFieldChange('firstName', $event)"
                                            placeholder="First name" />
                                    </div>
                                    <div class="col-md-6">
                                        <label class="field-label">Last Name</label>
                                        <input type="text" class="field-input"
                                            [ngModel]="state.lastName()"
                                            (ngModelChange)="onFieldChange('lastName', $event)"
                                            placeholder="Last name" />
                                    </div>
                                    <div class="col-md-6">
                                        <label class="field-label">Email</label>
                                        <input type="email" class="field-input"
                                            [ngModel]="state.email()"
                                            (ngModelChange)="onFieldChange('email', $event)"
                                            placeholder="you&#64;example.com" />
                                    </div>
                                    <div class="col-md-6">
                                        <label class="field-label">Phone</label>
                                        <input type="tel" class="field-input"
                                            [ngModel]="state.phone()"
                                            (ngModelChange)="onFieldChange('phone', $event)"
                                            placeholder="(555) 123-4567" />
                                    </div>
                                    <div class="col-md-6">
                                        <label class="field-label">Username</label>
                                        <input type="text" class="field-input"
                                            [ngModel]="state.username()"
                                            (ngModelChange)="onFieldChange('username', $event)"
                                            placeholder="At least 6 characters" />
                                        <small class="wizard-tip">Minimum 6 characters</small>
                                    </div>
                                    <div class="col-md-6">
                                        <label class="field-label">Password</label>
                                        <input type="password" class="field-input"
                                            [ngModel]="state.password()"
                                            (ngModelChange)="onFieldChange('password', $event)"
                                            placeholder="At least 6 characters" />
                                        <small class="wizard-tip">Minimum 6 characters</small>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Right card: Sign In -->
                    <div class="col-md-5">
                        <div class="card h-100 shadow-sm" style="border-radius: var(--radius-md); border-left: 3px solid var(--bs-primary);">
                            <div class="card-body d-flex flex-column">
                                <h5 class="card-title mb-3">
                                    <i class="bi bi-box-arrow-in-right me-2 text-primary"></i>Already Registered?
                                </h5>
                                <p class="text-muted mb-3">
                                    Sign in with your existing account to add an adult role.
                                </p>

                                <div class="mb-3">
                                    <label class="field-label">Username</label>
                                    <input type="text" class="field-input"
                                        [(ngModel)]="loginUsername"
                                        placeholder="Your username" />
                                </div>
                                <div class="mb-3">
                                    <label class="field-label">Password</label>
                                    <input type="password" class="field-input"
                                        [(ngModel)]="loginPassword"
                                        (keydown.enter)="onLogin()" />
                                </div>

                                @if (loginError()) {
                                    <div class="alert alert-danger py-2" role="alert">
                                        {{ loginError() }}
                                    </div>
                                }

                                <button class="btn btn-primary w-100 mt-auto"
                                    [disabled]="loginLoading() || !loginUsername || !loginPassword"
                                    (click)="onLogin()">
                                    @if (loginLoading()) {
                                        <span class="spinner-border spinner-border-sm me-1" role="status"></span>
                                    }
                                    Sign In
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            }
        </div>
    `,
    styles: [`
        .step-title {
            font-weight: var(--font-weight-semibold);
        }
    `],
})
export class AccountStepComponent implements OnInit {
    readonly state = inject(AdultWizardStateService);
    private readonly auth = inject(AuthService);
    readonly autoAdvance = output<void>();

    readonly isAuthenticated = signal(false);
    readonly loginLoading = signal(false);
    readonly loginError = signal<string | null>(null);

    loginUsername = '';
    loginPassword = '';

    private draft = {
        username: '', password: '',
        firstName: '', lastName: '',
        email: '', phone: '',
    };

    ngOnInit(): void {
        // Detect if already authenticated
        if (this.auth.isAuthenticated()) {
            this.isAuthenticated.set(true);
            this.state.setMode('login');
            this.state.populateFromAuth();
        }

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

    onLogin(): void {
        if (!this.loginUsername || !this.loginPassword) return;

        this.loginLoading.set(true);
        this.loginError.set(null);

        this.auth.login({
            username: this.loginUsername,
            password: this.loginPassword,
        }).subscribe({
            next: () => {
                this.loginLoading.set(false);
                this.isAuthenticated.set(true);
                this.state.setMode('login');
                this.state.populateFromAuth();
                this.autoAdvance.emit();
            },
            error: (err: unknown) => {
                this.loginLoading.set(false);
                const httpErr = err as { error?: { message?: string } };
                this.loginError.set(httpErr?.error?.message ?? 'Login failed. Please check your credentials.');
            },
        });
    }
}
