import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { PlayerWizardStateService } from './state/player-wizard-state.service';
import { PaymentV2Service } from './state/payment-v2.service';
import { TeamService } from './services/team.service';
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
    selector: 'app-registration-player',
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
      [showBack]="showBack()"
      [showContinue]="showContinue()"
      [continueLabel]="continueLabel()"
      (back)="back()"
      (continue)="next()"
      (goToStep)="goToStep($event)">
      @switch (currentStepId()) {
        @case ('family-check') { <app-prw-family-check-step (advance)="next()" /> }
        @case ('players') { <app-prw-player-selection-step (advance)="next()" /> }
        @case ('eligibility') { <app-prw-eligibility-step (advance)="next()" /> }
        @case ('teams') { <app-prw-team-selection-step (advance)="next()" /> }
        @case ('forms') { <app-prw-player-forms-step (advance)="next()" /> }
        @case ('waivers') { <app-prw-waivers-step (advance)="next()" /> }
        @case ('review') { <app-prw-review-step (advance)="next()" /> }
        @case ('payment') { <app-prw-payment-step (advance)="next()" /> }
        @case ('confirmation') { <app-prw-confirmation-step (finished)="finish()" /> }
      }
    </app-wizard-shell>
  `,
    styleUrls: ['./player.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly toast = inject(ToastService);
    private readonly teamService = inject(TeamService);
    private readonly paySvc = inject(PaymentV2Service);
    readonly state = inject(PlayerWizardStateService);

    private readonly _currentIndex = signal(0);
    readonly currentIndex = this._currentIndex.asReadonly();

    // ── Step definitions ──────────────────────────────────────────────
    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'family-check', label: 'Account', enabled: true, showInIndicator: false },
        { id: 'players', label: 'Players', enabled: true },
        {
            id: 'eligibility', label: this.eligibilityStepLabel(),
            enabled: !!this.state.eligibility.teamConstraintType(),
        },
        { id: 'teams', label: this.state.jobCtx.isCacMode() ? 'Events' : 'Teams', enabled: true },
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

    readonly eligibilityStepLabel = computed(() => {
        const ct = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        if (ct === 'BYGRADYEAR') return 'Grad Year';
        if (ct === 'BYAGEGROUP') return 'Age Group';
        if (ct === 'BYAGERANGE') return 'Age Range';
        if (ct === 'BYCLUBNAME') return 'Club';
        return 'Eligibility';
    });

    readonly shellConfig = computed<WizardShellConfig>(() => ({
        title: 'Player Registration',
        theme: 'player',
        badge: this.state.familyPlayers.familyUser()?.userName
            ? `Family Account: ${this.state.familyPlayers.familyUser()!.userName}`
            : null,
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
            case 'payment': return this.paySvc.currentTotal() <= 0;
            case 'confirmation': return !!this.state.confirmation();
            default: return false;
        }
    });

    readonly showBack = computed(() => {
        const id = this.currentStepId();
        if (id === 'family-check' || id === 'players' || id === 'confirmation') return false;
        return true;
    });

    readonly showContinue = computed(() => {
        const id = this.currentStepId();
        if (id === 'family-check') return false;
        if (id === 'players') return this.state.familyPlayers.selectedPlayerIds().length > 0;
        if (id === 'payment') return this.paySvc.currentTotal() <= 0;
        if (id === 'confirmation') return !!this.state.confirmation();
        return true;
    });

    readonly continueLabel = computed(() => {
        if (this.currentStepId() === 'confirmation') return 'Finish';
        return 'Continue';
    });

    private readonly authService = inject(AuthService);

    // ── Lifecycle ─────────────────────────────────────────────────────
    ngOnInit(): void {
        const jobPath = this.resolveJobPath();

        // Start clean unless user already holds a Family/Player role for this job
        const user = this.authService.currentUser();
        const validRole = user?.role === 'Family' || user?.role === 'Player';
        const sameJob = user?.jobPath?.toLowerCase() === jobPath.toLowerCase();
        if (!validRole || !sameJob) {
            this.authService.logoutLocal();
            // Don't navigate away — wizard starts at family-check (login) step
        }

        // Always reset wizard state (clears stale errors from prior sessions)
        this.state.reset();

        if (jobPath) {
            this.state.jobCtx.setJobPath(jobPath);
            // Only pre-load if already authenticated as Family/Player for this job
            if (validRole && sameJob) {
                this.state.initialize(jobPath);
                this.teamService.loadForJob(jobPath);
            }
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

    goToStep(stepIndex: number): void {
        if (stepIndex < this._currentIndex()) {
            this._currentIndex.set(stepIndex);
        }
    }

    async next(): Promise<void> {
        // Confirmation step: Finish navigates to job home
        if (this.currentStepId() === 'confirmation') {
            this.finish();
            return;
        }

        const active = this.activeSteps();
        const idx = this._currentIndex();
        if (idx >= active.length - 1) return;

        // Phase 1: reserve team spots at team selection → forms transition
        if (this.currentStepId() === 'teams') {
            try {
                const resp = await this.state.reserveTeams();
                if (resp.hasFullTeams) {
                    const fullTeams = resp.teamResults
                        .filter(r => r.isFull)
                        .map(r => r.teamName)
                        .join(', ');
                    this.toast.show(
                        `Team(s) full: ${fullTeams}. Please choose different teams.`,
                        'warning', 5000,
                    );
                    return; // stay on teams step
                }
                // Notify parent if any players were placed on a waitlist
                const waitlisted = resp.teamResults.filter(r => r.isWaitlisted);
                if (waitlisted.length > 0) {
                    const names = waitlisted.map(r => r.waitlistTeamName ?? r.teamName).join(', ');
                    this.toast.show(
                        `Note: Your selected team is full. You have been placed on the waitlist: ${names}`,
                        'warning', 8000,
                    );
                }
            } catch (err: unknown) {
                console.error('[PlayerWizard] reserveTeams failed', err);
                this.toast.show('Could not reserve team spots. Please try again.', 'danger', 5000);
                return;
            }
        }

        // Phase 2: finalize at review -> payment (apply form values, validate, insurance)
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
        this.authService.logoutLocal();
        this.router.navigate([`/${this.resolveJobPath()}`]);
    }

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
