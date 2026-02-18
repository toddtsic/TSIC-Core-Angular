import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '@shared-ui/toast.service';
import { PlayerWizardStateService } from './state/player-wizard-state.service';
import { RegistrationWizardService } from '@views/registration/wizards/player-registration-wizard/registration-wizard.service';
import { WizardShellComponent } from '../shared/wizard-shell/wizard-shell.component';
import { FamilyCheckStepComponent } from './steps/family-check-step.component';
import { PlayerSelectionStepComponent } from './steps/player-selection-step.component';
import { EligibilityStepComponent } from './steps/eligibility-step.component';
import { TeamSelectionStepComponent } from './steps/team-selection-step.component';
import { PlayerFormsStepComponent } from './steps/player-forms-step.component';
import { WaiversStepComponent } from './steps/waivers-step.component';
import { ReviewStepComponent } from './steps/review-step.component';
import { PaymentStepComponent } from './steps/payment-step.component';
import { ConfirmationStepComponent } from './steps/confirmation-step.component';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

@Component({
    selector: 'app-player-wizard-v2',
    standalone: true,
    imports: [
        WizardShellComponent,
        FamilyCheckStepComponent,
        PlayerSelectionStepComponent,
        EligibilityStepComponent,
        TeamSelectionStepComponent,
        PlayerFormsStepComponent,
        WaiversStepComponent,
        ReviewStepComponent,
        PaymentStepComponent,
        ConfirmationStepComponent,
    ],
    template: `
    <app-wizard-shell
      [steps]="steps()"
      [currentIndex]="currentIndex()"
      [config]="shellConfig()"
      [canContinue]="canContinue()"
      [showContinue]="showContinue()"
      [continueLabel]="continueLabel()"
      (back)="back()"
      (continue)="next()">
      @switch (currentStepId()) {
        @case ('family-check') { <app-prw-family-check-step (advance)="next()" /> }
        @case ('players') { <app-prw-player-selection-step /> }
        @case ('eligibility') { <app-prw-eligibility-step /> }
        @case ('teams') { <app-prw-team-selection-step /> }
        @case ('forms') { <app-prw-player-forms-step /> }
        @case ('waivers') { <app-prw-waivers-step /> }
        @case ('review') { <app-prw-review-step (advance)="next()" /> }
        @case ('payment') { <app-prw-payment-step (advance)="next()" /> }
        @case ('confirmation') { <app-prw-confirmation-step (finished)="finish()" /> }
      }
    </app-wizard-shell>
  `,
    styleUrls: ['./player-wizard.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly toast = inject(ToastService);
    readonly state = inject(PlayerWizardStateService);

    // Bridge: sync v2 state to old RegistrationWizardService so TeamService auto-loads teams.
    // TeamService uses effect(wizard.jobPath()) and reads wizard.teamConstraintType/Value.
    private readonly oldWizard = inject(RegistrationWizardService);
    private readonly _bridgeJobPath = effect(() => {
        const jp = this.state.jobCtx.jobPath();
        if (jp) this.oldWizard.setJobPath(jp);
    });
    private readonly _bridgeConstraintType = effect(() => {
        this.oldWizard.setTeamConstraintType(this.state.eligibility.teamConstraintType());
    });
    private readonly _bridgeConstraintValue = effect(() => {
        this.oldWizard.setTeamConstraintValue(this.state.eligibility.teamConstraintValue());
    });

    private readonly _currentIndex = signal(0);
    readonly currentIndex = this._currentIndex.asReadonly();

    // ── Step definitions ──────────────────────────────────────────────
    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'family-check', label: 'Account', enabled: true },
        { id: 'players', label: 'Players', enabled: true },
        {
            id: 'eligibility', label: 'Eligibility',
            enabled: !!this.state.eligibility.teamConstraintType(),
        },
        { id: 'teams', label: 'Teams', enabled: true },
        { id: 'forms', label: 'Forms', enabled: true },
        {
            id: 'waivers', label: 'Waivers',
            enabled: this.state.jobCtx.waiverDefinitions().length > 0,
        },
        { id: 'review', label: 'Review', enabled: true },
        { id: 'payment', label: 'Payment', enabled: true },
        { id: 'confirmation', label: 'Done', enabled: true },
    ]);

    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));

    readonly currentStepId = computed(() => {
        const active = this.activeSteps();
        const idx = Math.min(this._currentIndex(), active.length - 1);
        return active[idx]?.id ?? 'family-check';
    });

    readonly shellConfig = computed<WizardShellConfig>(() => ({
        title: 'Player Registration',
        theme: 'player',
        badge: this.state.familyPlayers.familyUser()?.displayName ?? null,
    }));

    // ── canContinue ───────────────────────────────────────────────────
    readonly canContinue = computed(() => {
        switch (this.currentStepId()) {
            case 'family-check': return false; // has its own CTAs
            case 'players': return this.state.familyPlayers.selectedPlayerIds().length > 0;
            case 'eligibility': {
                const selected = this.state.familyPlayers.selectedPlayerIds();
                return selected.every(id => !!this.state.eligibility.getEligibilityForPlayer(id));
            }
            case 'teams': {
                const selected = this.state.familyPlayers.selectedPlayerIds();
                const teams = this.state.eligibility.selectedTeams();
                return selected.every(id => !!teams[id]);
            }
            case 'forms': {
                const schemas = this.state.jobCtx.profileFieldSchemas();
                const ids = this.state.familyPlayers.selectedPlayerIds();
                const wfn = this.state.jobCtx.waiverFieldNames();
                const tct = this.state.eligibility.teamConstraintType();
                return this.state.playerForms.areFormsValid(
                    schemas, ids,
                    pid => this.state.familyPlayers.isPlayerLocked(pid),
                    (pid, f) => this.state.playerForms.isFieldVisibleForPlayer(pid, f, wfn, tct),
                );
            }
            case 'waivers': return this.state.jobCtx.allRequiredWaiversAccepted();
            case 'review': return true;
            case 'payment': return false; // payment step handles its own flow
            case 'confirmation': return false; // end
            default: return false;
        }
    });

    readonly showContinue = computed(() => {
        const id = this.currentStepId();
        return id !== 'family-check' && id !== 'payment' && id !== 'confirmation';
    });

    readonly continueLabel = computed(() => {
        if (this.currentStepId() === 'review') return 'Submit';
        return 'Continue';
    });

    // ── Lifecycle ─────────────────────────────────────────────────────
    ngOnInit(): void {
        const jobPath = this.route.snapshot.paramMap.get('jobPath') || '';
        if (jobPath) {
            this.state.reset();
            this.state.initialize(jobPath);
        }
        // Deep-link via query param
        const stepParam = this.route.snapshot.queryParamMap.get('step');
        if (stepParam) {
            const idx = this.activeSteps().findIndex(s => s.id === stepParam);
            if (idx >= 0) this._currentIndex.set(idx);
        }
    }

    // ── Navigation ────────────────────────────────────────────────────
    back(): void {
        if (this._currentIndex() > 0) {
            this._currentIndex.set(this._currentIndex() - 1);
        }
    }

    async next(): Promise<void> {
        const active = this.activeSteps();
        const idx = this._currentIndex();
        if (idx >= active.length - 1) return;

        // Special handling: review -> payment requires preSubmit
        if (this.currentStepId() === 'review') {
            try {
                await this.state.preSubmitRegistration();
            } catch (err: unknown) {
                console.error('[PlayerWizard] preSubmit failed', err);
                this.toast.show('Registration submission failed. Please review and try again.', 'danger', 5000);
                return; // stay on review step
            }
        }

        this._currentIndex.set(idx + 1);
    }

    finish(): void {
        const jobPath = this.route.snapshot.paramMap.get('jobPath') || '';
        this.router.navigate([`/${jobPath}`]);
    }
}
