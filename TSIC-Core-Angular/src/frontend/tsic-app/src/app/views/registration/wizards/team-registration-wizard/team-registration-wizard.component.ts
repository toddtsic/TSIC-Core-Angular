import { Component, inject, OnInit, OnDestroy, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, switchMap, catchError, of } from 'rxjs';
import { TeamsStepComponent } from './teams-step/teams-step.component';
import { ClubTeamManagementComponent } from './club-team-management/club-team-management.component';
import { TwActionBarComponent } from './action-bar/tw-action-bar.component';
import { TwStepIndicatorComponent } from './step-indicator/tw-step-indicator.component';
import { ClubRepLoginStepComponent, LoginStepResult } from './login-step/club-rep-login-step.component';
import { ClubManagementModalComponent } from './club-management/club-management-modal.component';
import { FormFieldDataService, SelectOption } from '@infrastructure/services/form-field-data.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { ClubService } from '@infrastructure/services/club.service';
import { TeamRegistrationService } from './services/team-registration.service';
import { UserPreferencesService } from '@infrastructure/services/user-preferences.service';
import { ToastService } from '@shared-ui/toast.service';
import type { ClubRepClubDto, ClubSearchResult } from '@core/api';

@Component({
    selector: 'app-team-registration-wizard',
    templateUrl: './team-registration-wizard.component.html',
    styleUrls: ['./team-registration-wizard.component.scss'],
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, FormsModule, TeamsStepComponent, ClubTeamManagementComponent, TwActionBarComponent, TwStepIndicatorComponent, ClubRepLoginStepComponent, ClubManagementModalComponent]
})
export class TeamRegistrationWizardComponent implements OnInit, OnDestroy {
    // All reactive state as signals
    readonly step = signal(1);
    readonly clubName = signal<string | null>(null);
    readonly availableClubs = signal<ClubRepClubDto[]>([]);
    readonly selectedClub = signal<string | null>(null);
    readonly hasTeamsInLibrary = signal(false);
    readonly showClubSelectionModal = signal(false);
    readonly showAddClubModal = signal(false);
    readonly showManageClubsModal = signal(false);
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
    addClubForm!: FormGroup;
    statesOptions: SelectOption[] = [];
    private metadataSubscription?: Subscription;
    private addClubSubscription?: Subscription;
    private reloadClubsSubscription?: Subscription;
    private addClubTimeoutId?: ReturnType<typeof setTimeout>;

