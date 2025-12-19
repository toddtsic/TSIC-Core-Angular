import { Component, inject, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TeamsStepComponent } from './teams-step/teams-step.component';
import { TwActionBarComponent } from './action-bar/tw-action-bar.component';
import { TeamRegistrationService } from './services/team-registration.service';
import { FormFieldDataService, SelectOption } from '../../core/services/form-field-data.service';
import { JobService } from '../../core/services/job.service';
import { JobContextService } from '../../core/services/job-context.service';
import { ClubService } from '../../core/services/club.service';
import { AuthService } from '../../core/services/auth.service';
import { InfoTooltipComponent } from '../../shared/components/info-tooltip.component';
import type { ClubRepClubDto, ClubRepRegistrationRequest, ClubSearchResult } from '../../core/api/models';

@Component({
    selector: 'app-team-registration-wizard',
    templateUrl: './team-registration-wizard.component.html',
    styleUrls: ['./team-registration-wizard.component.scss'],
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, FormsModule, TeamsStepComponent, TwActionBarComponent, InfoTooltipComponent]
})
export class TeamRegistrationWizardComponent implements OnInit {
    step = 1;
    hasClubRepAccount: 'yes' | 'no' | null = null;
    clubName: string | null = null;
    availableClubs: ClubRepClubDto[] = [];
    selectedClub: string | null = null;
    username = '';
    password = '';
    submitting = false;
    submittingAction: 'register' | 'manage' | null = null;
    inlineError: string | null = null;

    // Computed: determine if user is logged in
    private readonly isLoggedIn = computed(() => this.authService.currentUser() !== null);

    stepLabels: Record<number, string> = {
        1: 'Login',
        2: 'Teams'
    };
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly jobService = inject(JobService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly clubService = inject(ClubService);
    private readonly teamRegService = inject(TeamRegistrationService);
    readonly authService = inject(AuthService);
    private readonly fb = inject(FormBuilder);
    statesOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');
    registrationForm: FormGroup;
    registrationError: string | null = null;
    similarClubs: ClubSearchResult[] = [];

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
    }

    ngOnInit(): void {
        // Initialize hasClubRepAccount based on current auth state
        const loggedIn = this.isLoggedIn();
        if (loggedIn) {
            this.hasClubRepAccount = 'yes';
            const currentUser = this.authService.currentUser();
            if (currentUser?.username) {
                this.username = currentUser.username;
            }
        }

        // Get jobPath from route params using service
        const jobPath = this.jobContext.resolveFromRoute(this.route);

        if (jobPath) {
            // Load job metadata and set JsonOptions for dropdowns
            this.jobService.fetchJobMetadata(jobPath).subscribe({
                next: (job) => {
                    this.fieldData.setJobOptions(job.jsonOptions);
                },
                error: (err) => {
                    console.error('Failed to load job metadata:', err);
                }
            });
        }
    }

    nextStep() {
        this.step++;
    }

    goBackToStep1() {
        this.step = 1;
    }

    prevStep() {
        this.step--;
    }

    selectClub(clubName: string): void {
        this.selectedClub = clubName;
        this.clubName = clubName;
    }

    private async doInlineLogin(): Promise<void> {
        this.inlineError = null;
        if (!this.username || !this.password || this.submitting) {
            return;
        }
        this.submitting = true;
        return new Promise((resolve, reject) => {
            this.authService.login({ username: this.username.trim(), password: this.password }).subscribe({
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

    async signInThenRegisterTeams(): Promise<void> {
        this.submittingAction = 'register';
        if (!this.username || !this.password) {
            this.submittingAction = null;
            return;
        }

        try {
            await this.doInlineLogin();
            if (this.inlineError) return;

            // Fetch clubs after login
            this.teamRegService.getMyClubs().subscribe({
                next: (clubs) => {
                    this.availableClubs = clubs;
                    if (clubs.length === 1) {
                        // Auto-select single club and proceed
                        this.selectedClub = clubs[0].clubName;
                        this.clubName = clubs[0].clubName;
                        this.hasClubRepAccount = 'yes';
                        this.nextStep();
                    } else if (clubs.length === 0) {
                        this.inlineError = 'You are not registered as a club representative.';
                    }
                    // If multiple clubs, wait for user selection (they'll click button again)
                },
                error: (err) => {
                    this.inlineError = 'Failed to load your clubs. Please try again.';
                    console.error('Failed to load clubs:', err);
                }
            });
        } finally {
            this.submittingAction = null;
        }
    }

    async signInThenManageAccount(): Promise<void> {
        this.submittingAction = 'manage';
        if (!this.username || !this.password) {
            this.submittingAction = null;
            return;
        }

        try {
            await this.doInlineLogin();
            if (this.inlineError) return;

            // TODO: Club Rep Account management page not yet implemented
            this.inlineError = 'Club Rep Account management coming soon! For now, use "Sign in & Register Teams" to proceed with team registration.';
            // const jobPath = this.jobContext.jobPath() || '';
            // const returnUrl = jobPath ? `/${jobPath}/register-team?step=1` : `/register-team?step=1`;
            // this.router.navigateByUrl(`/tsic/club-rep-account?mode=edit&returnUrl=${encodeURIComponent(returnUrl)}`);
        } finally {
            this.submittingAction = null;
        }
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
                        this.hasClubRepAccount = 'yes';
                        this.clubName = request.clubName;
                        this.nextStep();
                    },
                    error: (error: HttpErrorResponse) => {
                        // Registration succeeded but login failed - show error but allow manual retry
                        this.registrationError = 'Account created successfully, but auto-login failed. Please use the login form above.';
                        this.hasClubRepAccount = 'yes';
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