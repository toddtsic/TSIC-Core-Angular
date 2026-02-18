import {
    ChangeDetectionStrategy,
    Component,
    EventEmitter,
    Output,
    Input,
    inject,
    OnInit,
    DestroyRef,
    computed,
    signal,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
    ReactiveFormsModule,
    FormsModule,
    FormBuilder,
    FormGroup,
    Validators,
} from '@angular/forms';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { ClubRepWorkflowService } from '../services/club-rep-workflow.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import {
    FormFieldDataService,
    SelectOption,
} from '@infrastructure/services/form-field-data.service';
import { InfoTooltipComponent } from '@shared-ui/components/info-tooltip.component';
import { AutofocusDirective } from '@shared-ui/directives/autofocus.directive';
import { ToastService } from '@shared-ui/toast.service';
import { WizardModalComponent } from '../../shared/wizard-modal/wizard-modal.component';
import type {
    ClubRepClubDto,
    ClubRepRegistrationRequest,
    ClubSearchResult,
} from '@core/api';

export interface LoginStepResult {
    clubName: string;
    availableClubs: ClubRepClubDto[];
}

@Component({
    selector: 'app-club-rep-login-step',
    templateUrl: './club-rep-login-step.component.html',
    styleUrls: ['./club-rep-login-step.component.scss'],
    standalone: true,
    imports: [
        ReactiveFormsModule,
        FormsModule,
        InfoTooltipComponent,
        AutofocusDirective,
        WizardModalComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClubRepLoginStepComponent implements OnInit {
    @Input() jobPath: string | null = null;
    @Output() loginSuccess = new EventEmitter<LoginStepResult>();
    @Output() registrationSuccess = new EventEmitter<LoginStepResult>();

    /** UI state: 'yes' = has account, 'no' = needs registration, null = not selected */
    hasClubRepAccount = signal<'yes' | 'no' | null>(null);
    loginSubmitting = signal(false);
    registrationSubmitting = signal(false);
    inlineError = signal<string | null>(null);
    credentialsCollapsed = signal(true);
    registrationError = signal<string | null>(null);
    statesOptions: SelectOption[] = [];

    /** Login modal state - shows when user selects 'Yes' to having credentials */
    showLoginModal = signal(false);
    loginModalStep = signal<'credentials' | 'clubSelection'>('credentials');
    availableClubs = signal<ClubRepClubDto[]>([]);
    selectedClub = signal<string | null>(null);
    clubSelectionError = signal<string | null>(null);

    /** Modal state: atomic object containing all duplicate detection UI state */
    private readonly duplicateModalState = signal<{
        isOpen: boolean;
        message: string | null;
        clubs: ClubSearchResult[];
    }>({ isOpen: false, message: null, clubs: [] });

    /** Conflict warning modal state: shows when another rep has already registered teams */
    private readonly conflictWarningState = signal<{
        isOpen: boolean;
        otherRepUsername: string | null;
        teamCount: number;
        pendingEmit: LoginStepResult | null;
    }>({
        isOpen: false,
        otherRepUsername: null,
        teamCount: 0,
        pendingEmit: null,
    });

    registrationForm: FormGroup;
    loginForm: FormGroup;

    // Single subscription for workflow operations (manual cancel-and-resubscribe, auto-cleanup via takeUntilDestroyed)
    private workflowSubscription?: Subscription;
    private readonly destroyRef = inject(DestroyRef);

    // Expose modal state for template binding
    duplicateModalOpen = computed(() => this.duplicateModalState().isOpen);
    duplicateModalMessage = computed(() => this.duplicateModalState().message);
    duplicateClub = computed(() => this.duplicateModalState().clubs);

    // Expose conflict warning modal state for template binding
    conflictWarningOpen = computed(() => this.conflictWarningState().isOpen);
    conflictWarningUsername = computed(
        () => this.conflictWarningState().otherRepUsername,
    );
    conflictWarningTeamCount = computed(
        () => this.conflictWarningState().teamCount,
    );

    private readonly authService = inject(AuthService);
    private readonly jobService = inject(JobService);
    private readonly workflowService = inject(ClubRepWorkflowService);
    private readonly teamRegService = inject(TeamRegistrationService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly fb = inject(FormBuilder);
    private readonly toast = inject(ToastService);

    private readonly isLoggedIn = computed(
        () => this.authService.currentUser() !== null,
    );

    constructor() {
        this.registrationForm = this.fb.group({
            clubName: ['', Validators.required],
            firstName: ['', Validators.required],
            lastName: ['', Validators.required],
            street: ['', Validators.required],
            city: ['', Validators.required],
            state: ['', Validators.required],
            zip: ['', Validators.required],
            cellphone: [
                '',
                [Validators.required, Validators.pattern(/^[0-9-+\s()]+$/)],
            ],
            username: ['', Validators.required],
            password: ['', [Validators.required, Validators.minLength(6)]],
            email: ['', [Validators.required, Validators.email]],
        });

        this.loginForm = this.fb.group({
            username: ['', Validators.required],
            password: ['', Validators.required],
        });
    }

    ngOnInit(): void {
        this.statesOptions = this.fieldData.getOptionsForDataSource('states');

        const loggedIn = this.isLoggedIn();
        if (loggedIn) {
            this.hasClubRepAccount.set('yes');
            const currentUser = this.authService.currentUser();
            if (currentUser?.username) {
                this.loginForm.patchValue({ username: currentUser.username });
            }

            // Phase 1 auto-resume: If authenticated but no Phase 2 token (post-TOS),
            // fetch clubs and open modal at club-selection stage
            if (!this.authService.hasSelectedRole()) {
                this.loginSubmitting.set(true);
                this.inlineError.set(null);

                this.workflowSubscription = this.teamRegService.getMyClubs().pipe(
                    takeUntilDestroyed(this.destroyRef)
                ).subscribe({
                    next: (clubs) => {
                        this.loginSubmitting.set(false);
                        this.availableClubs.set(clubs);

                        if (clubs.length === 0) {
                            this.inlineError.set(
                                'You are not registered as a club representative.',
                            );
                            return;
                        }

                        // Auto-select if only one club
                        if (clubs.length === 1) {
                            this.selectedClub.set(clubs[0].clubName);
                        } else {
                            this.selectedClub.set(null);
                        }

                        // Open modal at club-selection stage (regardless of club count)
                        this.showLoginModal.set(true);
                        this.loginModalStep.set('clubSelection');
                    },
                    error: (err) => {
                        this.loginSubmitting.set(false);
                        console.error('Failed to fetch clubs on init:', err);
                        this.inlineError.set(
                            'Failed to load your clubs. Please try logging in again.',
                        );
                    },
                });
            }
        }
    }

    // ngOnDestroy removed — subscriptions auto-cleaned by takeUntilDestroyed

    signInThenProceed(): void {
        if (this.loginForm.invalid) {
            return;
        }

        // Cancel any in-flight login to prevent race condition
        this.workflowSubscription?.unsubscribe();

        this.inlineError.set(null);
        this.loginSubmitting.set(true);

        const credentials = {
            username: this.loginForm.value.username?.toString().trim() || '',
            password: this.loginForm.value.password?.toString() || '',
        };

        // Validate credentials aren't empty (could be stale if form was reset)
        if (!credentials.username || !credentials.password) {
            this.loginSubmitting.set(false);
            this.inlineError.set('Please enter both username and password.');
            return;
        }

        this.workflowSubscription = this.workflowService
            .loginAndPrepareClubs(
                credentials,
                this.jobPath,
                this.jobService.isTeamRegistrationOpen(),
            )
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (result) => {
                    this.loginSubmitting.set(false);
                    this.hasClubRepAccount.set('yes');
                    this.availableClubs.set(result.clubs);

                    // Auto-select if only one club
                    if (result.clubs.length === 1) {
                        this.selectedClub.set(result.clubs[0].clubName);
                    } else {
                        this.selectedClub.set(null);
                    }

                    const loginResult: LoginStepResult = {
                        clubName: result.clubs.length === 1 ? result.clubs[0].clubName : '',
                        availableClubs: result.clubs,
                    };

                    if (result.hasConflict && result.conflictDetails) {
                        // Show conflict warning modal (close login modal first)
                        this.showLoginModal.set(false);
                        this.conflictWarningState.set({
                            isOpen: true,
                            otherRepUsername:
                                result.conflictDetails.otherRepUsername || 'another club rep',
                            teamCount: result.conflictDetails.teamCount ?? 0,
                            pendingEmit: loginResult,
                        });
                    } else {
                        // Transition modal to club selection step
                        this.loginModalStep.set('clubSelection');
                        this.inlineError.set(null);
                    }
                },
                error: (err) => {
                    this.loginSubmitting.set(false);

                    // Handle specific error types
                    if (err.type === 'TOS_REQUIRED') {
                        // User redirected to TOS page, no error message needed
                        return;
                    }

                    if (err.type === 'NOT_A_CLUB_REP') {
                        this.inlineError.set(
                            'You are not registered as a club representative.',
                        );
                        return;
                    }

                    // Generic error
                    this.inlineError.set(
                        err.message || 'Login failed. Please try again.',
                    );
                },
            });
    }

    submitRegistration(): void {
        if (this.registrationForm.invalid) return;

        // Cancel any in-flight registration to prevent race condition
        this.workflowSubscription?.unsubscribe();

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
            cellphone: this.registrationForm.value.cellphone,
        };

        this.workflowSubscription = this.workflowService
            .registerAndAutoLogin(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (result) => {
                    this.registrationSubmitting.set(false);
                    this.registrationForm.reset();

                    if (result.autoLoginFailed) {
                        // Registration succeeded but auto-login failed
                        this.toast.show(
                            'Account created successfully! Please use your new credentials to login.',
                            'success',
                            5000,
                        );
                        this.registrationError.set(
                            'Account created successfully, but auto-login failed. Please use the login form above.',
                        );
                        this.hasClubRepAccount.set('yes');
                    } else if (result.clubs) {
                        // Full success - registered and logged in
                        this.toast.show('Account created successfully!', 'success', 5000);
                        this.hasClubRepAccount.set('yes');
                        this.registrationSuccess.emit({
                            clubName: request.clubName,
                            availableClubs: result.clubs,
                        });
                    } else {
                        // TOS redirect happened
                        this.hasClubRepAccount.set('yes');
                    }
                },
                error: (err) => {
                    this.registrationSubmitting.set(false);

                    // Handle duplicate/similar club conflict
                    if (err.type === 'REGISTRATION_FAILED' && err.similarClubs) {
                        this.setDuplicateModalState({
                            isOpen: true,
                            message:
                                err.message ||
                                'A club with a very similar name already exists. Please verify before creating a new club rep.',
                            clubs: err.similarClubs,
                        });
                        return;
                    }

                    // Generic error handling
                    this.registrationError.set(
                        err.message || 'Registration failed. Please try again.',
                    );
                },
            });
    }

    dismissDuplicateWarning(): void {
        this.setDuplicateModalState({ isOpen: false, message: null, clubs: [] });
        // Reset clubName to prevent resubmission of duplicate name
        this.registrationForm.patchValue({ clubName: '' });
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
    private setDuplicateModalState(state: {
        isOpen: boolean;
        message: string | null;
        clubs: ClubSearchResult[];
    }): void {
        this.duplicateModalState.set(state);
    }

    toggleCredentialsCollapsed(): void {
        this.credentialsCollapsed.update((v) => !v);
    }

    /** Handle credentials toggle - open login modal when 'Yes' is selected */
    onHasCredentialsChange(value: 'yes' | 'no'): void {
        this.hasClubRepAccount.set(value);
        if (value === 'yes') {
            this.showLoginModal.set(true);
            this.loginModalStep.set('credentials');
            this.resetLoginFormState();
        }
    }

    /** Close login modal and reset state */
    closeLoginModal(): void {
        this.showLoginModal.set(false);
        this.loginModalStep.set('credentials');
        this.selectedClub.set(null);
        this.clubSelectionError.set(null);
        this.inlineError.set(null);
        this.hasClubRepAccount.set(null);
        this.loginForm.reset();
    }

    /** Handle club selection in modal */
    selectClubInModal(clubName: string): void {
        this.selectedClub.set(clubName);
        this.clubSelectionError.set(null);
    }

    /** Continue from club selection step */
    continueFromClubSelection(): void {
        if (!this.selectedClub()) {
            this.clubSelectionError.set('Please select a club before continuing.');
            return;
        }

        if (!this.jobPath) {
            this.clubSelectionError.set(
                'Event information missing. Please refresh the page.',
            );
            return;
        }

        const clubName = this.selectedClub()!;

        // Call initialize-registration to create Registration record and get Phase 2 token
        // The service automatically updates the auth token and current user
        this.loginSubmitting.set(true);
        this.clubSelectionError.set(null);

        this.workflowSubscription?.unsubscribe();
        this.workflowSubscription = this.teamRegService
            .initializeRegistration(clubName, this.jobPath)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (response) => {
                    this.loginSubmitting.set(false);

                    // Store club count for refresh routing
                    this.authService.setClubRepClubCount(this.availableClubs().length);

                    const loginResult: LoginStepResult = {
                        clubName: clubName,
                        availableClubs: this.availableClubs(),
                    };

                    // Close modal and emit result
                    this.showLoginModal.set(false);
                    this.loginSuccess.emit(loginResult);
                },
                error: (err) => {
                    this.loginSubmitting.set(false);
                    const errorMessage =
                        err.error?.message ||
                        err.message ||
                        'Failed to initialize registration. Please try again.';
                    this.clubSelectionError.set(errorMessage);
                },
            });
    }

    /**
     * User acknowledges conflict warning and proceeds anyway.
     */
    proceedDespiteConflict(): void {
        const state = this.conflictWarningState();
        if (state.pendingEmit) {
            this.loginSuccess.emit(state.pendingEmit);
        }
        this.conflictWarningState.set({
            isOpen: false,
            otherRepUsername: null,
            teamCount: 0,
            pendingEmit: null,
        });
    }

    /**
     * User cancels due to conflict warning.
     * Resets UI state but keeps authentication (user successfully logged in).
     */
    cancelDueToConflict(): void {
        this.conflictWarningState.set({
            isOpen: false,
            otherRepUsername: null,
            teamCount: 0,
            pendingEmit: null,
        });
        this.hasClubRepAccount.set(null);
        // Note: Don't reset loginForm - user is already authenticated
    }

    /**
     * Ensure the login form controls are always enabled and clear the password field.
     * This guards against any stale disabled state from previous modal opens.
     */
    private resetLoginFormState(): void {
        const username = this.loginForm.get('username')?.value ?? '';
        this.loginForm.enable({ emitEvent: false });
        this.loginForm.get('password')?.enable({ emitEvent: false });
        this.loginForm.reset({ username, password: '' }, { emitEvent: false });
        this.loginForm.markAsPristine();
        this.loginForm.markAsUntouched();
    }
}
