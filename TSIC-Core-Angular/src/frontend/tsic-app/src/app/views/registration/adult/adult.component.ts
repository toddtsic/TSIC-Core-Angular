import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { isValidAdultRegRoleKey } from '@infrastructure/services/adult-registration.service';
import { WizardShellComponent } from '../shared/wizard-shell/wizard-shell.component';
import { AdultWizardStateService } from './state/adult-wizard-state.service';
import { AccountStepComponent } from './steps/account-step.component';
import { ProfileStepComponent } from './steps/profile-step.component';
import { WaiversStepComponent } from './steps/waivers-step.component';
import { ReviewStepComponent } from './steps/review-step.component';
import { PaymentStepComponent } from './steps/payment-step.component';
import { ConfirmationStepComponent } from './steps/confirmation-step.component';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

type AdultStepId = 'account' | 'profile' | 'waivers' | 'review' | 'payment' | 'confirmation';

/**
 * Adult Registration Wizard — unified, role-config-driven.
 *
 * Flow (7 steps, config-driven):
 *   Account → ToS (create-mode only) → Profile → Waivers → Review → Payment (if fees) → Confirmation
 *
 * Entry requires <c>?role=&lt;roleKey&gt;</c> where roleKey is one of coach/referee/recruiter.
 * Backend resolves the actual assigned role (UnassignedAdult / Staff / Referee / Recruiter)
 * based on the job type. If role is missing or rejected (security invariant violation,
 * unsupported job type), the wizard shows an error card and does not render the stepper.
 */
@Component({
    selector: 'app-registration-adult',
    standalone: true,
    imports: [
        WizardShellComponent,
        AccountStepComponent,
        ProfileStepComponent,
        WaiversStepComponent,
        ReviewStepComponent,
        PaymentStepComponent,
        ConfirmationStepComponent,
    ],
    templateUrl: './adult.component.html',
    styleUrls: ['./adult.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdultWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly auth = inject(AuthService);
    readonly state = inject(AdultWizardStateService);

    jobPath = '';

    // ── Error-card state (shown when role config can't load) ───────
    readonly configError = signal<string | null>(null);

    // ── Step management ─────────────────────────────────────────────
    readonly currentIndex = signal(0);

    readonly steps = computed<WizardStepDef[]>(() => [
        // Account step collects credentials + ToS inline (create-mode) or embedded login (existing user).
        { id: 'account', label: 'Account', enabled: true },
        { id: 'profile', label: 'Profile', enabled: this.state.roleConfig() !== null },
        { id: 'waivers', label: 'Waivers', enabled: this.state.waivers().length > 0 },
        { id: 'review', label: 'Review', enabled: true },
        { id: 'payment', label: 'Payment', enabled: this.state.hasFees() },
        { id: 'confirmation', label: 'Done', enabled: this.state.isComplete() },
    ]);

    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));
    readonly currentStepId = computed<AdultStepId>(
        () => (this.activeSteps()[this.currentIndex()]?.id ?? 'account') as AdultStepId,
    );

    readonly shellConfig = computed<WizardShellConfig>(() => ({
        title: this.state.roleDisplayName()
            ? `${this.state.roleDisplayName()} Registration`
            : 'Adult Registration',
        theme: 'adult',
        badge: null,
    }));

    readonly canContinue = computed(() => {
        switch (this.currentStepId()) {
            case 'account':      return this.state.accountStepReady();
            case 'profile':      return this.state.hasValidProfile();
            case 'waivers':      return this.state.hasAcceptedAllWaivers();
            case 'review':       return !this.state.preSubmitting();
            case 'payment':      return this.state.paymentSuccess();
            case 'confirmation': return false; // Own Finish button
            default:             return false;
        }
    });

    readonly showContinue = computed(() => {
        const step = this.currentStepId();
        // Account, Payment, Confirmation all have their own CTAs.
        return step !== 'account' && step !== 'payment' && step !== 'confirmation';
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

        // Always start with a clean session. The user re-authenticates (via embedded
        // login) or creates a new account for this specific adult role — we don't
        // inherit whoever happened to be logged in previously.
        this.auth.logoutLocal();

        // Walk up the route tree to find the jobPath param (lives on a grandparent
        // route — same pattern as the player wizard's resolveJobPath).
        this.jobPath = this.resolveJobPath();
        this.state.setJobPath(this.jobPath);

        const roleParam = this.route.snapshot.queryParamMap.get('role')?.trim().toLowerCase() ?? '';

        if (!roleParam) {
            this.configError.set(
                'This registration link is incomplete. Please contact the tournament director ' +
                'for the correct registration link.');
            return;
        }

        if (!isValidAdultRegRoleKey(roleParam)) {
            this.configError.set(`The registration role "${roleParam}" is not available.`);
            return;
        }

        this.state.setRoleKey(roleParam);

        // Load role config — backend enforces security invariants here.
        this.state.loadRoleConfig(this.jobPath, roleParam).then(success => {
            if (!success) {
                // State service already captured the error message.
                const err = this.state.roleConfigError();
                this.configError.set(err ?? 'Unable to load registration configuration.');
            }
        });

        // Deep-link: ?step=<id>
        const stepParam = this.route.snapshot.queryParamMap.get('step');
        if (stepParam) {
            const idx = this.activeSteps().findIndex(s => s.id === stepParam);
            if (idx >= 0) this.currentIndex.set(idx);
        }
    }

    // ── Navigation ──────────────────────────────────────────────────
    async next(): Promise<void> {
        // PreSubmit runs when leaving Review.
        if (this.currentStepId() === 'review') {
            const valid = await this.state.preSubmit();
            if (!valid) return;

            if (!this.state.hasFees()) {
                const submitted = await this.state.submit();
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

    /**
     * Account step emits this on successful login OR on "Accept and Continue" in
     * create-mode (inline ToS). In both cases the next step is Profile.
     */
    onAccountAdvance(): void {
        this.next();
    }

    /** Confirmation step "Finish" button. */
    onFinishConfirmation(): void {
        this.router.navigateByUrl(`/${this.jobPath}`);
    }

    /**
     * Walk up the route tree to find the <c>jobPath</c> param. The wizard lives
     * at <c>:jobPath/registration/adult</c>, so the param is on a grandparent
     * route, not the direct parent. Matches the player wizard's pattern.
     */
    private resolveJobPath(): string {
        let snap: import('@angular/router').ActivatedRouteSnapshot | null = this.route.snapshot;
        while (snap) {
            const jp = snap.paramMap.get('jobPath');
            if (jp) return jp;
            snap = snap.parent;
        }
        return '';
    }
}
