import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { LoginComponent } from '@views/auth/login/login.component';
import { TosAcceptanceStepComponent } from '../../shared/components/tos-acceptance-step.component';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';

const US_STATES: ReadonlyArray<{ value: string; label: string }> = [
    { value: 'AL', label: 'Alabama' }, { value: 'AK', label: 'Alaska' },
    { value: 'AZ', label: 'Arizona' }, { value: 'AR', label: 'Arkansas' },
    { value: 'CA', label: 'California' }, { value: 'CO', label: 'Colorado' },
    { value: 'CT', label: 'Connecticut' }, { value: 'DE', label: 'Delaware' },
    { value: 'DC', label: 'District of Columbia' }, { value: 'FL', label: 'Florida' },
    { value: 'GA', label: 'Georgia' }, { value: 'HI', label: 'Hawaii' },
    { value: 'ID', label: 'Idaho' }, { value: 'IL', label: 'Illinois' },
    { value: 'IN', label: 'Indiana' }, { value: 'IA', label: 'Iowa' },
    { value: 'KS', label: 'Kansas' }, { value: 'KY', label: 'Kentucky' },
    { value: 'LA', label: 'Louisiana' }, { value: 'ME', label: 'Maine' },
    { value: 'MD', label: 'Maryland' }, { value: 'MA', label: 'Massachusetts' },
    { value: 'MI', label: 'Michigan' }, { value: 'MN', label: 'Minnesota' },
    { value: 'MS', label: 'Mississippi' }, { value: 'MO', label: 'Missouri' },
    { value: 'MT', label: 'Montana' }, { value: 'NE', label: 'Nebraska' },
    { value: 'NV', label: 'Nevada' }, { value: 'NH', label: 'New Hampshire' },
    { value: 'NJ', label: 'New Jersey' }, { value: 'NM', label: 'New Mexico' },
    { value: 'NY', label: 'New York' }, { value: 'NC', label: 'North Carolina' },
    { value: 'ND', label: 'North Dakota' }, { value: 'OH', label: 'Ohio' },
    { value: 'OK', label: 'Oklahoma' }, { value: 'OR', label: 'Oregon' },
    { value: 'PA', label: 'Pennsylvania' }, { value: 'RI', label: 'Rhode Island' },
    { value: 'SC', label: 'South Carolina' }, { value: 'SD', label: 'South Dakota' },
    { value: 'TN', label: 'Tennessee' }, { value: 'TX', label: 'Texas' },
    { value: 'UT', label: 'Utah' }, { value: 'VT', label: 'Vermont' },
    { value: 'VA', label: 'Virginia' }, { value: 'WA', label: 'Washington' },
    { value: 'WV', label: 'West Virginia' }, { value: 'WI', label: 'Wisconsin' },
    { value: 'WY', label: 'Wyoming' },
];

/**
 * Account step — matches the player/team wizard account pattern:
 * single card, welcome hero, embedded <app-login>, "or" divider, inline
 * "Create New Account" form revealed below (no modal, no content swap).
 *
 * The wizard's ngOnInit calls auth.logoutLocal() so this step always starts
 * unauthenticated. Login via the embedded widget auto-advances; clicking
 * "Create New Account" reveals the full legacy StaffRegister field set inline.
 */
