import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { WizardShellComponent } from '../shared/wizard-shell/wizard-shell.component';
import { AdultWizardStateService } from './state/adult-wizard-state.service';
import { AccountStepComponent } from './steps/account-step.component';
import { RoleStepComponent } from './steps/role-step.component';
import { ProfileStepComponent } from './steps/profile-step.component';
import { WaiversStepComponent } from './steps/waivers-step.component';
import { ReviewStepComponent } from './steps/review-step.component';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

type AdultStepId = 'account' | 'role' | 'profile' | 'waivers' | 'review';

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
    ],
    templateUrl: './adult.component.html',
    styleUrls: ['./adult.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdultWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    readonly state = inject(AdultWizardStateService);

    jobPath = '';

    // ── Step management ─────────────────────────────────────────────
    readonly currentIndex = signal(0);

    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'account', label: 'Account', enabled: this.state.mode() === 'create' },
        { id: 'role', label: 'Role', enabled: true },
        { id: 'profile', label: 'Profile', enabled: this.state.formSchema() !== null },
        { id: 'waivers', label: 'Waivers', enabled: (this.state.waivers().length > 0) },
        { id: 'review', label: 'Review', enabled: true },
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
            case 'review': return false; // review has its own submit button
            default: return false;
        }
    });

    readonly showContinue = computed(() => this.currentStepId() !== 'review');

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

    /** Called by review step — either submit or navigate away on success. */
    onSubmitOrFinish(): void {
        if (this.state.submitSuccess()) {
            // Registration done — navigate to job home
            this.router.navigateByUrl(`/${this.jobPath}`);
        } else {
            // Trigger submission
            this.state.submit(this.jobPath);
        }
    }
}