    // Conditional step configuration based on registration status
    wizardSteps = computed(() => {
        const isOpen = this.isTeamRegistrationOpen();

        if (isOpen) {
            // Registration OPEN - Full flow
            return [
                { stepNumber: 1, label: 'Login' },
                { stepNumber: 2, label: 'Manage Teams' },
                { stepNumber: 3, label: 'Register' },
                { stepNumber: 4, label: 'Payment' },
                { stepNumber: 5, label: 'Confirmation' }
            ];
        } else {
            // Registration CLOSED - Build mode
            return [
                { stepNumber: 1, label: 'Login' },
                { stepNumber: 2, label: 'Build Library' }
            ];
        }
    });

    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly jobService = inject(JobService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly fb = inject(FormBuilder);
    private readonly clubService = inject(ClubService);
    private readonly teamRegService = inject(TeamRegistrationService);
    private readonly userPrefs = inject(UserPreferencesService);
    private readonly toast = inject(ToastService);

    // Expose registration status and loading state to template
    readonly isTeamRegistrationOpen = computed(() => this.jobService.isTeamRegistrationOpen());
    readonly isLoadingMetadata = computed(() => this.jobService.jobMetadataLoading());

    // Step-specific banner messaging
    readonly stepBannerConfig = computed(() => {
        const currentStep = this.step();
        const isOpen = this.isTeamRegistrationOpen();

        if (isOpen) {
            // Registration OPEN - Full 5-step flow
            switch (currentStep) {
                case 1:
                    return {
                        icon: 'bi-box-arrow-in-right',
                        alertClass: 'alert-primary',
                        title: 'Club Rep Login',
                        message: 'Log in with your Club Rep credentials to access your <strong>club team library</strong> and register for this event.'
                    };
                case 2:
                    return {
                        icon: 'bi-collection',
                        alertClass: 'alert-info',
                        title: 'Manage Your Team Library',
                        message: 'Add, edit, or organize your club\'s teams. This is your <strong>club team library</strong> for future registrations.'
                    };
                case 3:
                    return {
                        icon: 'bi-check-circle-fill',
                        alertClass: 'alert-success',
                        title: 'Registration is OPEN!',
                        message: 'Select teams from your <strong>club team library</strong> to register for this event.'
                    };
                case 4:
                    return {
                        icon: 'bi-credit-card',
                        alertClass: 'alert-warning',
                        title: 'Payment',
                        message: 'Review your team registrations and submit payment to complete registration.'
                    };
                case 5:
                    return {
                        icon: 'bi-check-circle-fill',
                        alertClass: 'alert-success',
                        title: 'Registration Complete!',
                        message: 'Your teams are registered for this event. Confirmation details have been sent.'
                    };
                default:
                    return {
                        icon: 'bi-info-circle-fill',
                        alertClass: 'alert-info',
                        title: '',
                        message: ''
                    };
            }
        } else {
            // Registration CLOSED - Build mode (2 steps)
            switch (currentStep) {
                case 1:
                    return {
                        icon: 'bi-box-arrow-in-right',
                        alertClass: 'alert-primary',
                        title: 'Club Rep Login',
                        message: 'Log in with your Club Rep credentials to build your <strong>club team library</strong> for when registration opens.'
                    };
                case 2:
                    return {
                        icon: 'bi-info-circle-fill',
                        alertClass: 'alert-info',
                        title: 'Build Your Club Team Library!',
                        message: 'Registration isn\'t open yet, but you can prepare by creating your club\'s teams. When registration opens, you\'ll be ready to go!'
                    };
                default:
                    return {
                        icon: 'bi-info-circle-fill',
                        alertClass: 'alert-info',
                        title: '',
                        message: ''
                    };
            }
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

        // Auto-select if only one club, otherwise require selection
        if (result.availableClubs.length === 1) {
            this.selectedClub.set(result.availableClubs[0].clubName);
            this.clubName.set(result.availableClubs[0].clubName);
        } else {
            this.selectedClub.set(null);
        }

        // Show modal for club confirmation/selection
        this.showClubSelectionModal.set(true);
    }

    handleRegistrationSuccess(result: LoginStepResult): void {
        this.clubName.set(result.clubName);
        this.availableClubs.set(result.availableClubs);
        this.step.set(2);
    }

    goBackToStep1() {
        this.step.set(1);
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
        this.step.set(2);
    }

    cancelClubSelection(): void {
        this.showClubSelectionModal.set(false);
        this.selectedClub.set(null);
        this.inlineError.set(null);
    }

    onTeamsLoaded(count: number): void {
        this.hasTeamsInLibrary.set(count > 0);
    }

    toggleClubInfoCollapsed(): void {
        this.clubInfoCollapsed.update(collapsed => !collapsed);
    }

    acknowledgeClubRepInfo(): void {
        this.userPrefs.markClubRepModalInfoAsRead();
        this.clubRepInfoAlreadyRead.set(true);
        this.clubInfoCollapsed.set(true);
    }

    goToTeamsStep(): void {
        const isOpen = this.isTeamRegistrationOpen();

        if (isOpen) {
            // Registration OPEN - Continue to event registration (step 3)
            this.step.set(3);
        } else if (this.jobPath()) {
            // Registration CLOSED - Navigate back to job home (library management complete)
            this.router.navigate([`/${this.jobPath()}/home`]);
        }
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

    openManageClubsModal(): void {
        this.showManageClubsModal.set(true);
    }

    closeManageClubsModal(): void {
        this.showManageClubsModal.set(false);
    }

    handleClubsChanged(): void {
        // Reload clubs from server and handle selected club removal
        const currentSelected = this.selectedClub();
        
        this.reloadClubsSubscription?.unsubscribe();
        
        this.reloadClubsSubscription = this.teamRegService.getMyClubs().subscribe({
            next: (clubs) => {
                this.availableClubs.set(clubs);
                
                // Check if currently selected club still exists
                if (currentSelected) {
                    const stillExists = clubs.some(c => c.clubName === currentSelected);
                    if (!stillExists) {
                        // Selected club was removed OR renamed - clear selection
                        this.selectedClub.set(null);
                        this.clubName.set(null);
                        
                        // Auto-select if only one club remains
                        if (clubs.length === 1) {
                            const onlyClub = clubs[0];
                            this.selectedClub.set(onlyClub.clubName);
                            this.clubName.set(onlyClub.clubName);
                            this.selectClub(onlyClub.clubName);
                        }
                    }
                }
            },
            error: (err) => {
                console.error('Failed to reload clubs after change:', err);
                this.toast.show('Failed to reload clubs. Please try refreshing the page.', 'danger', 5000);
            }
        });
    }
}