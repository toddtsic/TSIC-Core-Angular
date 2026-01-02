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

    hasClubRepAccount = signal<'yes' | 'no' | null>(null);
    username = '';
    password = '';
    submitting = signal(false);
    inlineError = signal<string | null>(null);
    credentialsCollapsed = signal(true);

    registrationForm: FormGroup;
    loginForm: FormGroup;
    registrationError = signal<string | null>(null);
    similarClubs = signal<ClubSearchResult[]>([]);
    statesOptions: SelectOption[] = [];

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
                this.username = currentUser.username;
                this.loginForm.patchValue({ username: currentUser.username });
            }
        }
    }

    private async doInlineLogin(): Promise<void> {
        this.inlineError.set(null);
        const u = (this.loginForm.value.username || '').toString();
        const p = (this.loginForm.value.password || '').toString();
        if (!u || !p || this.submitting()) {
            return;
        }
        this.submitting.set(true);
        console.error('club-rep inline login start', { username: u });
        return new Promise((resolve, reject) => {
            this.authService.login({ username: u.trim(), password: p }).subscribe({
                next: (response) => {
                    this.submitting.set(false);
                    console.error('club-rep inline login success', { submitting: this.submitting(), inlineError: this.inlineError() });
                    // Check TOS requirement before proceeding
                    if (this.authService.checkAndNavigateToTosIfRequired(response, this.router, this.router.url)) {
                        reject(new Error('TOS required')); // Prevent wizard progression until TOS signed
                        return;
                    }
                    resolve();
                },
                error: (err) => {
                    this.submitting.set(false);
                    console.error('club-rep inline login error', {
                        status: err?.status,
                        topLevelMessage: err?.message,
                        errorShape: err?.error,
                        nestedError: err?.error?.error,
                        submitting: this.submitting
                    });
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
                    console.error('club-rep inline login set inlineError', {
                        inlineError: this.inlineError(),
                        submitting: this.submitting()
                    });
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
            await this.doInlineLogin();
            if (this.inlineError()) return;

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

        this.submitting.set(true);
        this.registrationError.set(null);
        this.similarClubs.set([]);

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
                this.submitting.set(false);

                if (response.similarClubs && response.similarClubs.length > 0) {
                    this.similarClubs.set(response.similarClubs);
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
                this.submitting.set(false);
                console.error('Club registration error:', error);

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

    dismissSimilarClubsWarning(): void {
        this.similarClubs.set([]);
    }

    toggleCredentialsCollapsed(): void {
        this.credentialsCollapsed.update((v) => !v);
    }
}
