import { Component, inject, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TeamsStepComponent } from './teams-step/teams-step.component';
import { ClubTeamManagementComponent } from './club-team-management/club-team-management.component';
import { TwActionBarComponent } from './action-bar/tw-action-bar.component';
import { TwStepIndicatorComponent, WizardStep } from './step-indicator/tw-step-indicator.component';
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
    imports: [CommonModule, ReactiveFormsModule, FormsModule, TeamsStepComponent, ClubTeamManagementComponent, TwActionBarComponent, TwStepIndicatorComponent, InfoTooltipComponent]
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
    inlineError: string | null = null;
    hasTeamsInLibrary = false;
    showClubSelectionModal = false;

    // Accordion collapse state
    credentialsCollapsed = true;

    // Computed: determine if user is logged in
    private readonly isLoggedIn = computed(() => this.authService.currentUser() !== null);

    stepLabels: Record<number, string> = {
        1: 'Login',
        2: 'Manage Club Teams',
        3: 'Register Teams',
        4: 'Payment',
        5: 'Confirmation'
    };

    wizardSteps: WizardStep[] = [
        { stepNumber: 1, label: 'Login' },
        { stepNumber: 2, label: 'Manage Teams' },
        { stepNumber: 3, label: 'Register' },
        { stepNumber: 4, label: 'Payment' },
        { stepNumber: 5, label: 'Confirmation' }
    ];

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

    async signInThenGoToStep2(): Promise<void> {
        if (!this.username || !this.password) {
            return;
        }

        try {
            await this.doInlineLogin();
            if (this.inlineError) return;

            // Fetch clubs after login - stay on step 1 until club selected
            this.teamRegService.getMyClubs().subscribe({
                next: (clubs) => {
                    this.availableClubs = clubs;

                    if (clubs.length === 0) {
                        this.inlineError = 'You are not registered as a club representative.';
                        return;
                    }

                    // Auto-select if only one club, otherwise require selection
                    if (clubs.length === 1) {
                        this.selectedClub = clubs[0].clubName;
                        this.clubName = clubs[0].clubName;
                    } else {
                        this.selectedClub = null;
                    }
                    this.hasClubRepAccount = 'yes';
                    // Show modal for club confirmation/selection
                    this.showClubSelectionModal = true;
                },
                error: (err) => {
                    this.inlineError = 'Failed to load your clubs. Please try again.';
                    console.error('Failed to load clubs:', err);
                }
            });
        } finally {
            // no-op; doInlineLogin toggles submitting
        }
    }

    continueFromStep1(): void {
        if (!this.selectedClub) {
            this.inlineError = 'Please select a club before continuing.';
            return;
        }
        this.clubName = this.selectedClub;
        this.inlineError = null;
        this.showClubSelectionModal = false;
        this.step = 2;
    }

    cancelClubSelection(): void {
        this.showClubSelectionModal = false;
        this.selectedClub = null;
        this.inlineError = null;
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

    onTeamsLoaded(count: number): void {
        this.hasTeamsInLibrary = count > 0;
    }

    goToTeamsStep(): void {
        this.step = 3;
    }
}