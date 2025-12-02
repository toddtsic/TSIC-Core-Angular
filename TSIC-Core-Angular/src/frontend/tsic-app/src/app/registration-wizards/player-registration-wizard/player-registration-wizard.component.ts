import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { PlayerSelectionComponent } from './steps/player-selection.component';
import { TeamSelectionComponent } from './steps/team-selection.component';
import { ReviewComponent } from './steps/review.component';
import { ConstraintSelectionComponent as EligibilitySelectionComponent } from './steps/constraint-selection.component';
import { PlayerFormsComponent } from './steps/player-forms.component';
import { PaymentComponent } from './steps/payment.component';
import { ConfirmationComponent } from './steps/confirmation.component';
import { WaiversComponent } from './steps/waivers.component';
import { RegistrationWizardService } from './registration-wizard.service';
import { InsuranceStateService } from './services/insurance-state.service';
import { PaymentService } from './services/payment.service';
import { InsuranceService } from './services/insurance.service';
import { WaiverStateService } from './services/waiver-state.service';
// Start step retired; StartChoiceComponent removed from flow
import { FamilyCheckStepComponent } from './steps/family-check.component';
import { AuthService } from '../../core/services/auth.service';
import { JobContextService } from '../../core/services/job-context.service';
import { WizardThemeDirective } from '../../shared/directives/wizard-theme.directive';
import { RwActionBarComponent } from './action-bar/rw-action-bar.component';
import { ToastService } from '../../shared/toast.service';

export type StepId = 'family-check' | 'players' | 'eligibility' | 'teams' | 'forms' | 'waivers' | 'review' | 'payment' | 'confirmation';

@Component({
    selector: 'app-player-registration-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, WizardThemeDirective, RwActionBarComponent, FamilyCheckStepComponent, PlayerSelectionComponent, TeamSelectionComponent, ReviewComponent, EligibilitySelectionComponent, PlayerFormsComponent, WaiversComponent, PaymentComponent, ConfirmationComponent],
    templateUrl: './player-registration-wizard.component.html',
    styleUrls: ['./player-registration-wizard.component.scss'],
    host: {}
})
export class PlayerRegistrationWizardComponent implements OnInit {
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    readonly state = inject(RegistrationWizardService);
    private readonly waiverState = inject(WaiverStateService);
    private readonly auth = inject(AuthService);
    private readonly jobContext = inject(JobContextService);

    // Steps managed by stable IDs for deep-linking
    // Note: 'constraint' may be skipped in a future enhancement if job has no constraint.
    // Start step retired; Family Check now offers CTAs to proceed directly
    // Unified steps (edit lookup removed; flow determined solely by backend flags)
    private readonly baseSteps: StepId[] = ['family-check', 'players', 'eligibility', 'teams', 'forms', 'waivers', 'review', 'payment', 'confirmation'];

    // Current index into the computed steps array
    currentIndex = signal(0);

