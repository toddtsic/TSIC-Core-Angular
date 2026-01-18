import { Component, inject, OnInit, OnDestroy, signal, computed, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, switchMap, catchError, of } from 'rxjs';
import { TeamsStepComponent } from './teams-step/teams-step.component';
import { TwActionBarComponent } from './action-bar/tw-action-bar.component';
import { TwStepIndicatorComponent } from './step-indicator/tw-step-indicator.component';
import { ClubRepLoginStepComponent, LoginStepResult } from './login-step/club-rep-login-step.component';
import { TeamPaymentStepComponent } from './payment-step/payment.component';
import { FormFieldDataService, SelectOption } from '@infrastructure/services/form-field-data.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { ClubService } from '@infrastructure/services/club.service';
import { TeamRegistrationService } from './services/team-registration.service';
import { TeamPaymentService } from './services/team-payment.service';
import { UserPreferencesService } from '@infrastructure/services/user-preferences.service';
import { ToastService } from '@shared-ui/toast.service';
import type { ClubRepClubDto, ClubSearchResult } from '@core/api';

enum WizardStep {
    Login = 1,
    RegisterTeams = 2,
    Payment = 3,
    Review = 4
}

@Component({
    selector: 'app-team-registration-wizard',
    templateUrl: './team-registration-wizard.component.html',
    styleUrls: ['./team-registration-wizard.component.scss'],
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, FormsModule, TeamsStepComponent, TeamPaymentStepComponent, TwActionBarComponent, TwStepIndicatorComponent, ClubRepLoginStepComponent]
})
export class TeamRegistrationWizardComponent implements OnInit, OnDestroy {
    // Expose enum to template
    readonly WizardStep = WizardStep;

    // All reactive state as signals
    readonly step = signal(WizardStep.Login);
    readonly clubName = signal<string | null>(null);
    readonly availableClubs = signal<ClubRepClubDto[]>([]);
    readonly selectedClub = signal<string | null>(null);
    readonly showClubSelectionModal = signal(false);
    readonly showAddClubModal = signal(false);
    readonly inlineError = signal<string | null>(null);
    readonly jobPath = signal<string | null>(null);
    readonly addClubSubmitting = signal(false);
    readonly addClubError = signal<string | null>(null);
    readonly addClubSuccess = signal<string | null>(null);
    readonly similarClubs = signal<ClubSearchResult[]>([]);
    readonly clubInfoCollapsed = signal(false);
    readonly clubRepInfoAlreadyRead = signal(false);
    readonly metadataError = signal<string | null>(null);

    // Non-reactive properties
    @ViewChild(TeamsStepComponent) teamsStep?: TeamsStepComponent;
    addClubForm!: FormGroup;
    statesOptions: SelectOption[] = [];
    private metadataSubscription?: Subscription;
    private addClubSubscription?: Subscription;
    private reloadClubsSubscription?: Subscription;
    private addClubTimeoutId?: ReturnType<typeof setTimeout>;

