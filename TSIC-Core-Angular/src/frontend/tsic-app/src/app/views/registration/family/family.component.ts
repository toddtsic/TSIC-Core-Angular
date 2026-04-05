import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { JobService } from '@infrastructure/services/job.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { FamilyService as FamilyHttpService } from '@infrastructure/services/family.service';
import { WizardShellComponent } from '../shared/wizard-shell/wizard-shell.component';
import { FamilyStateService } from './state/family-state.service';
import { CredentialsStepComponent } from './steps/credentials-step.component';
import { ContactsStepComponent } from './steps/contacts-step.component';
import { AddressStepComponent } from './steps/address-step.component';
import { ChildrenStepComponent } from './steps/children-step.component';
import { ReviewStepComponent } from './steps/review-step.component';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

type FamilyStepId = 'credentials' | 'contacts' | 'address' | 'children' | 'review';

@Component({
    selector: 'app-registration-family',
    standalone: true,
    imports: [
        WizardShellComponent,
        CredentialsStepComponent,
        ContactsStepComponent,
        AddressStepComponent,
        ChildrenStepComponent,
        ReviewStepComponent,
    ],
    templateUrl: './family.component.html',
    styleUrls: ['./family.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FamilyWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly jobService = inject(JobService);
    private readonly auth = inject(AuthService);
    private readonly familyHttp = inject(FamilyHttpService);
    private readonly destroyRef = inject(DestroyRef);
    readonly state = inject(FamilyStateService);

    @ViewChild(CredentialsStepComponent) credentialsStep?: CredentialsStepComponent;

    // ── Step management ─────────────────────────────────────────────
    readonly currentIndex = signal(0);
    readonly validating = signal(false);

    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'credentials', label: 'Account', enabled: true },
        { id: 'contacts', label: 'Contacts', enabled: true },
        { id: 'address', label: 'Address', enabled: true },
        { id: 'children', label: 'Children', enabled: true },
        { id: 'review', label: 'Review', enabled: true },
    ]);

    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));
    readonly currentStepId = computed<FamilyStepId>(() => (this.activeSteps()[this.currentIndex()]?.id ?? 'credentials') as FamilyStepId);

    // ── Shell config ────────────────────────────────────────────────
    readonly shellConfig = computed<WizardShellConfig>(() => ({
        title: 'Family Account',
        theme: 'family',
        badge: null,
    }));

    readonly canContinue = computed(() => {
        if (this.validating()) return false;
        switch (this.currentStepId()) {
            case 'credentials': return this.state.hasValidCredentials() || this.auth.isAuthenticated();
            case 'contacts': return this.state.hasValidParent1() && this.state.hasValidParent2();
            case 'address': return this.state.hasValidAddress();
            case 'children': return this.state.hasChildren();
            case 'review': return false;
            default: return false;
        }
    });

    readonly showContinue = computed(() => this.currentStepId() !== 'review');

    // ── Query params ────────────────────────────────────────────────
    private returnUrl: string | null = null;
    private nextAction: 'register-player' | null = null;

    // ── Lifecycle ───────────────────────────────────────────────────
    ngOnInit(): void {
        this.state.reset();

        const qp = this.route.snapshot.queryParamMap;
        this.returnUrl = qp.get('returnUrl');
        if (qp.get('next') === 'register-player') this.nextAction = 'register-player';

        // Pre-load job metadata for dynamic labels if returnUrl carries a jobPath
        const jobPath = this.extractJobPathFromUrl(this.returnUrl);
        if (jobPath) this.jobService.loadJobMetadata(jobPath);

        // Deep-link: ?step=<id>
        const stepParam = qp.get('step');
        if (stepParam) {
            const idx = this.activeSteps().findIndex(s => s.id === stepParam);
            if (idx >= 0) this.currentIndex.set(idx);
        }
    }

    // ── Navigation ──────────────────────────────────────────────────
    next(): void {
        // Intercept credentials step — validate against backend before advancing
        if (this.currentStepId() === 'credentials') {
            this.validateAndAdvance();
            return;
        }

        if (this.currentIndex() < this.activeSteps().length - 1) {
            this.currentIndex.update(i => i + 1);
        }
    }

    back(): void {
        this.currentIndex.update(i => Math.max(0, i - 1));
    }

    /** Validate credentials against backend, then advance or show error. */
    private validateAndAdvance(): void {
        this.validating.set(true);
        if (this.credentialsStep) {
            this.credentialsStep.validationError.set(null);
        }

        this.familyHttp.validateCredentials({
            username: this.state.username(),
            password: this.state.password(),
        }).pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (res) => {
                    this.validating.set(false);
                    if (res.message) {
                        // Backend returned an error (wrong password, privilege conflict)
                        if (this.credentialsStep) {
                            this.credentialsStep.validationError.set(res.message);
                        }
                        return;
                    }
                    if (res.exists && res.profile) {
                        // Existing account — switch to edit mode and prefill
                        this.state.populateFromProfile(res.profile);
                    } else {
                        // New account — stay in create mode
                        this.state.setAccountExists(false);
                    }
                    this.currentIndex.update(i => i + 1);
                },
                error: (err: unknown) => {
                    this.validating.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    const msg = httpErr?.error?.message ?? 'Unable to validate credentials. Please try again.';
                    if (this.credentialsStep) {
                        this.credentialsStep.validationError.set(msg);
                    }
                },
            });
    }

    /** Called by review step on completion. */
    finish(action?: 'home' | 'register'): void {
        const ru = this.returnUrl;

        // Honor explicit returnUrl unless overridden
        if (ru && action !== 'register' && this.nextAction !== 'register-player') {
            try {
                const u = new URL(ru, globalThis.location.origin);
                this.router.navigateByUrl(`${u.pathname}${u.search}${u.hash}`);
                return;
            } catch { /* fall through */ }
        }

        const jobPath = this.jobService.getCurrentJob()?.jobPath ?? this.extractJobPathFromUrl(ru);
        if (!jobPath) {
            this.router.navigateByUrl('/tsic/role-selection');
            return;
        }

        if (this.nextAction === 'register-player' && action !== 'home') {
            this.router.navigateByUrl(`/${jobPath}/register-player`);
        } else if (action === 'register') {
            this.router.navigateByUrl(`/${jobPath}/register-player`);
        } else {
            this.router.navigateByUrl(`/${jobPath}`);
        }
    }

    private extractJobPathFromUrl(url: string | null): string | null {
        if (!url) return null;
        try {
            const u = new URL(url, globalThis.location.origin);
            return u.pathname.split('/').find(Boolean) ?? null;
        } catch {
            return null;
        }
    }
}
