import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { WizardShellComponent } from '../shared/wizard-shell/wizard-shell.component';
import { AdultWizardStateService } from './state/adult-wizard-state.service';
import { AccountStepComponent } from './steps/account-step.component';
import { RoleStepComponent } from './steps/role-step.component';
import { ProfileStepComponent } from './steps/profile-step.component';
import { WaiversStepComponent } from './steps/waivers-step.component';
import { ReviewStepComponent } from './steps/review-step.component';
import { TosAcceptanceStepComponent } from '../shared/components/tos-acceptance-step.component';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

type AdultStepId = 'account' | 'role' | 'profile' | 'waivers' | 'review' | 'tos';

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

    // ── ToS state (create mode only) ─────────────────────────────
    readonly tosSubmitting = signal(false);
    readonly tosError = signal<string | null>(null);

    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'account', label: 'Account', enabled: this.state.mode() === 'create' },
        { id: 'role', label: 'Role', enabled: true },
        { id: 'profile', label: 'Profile', enabled: this.state.formSchema() !== null },
        { id: 'waivers', label: 'Waivers', enabled: (this.state.waivers().length > 0) },
        { id: 'review', label: 'Review', enabled: true },
        { id: 'tos', label: 'Terms of Service', enabled: this.state.mode() === 'create' },
    ]);

    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));
    readonly currentStepId = computed<AdultStepId>(
        () => (this.activeSteps()[this.currentIndex()]?.id ?? 'role') as AdultStepId,
    );

    // ── Shell config ────────────────────────────────────────────────
    readonly shellConfig = computed<WizardShellConfig>(() => ({
        title: 'Adult Registration',
        theme: 'adult',
        badge: this.state.selectedRole()?.displayName ?? null,
    }));

    readonly canContinue = computed(() => {
        switch (this.currentStepId()) {
            case 'account': return this.state.hasValidCredentials();
            case 'role': return this.state.hasSelectedRole() && !this.state.schemaLoading();
            case 'profile': return this.state.hasValidProfile();
            case 'waivers': return this.state.hasAcceptedAllWaivers();
            case 'review': return this.state.submitSuccess();
            case 'tos': return false; // tos has its own accept button
            default: return false;
        }
    });

    readonly showContinue = computed(() => this.currentStepId() !== 'tos');

    // ── Lifecycle ───────────────────────────────────────────────────
    ngOnInit(): void {
        this.state.reset();

        // Extract jobPath from parent route
        this.jobPath = this.route.parent?.snapshot.paramMap.get('jobPath') ?? '';

        if (this.jobPath) {
            this.state.loadJobInfo(this.jobPath);
        }

        // Deep-link: ?step=<id>
        const stepParam = this.route.snapshot.queryParamMap.get('step');
        if (stepParam) {
            const idx = this.activeSteps().findIndex(s => s.id === stepParam);
            if (idx >= 0) this.currentIndex.set(idx);
        }
    }

    // ── Navigation ──────────────────────────────────────────────────
    next(): void {
        if (this.currentIndex() < this.activeSteps().length - 1) {
            this.currentIndex.update(i => i + 1);
        }
    }

    back(): void {
        this.currentIndex.update(i => Math.max(0, i - 1));
    }

    /** Called by review step — submit registration. */
    onSubmitOrFinish(): void {
        if (this.state.submitSuccess()) {
            this.router.navigateByUrl(`/${this.jobPath}`);
            return;
        }
        this.state.submit(this.jobPath);
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
