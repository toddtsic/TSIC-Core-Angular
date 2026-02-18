import {
    ChangeDetectionStrategy, Component, OnInit, ViewChild,
    computed, inject, signal,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobContextService as InfraJobContext } from '@infrastructure/services/job-context.service';
import { TeamWizardStateService } from './state/team-wizard-state.service';
import { WizardShellComponent } from '../shared/wizard-shell/wizard-shell.component';
import { TeamLoginStepComponent } from './steps/login-step.component';
import { TeamTeamsStepComponent } from './steps/teams-step.component';
import { TeamPaymentStepV2Component } from './steps/payment-step.component';
import { TeamReviewStepComponent } from './steps/review-step.component';
import { formatCurrency } from '@views/registration/wizards/shared/utils/format.util';
import type { WizardStepDef, WizardShellConfig } from '../shared/types/wizard-shell.types';

@Component({
    selector: 'app-team-wizard-v2',
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
      (continue)="next()">
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

        // Handle refresh: if user already has Phase 2 token, skip login
        if (this.auth.hasSelectedRole()) {
            const clubCount = this.auth.getClubRepClubCount();
            if (clubCount >= 1) {
                this._currentIndex.set(1); // teams step
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

    finish(): void {
        const jobPath = this.state.jobPath();
        if (jobPath) {
            this.router.navigate(['/', jobPath]);
        }
    }

    // ── Login callbacks ─────────────────────────────────────────────
    onLoginSuccess(result: { availableClubs: unknown[]; clubName: string | null }): void {
        this.state.resetForRepSwitch();
        this.state.clubRep.setAvailableClubs(result.availableClubs as import('@core/api').ClubRepClubDto[]);
        this.state.clubRep.setSelectedClub(result.clubName);
        this._currentIndex.set(1); // advance to teams step
    }

    onRegistrationSuccess(result: { availableClubs: unknown[]; clubName?: string | null }): void {
        this.state.resetForRepSwitch();
        this.state.clubRep.setAvailableClubs(result.availableClubs as import('@core/api').ClubRepClubDto[]);
        this._currentIndex.set(1); // advance to teams step
    }
}
