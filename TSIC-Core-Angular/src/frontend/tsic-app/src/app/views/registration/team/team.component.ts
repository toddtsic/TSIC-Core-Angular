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
import { formatCurrency } from '@views/registration/shared/utils/format.util';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

@Component({
    selector: 'app-registration-team',
    standalone: true,
    imports: [
        WizardShellComponent,
        TeamLoginStepComponent,
        TeamTeamsStepComponent,
        TeamPaymentStepV2Component,
        TeamReviewStepComponent,
    ],
    template: `
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
    styles: [`:host { display: block; }`],
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

    private readonly _currentIndex = signal(0);
    readonly currentIndex = this._currentIndex.asReadonly();

    // ── Step definitions ──────────────────────────────────────────────
    readonly steps = computed<WizardStepDef[]>(() => [
        { id: 'login', label: 'Login', enabled: true },
        { id: 'teams', label: 'Teams', enabled: true },
        { id: 'payment', label: 'Payment', enabled: true },
        { id: 'review', label: 'Review', enabled: true },
    ]);

    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));

    readonly currentStepId = computed(() => {
        const active = this.activeSteps();
        const idx = Math.min(this._currentIndex(), active.length - 1);
        return active[idx]?.id ?? 'login';
    });

    readonly shellConfig = computed<WizardShellConfig>(() => ({
        title: 'Team Registration',
        theme: 'team',
        badge: this.state.clubRep.selectedClub(),
    }));

    // ── canContinue ─────────────────────────────────────────────────
    readonly canContinue = computed(() => {
        switch (this.currentStepId()) {
            case 'login': return false; // login step has own CTAs
            case 'teams': return true; // validation in proceedToPayment()
            case 'payment': return !this.state.teamPayment.hasBalance();
            case 'review': return true;
            default: return false;
        }
    });

    readonly showContinue = computed(() => {
        const id = this.currentStepId();
        return id !== 'login';
    });

    readonly continueLabel = computed(() => {
        switch (this.currentStepId()) {
            case 'teams': return 'Proceed to Payment';
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
    }

    // ── Navigation ──────────────────────────────────────────────────
    back(): void {
        if (this._currentIndex() > 0) {
            this._currentIndex.set(this._currentIndex() - 1);
        }
    }

    next(): void {
        const active = this.activeSteps();
        const idx = this._currentIndex();
        if (idx >= active.length - 1) return;

        if (this.currentStepId() === 'review') {
            this.finish();
            return;
        }

        this._currentIndex.set(idx + 1);
    }

    goToStep(stepIndex: number): void {
        if (stepIndex < this._currentIndex()) {
            this._currentIndex.set(stepIndex);
        }
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
                    this._currentIndex.set(1); // advance to teams step
                },
                error: () => {
                    // Interceptor safety net handles the toast.
                    // Return to login — don't advance with broken token.
                    this.auth.logoutLocal();
                    this._currentIndex.set(0);
                },
            });
    }
}
