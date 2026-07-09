import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { ToastService } from '@shared-ui/toast.service';
import { PlayerWizardStateService } from './state/player-wizard-state.service';
import { PaymentV2Service } from './state/payment-v2.service';
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
import { isPlayerRegistrationEffectivelyOpen } from '@shared/landing/landing-phase';

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
    @if (registrationClosed()) {
      <div class="reg-closed-wrap">
        <div class="card shadow border-0 card-rounded reg-closed-card">
          <div class="card-body text-center py-5 px-4">
            <i class="bi bi-calendar-x reg-closed-icon" aria-hidden="true"></i>
            <h3 class="reg-closed-title">Player Registration Not Currently Open</h3>
            <p class="reg-closed-body">
              This event isn't currently accepting new player signups.
              You can still review or complete your existing registrations
              from the event home page.
            </p>
            <button type="button" class="btn btn-primary" (click)="goToJobHome()">
              <i class="bi bi-arrow-left me-1"></i> Return to Event Home
            </button>
          </div>
        </div>
      </div>
    } @else {
      <app-wizard-shell
        [steps]="steps()"
        [currentIndex]="currentIndex()"
        [config]="shellConfig()"
        [canContinue]="canContinue()"
        [busy]="transitioning()"
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
          @case ('forms') { <app-prw-player-forms-step /> }
          @case ('waivers') { <app-prw-waivers-step /> }
          @case ('review') { <app-prw-review-step (advance)="next()" /> }
          @case ('payment') { <app-prw-payment-step (advance)="next()" /> }
          @case ('confirmation') { <app-prw-confirmation-step (finished)="finish()" /> }
        }
      </app-wizard-shell>
    }
  `,
    styleUrls: ['./player.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly destroyRef = inject(DestroyRef);
    private readonly toast = inject(ToastService);
    private readonly paySvc = inject(PaymentV2Service);
    private readonly jobPulseService = inject(JobPulseService);
    readonly state = inject(PlayerWizardStateService);

    /**
     * True when the viewer already holds a registration in THIS job — a phase-2 token
     * (regId claim, bound to this jobPath) for the Family/Player role. Such a viewer
     * reached the wizard via "My Registration" to review / pay / manage what already
     * exists, so the new-signup closed gate must not bar them. Their granular CRUD
     * (add a new player, edit, cancel) is enforced canonically server-side by
     * IJobRegistrationCapabilities (CanRegisterPlayer); payment is never gated. This
     * door only stops a brand-new signup with nothing here yet.
     */
    private readonly hasExistingRegistration = computed(() => {
        const u = this.authService.currentUser();
        if (!u?.regId) return false;
        const validRole = u.role === 'Family' || u.role === 'Player';
        const sameJob = u.jobPath?.toLowerCase() === this.resolveJobPath().toLowerCase();
        return validRole && sameJob;
    });

    /**
     * True when the job's pulse says player registration is effectively closed:
     * either the admin toggle is off, or no team is currently within its
     * registration-availability window. Null pulse (not yet loaded) is not
     * treated as closed — we render the wizard optimistically and swap to the
     * closed panel if the pulse later confirms closure. An existing registrant is
     * always let in (they came to manage what exists, not to start a new signup).
     */
    readonly registrationClosed = computed(() => {
        const p = this.jobPulseService.pulse();
        if (!p) return false; // pulse not loaded yet — render optimistically, don't flash "closed"
        if (this.hasExistingRegistration()) return false; // managing an existing reg, not a new signup
        return !isPlayerRegistrationEffectivelyOpen(p);
    });

    goToJobHome(): void {
        this.router.navigate([`/${this.resolveJobPath()}`]);
    }

    private readonly _currentIndex = signal(0);
    readonly currentIndex = this._currentIndex.asReadonly();

    /**
     * True while an async step transition is in flight (the Review→Payment PreSubmit).
     * Drives the shared action-bar spinner and disables Continue so a slow round-trip
     * can't be double-clicked into a second preSubmit.
     */
    private readonly _transitioning = signal(false);
    readonly transitioning = this._transitioning.asReadonly();

    // ── Step definitions ──────────────────────────────────────────────
    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'family-check', label: 'Account', enabled: true, showInIndicator: false },
        { id: 'players', label: 'Players', enabled: true },
        {
            id: 'eligibility', label: this.eligibilityStepLabel(),
            enabled: !!this.state.eligibility.teamConstraintType(),
        },
        { id: 'teams', label: this.state.jobCtx.isCacMode() ? 'Events' : 'Team', enabled: true },
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
                const cac = this.state.jobCtx.isCacMode();
                return selected.every(id => {
                    const v = teams[id];
                    if (cac) {
                        // selectedTeams holds `string | string[]` — a single prior registration
                        // rehydrated by prefillTeamsFromPriorRegistrations is a bare string, not an
                        // array. Normalize the same way getSelectedTeamIds does so a returning player
                        // with one prior event still counts (badge shows 1, gate now agrees).
                        const arr = Array.isArray(v) ? v : (v ? [v] : []);
                        return arr.length > 0;
                    }
                    return !!v;
                });
            }
            case 'forms': {
                const schemas = this.state.jobCtx.profileFieldSchemas();
                const ids = this.state.familyPlayers.selectedPlayerIds();
                return this.state.playerForms.areFormsValid(
                    schemas, ids,
                    pid => this.state.familyPlayers.isPlayerLocked(pid),
                    (pid, f) => this.state.isFieldVisibleForPlayer(pid, f),
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
        if (this.currentStepId() === 'review') {
            const count = this.newRegistrationCount();
            if (count > 0) return count === 1 ? 'Submit Registration' : 'Submit Registrations';
        }
        return 'Continue';
    });

    private newRegistrationCount(): number {
        return this.state.familyPlayers.familyPlayers()
            .filter(p => p.selected && !p.registered).length;
    }

    private hasNewRegistrations(): boolean {
        return this.newRegistrationCount() > 0;
    }

    private readonly authService = inject(AuthService);

    // ── Lifecycle ─────────────────────────────────────────────────────
    ngOnInit(): void {
        const jobPath = this.resolveJobPath();

        // Start clean unless user already holds a Family/Player role for this job.
        // Phase-1 tokens (no role) are harmless — don't log out, let family-check handle login.
        const user = this.authService.currentUser();
        const validRole = user?.role === 'Family' || user?.role === 'Player';
        const noRole = !user?.role;
        const sameJob = user?.jobPath?.toLowerCase() === jobPath.toLowerCase();
        if (user && !noRole && (!validRole || !sameJob)) {
            // Wrong role or wrong job — clear and start fresh
            this.authService.logoutLocal();
        }

        // Always reset wizard state (clears stale errors from prior sessions)
        this.state.reset();

        if (jobPath) {
            this.state.jobCtx.setJobPath(jobPath);
            // Only pre-load if already authenticated as Family/Player for this job
            if (validRole && sameJob) {
                this.state.initialize(jobPath);
                // Skip family-check — already authenticated for this job
                const playersIdx = this.activeSteps().findIndex(s => s.id === 'players');
                if (playersIdx >= 0) this._currentIndex.set(playersIdx);
            }
        }
        // Deep-link via query param (overrides skip above if specified).
        // Subscribe (not snapshot) so role-menu clicks that differ only in ?step=
        // while already on the wizard actually move to the requested step.
        this.route.queryParamMap
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe(params => {
                const stepParam = params.get('step');
                if (stepParam) {
                    const idx = this.activeSteps().findIndex(s => s.id === stepParam);
                    if (idx >= 0) this._currentIndex.set(idx);
                }
            });
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

        // Guard against double-clicks while an async transition (PreSubmit) is in flight —
        // a second click here would fire a second preSubmit against the same registration.
        if (this._transitioning()) return;

        const active = this.activeSteps();
        const idx = this._currentIndex();
        if (idx >= active.length - 1) return;

        // Teams step: pure client-side selection — no backend round-trip. Registrations are
        // created at PreSubmit (review → payment) and nowhere else; the roster-max reconcile
        // and any waitlist move happen there (and again, as a backstop, at the charge).

        // Finalize at review -> payment (create registrations, apply form values, validate,
        // reconcile seats, insurance).
        if (this.currentStepId() === 'review') {
            this._transitioning.set(true);
            try {
            const newRegs = this.hasNewRegistrations();
            try {
                const resp = await this.state.preSubmitRegistration();
                // A 200 response can still carry validation errors (the server applies form
                // values but does NOT save when validation fails). Treat that as a hard stop:
                // advancing here would charge the card against a registration whose profile was
                // never persisted. Stay on Review and surface the captured errors instead.
                if (resp?.validationErrors?.length) {
                    this.toast.show('Some required information is missing or invalid. Please correct the highlighted fields before continuing.', 'warning', 7000);
                    return; // stay on review step — do NOT advance to Payment
                }
                // PreSubmit reconciles seats: any player whose real team filled up was auto-moved
                // to the $0 waitlist twin (not charged). Surface that plainly before payment so the
                // family knows which kids weren't seated. The team list re-reflects via the
                // response's rawTeams (applied in the state service).
                if (resp?.movedToWaitlist?.length) {
                    // Name each child + the team they wanted (strip the "WAITLIST - " prefix off the
                    // twin's name). A waitlist placement is a COMPLETED $0 registration, not a pending
                    // hold, so say that plainly rather than "won't be charged".
                    const parts = resp.movedToWaitlist.map(m => {
                        const name = m.playerName?.trim() || 'A player';
                        const team = (m.teamName || '').replace(/^WAITLIST - /i, '').trim();
                        return team ? `${name} (${team})` : name;
                    });
                    const verb = resp.movedToWaitlist.length === 1 ? 'is' : 'are';
                    this.toast.show(
                        `${parts.join(', ')} ${verb} now on the waitlist — that team just filled up. `
                        + `Registration is complete at no charge; we'll notify you if a spot opens.`,
                        'warning', 10000,
                    );
                }
            } catch (err: unknown) {
                console.error('[PlayerWizard] preSubmit failed', err);
                this.toast.show('Registration submission failed. Please review and try again.', 'danger', 5000);
                return; // stay on review step
            }
            // Reload family players so the payment tab has full financials
            // (feeBase, feeProcessing, etc.) from the registration just created.
            try {
                const jobPath = this.state.jobCtx.jobPath();
                const apiBase = this.state.jobCtx.resolveApiBase();
                await this.state.familyPlayers.loadFamilyPlayersOnce(jobPath, apiBase);
                // Re-point selections at each player's actual (reloaded) reg team. A player the
                // seat reconcile bounced to the $0 WAITLIST twin now has an active reg there, but
                // selectedTeams still names the full real team — without this the payment table
                // re-bills the real-team fee instead of showing the $0 waitlist line.
                this.state.reconcileSelectionsFromCurrentRegistrations();
            } catch (err: unknown) {
                console.warn('[PlayerWizard] post-preSubmit reload failed', err);
            }
            // SP-042: Legacy success toast (verbatim) — only on a NEW registration that
            // actually owes something. A genuinely-free registration ($0 waitlist twin,
            // self-rostering, or free CAC) is already active (OwedTotal=0 -> ActivateIfFree),
            // so the "not paid in full -> INACTIVE -> pay to activate" message is both false
            // and confusing. The payment tab's own "No payment due" alert (showNoPaymentInfo)
            // covers that case; totalAmount() is the same balance signal that gates it.
            if (newRegs && this.paySvc.totalAmount() > 0) {
                this.toast.show(
                    'Player registration data HAS BEEN SAVED SUCCESSFULLY. '
                    + 'However, players not paid in full are marked INACTIVE and will NOT be placed on rosters. '
                    + 'Please be sure to pay for your players to ensure their registrations are activated '
                    + 'and they are available to be placed on rosters.',
                    'success', 10000,
                );
            }
            } finally {
                this._transitioning.set(false);
            }
        }

        this._currentIndex.set(idx + 1);
    }

    finish(): void {
        // Navigate to job home WITHOUT logging out — the family stays authenticated so
        // they can register again, view their dashboard, or browse the store. Logout is
        // an explicit header action, not a side effect of finishing a registration.
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
