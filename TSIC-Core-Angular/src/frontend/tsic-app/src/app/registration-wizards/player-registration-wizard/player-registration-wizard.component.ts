import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { PlayerSelectionComponent } from './steps/player-selection.component';
import { TeamSelectionComponent } from './steps/team-selection.component';
import { ReviewComponent } from './steps/review.component';
import { ConstraintSelectionComponent } from './steps/constraint-selection.component';
import { PlayerFormsComponent } from './steps/player-forms.component';
import { PaymentComponent } from './steps/payment.component';
import { RegistrationWizardService } from './registration-wizard.service';
import { StartChoiceComponent, StartChoice } from './steps/start-choice.component';
import { EditLookupComponent } from './steps/edit-lookup.component';
import { FamilyCheckStepComponent } from './steps/family-check.component';
import { AuthService } from '../../core/services/auth.service';

export type StepId = 'start' | 'family-check' | 'edit-lookup' | 'players' | 'constraint' | 'teams' | 'forms' | 'review' | 'payment';

@Component({
    selector: 'app-player-registration-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, StartChoiceComponent, FamilyCheckStepComponent, EditLookupComponent, PlayerSelectionComponent, TeamSelectionComponent, ReviewComponent, ConstraintSelectionComponent, PlayerFormsComponent, PaymentComponent],
    templateUrl: './player-registration-wizard.component.html',
    styleUrls: ['./player-registration-wizard.component.scss']
})
export class PlayerRegistrationWizardComponent implements OnInit {
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    readonly state = inject(RegistrationWizardService);
    private readonly auth = inject(AuthService);

    // Steps managed by stable IDs for deep-linking
    // Note: 'constraint' may be skipped in a future enhancement if job has no constraint.
    // Reordered so 'family-check' is always first (Step 1), 'start' becomes Step 2
    private readonly allStepsEdit: StepId[] = ['family-check', 'start', 'edit-lookup', 'forms', 'review', 'payment'];
    private readonly allStepsNewUnauthed: StepId[] = ['family-check', 'start', 'players', 'constraint', 'teams', 'forms', 'review', 'payment'];
    private readonly allStepsNewAuthed: StepId[] = ['family-check', 'start', 'players', 'constraint', 'teams', 'forms', 'review', 'payment'];

    // Current index into the computed steps array
    currentIndex = signal(0);

    steps = computed<StepId[]>(() => {
        const mode = this.state.startMode();
        // make reactive to auth changes
        const user = this.auth.currentUser();
        const authed = !!user;
        if (mode === 'edit') return this.allStepsEdit;
        // default when mode is null or 'new' or 'parent'
        return authed ? this.allStepsNewAuthed : this.allStepsNewUnauthed;
    });

    currentStepId = computed<StepId>(() => this.steps()[Math.min(this.currentIndex(), this.steps().length - 1)]);
    progressPercent = computed(() => Math.round(((this.currentIndex() + 1) / this.steps().length) * 100));

    // Labels for steps used in the header indicator
    readonly stepLabels: Record<StepId, string> = {
        start: 'Start',
        'family-check': 'Family account?',
        'edit-lookup': 'Edit lookup',
        players: 'Players',
        constraint: 'Constraint',
        teams: 'Teams',
        forms: 'Forms',
        review: 'Review',
        payment: 'Payment'
    };

    ngOnInit(): void {
        // Force a clean unauthenticated state when entering the wizard
        this.auth.logoutLocal();
        // Initialize jobPath from route
        const jobPath = this.route.snapshot.paramMap.get('jobPath') ?? '';
        this.state.jobPath.set(jobPath);

        // Initialize from query params (mode and step) for deep-linking
        const qpMode = this.route.snapshot.queryParamMap.get('mode') as StartChoice | null;
        if (qpMode === 'new' || qpMode === 'edit' || qpMode === 'parent') {
            this.state.startMode.set(qpMode);
            // Keep index at 0 by default so Family Check is always first
        }

        const qpStep = this.route.snapshot.queryParamMap.get('step');
        if (qpStep) {
            // If deep-linking to 'players' but user isn't authed, redirect to 'family-check' step
            const user = this.auth.currentUser();
            const authed = !!user;
            const desired: StepId = (!authed && qpStep === 'players') ? 'family-check' : (qpStep as StepId);
            const targetIndex = this.steps().indexOf(desired);
            if (targetIndex >= 0) this.currentIndex.set(targetIndex);
        }
    }

    next(): void {
        this.currentIndex.update(i => Math.min(i + 1, this.steps().length - 1));
    }

    back(): void {
        this.currentIndex.update(i => Math.max(0, i - 1));
    }

    // Handle selection from StartChoice step
    onStartChoice(choice: StartChoice): void {
        this.state.startMode.set(choice);
        // After Family Check (now Step 1), Start (Step 2) should branch normally
        if (choice === 'edit') {
            const idx = this.steps().indexOf('edit-lookup');
            this.currentIndex.set(idx >= 0 ? idx : 2);
            this.router.navigate([], {
                relativeTo: this.route,
                queryParams: { mode: choice, step: 'edit-lookup' },
                queryParamsHandling: 'merge'
            });
            return;
        }

        if (choice === 'parent') {
            // Send user to Family Account wizard in edit mode and return to this Start step afterward
            const jobPath = this.state.jobPath();
            const returnUrl = `/${jobPath}/register-player?step=start`;
            this.router.navigate(['/tsic/family-account'], { queryParams: { mode: 'edit', returnUrl } });
            return;
        }

        // For 'new': proceed to players if authenticated; otherwise send back to family-check
        const authed = !!this.auth.currentUser();
        const target: StepId = authed ? 'players' : 'family-check';
        const idx = this.steps().indexOf(target);
        this.currentIndex.set(idx >= 0 ? idx : 2);
        this.router.navigate([], {
            relativeTo: this.route,
            queryParams: { mode: choice, step: target },
            queryParamsHandling: 'merge'
        });
    }
}
