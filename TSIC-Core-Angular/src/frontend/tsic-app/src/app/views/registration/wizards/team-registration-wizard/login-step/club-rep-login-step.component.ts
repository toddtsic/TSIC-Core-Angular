import { Component, EventEmitter, Output, inject, OnInit, computed, signal } from '@angular/core';

import { ReactiveFormsModule, FormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { ClubService } from '@infrastructure/services/club.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { FormFieldDataService, SelectOption } from '@infrastructure/services/form-field-data.service';
import { InfoTooltipComponent } from '@shared-ui/components/info-tooltip.component';
import type { ClubRepClubDto, ClubRepRegistrationRequest, ClubSearchResult } from '@core/api';

export interface LoginStepResult {
    clubName: string;
    availableClubs: ClubRepClubDto[];
}

@Component({
    selector: 'app-club-rep-login-step',
    templateUrl: './club-rep-login-step.component.html',
    styleUrls: ['./club-rep-login-step.component.scss'],
    standalone: true,
    imports: [ReactiveFormsModule, FormsModule, InfoTooltipComponent]
})
export class ClubRepLoginStepComponent implements OnInit {
    @Output() loginSuccess = new EventEmitter<LoginStepResult>();
    @Output() registrationSuccess = new EventEmitter<LoginStepResult>();

    /** UI state: 'yes' = has account, 'no' = needs registration, null = not selected */
    hasClubRepAccount = signal<'yes' | 'no' | null>(null);
    username = signal('');
    password = signal('');
    loginSubmitting = signal(false);
    registrationSubmitting = signal(false);
    inlineError = signal<string | null>(null);
    credentialsCollapsed = signal(true);
    registrationError = signal<string | null>(null);
    statesOptions: SelectOption[] = [];

    /** Modal state: atomic object containing all duplicate detection UI state */
    private readonly duplicateModalState = signal<{
        isOpen: boolean;
        message: string | null;
        clubs: ClubSearchResult[];
    }>({ isOpen: false, message: null, clubs: [] });

    registrationForm: FormGroup;
    loginForm: FormGroup;

    // Expose modal state for template binding
    duplicateModalOpen = computed(() => this.duplicateModalState().isOpen);
    duplicateModalMessage = computed(() => this.duplicateModalState().message);
    duplicateClub = computed(() => this.duplicateModalState().clubs);

    private readonly authService = inject(AuthService);
    private readonly clubService = inject(ClubService);
    private readonly teamRegService = inject(TeamRegistrationService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly fb = inject(FormBuilder);
    private readonly router = inject(Router);

    private readonly isLoggedIn = computed(() => this.authService.currentUser() !== null);

    constructor() {
        this.registrationForm = this.fb.group({
            clubName: ['', Validators.required],
            firstName: ['', Validators.required],
            lastName: ['', Validators.required],
            street: ['', Validators.required],
            city: ['', Validators.required],
            state: ['', Validators.required],
            zip: ['', Validators.required],
            cellphone: ['', [Validators.required, Validators.pattern(/^[0-9-+\s()]+$/)]],
            username: ['', Validators.required],
            password: ['', [Validators.required, Validators.minLength(6)]],
            email: ['', [Validators.required, Validators.email]]
        });

        this.loginForm = this.fb.group({
            username: ['', Validators.required],
            password: ['', Validators.required]
        });
    }

    ngOnInit(): void {
        this.statesOptions = this.fieldData.getOptionsForDataSource('states');

        const loggedIn = this.isLoggedIn();
        if (loggedIn) {
            this.hasClubRepAccount.set('yes');
            const currentUser = this.authService.currentUser();
            if (currentUser?.username) {
                this.username.set(currentUser.username);
                this.loginForm.patchValue({ username: currentUser.username });
            }
        }
    }

    private async doInlineLogin(): Promise<void> {
        this.inlineError.set(null);
        const u = (this.loginForm.value.username || '').toString().trim();
        const p = (this.loginForm.value.password || '').toString();

        // Defensive: explicit validation with user feedback
        if (!u || !p) {
            this.inlineError.set('Username and password are required.');
            return;
        }

        if (this.loginSubmitting()) {
            // Silent skip—UI spinner already shows in-flight state
            return;
        }

        this.loginSubmitting.set(true);
        console.debug('ClubRep login flow', { event: 'login_start', username: u });

        return new Promise((resolve, reject) => {
            this.authService.login({ username: u, password: p }).subscribe({
                next: (response) => {
                    this.loginSubmitting.set(false);
                    console.debug('ClubRep login flow', { event: 'login_success', submitting: this.loginSubmitting() });
                    // Check TOS requirement before proceeding
                    if (this.authService.checkAndNavigateToTosIfRequired(response, this.router, this.router.url)) {
                        reject(new Error('TOS required')); // Prevent wizard progression until TOS signed
                        return;
                    }
                    resolve();
                },
                error: (err) => {
                    this.loginSubmitting.set(false);
                    console.debug('ClubRep login flow', { event: 'login_error', status: err?.status });
                    // Handle various error response structures
                    let errorMessage = 'Login failed. Please check your username and password.';
                    if (err?.error) {
                        if (typeof err.error === 'string') {
                            errorMessage = err.error;
                        } else if (err.error.error) {
                            // Nested error structure: {error: {error: 'message'}}
                            errorMessage = err.error.error;
                        } else if (err.error.message) {
                            errorMessage = err.error.message;
                        }
                    }
                    this.inlineError.set(errorMessage);
                    reject(err);
                }
            });
        });
    }
    async signInThenProceed(): Promise<void> {
        if (this.loginForm.invalid) {
            return;
        }

        try {
            const loginSucceeded = await this.doInlineLogin().then(() => true).catch(() => false);
            if (!loginSucceeded || this.inlineError()) return;

            this.teamRegService.getMyClubs().subscribe({
                next: (clubs) => {
                    if (clubs.length === 0) {
                        this.inlineError.set('You are not registered as a club representative.');
                        return;
                    }

                    this.hasClubRepAccount.set('yes');

                    // Emit success with club data - parent handles club selection modal
                    this.loginSuccess.emit({
                        clubName: clubs.length === 1 ? clubs[0].clubName : '',
                        availableClubs: clubs
                    });
                },
                error: (err) => {
                    this.inlineError.set('Failed to load your clubs. Please try again.');
                    console.error('Failed to load clubs:', err);
                }
            });
        } finally {
            // doInlineLogin handles submitting state
        }
    }

    submitRegistration(): void {
        if (this.registrationForm.invalid) return;

        this.registrationSubmitting.set(true);
        this.registrationError.set(null);
        this.setDuplicateModalState({ isOpen: false, message: null, clubs: [] });

        const request: ClubRepRegistrationRequest = {
            clubName: this.registrationForm.value.clubName,
            firstName: this.registrationForm.value.firstName,
            lastName: this.registrationForm.value.lastName,
            email: this.registrationForm.value.email,
            username: this.registrationForm.value.username,
            password: this.registrationForm.value.password,
            streetAddress: this.registrationForm.value.street,
            city: this.registrationForm.value.city,
            state: this.registrationForm.value.state,
            postalCode: this.registrationForm.value.zip,
            cellphone: this.registrationForm.value.cellphone
        };

        this.clubService.registerClub(request).subscribe({
            next: (response) => {
                this.registrationSubmitting.set(false);

                const similarClubResults = (response.similarClubs ?? []) as ClubSearchResult[];

                if (!response.success) {
                    this.setDuplicateModalState({
                        isOpen: true,
                        message: response.message ?? 'A club with a very similar name already exists. Please verify before creating a new club rep.',
                        clubs: similarClubResults
                    });
                    return; // do not proceed to auto-login
                }

                // Registration succeeded; reset form and attempt auto-login
                this.registrationForm.reset();

                if (similarClubResults.length > 0) {
                    console.debug('ClubRep registration', { event: 'similar_clubs_found', count: similarClubResults.length });
                }

                this.authService.login({
                    username: request.username,
                    password: request.password
                }).subscribe({
                    next: (response) => {
                        // Check TOS requirement before proceeding
                        if (this.authService.checkAndNavigateToTosIfRequired(response, this.router, this.router.url)) {
                            return; // User redirected to TOS
                        }
                        this.hasClubRepAccount.set('yes');
                        this.registrationSuccess.emit({
                            clubName: request.clubName,
                            availableClubs: [{ clubName: request.clubName, isInUse: false }]
                        });
                    },
                    error: (error: HttpErrorResponse) => {
                        this.registrationError.set('Account created successfully, but auto-login failed. Please use the login form above.');
                        this.hasClubRepAccount.set('yes');
                    }
                });
            },
            error: (error: HttpErrorResponse) => {
                this.registrationSubmitting.set(false);
                console.debug('ClubRep registration error', { status: error.status, hasMessage: !!error.error?.message });

                // Handle duplicate/similar club conflict (409 or 400 with similarClubs payload)
                const hasSimilarClubsPayload = error.error && Array.isArray(error.error.similarClubs);
                if ((error.status === 409 || (error.status === 400 && hasSimilarClubsPayload)) && error.error) {
                    const similar = (error.error.similarClubs ?? []) as ClubSearchResult[];
                    this.setDuplicateModalState({
                        isOpen: true,
                        message: error.error.message ?? 'A club with a very similar name already exists. Please verify before creating a new club rep.',
                        clubs: similar
                    });
                    return;
                }

                // Generic error handling for other failures
                let errorMessage = 'Registration failed. Please try again.';
                if (error.error) {
                    if (typeof error.error === 'string') {
                        errorMessage = error.error;
                    } else if (error.error.message) {
                        errorMessage = error.error.message;
                    } else if (error.error.title) {
                        errorMessage = error.error.title;
                        if (error.error.detail) {
                            errorMessage += ': ' + error.error.detail;
                        }
                    } else if (error.error.errors) {
                        const errors = Object.values(error.error.errors).flat();
                        errorMessage = errors.join(', ');
                    }
                }

                if (error.status >= 500) {
                    errorMessage += ` (Server Error ${error.status})`;
                }

                this.registrationError.set(errorMessage);
            }
        });
    }

    dismissDuplicateWarning(): void {
        this.setDuplicateModalState({ isOpen: false, message: null, clubs: [] });
    }

    /**
     * Atomic reset of duplicate modal state and form.
     * Returns user to initial "Have credentials?" prompt.
     */
    closeDuplicateModal(): void {
        this.setDuplicateModalState({ isOpen: false, message: null, clubs: [] });
        this.hasClubRepAccount.set(null);
    }

    /**
     * Update duplicate modal state atomically.
     * Ensures modal, message, and club list stay in sync.
     */
    private setDuplicateModalState(state: { isOpen: boolean; message: string | null; clubs: ClubSearchResult[] }): void {
        this.duplicateModalState.set(state);
    }

    toggleCredentialsCollapsed(): void {
        this.credentialsCollapsed.update((v) => !v);
    }
}
