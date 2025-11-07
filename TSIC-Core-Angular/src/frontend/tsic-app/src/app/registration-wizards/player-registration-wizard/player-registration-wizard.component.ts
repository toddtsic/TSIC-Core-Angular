import { Component, OnInit, computed, inject, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { PlayerSelectionComponent } from './steps/player-selection.component';
import { TeamSelectionComponent } from './steps/team-selection.component';
import { ReviewComponent } from './steps/review.component';
import { ConstraintSelectionComponent } from './steps/constraint-selection.component';
import { PlayerFormsComponent } from './steps/player-forms.component';
import { PaymentComponent } from './steps/payment.component';
import { RegistrationWizardService } from './registration-wizard.service';
// Start step retired; StartChoiceComponent removed from flow
import { EditLookupComponent } from './steps/edit-lookup.component';
import { FamilyCheckStepComponent } from './steps/family-check.component';
import { AuthService } from '../../core/services/auth.service';
import { WizardThemeDirective } from '../../shared/directives/wizard-theme.directive';

export type StepId = 'family-check' | 'edit-lookup' | 'players' | 'constraint' | 'teams' | 'forms' | 'review' | 'payment';

@Component({
    selector: 'app-player-registration-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, WizardThemeDirective, FamilyCheckStepComponent, EditLookupComponent, PlayerSelectionComponent, TeamSelectionComponent, ReviewComponent, ConstraintSelectionComponent, PlayerFormsComponent, PaymentComponent],
    templateUrl: './player-registration-wizard.component.html',
    styleUrls: ['./player-registration-wizard.component.scss'],
    host: {}
})
export class PlayerRegistrationWizardComponent implements OnInit {
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    readonly state = inject(RegistrationWizardService);
    private readonly auth = inject(AuthService);

    // Steps managed by stable IDs for deep-linking
    // Note: 'constraint' may be skipped in a future enhancement if job has no constraint.
    // Start step retired; Family Check now offers CTAs to proceed directly
    private readonly allStepsEdit: StepId[] = ['family-check', 'edit-lookup', 'forms', 'review', 'payment'];
    private readonly allStepsNewUnauthed: StepId[] = ['family-check', 'players', 'constraint', 'teams', 'forms', 'review', 'payment'];
    private readonly allStepsNewAuthed: StepId[] = ['family-check', 'players', 'constraint', 'teams', 'forms', 'review', 'payment'];

    // Current index into the computed steps array
    currentIndex = signal(0);

    steps = computed<StepId[]>(() => {
        try {
            const mode = this.state.startMode();
            const authed = !!this.auth.currentUser();
            let arr: StepId[];
            if (mode === 'edit') {
                arr = this.allStepsEdit;
            } else if (authed) {
                arr = this.allStepsNewAuthed;
            } else {
                arr = this.allStepsNewUnauthed;
            }
            return arr?.length ? arr : this.allStepsNewUnauthed;
        } catch (err) {
            console.error('[PRW] Error computing steps; fallback applied', err);
            return this.allStepsNewUnauthed;
        }
    });

    currentStepId = computed<StepId>(() => {
        const arr = this.steps();
        if (!arr?.length) return 'family-check';
        const safeIndex = Math.min(this.currentIndex(), arr.length - 1);
        return arr[safeIndex];
    });
    progressPercent = computed(() => Math.round(((this.currentIndex() + 1) / this.steps().length) * 100));

    // Only show an account badge when authenticated family user is present (via state.activeFamilyUser)

    readonly stepLabels: Record<StepId, string> = {
        'family-check': 'Family account?',
        'edit-lookup': 'Edit lookup',
        players: 'Players',
        constraint: 'Constraint',
        teams: 'Teams',
        forms: 'Forms',
        review: 'Review',
        payment: 'Payment'
    };

    private readonly _familyUserEffect = effect(() => {
        const fam = this.state.activeFamilyUser();
        let next: boolean | null = null;
        if (fam) next = this.state.startMode() === 'edit';
        if (this.state.existingRegistrationAvailable() !== next) {
            this.state.existingRegistrationAvailable.set(next);
        }
    }, { allowSignalWrites: true });

    ngOnInit(): void {
        // Ensure a clean wizard state each time this route is entered (does not affect auth)
        this.state.reset();
        // Job path
        const jobPath = this.route.snapshot.paramMap.get('jobPath') ?? '';
        this.state.jobPath.set(jobPath);

        // No localStorage fallback: unauthenticated users must choose explicitly on Family Check.

        // Apply query params (mode + step)
        const qpMode = this.route.snapshot.queryParamMap.get('mode') as 'new' | 'edit' | 'parent' | null;
        if (qpMode === 'new' || qpMode === 'edit' || qpMode === 'parent') {
            this.state.startMode.set(qpMode);
        }
        const qpStep = this.route.snapshot.queryParamMap.get('step');
        let hadStepFromQuery = false;
        if (qpStep) {
            const authed = !!this.auth.currentUser();
            const desired: StepId = (!authed && qpStep === 'players') ? 'family-check' : (qpStep as StepId);
            const targetIndex = this.steps().indexOf(desired);
            if (targetIndex >= 0) {
                this.currentIndex.set(targetIndex);
                hadStepFromQuery = true;
            }
        }

        // Auto-advance: if we have a stored family account AND are authenticated, jump straight to players
        if (!hadStepFromQuery && !!this.auth.currentUser()) {
            const playersIdx = this.steps().indexOf('players');
            if (playersIdx >= 0) this.currentIndex.set(playersIdx);
        }
    }

    next(): void {
        this.currentIndex.update(i => Math.min(i + 1, this.steps().length - 1));
    }

    back(): void {
        this.currentIndex.update(i => Math.max(0, i - 1));
    }

    // Start step removed; branching handled via Family Check CTAs and direct deep-links
}
