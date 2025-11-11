import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { PlayerSelectionComponent } from './steps/player-selection.component';
import { TeamSelectionComponent } from './steps/team-selection.component';
import { ReviewComponent } from './steps/review.component';
import { ConstraintSelectionComponent as EligibilitySelectionComponent } from './steps/constraint-selection.component';
import { PlayerFormsComponent } from './steps/player-forms.component';
import { PaymentComponent } from './steps/payment.component';
import { WaiversComponent } from './steps/waivers.component';
import { RegistrationWizardService } from './registration-wizard.service';
// Start step retired; StartChoiceComponent removed from flow
import { FamilyCheckStepComponent } from './steps/family-check.component';
import { AuthService } from '../../core/services/auth.service';
import { JobContextService } from '../../core/services/job-context.service';
import { WizardThemeDirective } from '../../shared/directives/wizard-theme.directive';

export type StepId = 'family-check' | 'players' | 'eligibility' | 'teams' | 'forms' | 'waivers' | 'review' | 'payment';

@Component({
    selector: 'app-player-registration-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, WizardThemeDirective, FamilyCheckStepComponent, PlayerSelectionComponent, TeamSelectionComponent, ReviewComponent, EligibilitySelectionComponent, PlayerFormsComponent, WaiversComponent, PaymentComponent],
    templateUrl: './player-registration-wizard.component.html',
    styleUrls: ['./player-registration-wizard.component.scss'],
    host: {}
})
export class PlayerRegistrationWizardComponent implements OnInit {
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    readonly state = inject(RegistrationWizardService);
    private readonly auth = inject(AuthService);
    private readonly jobContext = inject(JobContextService);

    // Steps managed by stable IDs for deep-linking
    // Note: 'constraint' may be skipped in a future enhancement if job has no constraint.
    // Start step retired; Family Check now offers CTAs to proceed directly
    // Unified steps (edit lookup removed; flow determined solely by backend flags)
    private readonly baseSteps: StepId[] = ['family-check', 'players', 'eligibility', 'teams', 'forms', 'waivers', 'review', 'payment'];

    // Current index into the computed steps array
    currentIndex = signal(0);

    steps = computed<StepId[]>(() => {
        try {
            const hasWaivers = ((this.state.waiverDefinitions()?.length ?? 0) > 0) || ((this.state.waiverFieldNames()?.length ?? 0) > 0);
            const hasEligibility = !!this.state.teamConstraintType();
            const authed = !!this.auth.currentUser();
            let stepsBase = this.baseSteps;
            // If no eligibility constraint configured, drop that step entirely
            if (!hasEligibility) stepsBase = stepsBase.filter(s => s !== 'eligibility') as StepId[];
            // Always keep the Teams step in this flow; only eligibility may be skipped
            // If no waivers present, drop waivers step
            const withWaivers = hasWaivers ? stepsBase : (stepsBase.filter(s => s !== 'waivers') as StepId[]);
            return withWaivers;
        } catch (err) {
            console.error('[PRW] Error computing steps; fallback applied', err);
            return this.baseSteps;
        }
    });

    currentStepId = computed<StepId>(() => {
        const arr = this.steps();
        if (!arr?.length) return 'family-check';
        const safeIndex = Math.min(this.currentIndex(), arr.length - 1);
        return arr[safeIndex];
    });
    progressPercent = computed(() => Math.round(((this.currentIndex() + 1) / this.steps().length) * 100));
    // CAC-style: allow multiple team selections when no explicit eligibility constraint (or BYCLUBNAME)
    isMultiTeamMode = computed(() => {
        const t = (this.state.teamConstraintType() || '').toUpperCase();
        return !t || t === 'BYCLUBNAME';
    });

    // Only show an account badge when authenticated family user is present (via state.familyUser)

    readonly stepLabels: Record<StepId, string> = {
        'family-check': 'Family account?',
        players: 'Players',
        eligibility: 'Eligibility',
        teams: 'Teams',
        forms: 'Forms',
        waivers: 'Waivers',
        review: 'Review',
        payment: 'Payment'
    };

    // Removed existingRegistrationAvailable logic; edit mode concept discarded.

    // Context is simplified: players and job metadata are loaded directly when jobPath is known.

