import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, take } from 'rxjs';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { WizardShellComponent } from '../shared/wizard-shell/wizard-shell.component';
import { AdultWizardStateService } from './state/adult-wizard-state.service';
import { AccountStepComponent } from './steps/account-step.component';
import { RoleStepComponent } from './steps/role-step.component';
import { ProfileStepComponent } from './steps/profile-step.component';
import { WaiversStepComponent } from './steps/waivers-step.component';
import { ReviewStepComponent } from './steps/review-step.component';
import { PaymentStepComponent } from './steps/payment-step.component';
import { ConfirmationStepComponent } from './steps/confirmation-step.component';
import { TosAcceptanceStepComponent } from '../shared/components/tos-acceptance-step.component';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

type AdultStepId = 'account' | 'role' | 'profile' | 'waivers' | 'review' | 'payment' | 'confirmation' | 'tos';

@Component({
    selector: 'app-registration-adult',
    standalone: true,
    imports: [
        WizardShellComponent,
        AccountStepComponent,
        RoleStepComponent,
        ProfileStepComponent,
        WaiversStepComponent,
        ReviewStepComponent,
        PaymentStepComponent,
        ConfirmationStepComponent,
        TosAcceptanceStepComponent,
    ],
    templateUrl: './adult.component.html',
    styleUrls: ['./adult.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdultWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly auth = inject(AuthService);
    private readonly destroyRef = inject(DestroyRef);
    readonly state = inject(AdultWizardStateService);

    jobPath = '';

    // ── Step management ─────────────────────────────────────────────
    readonly currentIndex = signal(0);

    /** When ?role= param is provided, the role step is skipped. */
    readonly rolePreselected = signal(false);

    // ── ToS state (create mode only) ─────────────────────────────
    readonly tosSubmitting = signal(false);
    readonly tosError = signal<string | null>(null);

    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'account', label: 'Account', enabled: true },
        { id: 'role', label: 'Role', enabled: !this.rolePreselected() },
        { id: 'profile', label: 'Profile', enabled: this.state.formSchema() !== null },
        { id: 'waivers', label: 'Waivers', enabled: (this.state.waivers().length > 0) },
        { id: 'review', label: 'Review', enabled: true },
        { id: 'payment', label: 'Payment', enabled: this.state.hasFees() },
        { id: 'confirmation', label: 'Confirmation', enabled: this.state.isComplete() },
        { id: 'tos', label: 'Terms of Service', enabled: this.state.mode() === 'create' && this.state.isComplete() },
    ]);

    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));
    readonly currentStepId = computed<AdultStepId>(
        () => (this.activeSteps()[this.currentIndex()]?.id ?? 'account') as AdultStepId,
    );

    // ── Shell config ────────────────────────────────────────────────
    readonly shellConfig = computed<WizardShellConfig>(() => ({
        title: 'Adult Registration',
        theme: 'adult',
        badge: this.state.selectedRole()?.displayName ?? null,
    }));

    readonly canContinue = computed(() => {
        switch (this.currentStepId()) {
            case 'account':
                // Login mode: always can continue (already authenticated)
                // Create mode: need valid credentials
                return this.state.mode() === 'login'
                    ? true
                    : this.state.hasValidCredentials();
            case 'role':
                return this.state.hasSelectedRole() && !this.state.schemaLoading();
            case 'profile':
                return this.state.hasValidProfile();
            case 'waivers':
                return this.state.hasAcceptedAllWaivers();
            case 'review':
                return !this.state.preSubmitting();
            case 'payment':
                return this.state.paymentSuccess();
            case 'confirmation':
                return true;
            case 'tos':
                return false; // tos has its own accept button
            default:
                return false;
        }
    });

    readonly showContinue = computed(() => {
        const step = this.currentStepId();
        return step !== 'tos' && step !== 'payment' && step !== 'confirmation';
    });

    readonly continueLabel = computed(() => {
        if (this.currentStepId() === 'review') {
            return this.state.hasFees() ? 'Continue to Payment' : 'Submit Registration';
        }
        return 'Continue';
    });

    // ── Lifecycle ───────────────────────────────────────────────────
    ngOnInit(): void {
        this.state.reset();

        // Extract jobPath from parent route
        this.jobPath = this.route.parent?.snapshot.paramMap.get('jobPath') ?? '';
        this.state.setJobPath(this.jobPath);

        // Read ?role= query param (0=Coach, 1=Referee, 2=Recruiter)
        const roleParam = this.route.snapshot.queryParamMap.get('role');
        const preselectedRoleType = roleParam !== null ? parseInt(roleParam, 10) : null;

        if (this.jobPath) {
            this.state.loadJobInfo(this.jobPath);
        }

        // When job info loads and a role was preselected, auto-select it
        if (preselectedRoleType !== null && !isNaN(preselectedRoleType)) {
            this.rolePreselected.set(true);
            this.waitForJobInfoAndSelectRole(preselectedRoleType);
        }

        // Deep-link: ?step=<id>
        const stepParam = this.route.snapshot.queryParamMap.get('step');
        if (stepParam) {
            const idx = this.activeSteps().findIndex(s => s.id === stepParam);
            if (idx >= 0) this.currentIndex.set(idx);
        }
    }

    /** Wait for jobInfo to load, then auto-select the preselected role. */
    private waitForJobInfoAndSelectRole(roleType: number): void {
        toObservable(this.state.jobInfo).pipe(
            filter(info => info !== null),
            take(1),
            takeUntilDestroyed(this.destroyRef),
        ).subscribe(info => {
            const role = info.availableRoles.find(r => r.roleType === roleType);
            if (role) {
                this.state.selectRole(role, this.jobPath);
            } else {
                // Invalid role param — fall back to showing role step
                this.rolePreselected.set(false);
            }
        });
    }

    // ── Navigation ──────────────────────────────────────────────────
    async next(): Promise<void> {
        // PreSubmit when leaving review step
        if (this.currentStepId() === 'review') {
            const valid = await this.state.preSubmit(this.jobPath);
            if (!valid) return;

            // If no fees: submit registration now (create-mode) or mark complete (login-mode)
            if (!this.state.hasFees()) {
                const submitted = await this.state.submit(this.jobPath);
                if (!submitted) return;
            }
        }

        if (this.currentIndex() < this.activeSteps().length - 1) {
            this.currentIndex.update(i => i + 1);
        }
    }

    back(): void {
        this.currentIndex.update(i => Math.max(0, i - 1));
    }

    goToStep(index: number): void {
        if (index < this.currentIndex()) {
            this.currentIndex.set(index);
        }
    }

    /** Called when account step login succeeds → auto-advance to role. */
    onAccountAutoAdvance(): void {
        this.next();
    }

    /** Called by confirmation step "Finish" button. */
    onFinishConfirmation(): void {
        // If create mode, advance to ToS; otherwise navigate home
        if (this.state.mode() === 'create') {
            this.next();
        } else {
            this.router.navigateByUrl(`/${this.jobPath}`);
        }
    }

    /** Called by inline ToS step — auto-login, accept ToS, then navigate home. */
    onTosAccepted(): void {
        this.tosSubmitting.set(true);
        this.tosError.set(null);

        this.auth.login({
            username: this.state.username(),
            password: this.state.password(),
        }).pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.auth.acceptTos()
                        .pipe(takeUntilDestroyed(this.destroyRef))
                        .subscribe({
                            next: () => {
                                this.tosSubmitting.set(false);
                                this.router.navigateByUrl(`/${this.jobPath}`);
                            },
                            error: (err: unknown) => {
                                this.tosSubmitting.set(false);
                                const httpErr = err as { error?: { message?: string } };
                                this.tosError.set(httpErr?.error?.message ?? 'Failed to accept Terms of Service. Please try again.');
                            },
                        });
                },
                error: () => {
                    this.tosSubmitting.set(false);
                    this.tosError.set('Unable to sign in. Please try again.');
                },
            });
    }
}