    // Simplified 2-step flow: Login → Register Teams
    wizardSteps = computed(() => {
        return [
            { stepNumber: WizardStep.Login, label: 'Login' },
            { stepNumber: WizardStep.RegisterTeams, label: 'Register Teams' },
            { stepNumber: WizardStep.Payment, label: 'Payment' },
            { stepNumber: WizardStep.Review, label: 'Review' }
        ];
    });

    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly jobService = inject(JobService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly fb = inject(FormBuilder);
    private readonly clubService = inject(ClubService);
    private readonly teamRegService = inject(TeamRegistrationService);
    private readonly teamPaymentService = inject(TeamPaymentService);
    private readonly userPrefs = inject(UserPreferencesService);
    private readonly toast = inject(ToastService);

    // Expose registration status and loading state to template
    readonly isTeamRegistrationOpen = computed(() => this.jobService.isTeamRegistrationOpen());
    readonly isLoadingMetadata = computed(() => this.jobService.jobMetadataLoading());

    // Step-specific banner messaging
    readonly stepBannerConfig = computed(() => {
        const currentStep = this.step();

        switch (currentStep) {
            case WizardStep.Login:
                return {
                    icon: 'bi-box-arrow-in-right',
                    alertClass: 'alert-primary',
                    title: 'Club Rep Login',
                    message: 'Log in with your Club Rep credentials to register your teams.'
                };
            case WizardStep.RegisterTeams:
                return {
                    icon: 'bi-trophy-fill',
                    alertClass: 'alert-success',
                    title: 'Register Teams',
                    message: 'Enter your team details and select age groups to register for this event.'
                };
            case WizardStep.Payment:
                return {
                    icon: 'bi-credit-card-fill',
                    alertClass: 'alert-info',
                    title: 'Payment',
                    message: 'Review your team registrations and complete payment.'
                };
            default:
                return {
                    icon: 'bi-info-circle-fill',
                    alertClass: 'alert-info',
                    title: '',
                    message: ''
                };
        }
    });

    constructor() {
        this.addClubForm = this.fb.group({
            clubName: ['', Validators.required]
        });
        // Initialize accordion state based on whether user has read it before
        const hasRead = this.userPrefs.isClubRepModalInfoRead();
        this.clubInfoCollapsed.set(hasRead);
        this.clubRepInfoAlreadyRead.set(hasRead);
    }

    ngOnInit(): void {
        // Load state options for add club form
        this.statesOptions = this.fieldData.getOptionsForDataSource('states');

        // Get jobPath from route params using service
        const resolvedPath = this.jobContext.resolveFromRoute(this.route);
        this.jobPath.set(resolvedPath);

        if (resolvedPath) {
            this.loadMetadata(resolvedPath);
        }
    }

    loadMetadata(jobPath: string): void {
        // Validate jobPath before attempting fetch
        if (!jobPath) {
            this.metadataError.set('Invalid job path. Please refresh the page.');
            return;
        }

        // Cancel any pending request to prevent duplicate calls
        this.metadataSubscription?.unsubscribe();

        this.metadataError.set(null);
        // Single fetch - sets both JsonOptions and registration status
        this.metadataSubscription = this.jobService.fetchJobMetadata(jobPath).subscribe({
            next: (job) => {
                this.fieldData.setJobOptions(job.jsonOptions);
                // Registration status now available via jobService.isTeamRegistrationOpen()
            },
            error: (err) => {
                console.error('Failed to load job metadata:', err);
                this.metadataError.set('Failed to load registration information. Please try again.');
            }
        });
    }

    ngOnDestroy(): void {
        // Clean up subscriptions to prevent memory leaks
        this.metadataSubscription?.unsubscribe();
        this.addClubSubscription?.unsubscribe();
        this.reloadClubsSubscription?.unsubscribe();

        // Clear any pending timeouts
        if (this.addClubTimeoutId) {
            clearTimeout(this.addClubTimeoutId);
        }
    }

    handleLoginSuccess(result: LoginStepResult): void {
        this.availableClubs.set(result.availableClubs);
        this.selectedClub.set(result.clubName);
        this.clubName.set(result.clubName);

        // Club selection now handled in login modal - proceed directly to teams step
        this.step.set(WizardStep.RegisterTeams);
    }

    handleRegistrationSuccess(result: LoginStepResult): void {
        this.clubName.set(result.clubName);
        this.availableClubs.set(result.availableClubs);
        this.step.set(WizardStep.RegisterTeams);
    }

    goBackToStep1() {
        this.step.set(WizardStep.Login);
    }

    prevStep() {
        this.step.update(s => s - 1);
    }

    selectClub(clubName: string): void {
        this.selectedClub.set(clubName);
        this.clubName.set(clubName);
    }

    loadClubs(): void {
        this.reloadClubsSubscription?.unsubscribe();

        this.reloadClubsSubscription = this.teamRegService.getMyClubs().subscribe({
            next: (clubs) => {
                this.availableClubs.set(clubs);
            },
            error: (err) => {
                console.error('Failed to reload clubs:', err);
            }
        });
    }

    continueFromStep1(): void {
        if (!this.selectedClub()) {
            this.inlineError.set('Please select a club before continuing.');
            return;
        }
        this.clubName.set(this.selectedClub());
        this.inlineError.set(null);
        this.showClubSelectionModal.set(false);
        this.step.set(WizardStep.RegisterTeams);
    }

    cancelClubSelection(): void {
        this.showClubSelectionModal.set(false);
        this.selectedClub.set(null);
        this.inlineError.set(null);
    }

    toggleClubInfoCollapsed(): void {
        this.clubInfoCollapsed.update(collapsed => !collapsed);
    }

    acknowledgeClubRepInfo(): void {
        this.userPrefs.markClubRepModalInfoAsRead();
        this.clubRepInfoAlreadyRead.set(true);
        this.clubInfoCollapsed.set(true);
    }

    showAddClubForm(): void {
        this.showAddClubModal.set(true);
        this.addClubError.set(null);
        this.addClubSuccess.set(null);
        this.similarClubs.set([]);
        this.addClubForm.reset();
    }

    cancelAddClub(): void {
        this.showAddClubModal.set(false);
        this.addClubForm.reset();
        this.addClubError.set(null);
        this.addClubSuccess.set(null);
        this.similarClubs.set([]);
    }

    submitAddClub(): void {
        if (this.addClubForm.invalid) return;

        this.addClubSubmitting.set(true);
        this.addClubError.set(null);
        this.addClubSuccess.set(null);

        const request = {
            clubName: this.addClubForm.value.clubName,
            useExistingClubId: undefined
        };

        // Cancel any pending add club request
        this.addClubSubscription?.unsubscribe();

        this.addClubSubscription = this.clubService.addClub(request).pipe(
            switchMap((response) => {
                if (response.success) {
                    this.addClubSuccess.set('Club added successfully!');
                    // Chain the clubs reload
                    return this.teamRegService.getMyClubs();
                } else {
                    this.addClubError.set(response.message || 'Failed to add club');
                    if (response.similarClubs) {
                        this.similarClubs.set(response.similarClubs);
                    }
                    this.addClubSubmitting.set(false);
                    // Return empty observable to complete the chain
                    return of(null);
                }
            }),
            catchError((err) => {
                this.addClubError.set('Failed to add club. Please try again.');
                this.addClubSubmitting.set(false);
                console.error('Add club error:', err);
                return of(null);
            })
        ).subscribe({
            next: (clubs) => {
                if (clubs) {
                    // Successfully added and reloaded clubs
                    this.availableClubs.set(clubs);
                    // Clear any existing timeout
                    if (this.addClubTimeoutId) {
                        clearTimeout(this.addClubTimeoutId);
                    }
                    this.addClubTimeoutId = globalThis.setTimeout(() => {
                        this.showAddClubModal.set(false);
                        this.addClubForm.reset();
                        this.addClubSubmitting.set(false);
                        this.addClubTimeoutId = undefined;
                    }, 1500);
                }
            },
            error: (err) => {
                // This should be caught by catchError above, but just in case
                console.error('Unexpected error in submitAddClub:', err);
                this.addClubSubmitting.set(false);
            }
        });
    }
}