    steps = computed<StepId[]>(() => {
        try {
            const hasWaivers = ((this.waiverState.waiverDefinitions()?.length ?? 0) > 0) || ((this.waiverState.waiverFieldNames()?.length ?? 0) > 0);
            const hasEligibility = !!this.state.teamConstraintType();
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

    // Family last name for compact badge in the sticky header
    familyLastName = computed(() => {
        const fu = this.state.familyUser();
        const source = (fu?.displayName || fu?.userName || '').trim();
        if (!source) return '';
        // Prefer "Last, First" -> take part before comma
        const commaIdx = source.indexOf(',');
        if (commaIdx > 0) {
            return source.slice(0, commaIdx).trim();
        }
        // Otherwise use the last token as the last name (handles "First Last" and single-token usernames)
        const parts = source.split(/\s+/g).filter(Boolean);
        return parts.length ? (parts.at(-1) || '') : source;
    });

    private readonly insuranceState = inject(InsuranceStateService);
    private readonly paymentSvc = inject(PaymentService);
    private readonly insuranceSvc = inject(InsuranceService);
    private readonly toast = inject(ToastService);

    // Helper: registrations relevant for ARB active determination
    private relevantRegs() {
        const playerIds = new Set(this.state.familyPlayers().filter(p => p.selected || p.registered).map(p => p.playerId));
        return this.state.familyPlayers().filter(p => playerIds.has(p.playerId)).flatMap(p => p.priorRegistrations || []);
    }
    private arbAllActive(): boolean {
        const regs = this.relevantRegs();
        if (!regs.length) return false;
        return regs.every(r => !!r.adnSubscriptionId && (r.adnSubscriptionStatus || '').toLowerCase() === 'active');
    }
    private viCcOnlyFlow(): boolean {
        // Premium-only insurance flow (no TSIC balance, insurance confirmed, quotes present)
        return this.paymentSvc.currentTotal() === 0
            && this.insuranceState.offerPlayerRegSaver()
            && this.insuranceState.verticalInsureConfirmed()
            && this.insuranceSvc.quotes().length > 0;
    }
    private tsicChargeDue(): boolean {
        if (this.arbAllActive()) return false;
        return this.paymentSvc.currentTotal() > 0;
    }

    // Unified action bar state (includes payment step logic)
    showContinueButton = computed(() => {
        const step = this.currentStepId();
        if (step === 'family-check') return false;
        if (step === 'payment') {
            // Hide when TSIC charge due.
            if (this.tsicChargeDue()) return false;
            // Hide when insurance confirmed and quotes present (must use insurance submit button).
            if (this.insuranceState.verticalInsureConfirmed() && this.insuranceSvc.quotes().length > 0) return false;
            // Hide when insurance-only flow (confirmed, zero TSIC balance) -> uses insurance submit button.
            if (this.viCcOnlyFlow()) return false;
            return true;
        }
        if (step === 'confirmation') return false; // end of flow
        return true;
    });
    continueLabel = computed(() => {
        const step = this.currentStepId();
        if (step === 'review') return 'Proceed to Payment';
        return 'Continue';
    });
    canContinue = computed(() => {
        const step = this.currentStepId();
        if (step === 'players') return this.canContinuePlayers();
        if (step === 'eligibility') return this.canContinueEligibility();
        if (step === 'teams') return this.canContinueTeams();
        if (step === 'forms') return this.canContinueForms();
        if (step === 'waivers') return this.canContinueWaivers();
        if (step === 'review') return true;
        if (step === 'payment') return this.canContinuePayment();
        return false;
    });

    private canContinuePlayers(): boolean { return this.state.selectedPlayerIds().length > 0; }
    private canContinueEligibility(): boolean {
        const type = (this.state.teamConstraintType() || '').toUpperCase();
        if (!type) return true;
        const selected = this.state.familyPlayers().filter(p => p.selected || p.registered);
        const map = this.state.eligibilityByPlayer();
        for (const p of selected) {
            if (p.registered) continue;
            const v = map[p.playerId ?? ''];
            if (!v || String(v).trim() === '') return false;
        }
        return true;
    }
    private canContinueTeams(): boolean {
        const selected = this.state.familyPlayers().filter(p => p.selected || p.registered);
        if (selected.length === 0) return false;
        const map = this.state.selectedTeams();
        for (const p of selected) {
            const val = map[p.playerId ?? ''] as any;
            if (!val || (Array.isArray(val) && val.length === 0)) return false;
        }
        return true;
    }
    private canContinueForms(): boolean { return this.state.areFormsValid(); }
    private canContinueWaivers(): boolean {
        const ok = this.waiverState.waiversGateOk();
        if (ok) return true;
        try {
            const defs = this.waiverState.waiverDefinitions();
            const acc = this.waiverState.waiversAccepted();
            const required = defs.filter(d => d.required);
            if (required.length === 0) return true;
            const allOk = required.every(d => this.waiverState.isWaiverAccepted(d.id));
            if (allOk) return true;
            const acceptedCount = Object.values(acc).filter(Boolean).length;
            return acceptedCount >= required.length;
        } catch { return false; }
    }
    private canContinuePayment(): boolean {
        if (!this.showContinueButton()) return false;
        // Allow prior to decision (toast will gate) when no TSIC or VI premium yet.
        if (this.insuranceState.offerPlayerRegSaver() && !this.insuranceState.hasVerticalInsureDecision()) return true;
        // Declined -> can continue.
        if (this.insuranceState.verticalInsureDeclined()) return true;
        // No insurance offered -> can continue.
        if (!this.insuranceState.offerPlayerRegSaver()) return true;
        // Confirmed with quotes (premium) -> must NOT use continue (button hidden earlier, defensive false).
        if (this.insuranceState.verticalInsureConfirmed() && this.insuranceSvc.quotes().length > 0) return false;
        // Confirmed with no quotes (zero premium scenario) -> can continue.
        if (this.insuranceState.verticalInsureConfirmed()) return true;
        return false;
    }

    // Only show an account badge when authenticated family user is present (via state.familyUser)

    readonly stepLabels: Record<StepId, string> = {
        'family-check': 'Family account?',
        players: 'Players',
        eligibility: 'Eligibility',
        teams: 'Teams',
        forms: 'Forms',
        waivers: 'Waivers',
        review: 'Review',
        payment: 'Payment',
        confirmation: 'Confirmation'
    };

    // Removed existingRegistrationAvailable logic; edit mode concept discarded.

    // Context is simplified: players and job metadata are loaded directly when jobPath is known.

    ngOnInit(): void { this.initializeWizard(); }

    private initializeWizard(): void {
        this.resetWizardState();
        const jobPath = this.resolveJobPath();
        this.state.jobPath.set(jobPath);
        this.loadPlayers(jobPath);
        const hadStep = this.applyQueryStep();
        this.autoAdvanceIfAuthenticated(hadStep);
        this.ensureFamilyCheckStart();
        this.debugUnauthenticated();
    }
    private resetWizardState(): void { this.state.reset(); try { this.jobContext.init(); } catch { /* ignore */ } }
    private resolveJobPath(): string {
        const existing = this.jobContext.jobPath();
        if (existing) { console.debug('[PRW] jobPath:', existing); return existing; }
        const qpParam = this.route.snapshot.paramMap.get('jobPath')
            || this.route.parent?.snapshot.paramMap.get('jobPath')
            || this.route.root.firstChild?.snapshot.paramMap.get('jobPath')
            || '';
        if (qpParam) console.debug('[PRW] jobPath:', qpParam);
        else console.warn('[PRW] jobPath was not found in URL; wizard may not load data.');
        return qpParam;
    }
    private loadPlayers(jobPath: string): void { if (jobPath) this.state.loadFamilyPlayers(jobPath); }
    private applyQueryStep(): boolean {
        const qpStep = this.route.snapshot.queryParamMap.get('step');
        if (!qpStep) return false;
        const qpLower = qpStep.toLowerCase();
        const desired: StepId = (!this.auth.currentUser() && qpLower === 'players') ? 'family-check' : (qpLower as StepId);
        const idx = this.steps().indexOf(desired);
        if (idx >= 0) this.currentIndex.set(idx);
        return idx >= 0;
    }
    private autoAdvanceIfAuthenticated(hadStepFromQuery: boolean): void {
        if (hadStepFromQuery || !this.auth.currentUser()) return;
        const playersIdx = this.steps().indexOf('players');
        if (playersIdx >= 0) this.currentIndex.set(playersIdx);
    }
    private ensureFamilyCheckStart(): void {
        if (this.auth.currentUser()) return;
        const famIdx = this.steps().indexOf('family-check');
        if (famIdx >= 0) this.currentIndex.set(famIdx);
    }
    private debugUnauthenticated(): void {
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

    onContinue(): void {
        const step = this.currentStepId();
        if (step === 'review') {
            // Special action: preSubmit and route accordingly
            this.proceedToPayment();
            return;
        }
        if (step === 'payment') {
            this.handlePaymentContinue();
            return;
        }
        this.next();
    }

    // Start step removed; branching handled via Family Check CTAs and direct deep-links

    async proceedToPayment() {
        // Call preSubmit before showing payment step
        try {
            const result = await this.state.preSubmitRegistration();
            if (result.teamResults?.some(r => r.isFull)) {
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

    private handlePaymentContinue(): void {
        if (this.tsicChargeDue()) return; // guard
        // If insurance confirmed AND quotes present (premium due) OR insurance-only flow -> block and instruct.
        if ((this.insuranceState.verticalInsureConfirmed() && this.insuranceSvc.quotes().length > 0) || this.viCcOnlyFlow()) {
            this.toast.show('Insurance premium requires credit card submission. Use "Proceed with Insurance Processing" after entering card details.', 'danger', 5000);
            return;
        }
        // Treat existing stored policy (regSaverDetails) as a confirmed decision even if viConsent signal not set (persistence from earlier session)
        const policyOnFile = !!this.insuranceState.regSaverDetails();
        if (!this.insuranceState.offerPlayerRegSaver()) { this.advanceToConfirmation(); return; }
        const noDecisionYet = !policyOnFile && !this.insuranceState.hasVerticalInsureDecision();
        if (noDecisionYet) {
            this.toast.show('Insurance is optional. Please Confirm Purchase or Decline to continue.', 'danger', 4000);
            return;
        }
        if (this.insuranceState.verticalInsureDeclined()) { this.advanceToConfirmation(); return; }
        if (this.insuranceState.verticalInsureConfirmed() || policyOnFile) { this.advanceToConfirmation(); }
    }
    private advanceToConfirmation(): void {
        const confIdx = this.steps().indexOf('confirmation');
        if (confIdx >= 0) this.currentIndex.set(confIdx);
    }

    // Navigate back to job home after Finish on confirmation tab
    finishToJobHome(): void {
        const jp = this.state.jobPath();
        if (jp) {
            try { this.router.navigate(['/', jp]); } catch { /* ignore */ }
        } else {
            try { this.router.navigate(['/tsic']); } catch { /* ignore */ }
        }
    }
}