    ngOnInit(): void {
        // Ensure a clean wizard state each time this route is entered (does not affect auth)
        this.state.reset();
        // Derive canonical jobPath from JobContextService (URL is source of truth).
        // Ensure service is initialized; then fall back to route params if needed.
        try { this.jobContext.init(); } catch { /* no-op */ }
        let jobPath = this.jobContext.jobPath() || '';
        if (!jobPath) {
            const qpParam = this.route.snapshot.paramMap.get('jobPath')
                || this.route.parent?.snapshot.paramMap.get('jobPath')
                || this.route.root.firstChild?.snapshot.paramMap.get('jobPath')
                || '';
            jobPath = qpParam || '';
        }
        if (jobPath) {
            console.debug('[PRW] jobPath:', jobPath);
        } else {
            console.warn('[PRW] jobPath was not found in URL; wizard may not load data.');
        }
        this.state.jobPath.set(jobPath);

        // If already authenticated, proactively load family players for this job
        if (!!this.auth.currentUser() && !!jobPath) {
            this.state.loadFamilyPlayers(jobPath);
        }

        // No localStorage fallback: unauthenticated users must choose explicitly on Family Check.

        // Apply query params (mode + step)
        // Mode parameter deprecated; ignore if present.
        const qpStep = this.route.snapshot.queryParamMap.get('step');
        let hadStepFromQuery = false;
        if (qpStep) {
            // case-insensitive & guard unauthenticated deep-link to players
            const qpLower = qpStep.toLowerCase();
            const desired: StepId = (!this.auth.currentUser() && qpLower === 'players') ? 'family-check' : (qpLower as StepId);
            const targetIndex = this.steps().indexOf(desired);
            if (targetIndex >= 0) {
                this.currentIndex.set(targetIndex);
                hadStepFromQuery = true;
            }
        }

        // If authenticated, optionally auto-advance to players
        if (!hadStepFromQuery && !!this.auth.currentUser()) {
            const playersIdx = this.steps().indexOf('players');
            if (playersIdx >= 0) this.currentIndex.set(playersIdx);
        }

        // Hard baseline for unauthenticated sessions: always start at family-check
        if (!this.auth.currentUser()) {
            const famIdx = this.steps().indexOf('family-check');
            if (famIdx >= 0) this.currentIndex.set(famIdx);
        }

        // Debug logging (temporary) to trace blank-step issue
        if (!this.auth.currentUser()) {
            console.debug('[PRW] Init unauth user: steps=', this.steps(), 'currentStepId=', this.currentStepId(), 'hasFamilyAccount=', this.state.hasFamilyAccount());
        }
    }

    // Temporary helper to expose debug data (could be removed later)
    get debugState() {
        return {
            authed: !!this.auth.currentUser(),
            stepIds: this.steps(),
            currentStep: this.currentStepId(),
            index: this.currentIndex(),
            // startMode removed
            hasFamilyAccount: this.state.hasFamilyAccount(),
            jobPath: this.state.jobPath(),
            familyUser: this.state.familyUser()
        };
    }

    next(): void {
        this.currentIndex.update(i => Math.min(i + 1, this.steps().length - 1));
    }

    back(): void {
        this.currentIndex.update(i => Math.max(0, i - 1));
    }

    // Start step removed; branching handled via Family Check CTAs and direct deep-links

    async proceedToPayment() {
        // Call preSubmit before showing payment step
        try {
            const result = await this.state.preSubmitRegistration();
            if (result.teamResults.some(r => r.isFull)) {
                // At least one team is full, go back to Team tab and show message
                const teamsIdx = this.steps().indexOf('teams');
                if (teamsIdx >= 0) this.currentIndex.set(teamsIdx);
                // Optionally, display a message to the user (implement your own notification system)
                alert('One or more selected teams are full. Please update your selections.');
            } else {
                // All teams OK, proceed to Forms or Payment
                const nextIdx = this.steps().indexOf(result.nextTab === 'Forms' ? 'forms' : 'payment');
                if (nextIdx >= 0) this.currentIndex.set(nextIdx);
            }
        } catch (err) {
            alert('Error checking team availability: ' + err);
        }
    }
}
