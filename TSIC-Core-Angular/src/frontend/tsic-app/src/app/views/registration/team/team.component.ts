import {
    ChangeDetectionStrategy, Component, DestroyRef, OnInit, ViewChild,
    computed, inject, signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobContextService as InfraJobContext } from '@infrastructure/services/job-context.service';
import { TeamWizardStateService } from './state/team-wizard-state.service';
import { TeamRegistrationService } from './services/team-registration.service';
import { WizardShellComponent } from '../shared/wizard-shell/wizard-shell.component';
import { TeamLoginStepComponent } from './steps/login-step.component';
import { TeamTeamsStepComponent } from './steps/teams-step.component';
import { TeamPaymentStepV2Component } from './steps/payment-step.component';
import { TeamReviewStepComponent } from './steps/review-step.component';
import { TeamWaiversStepComponent } from './steps/waivers-step.component';
import { formatCurrency } from '@views/registration/shared/utils/format.util';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

@Component({
    selector: 'app-registration-team',
    standalone: true,
    imports: [
        WizardShellComponent,
        TeamLoginStepComponent,
        TeamTeamsStepComponent,
        TeamWaiversStepComponent,
        TeamPaymentStepV2Component,
        TeamReviewStepComponent,
    ],
    template: `
    @if (jobName()) {
      <h1 class="job-context">
        <i class="bi bi-trophy-fill job-context-icon"></i>
        <span>{{ jobName() }}</span>
      </h1>
    }
    <app-wizard-shell
      [steps]="steps()"
      [currentIndex]="currentIndex()"
      [config]="shellConfig()"
      [canContinue]="canContinue()"
      [showContinue]="showContinue()"
      [continueLabel]="continueLabel()"
      [detailsBadgeLabel]="detailsBadge()"
      [detailsBadgeClass]="detailsBadgeClass()"
      (back)="back()"
      (continue)="next()"
      (goToStep)="goToStep($event)">
      @switch (currentStepId()) {
        @case ('login') {
          <app-trw-login-step
            (loginSuccess)="onLoginSuccess($event)"
            (registrationSuccess)="onRegistrationSuccess($event)" />
        }
        @case ('waivers') {
          <app-trw-waivers-step />
        }
        @case ('teams') {
          <app-trw-teams-step
            (proceedToPayment)="next()" />
        }
        @case ('payment') {
          <app-trw-payment-step
            (submitted)="next()" />
        }
        @case ('review') {
          <app-trw-review-step
            (finished)="finish()" />
        }
      }
    </app-wizard-shell>
  `,
    styles: [`
    :host { display: block; }

    .job-context {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-2);
      margin: 0 auto var(--space-3);
      padding: var(--space-2) var(--space-3);
      max-width: 720px;
      font-size: var(--font-size-2xl);
      font-weight: var(--font-weight-bold);
      color: var(--brand-text);
      letter-spacing: -0.01em;
      line-height: 1.2;
    }

    .job-context-icon {
      color: var(--bs-primary);
      font-size: 1.1em;
    }

    @media (max-width: 575.98px) {
      .job-context {
        font-size: var(--font-size-xl);
        margin-bottom: var(--space-2);
      }
    }
  `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamWizardV2Component implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);
    private readonly infraJobCtx = inject(InfraJobContext);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly destroyRef = inject(DestroyRef);
    readonly state = inject(TeamWizardStateService);

    // Step id, not index, is the source of truth. Index-based state breaks deep links
    // when activeSteps changes shape after async metadata load (e.g. waivers coming
    // online gated on hasRefundPolicy pushed every subsequent step up by one).
    private readonly _currentStepId = signal<string>('login');
    readonly currentStepId = this._currentStepId.asReadonly();

    // ── Step definitions ──────────────────────────────────────────────
    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'login', label: 'Login', enabled: true },
        { id: 'waivers', label: 'Waivers', enabled: this.state.hasRefundPolicy() },
        { id: 'teams', label: 'Teams', enabled: true },
        { id: 'payment', label: 'Payment', enabled: true },
        { id: 'review', label: 'Review', enabled: true },
    ]);

    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));

    readonly currentIndex = computed(() => {
        const active = this.activeSteps();
        const idx = active.findIndex(s => s.id === this._currentStepId());
        return idx >= 0 ? idx : 0;
    });

    readonly shellConfig = computed<WizardShellConfig>(() => {
        const club = this.state.clubRep.selectedClub();
        return {
            title: club ? 'Team Registration for' : 'Team Registration',
            theme: 'team',
            titleAccent: club,
            badge: null,
        };
    });

    /** Event name displayed as page-top context across every step. */
    readonly jobName = computed(() => this.jobService.currentJob()?.jobName ?? '');

    // ── canContinue ─────────────────────────────────────────────────
    readonly canContinue = computed(() => {
        switch (this.currentStepId()) {
            case 'login': return false; // login step has own CTAs
            case 'teams': return true; // validation in proceedToPayment()
            case 'waivers': return this.state.waiverAccepted();
            case 'payment': return !this.state.teamPayment.hasBalance();
            case 'review': return true;
            default: return false;
        }
    });

    readonly showContinue = computed(() => {
        const id = this.currentStepId();
        // Teams step manages its own internal navigation (micro-steps + proceedToPayment output).
        // Showing the outer shell button during teams causes a confusing duplicate "Proceed to Payment".
        return id !== 'login' && id !== 'teams';
    });

    readonly continueLabel = computed(() => {
        switch (this.currentStepId()) {
            case 'teams': return 'Proceed to Payment';
            case 'waivers': return 'Continue';
            case 'payment': return 'Proceed to Review';
            case 'review': return 'Return Home';
            default: return 'Continue';
        }
    });

    readonly detailsBadge = computed<string | null>(() => {
        switch (this.currentStepId()) {
            case 'teams': {
                const bal = this.state.teamPayment.balanceDue();
                return bal > 0 ? formatCurrency(bal) + ' due' : null;
            }
            case 'payment': {
                const bal = this.state.teamPayment.balanceDue();
                return bal > 0 ? 'Payment Due: ' + formatCurrency(bal) : null;
            }
            default: return null;
        }
    });

    readonly detailsBadgeClass = computed(() => {
        switch (this.currentStepId()) {
            case 'teams': return 'badge-warning';
            case 'payment': return 'badge-danger';
            default: return '';
        }
    });

    // ── Lifecycle ───────────────────────────────────────────────────
    ngOnInit(): void {
        const jobPath = this.infraJobCtx.resolveFromRoute(this.route);
        if (jobPath) {
            this.state.reset();
            this.state.initialize(jobPath);
        }

        // Only Club Rep accounts are allowed in the team wizard.
        // Clear any non-ClubRep session or stale cross-job tokens.
        const currentUser = this.auth.currentUser();
        if (currentUser) {
            if (currentUser.role && currentUser.role !== 'Club Rep') {
                // Wrong role entirely (SuperUser, Family, Player, etc.)
                this.auth.logoutLocal();
            } else if (currentUser.role === 'Club Rep' && currentUser.regId && currentUser.jobPath !== jobPath) {
                // Right role but stale token from a different job
                this.auth.logoutLocal();
            }
        }

        // Subscribe (not snapshot) so clicking a role-menu item that differs only in
        // ?step= while already on the wizard actually moves to the requested step.
        // Resolve against the full step list (not activeSteps) so deep links land
        // correctly even when a step becomes enabled later (metadata-gated).
        this.route.queryParamMap
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe(params => {
                const stepParam = params.get('step');
                if (stepParam && this.steps().some(s => s.id === stepParam)) {
                    this._currentStepId.set(stepParam);
                }
            });

        // Deep-link past login: hydrate cross-cutting state (payment config, waiver,
        // refund policy, contact info) so payment/review steps render correctly even
        // when the user bypasses login + teams. Teams-step does its own richer load
        // when mounted, so arriving at step=teams also gets this state populated.
        if (this._currentStepId() !== 'login' && currentUser?.role === 'Club Rep') {
            this.hydrateForDeepLink();
        }
    }

    /** Fetch teams metadata once and apply to wizard state — deep-link entry path. */
    private hydrateForDeepLink(): void {
        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: meta => this.state.applyTeamsMetadata(meta),
                error: () => { /* Interceptor surfaces errors; each step falls back to its own load. */ },
            });
    }

    // ── Navigation ──────────────────────────────────────────────────
    back(): void {
        const active = this.activeSteps();
        const idx = this.currentIndex();
        if (idx > 0) {
            this._currentStepId.set(active[idx - 1].id);
        }
    }

    next(): void {
        const active = this.activeSteps();
        const idx = this.currentIndex();
        if (idx >= active.length - 1) return;

        if (this.currentStepId() === 'review') {
            this.finish();
            return;
        }

        this._currentStepId.set(active[idx + 1].id);
    }

    goToStep(stepIndex: number): void {
        const active = this.activeSteps();
        if (stepIndex >= 0 && stepIndex < active.length && stepIndex < this.currentIndex()) {
            this._currentStepId.set(active[stepIndex].id);
        }
    }

    /**
     * Advance to the first step after login — skipping any already-completed step.
     * A returning club rep with BWaiverSigned3=true skips straight past waivers to teams;
     * the step itself stays visible in the indicator and can be revisited via Back or
     * the step indicator (it renders read-only in that state).
     */
    private advancePastLogin(): void {
        const active = this.activeSteps();
        if (active.length <= 1) return;
        let nextIdx = 1;
        if (active[nextIdx]?.id === 'waivers' && this.state.waiverAccepted() && nextIdx + 1 < active.length) {
            nextIdx += 1;
        }
        this._currentStepId.set(active[nextIdx].id);
    }

    finish(): void {
        this.auth.logoutLocal();
        const jobPath = this.state.jobPath();
        if (jobPath) {
            this.router.navigate(['/', jobPath]);
        }
    }

    // ── Login callbacks ─────────────────────────────────────────────
    onLoginSuccess(result: { availableClubs: unknown[]; clubName: string | null }): void {
        // Reject accounts with no clubs (not a club rep)
        if (!result.availableClubs?.length) {
            this.auth.logoutLocal();
            return; // login step will show error via its own 403/empty handling
        }

        this.state.resetForRepSwitch();
        this.state.clubRep.setAvailableClubs(result.availableClubs as import('@core/api').ClubRepClubDto[]);
        this.state.clubRep.setSelectedClub(result.clubName);

        const clubName = result.clubName;
        const jobPath = this.state.jobPath();
        if (clubName && jobPath) {
            this.initAndAdvance(clubName, jobPath);
        } else {
            // Multi-club rep — need club picker before advancing
            // For now, use first club
            const firstClub = (result.availableClubs as import('@core/api').ClubRepClubDto[])[0]?.clubName;
            if (firstClub && jobPath) {
                this.state.clubRep.setSelectedClub(firstClub);
                this.initAndAdvance(firstClub, jobPath);
            }
        }
    }

    onRegistrationSuccess(result: { availableClubs: unknown[]; clubName?: string | null }): void {
        if (!result.availableClubs?.length) {
            this.auth.logoutLocal();
            return;
        }

        this.state.resetForRepSwitch();
        this.state.clubRep.setAvailableClubs(result.availableClubs as import('@core/api').ClubRepClubDto[]);

        const clubName = result.clubName ?? (result.availableClubs as import('@core/api').ClubRepClubDto[])[0]?.clubName ?? null;
        const jobPath = this.state.jobPath();
        if (clubName && jobPath) {
            this.state.clubRep.setSelectedClub(clubName);
            this.initAndAdvance(clubName, jobPath);
        }
    }

    /** Call initialize-registration to get a job-scoped Phase 2 token, then advance. */
    private initAndAdvance(clubName: string, jobPath: string): void {
        this.teamReg.initializeRegistration(clubName, jobPath)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    // Pre-fetch metadata to seed waiver state before step navigation.
                    // If the club rep already signed, waivers step will be skipped.
                    this.teamReg.getTeamsMetadata().pipe(takeUntilDestroyed(this.destroyRef))
                        .subscribe({
                            next: (meta) => {
                                this.state.applyTeamsMetadata(meta);
                                this.advancePastLogin();
                            },
                            error: () => {
                                this.advancePastLogin();
                            },
                        });
                },
                error: () => {
                    // Interceptor safety net handles the toast.
                    // Return to login — don't advance with broken token.
                    this.auth.logoutLocal();
                    this._currentStepId.set('login');
                },
            });
    }
}
