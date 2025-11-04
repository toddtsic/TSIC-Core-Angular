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
import { LoginStepComponent } from './steps/login.component';
import { AuthService } from '../../core/services/auth.service';

export type StepId = 'start' | 'login' | 'edit-lookup' | 'players' | 'constraint' | 'teams' | 'forms' | 'review' | 'payment';

@Component({
    selector: 'app-player-registration-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, StartChoiceComponent, LoginStepComponent, EditLookupComponent, PlayerSelectionComponent, TeamSelectionComponent, ReviewComponent, ConstraintSelectionComponent, PlayerFormsComponent, PaymentComponent],
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
    private readonly allStepsEdit: StepId[] = ['start', 'edit-lookup', 'forms', 'review', 'payment'];
    private readonly allStepsNewUnauthed: StepId[] = ['start', 'login', 'players', 'constraint', 'teams', 'forms', 'review', 'payment'];
    private readonly allStepsNewAuthed: StepId[] = ['start', 'players', 'constraint', 'teams', 'forms', 'review', 'payment'];

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
        login: 'Family login',
        'edit-lookup': 'Edit lookup',
        players: 'Players',
        constraint: 'Constraint',
        teams: 'Teams',
        forms: 'Forms',
        review: 'Review',
        payment: 'Payment'
    };

    ngOnInit(): void {
        // Initialize jobPath from route
        const jobPath = this.route.snapshot.paramMap.get('jobPath') ?? '';
        this.state.jobPath.set(jobPath);

        // Initialize from query params (mode and step) for deep-linking
        const qpMode = this.route.snapshot.queryParamMap.get('mode') as StartChoice | null;
        if (qpMode === 'new' || qpMode === 'edit' || qpMode === 'parent') {
            this.state.startMode.set(qpMode);
            // Default to the first post-start step
            this.currentIndex.set(1);
        }

        const qpStep = this.route.snapshot.queryParamMap.get('step');
        if (qpStep) {
            // If deep-linking to 'players' but user isn't authed, redirect to 'login' step
            const user = this.auth.currentUser();
            const authed = !!user;
            const desired = (!authed && qpStep === 'players') ? 'login' : (qpStep as StepId);
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
        if (choice === 'edit') {
            // Jump directly to Edit Lookup
            const idx = this.steps().indexOf('edit-lookup');
            const newIdx = idx >= 0 ? idx : 1;
            this.currentIndex.set(newIdx);
            // reflect in URL for deep-linkability
            this.router.navigate([], {
                relativeTo: this.route,
                queryParams: { mode: choice, step: 'edit-lookup' },
                queryParamsHandling: 'merge'
            });
            return;
        }

        // For 'new': if already authenticated, skip login, else go to login step
        const authed = !!this.auth.currentUser();
        const target: StepId = authed ? 'players' : 'login';
        const idx = this.steps().indexOf(target);
        const newIdx = idx >= 0 ? idx : 1;
        this.currentIndex.set(newIdx);
        // reflect in URL for deep-linkability
        this.router.navigate([], {
            relativeTo: this.route,
            queryParams: { mode: choice, step: target },
            queryParamsHandling: 'merge'
        });
    }
}
