import { Component, EventEmitter, Output, inject, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { ClubService } from '../../../core/services/club.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { FormFieldDataService, SelectOption } from '../../../core/services/form-field-data.service';
import { InfoTooltipComponent } from '../../../shared/components/info-tooltip.component';
import type { ClubRepClubDto, ClubRepRegistrationRequest, ClubSearchResult } from '../../../core/api/models';

export interface LoginStepResult {
    clubName: string;
    availableClubs: ClubRepClubDto[];
}

@Component({
    selector: 'app-club-rep-login-step',
    templateUrl: './club-rep-login-step.component.html',
    styleUrls: ['./club-rep-login-step.component.scss'],
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, FormsModule, InfoTooltipComponent]
})
export class ClubRepLoginStepComponent implements OnInit {
    @Output() loginSuccess = new EventEmitter<LoginStepResult>();
    @Output() registrationSuccess = new EventEmitter<LoginStepResult>();

    hasClubRepAccount: 'yes' | 'no' | null = null;
    username = '';
    password = '';
    submitting = false;
    inlineError: string | null = null;
    credentialsCollapsed = true;

    registrationForm: FormGroup;
    loginForm: FormGroup;
    registrationError: string | null = null;
    similarClubs: ClubSearchResult[] = [];
    statesOptions: SelectOption[] = [];

    private readonly authService = inject(AuthService);
    private readonly clubService = inject(ClubService);
    private readonly teamRegService = inject(TeamRegistrationService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly fb = inject(FormBuilder);

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
            this.hasClubRepAccount = 'yes';
            const currentUser = this.authService.currentUser();
            if (currentUser?.username) {
                this.username = currentUser.username;
                this.loginForm.patchValue({ username: currentUser.username });
            }
        }
    }

    private async doInlineLogin(): Promise<void> {
        this.inlineError = null;
        const u = (this.loginForm.value.username || '').toString();
        const p = (this.loginForm.value.password || '').toString();
        if (!u || !p || this.submitting) {
            return;
        }
        this.submitting = true;
        return new Promise((resolve, reject) => {
            this.authService.login({ username: u.trim(), password: p }).subscribe({
                next: () => {
                    this.submitting = false;
                    resolve();
                },
                error: (err) => {
                    this.submitting = false;
                    this.inlineError = err?.error?.message || 'Login failed. Please check your username and password.';
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
            if (this.inlineError) return;

            this.teamRegService.getMyClubs().subscribe({
                next: (clubs) => {
                    if (clubs.length === 0) {
                        this.inlineError = 'You are not registered as a club representative.';
                        return;
                    }

                    this.hasClubRepAccount = 'yes';

                    // Emit success with club data - parent handles club selection modal
                    this.loginSuccess.emit({
                        clubName: clubs.length === 1 ? clubs[0].clubName : '',
                        availableClubs: clubs
                    });
                },
                error: (err) => {
                    this.inlineError = 'Failed to load your clubs. Please try again.';
                    console.error('Failed to load clubs:', err);
                }
            });
        } finally {
            // doInlineLogin handles submitting state
        }
    }

    submitRegistration(): void {
        if (this.registrationForm.invalid) return;

        this.submitting = true;
        this.registrationError = null;
        this.similarClubs = [];

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
                this.submitting = false;

                if (response.similarClubs && response.similarClubs.length > 0) {
                    this.similarClubs = response.similarClubs;
                }

                this.authService.login({
                    username: request.username,
                    password: request.password
                }).subscribe({
                    next: () => {
                        this.hasClubRepAccount = 'yes';
                        this.registrationSuccess.emit({
                            clubName: request.clubName,
                            availableClubs: [{ clubName: request.clubName, clubId: 0, isInUse: false }]
                        });
                    },
                    error: (error: HttpErrorResponse) => {
                        this.registrationError = 'Account created successfully, but auto-login failed. Please use the login form above.';
                        this.hasClubRepAccount = 'yes';
                    }
                });
            },
            error: (error: HttpErrorResponse) => {
                this.submitting = false;
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

                this.registrationError = errorMessage;
            }
        });
    }

    dismissSimilarClubsWarning(): void {
        this.similarClubs = [];
    }
}