@Component({
    selector: 'app-adult-account-step',
    standalone: true,
    imports: [FormsModule, LoginComponent, TosAcceptanceStepComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="card shadow border-0 card-rounded">
            <div class="card-body account-step">

                @if (auth.isAuthenticated()) {
                    <!-- Returning user: review-your-info summary, with optional inline edit. -->
                    @if (state.selfProfileLoading()) {
                        <div class="summary-loading">
                            <span class="spinner-border spinner-border-sm me-2"></span>Loading your account...
                        </div>
                    } @else if (accountView() === 'edit') {
                        <!-- ═══ EDIT YOUR INFO ═══ -->
                        <div class="welcome-hero">
                            <h5 class="welcome-title">
                                <i class="bi bi-person-gear"></i>
                                Edit Your Info
                            </h5>
                            <p class="wizard-tip">
                                Update your contact and address details. To change your name, contact the tournament director.
                            </p>
                        </div>

                        <div class="text-end mb-3">
                            <button type="button" class="btn btn-sm btn-link text-decoration-none"
                                (click)="accountView.set('summary')">
                                <i class="bi bi-arrow-left me-1"></i>Back
                            </button>
                        </div>

                        <h6 class="section-header">About You</h6>
                        <div class="row g-3">
                            <div class="col-md-6">
                                <label class="field-label">First Name</label>
                                <input type="text" class="field-input" [value]="state.firstName()" disabled />
                            </div>
                            <div class="col-md-6">
                                <label class="field-label">Last Name</label>
                                <input type="text" class="field-input" [value]="state.lastName()" disabled />
                            </div>
                        </div>

                        <h6 class="section-header">Contact</h6>
                        <div class="row g-3">
                            <div class="col-md-6">
                                <label class="field-label">Email <span class="req">*</span></label>
                                <input type="email" class="field-input"
                                    [class.is-required]="!state.isEmailValid()"
                                    [ngModel]="state.email()"
                                    (ngModelChange)="state.setEmail($event)"
                                    placeholder="you&#64;example.com" />
                                @if (state.email() && !state.isEmailValid()) {
                                    <small class="wizard-tip text-danger">Invalid email format</small>
                                }
                            </div>
                            <div class="col-md-6">
                                <label class="field-label">
                                    Cell Phone <span class="req">*</span>
                                    <span class="text-muted small ms-1">(digits only)</span>
                                </label>
                                <input type="tel" class="field-input"
                                    inputmode="numeric" pattern="[0-9]*"
                                    [class.is-required]="!state.isPhoneValid()"
                                    [ngModel]="state.phone()"
                                    (ngModelChange)="state.setPhone($event)"
                                    placeholder="e.g. 5551234567" />
                                @if (state.phone() && !state.isPhoneValid()) {
                                    <small class="wizard-tip text-danger">At least 10 digits required</small>
                                }
                            </div>
                        </div>

                        <h6 class="section-header">Address</h6>
                        <div class="row g-3">
                            <div class="col-12">
                                <label class="field-label">Street Address <span class="req">*</span></label>
                                <input type="text" class="field-input"
                                    [class.is-required]="!state.streetAddress().trim()"
                                    [ngModel]="state.streetAddress()"
                                    (ngModelChange)="state.setStreetAddress($event)"
                                    placeholder="123 Main St" />
                            </div>
                            <div class="col-md-5">
                                <label class="field-label">City <span class="req">*</span></label>
                                <input type="text" class="field-input"
                                    [class.is-required]="!state.city().trim()"
                                    [ngModel]="state.city()"
                                    (ngModelChange)="state.setCity($event)"
                                    placeholder="City" />
                            </div>
                            <div class="col-md-4">
                                <label class="field-label">State <span class="req">*</span></label>
                                <select class="field-select"
                                    [class.is-required]="!state.state()"
                                    [ngModel]="state.state()"
                                    (ngModelChange)="state.setState($event)">
                                    <option value="">-- Select --</option>
                                    @for (s of states; track s.value) {
                                        <option [value]="s.value">{{ s.label }}</option>
                                    }
                                </select>
                            </div>
                            <div class="col-md-3">
                                <label class="field-label">ZIP <span class="req">*</span></label>
                                <input type="text" class="field-input"
                                    [class.is-required]="!state.postalCode().trim()"
                                    [ngModel]="state.postalCode()"
                                    (ngModelChange)="state.setPostalCode($event)"
                                    placeholder="ZIP" />
                            </div>
                        </div>

                        @if (state.selfProfileError()) {
                            <div class="alert alert-danger py-2 small mt-3 mb-0">{{ state.selfProfileError() }}</div>
                        }

                        <button type="button" class="btn btn-primary w-100 fw-semibold mt-4"
                            [disabled]="state.selfProfileSaving() || !state.selfProfileEditValid()"
                            (click)="onSaveProfile()">
                            @if (state.selfProfileSaving()) {
                                <span class="spinner-border spinner-border-sm me-1"></span>Saving...
                            } @else {
                                <i class="bi bi-check-lg me-1"></i>Save Changes
                            }
                        </button>
                    } @else {
                        <!-- ═══ ACCOUNT SUMMARY (review your info before continuing) ═══ -->
                        <div class="welcome-hero">
                            <h5 class="welcome-title">
                                <i class="bi" [class]="icon()"></i>
                                {{ heroTitle() }}
                            </h5>
                            <p class="wizard-tip">Review your account info, then continue to the registration details.</p>
                        </div>

                        <div class="summary-card">
                            <div class="summary-identity">
                                <i class="bi bi-person-check-fill"></i>
                                <div>
                                    <div class="name">{{ displayName() }}</div>
                                    @if (state.email()) {
                                        <span class="email">{{ state.email() }}</span>
                                    }
                                </div>
                            </div>
                            @if (summaryAddress()) {
                                <div class="summary-detail">
                                    <i class="bi bi-geo-alt"></i><span>{{ summaryAddress() }}</span>
                                </div>
                            }
                            @if (state.phone()) {
                                <div class="summary-detail">
                                    <i class="bi bi-telephone"></i><span>{{ state.phone() }}</span>
                                </div>
                            }
                        </div>

                        <button type="button" class="btn btn-outline-primary w-100 fw-semibold mt-3"
                            (click)="accountView.set('edit')">
                            <i class="bi bi-pencil me-2"></i>Edit Your Info
                        </button>
                        <button type="button" class="btn btn-primary w-100 fw-semibold mt-2"
                            (click)="onContinueFromSummary()">
                            Continue<i class="bi bi-arrow-right ms-2"></i>
                        </button>
                    }
                } @else if (!showCreateForm()) {
                    <!-- Sign-in view (default): welcome + embedded login + "or" + create CTA -->
                    <div class="welcome-hero">
                        <h5 class="welcome-title">
                            <i class="bi" [class]="icon()"></i>
                            {{ heroTitle() }}
                        </h5>
                        <p class="wizard-tip">
                            Sign in with your existing account, or create a new one to continue.
                        </p>
                    </div>

                    <app-login
                        [theme]="''"
                        [embedded]="true"
                        [headerText]="'Sign In'"
                        [subHeaderText]="'Enter your username and password'"
                        [returnUrl]="returnUrl()"
                        (loginSuccess)="onLoginContinue()" />

                    <div class="or-divider">or</div>

                    <div class="create-cta">
                        <p>Don't have an account yet?</p>
                        <button type="button"
                            class="btn btn-outline-primary fw-semibold w-100"
                            (click)="showCreateForm.set(true)">
                            <i class="bi bi-person-plus me-2"></i>Create NEW Account
                        </button>
                    </div>
                } @else {
                    <!-- Create-account view: hero + inline form + inline ToS (NO sign-in card). -->
                    <div class="welcome-hero">
                        <h5 class="welcome-title">
                            <i class="bi bi-person-plus"></i>
                            Create Your Account
                        </h5>
                        <p class="wizard-tip">
                            Fill in your details below. Your account will be created when you accept the Terms of Service.
                        </p>
                    </div>

                    <div class="text-end mb-3">
                        <button type="button" class="btn btn-sm btn-link text-decoration-none"
                            (click)="showCreateForm.set(false)">
                            <i class="bi bi-arrow-left me-1"></i>Back to sign in
                        </button>
                    </div>

                    <div class="create-inline">
                            <h6 class="section-header">About You</h6>
                            <div class="row g-3">
                                <div class="col-md-6">
                                    <label class="field-label">First Name <span class="req">*</span></label>
                                    <input type="text" class="field-input"
                                        [class.is-required]="!state.firstName().trim()"
                                        [ngModel]="state.firstName()"
                                        (ngModelChange)="state.setFirstName($event)"
                                        placeholder="First name" />
                                </div>
                                <div class="col-md-6">
                                    <label class="field-label">Last Name <span class="req">*</span></label>
                                    <input type="text" class="field-input"
                                        [class.is-required]="!state.lastName().trim()"
                                        [ngModel]="state.lastName()"
                                        (ngModelChange)="state.setLastName($event)"
                                        placeholder="Last name" />
                                </div>
                                <div class="col-md-5">
                                    <label class="field-label">Gender <span class="req">*</span></label>
                                    <select class="field-select"
                                        [class.is-required]="!state.gender()"
                                        [ngModel]="state.gender()"
                                        (ngModelChange)="state.setGender($event)">
                                        <option value="">-- Select --</option>
                                        <option value="F">Female</option>
                                        <option value="M">Male</option>
                                        <option value="U">Prefer not to say</option>
                                    </select>
                                </div>
                                <div class="col-md-7">
                                    <label class="field-label">
                                        Cell Phone <span class="req">*</span>
                                        <span class="text-muted small ms-1">(digits only)</span>
                                    </label>
                                    <input type="tel" class="field-input"
                                        inputmode="numeric" pattern="[0-9]*"
                                        [class.is-required]="!state.isPhoneValid()"
                                        [ngModel]="state.phone()"
                                        (ngModelChange)="state.setPhone($event)"
                                        placeholder="e.g. 5551234567" />
                                    @if (state.phone() && !state.isPhoneValid()) {
                                        <small class="wizard-tip text-danger">At least 10 digits required</small>
                                    }
                                </div>
                            </div>

                            <h6 class="section-header">Email</h6>
                            <div class="row g-3">
                                <div class="col-md-6">
                                    <label class="field-label">Email <span class="req">*</span></label>
                                    <input type="email" class="field-input"
                                        [class.is-required]="!state.isEmailValid()"
                                        [ngModel]="state.email()"
                                        (ngModelChange)="state.setEmail($event)"
                                        placeholder="you&#64;example.com" />
                                    @if (state.email() && !state.isEmailValid()) {
                                        <small class="wizard-tip text-danger">Invalid email format</small>
                                    }
                                </div>
                                <div class="col-md-6">
                                    <label class="field-label">Confirm Email <span class="req">*</span></label>
                                    <input type="email" class="field-input"
                                        [class.is-required]="!state.emailsMatch()"
                                        [ngModel]="state.confirmEmail()"
                                        (ngModelChange)="state.setConfirmEmail($event)"
                                        placeholder="Re-enter email" />
                                    @if (state.confirmEmail() && !state.emailsMatch()) {
                                        <small class="wizard-tip text-danger">Emails do not match</small>
                                    }
                                </div>
                            </div>

                            <h6 class="section-header">Address</h6>
                            <div class="row g-3">
                                <div class="col-12">
                                    <label class="field-label">Street Address <span class="req">*</span></label>
                                    <input type="text" class="field-input"
                                        [class.is-required]="!state.streetAddress().trim()"
                                        [ngModel]="state.streetAddress()"
                                        (ngModelChange)="state.setStreetAddress($event)"
                                        placeholder="123 Main St" />
                                </div>
                                <div class="col-md-5">
                                    <label class="field-label">City <span class="req">*</span></label>
                                    <input type="text" class="field-input"
                                        [class.is-required]="!state.city().trim()"
                                        [ngModel]="state.city()"
                                        (ngModelChange)="state.setCity($event)"
                                        placeholder="City" />
                                </div>
                                <div class="col-md-4">
                                    <label class="field-label">State <span class="req">*</span></label>
                                    <select class="field-select"
                                        [class.is-required]="!state.state()"
                                        [ngModel]="state.state()"
                                        (ngModelChange)="state.setState($event)">
                                        <option value="">-- Select --</option>
                                        @for (s of states; track s.value) {
                                            <option [value]="s.value">{{ s.label }}</option>
                                        }
                                    </select>
                                </div>
                                <div class="col-md-3">
                                    <label class="field-label">ZIP <span class="req">*</span></label>
                                    <input type="text" class="field-input"
                                        [class.is-required]="!state.postalCode().trim()"
                                        [ngModel]="state.postalCode()"
                                        (ngModelChange)="state.setPostalCode($event)"
                                        placeholder="ZIP" />
                                </div>
                            </div>

                            <h6 class="section-header">Account Credentials</h6>
                            <div class="row g-3">
                                <div class="col-md-4">
                                    <label class="field-label">Username <span class="req">*</span></label>
                                    <input type="text" class="field-input"
                                        name="adult-new-username"
                                        autocomplete="off"
                                        [class.is-required]="state.username().trim().length < 6"
                                        [ngModel]="state.username()"
                                        (ngModelChange)="state.setUsername($event)"
                                        placeholder="6+ characters" />
                                    <small class="wizard-tip">Minimum 6 characters</small>
                                </div>
                                <div class="col-md-4">
                                    <label class="field-label">Password <span class="req">*</span></label>
                                    <div class="position-relative">
                                        <input [type]="showPassword() ? 'text' : 'password'" class="field-input pe-5"
                                            name="adult-new-password"
                                            autocomplete="new-password"
                                            [class.is-required]="state.password().length < 6"
                                            [ngModel]="state.password()"
                                            (ngModelChange)="state.setPassword($event)"
                                            placeholder="6+ characters" />
                                        <button type="button" class="password-toggle"
                                                (click)="showPassword.set(!showPassword())"
                                                [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'" tabindex="-1">
                                            <i class="bi" [class.bi-eye]="!showPassword()" [class.bi-eye-slash]="showPassword()"></i>
                                        </button>
                                    </div>
                                    <small class="wizard-tip">Minimum 6 characters</small>
                                </div>
                                <div class="col-md-4">
                                    <label class="field-label">Confirm Password <span class="req">*</span></label>
                                    <div class="position-relative">
                                        <input [type]="showConfirm() ? 'text' : 'password'" class="field-input pe-5"
                                            name="adult-confirm-password"
                                            autocomplete="new-password"
                                            [class.is-required]="!state.passwordsMatch()"
                                            [ngModel]="state.confirmPassword()"
                                            (ngModelChange)="state.setConfirmPassword($event)"
                                            placeholder="Re-enter password" />
                                        <button type="button" class="password-toggle"
                                                (click)="showConfirm.set(!showConfirm())"
                                                [attr.aria-label]="showConfirm() ? 'Hide password' : 'Show password'" tabindex="-1">
                                            <i class="bi" [class.bi-eye]="!showConfirm()" [class.bi-eye-slash]="showConfirm()"></i>
                                        </button>
                                    </div>
                                    @if (state.confirmPassword() && !state.passwordsMatch()) {
                                        <small class="wizard-tip text-danger">Passwords do not match</small>
                                    }
                                </div>
                            </div>

                            <!-- Inline ToS — its "Accept and Continue" button IS the form's submit. -->
                            <div class="mt-4">
                                <app-tos-acceptance-step
                                    [error]="tosError()"
                                    (accepted)="onCreateContinue()" />
                            </div>
                        </div>
                }

            </div>
        </div>
    `,
    styles: [`
        :host { display: block; }

        .account-step {
            max-width: 680px;
            margin: 0 auto;
            padding-top: var(--space-6);
        }

        .summary-loading {
            text-align: center;
            padding: var(--space-6);
            color: var(--brand-text-muted);
            font-size: var(--font-size-sm);
        }

        .summary-card {
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
            background: var(--brand-surface);
            padding: var(--space-4);
            box-shadow: var(--shadow-sm);
        }
        .summary-identity {
            display: flex;
            align-items: flex-start;
            gap: var(--space-3);
        }
        .summary-identity i {
            color: var(--bs-success);
            font-size: 1.5rem;
            flex-shrink: 0;
        }
        .summary-identity .name {
            font-size: var(--font-size-base);
            font-weight: var(--font-weight-semibold);
            color: var(--brand-text);
            line-height: 1.2;
        }
        .summary-identity .email {
            display: block;
            font-size: var(--font-size-sm);
            color: var(--brand-text-muted);
            margin-top: 2px;
        }
        .summary-detail {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            margin-top: var(--space-3);
            padding-top: var(--space-3);
            border-top: 1px solid var(--border-color);
            font-size: var(--font-size-sm);
            color: var(--brand-text);
        }
        .summary-detail i { color: var(--bs-primary); flex-shrink: 0; }

        .or-divider {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            margin: var(--space-4) 0;
            color: var(--brand-text-muted);
            font-size: var(--font-size-sm);
        }
        .or-divider::before,
        .or-divider::after {
            content: '';
            flex: 1;
            height: 1px;
            background: var(--border-color);
        }

        .create-cta { text-align: center; }
        .create-cta p {
            color: var(--brand-text-muted);
            font-size: var(--font-size-sm);
            margin-bottom: var(--space-3);
        }

        .create-inline {
            border-top: 1px solid var(--border-color);
            padding-top: var(--space-2);
        }

        .section-header {
            font-size: var(--font-size-sm);
            font-weight: var(--font-weight-semibold);
            color: var(--text-secondary);
            text-transform: uppercase;
            letter-spacing: 0.04em;
            margin: var(--space-4) 0 var(--space-2);
        }
        .req { color: var(--bs-danger); }

        @media (max-width: 575.98px) {
            .account-step { padding-top: var(--space-4); }
            .or-divider { margin: var(--space-2) 0; font-size: var(--font-size-xs); }
        }
    `],
})
export class AccountStepComponent implements OnInit {
    readonly state = inject(AdultWizardStateService);
    readonly auth = inject(AuthService);
    private readonly route = inject(ActivatedRoute);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly autoAdvance = output<void>();

    readonly showCreateForm = signal(false);
    /** Returning-user view: 'summary' (review) or 'edit' (update contact/address). */
    readonly accountView = signal<'summary' | 'edit'>('summary');
    readonly tosError = signal<string | null>(null);
    readonly states = US_STATES;
    readonly showPassword = signal(false);
    readonly showConfirm = signal(false);

    ngOnInit(): void {
        // Wizard called auth.logoutLocal() on init; we always start fresh.
        // Clear any stale credentials (e.g. browser autofill tried to fill them).
        this.state.setUsername('');
        this.state.setPassword('');
        this.state.setConfirmPassword('');
        this.state.setAcceptedTos(false);
    }

    returnUrl(): string {
        // Preserve ?role=<key> so the ToS bounce-back (if the signed-in user
        // hasn't accepted ToS yet) lands back on a complete wizard URL. Without
        // the role param, my strict URL check would show "Registration Unavailable".
        const jobPath = this.state.jobPath();
        const roleKey = this.state.roleKey();
        if (!jobPath) return '';
        return roleKey
            ? `/${jobPath}/registration/adult?role=${encodeURIComponent(roleKey)}`
            : `/${jobPath}/registration/adult`;
    }

    icon(): string {
        return this.state.roleConfig()?.icon ?? 'bi-person-badge';
    }

    heroTitle(): string {
        const name = this.state.roleDisplayName();
        return name ? `${name} Registration` : 'Adult Registration';
    }

    /** Name for the summary card; falls back to the token username if profile is sparse. */
    displayName(): string {
        return this.state.fullName() || this.auth.getCurrentUser()?.username || '';
    }

    /** Single-line address for the summary card; empty when not enough is known. */
    summaryAddress(): string {
        const parts = [
            this.state.streetAddress().trim(),
            [this.state.city().trim(), this.state.state().trim()].filter(Boolean).join(', '),
            this.state.postalCode().trim(),
        ].filter(Boolean);
        return parts.join('  •  ');
    }

    /**
     * Fired on successful sign-in via the embedded login. Unlike before, this no
     * longer jumps straight to the Profile step — it loads the user's stored
     * profile + any existing registration and shows the account-summary view so
     * the returning user can review/correct their info first.
     */
    async onLoginContinue(): Promise<void> {
        this.state.setMode('login');
        this.state.populateFromAuth();
        this.accountView.set('summary');
        await this.state.loadSelfProfile();
        // Prefill Profile with existing registration data (teams, form values,
        // waivers) if the user already registered for this role on this job.
        await this.state.loadExistingRegistration(
            this.state.jobPath(),
            this.state.roleKey(),
        );
    }

    /** Summary "Continue" — proceed to the job-specific Profile step. */
    onContinueFromSummary(): void {
        this.autoAdvance.emit();
    }

    /** Persist edits, toast, and return to the summary. */
    async onSaveProfile(): Promise<void> {
        const ok = await this.state.saveSelfProfile();
        if (ok) {
            this.toast.show('Your info has been updated.', 'success', 2500);
            this.accountView.set('summary');
        }
    }

    /**
     * Fired by the inline <app-tos-acceptance-step> "Accept and Continue" button.
     * Validates the create form; if incomplete, shows an error inside the ToS card
     * and blocks advancement. Otherwise sets acceptedTos + mode and advances.
     */
    onCreateContinue(): void {
        if (!this.state.hasCompleteCreateForm()) {
            this.tosError.set('Please complete all required fields above before accepting the Terms of Service.');
            return;
        }
        this.tosError.set(null);
        this.state.setMode('create');
        this.state.setAcceptedTos(true);
        this.autoAdvance.emit();
    }
}
