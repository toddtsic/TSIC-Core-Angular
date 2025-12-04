import { Component, inject, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TwActionBarComponent } from '../shared/tw-action-bar.component';
import { TeamsStepComponent } from './teams-step/teams-step.component';
import { TeamRegistrationService } from './services/team-registration.service';
import { FormFieldDataService, SelectOption } from '../../core/services/form-field-data.service';
import { JobService } from '../../core/services/job.service';
import { JobContextService } from '../../core/services/job-context.service';
import { ClubService } from '../../core/services/club.service';
import { AuthService } from '../../core/services/auth.service';
import type { ClubRepRegistrationRequest, ClubSearchResult } from '../../core/api/models';

@Component({
    selector: 'app-team-registration-wizard',
    templateUrl: './team-registration-wizard.component.html',
    styleUrls: ['./team-registration-wizard.component.scss'],
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, TwActionBarComponent, TeamsStepComponent]
})
export class TeamRegistrationWizardComponent implements OnInit {
    step = 1;
    hasClubRepAccount: boolean | null = null;
    clubName: string | null = null;
    availableClubs: string[] = [];
    selectedClub: string | null = null;

    // Computed: determine if user is logged in
    private readonly isLoggedIn = computed(() => this.authService.currentUser() !== null);

    stepLabels: Record<number, string> = {
        1: 'Login',
        2: 'Teams'
    };
    loginForm: FormGroup;
    registrationForm: FormGroup;
    submitting = false;
    loginError: string | null = null;
    registrationError: string | null = null;
    similarClubs: ClubSearchResult[] = [];
    private readonly route = inject(ActivatedRoute);
    private readonly jobService = inject(JobService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly clubService = inject(ClubService);
    private readonly teamRegService = inject(TeamRegistrationService);
    readonly authService = inject(AuthService);
    statesOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');

    constructor(readonly fb: FormBuilder) {
        this.loginForm = this.fb.group({
            username: ['', Validators.required],
            password: ['', Validators.required]
        });
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
    }

    ngOnInit(): void {
        // Initialize hasClubRepAccount based on current auth state
        const loggedIn = this.isLoggedIn();
        console.log('TeamWizard ngOnInit - isLoggedIn:', loggedIn, 'currentUser:', this.authService.currentUser());
        if (loggedIn) {
            this.hasClubRepAccount = true;
        }

        // Get jobPath from JobContextService (extracted from URL)
        const jobPath = this.jobContext.jobPath();

        if (jobPath) {
            // Load job metadata and set JsonOptions for dropdowns
            this.jobService.fetchJobMetadata(jobPath).subscribe({
                next: (job) => {
                    this.fieldData.setJobOptions(job.jsonOptions);
                    // Job-specific dropdown options now available throughout the app
                },
                error: (err) => {
                    console.error('Failed to load job metadata:', err);
                    // Continue with static fallback options
                }
            });
        }
    }

    nextStep() {
        this.step++;
    }

    prevStep() {
        this.step--;
    }

    submitLogin() {
        if (this.loginForm.invalid) return;

        this.submitting = true;
        this.loginError = null;

        const credentials = {
            username: this.loginForm.value.username,
            password: this.loginForm.value.password
        };

        this.authService.login(credentials).subscribe({
            next: () => {
                this.submitting = false;
                this.hasClubRepAccount = true;
                // Fetch clubs for this user
                this.teamRegService.getMyClubs().subscribe({
                    next: (clubs) => {
                        this.availableClubs = clubs;
                        if (clubs.length === 1) {
                            // Auto-select single club
                            this.selectedClub = clubs[0];
                            this.clubName = clubs[0];
                        } else if (clubs.length === 0) {
                            this.loginError = 'You are not registered as a club representative.';
                        }
                        // If multiple clubs, user will select from UI
                    },
                    error: (err) => {
                        this.loginError = 'Failed to load your clubs. Please try again.';
                        console.error('Failed to load clubs:', err);
                    }
                });
            },
            error: (error: HttpErrorResponse) => {
                this.submitting = false;
                this.loginError = error?.error?.message || error?.message || 'Login failed. Please check your credentials.';
            }
        });
    }

    selectClub(clubName: string): void {
        this.selectedClub = clubName;
        this.clubName = clubName;
    }

    canProceedToTeams(): boolean {
        return this.hasClubRepAccount === true && this.selectedClub !== null;
    }

    submitRegistration() {
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

                // Show similar clubs warning if any found
                if (response.similarClubs && response.similarClubs.length > 0) {
                    this.similarClubs = response.similarClubs;
                }

                // Auto-login with the new credentials
                this.authService.login({
                    username: request.username,
                    password: request.password
                }).subscribe({
                    next: () => {
                        // Update component state to reflect logged-in status
                        this.hasClubRepAccount = true;
                        this.clubName = request.clubName;
                        this.nextStep();
                    },
                    error: (error: HttpErrorResponse) => {
                        // Registration succeeded but login failed - show error but allow manual retry
                        this.registrationError = 'Account created successfully, but auto-login failed. Please use the login form above.';
                        this.hasClubRepAccount = true;
                    }
                });
            },
            error: (error: HttpErrorResponse) => {
                this.submitting = false;
                console.error('Club registration error:', error);

                // Try to extract meaningful error details
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
                        // Validation errors object
                        const errors = Object.values(error.error.errors).flat();
                        errorMessage = errors.join(', ');
                    }
                }

                // Include status for debugging during development
                if (error.status >= 500) {
                    errorMessage += ` (Server Error ${error.status})`;
                }

                this.registrationError = errorMessage;
            }
        });
    }

    dismissSimilarClubsWarning() {
        this.similarClubs = [];
    }

    canContinue(): boolean {
        if (this.hasClubRepAccount === true) {
            return this.loginForm.valid;
        }
        if (this.hasClubRepAccount === false) {
            return this.registrationForm.valid;
        }
        return false;
    }

    continueLabel(): string {
        return this.step === 1 ? 'Continue' : 'Finish';
    }

    showContinueButton(): boolean {
        if (this.step === 1 && this.hasClubRepAccount === null) {
            return false;
        }
        return true;
    }

    currentStepLabel(): string {
        return this.stepLabels[this.step] || '';
    }
}