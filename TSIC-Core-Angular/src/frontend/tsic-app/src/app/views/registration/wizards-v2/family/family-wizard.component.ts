import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { JobService } from '@infrastructure/services/job.service';
import { AuthService } from '@infrastructure/services/auth.service';
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
    selector: 'app-family-wizard-v2',
    standalone: true,
    imports: [
        WizardShellComponent,
        CredentialsStepComponent,
        ContactsStepComponent,
        AddressStepComponent,
        ChildrenStepComponent,
        ReviewStepComponent,
    ],
    templateUrl: './family-wizard.component.html',
    styleUrls: ['./family-wizard.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FamilyWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly jobService = inject(JobService);
    private readonly auth = inject(AuthService);
    readonly state = inject(FamilyStateService);

    // ── Step management ─────────────────────────────────────────────
    readonly currentIndex = signal(0);

    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'credentials', label: 'Credentials', enabled: this.state.mode() === 'create' },
        { id: 'contacts', label: 'Contacts', enabled: true },
        { id: 'address', label: 'Address', enabled: true },
        { id: 'children', label: 'Children', enabled: true },
        { id: 'review', label: 'Review', enabled: true },
    ]);

    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));
    readonly currentStepId = computed<FamilyStepId>(() => (this.activeSteps()[this.currentIndex()]?.id ?? 'contacts') as FamilyStepId);

    // ── Shell config ────────────────────────────────────────────────
    readonly shellConfig = computed<WizardShellConfig>(() => ({
        title: 'Family Account',
        theme: 'family',
        badge: null,
    }));

    readonly canContinue = computed(() => {
        switch (this.currentStepId()) {
            case 'credentials': return this.state.hasValidCredentials() || this.auth.isAuthenticated();
            case 'contacts': return this.state.hasValidParent1();
            case 'address': return this.state.hasValidAddress();
            case 'children': return this.state.hasChildren();
            case 'review': return false; // review has its own submit button
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
        if (qp.get('mode') === 'edit') this.state.setMode('edit');

        // Pre-load job metadata for dynamic labels if returnUrl carries a jobPath
        const jobPath = this.extractJobPathFromUrl(this.returnUrl);
        if (jobPath) this.jobService.loadJobMetadata(jobPath);

        // If editing and not authenticated, redirect to login
        if (this.state.mode() === 'edit' && !this.auth.isAuthenticated()) {
            this.router.navigate(['/tsic/login'], {
                queryParams: {
                    returnUrl: this.router.url,
                    theme: 'family',
                    header: 'Family Account Login',
                    subHeader: 'Sign in to continue',
                },
            });
            return;
        }

        // If editing and authenticated, load existing profile
        if (this.state.mode() === 'edit' && this.auth.isAuthenticated()) {
            this.state.loadProfile();
        }

        // Deep-link: ?step=<id>
        const stepParam = qp.get('step');
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
