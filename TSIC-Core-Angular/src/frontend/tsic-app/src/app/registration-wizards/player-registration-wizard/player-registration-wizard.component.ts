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
import { StartChoiceComponent, StartChoice } from './steps/start-choice.component';
import { EditLookupComponent } from './steps/edit-lookup.component';
import { FamilyCheckStepComponent } from './steps/family-check.component';
import { AuthService } from '../../core/services/auth.service';
import { WizardThemeDirective } from '../../shared/directives/wizard-theme.directive';

export type StepId = 'start' | 'family-check' | 'edit-lookup' | 'players' | 'constraint' | 'teams' | 'forms' | 'review' | 'payment';

@Component({
    selector: 'app-player-registration-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, WizardThemeDirective, StartChoiceComponent, FamilyCheckStepComponent, EditLookupComponent, PlayerSelectionComponent, TeamSelectionComponent, ReviewComponent, ConstraintSelectionComponent, PlayerFormsComponent, PaymentComponent],
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
    // Reordered so 'family-check' is always first (Step 1), 'start' becomes Step 2
    private readonly allStepsEdit: StepId[] = ['family-check', 'start', 'edit-lookup', 'forms', 'review', 'payment'];
    private readonly allStepsNewUnauthed: StepId[] = ['family-check', 'start', 'players', 'constraint', 'teams', 'forms', 'review', 'payment'];
    private readonly allStepsNewAuthed: StepId[] = ['family-check', 'start', 'players', 'constraint', 'teams', 'forms', 'review', 'payment'];

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
            if (!arr || arr.length === 0) {
                console.warn('[PRW] Steps array unexpectedly empty – falling back to unauth new flow');
                return this.allStepsNewUnauthed;
            }
            return arr;
        } catch (err) {
            console.error('[PRW] Error computing steps; fallback applied', err);
            return this.allStepsNewUnauthed;
        }
    });

    currentStepId = computed<StepId>(() => {
        const arr = this.steps();
        if (!arr || arr.length === 0) return 'family-check';
        const idx = Math.min(this.currentIndex(), arr.length - 1);
        return arr[idx];
    });
    progressPercent = computed(() => Math.round(((this.currentIndex() + 1) / this.steps().length) * 100));

    // Lightweight visual hint: show family account username if present (until a FamilyUser is selected)
    familyAccountUsername = signal<string>('');

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
        // Reinstate unconditional local logout on entry:
        // Player registration wizard should always start from a clean auth slate to avoid
        // accidentally reusing a stale Phase 2 token tied to a prior registration/job.
        // Family login will re-hydrate Phase 1; selection inside the wizard will enrich as needed.
        this.auth.logoutLocal();
        // Initialize jobPath from route
        const jobPath = this.route.snapshot.paramMap.get('jobPath') ?? '';
        this.state.jobPath.set(jobPath);

        // Read last family login username to show as a visual cue
        try {
            const u = localStorage.getItem('last_username') || '';
            this.familyAccountUsername.set(u);
        } catch { /* ignore */ }

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
        // Reactive effect: when an active family user is set, determine if any existing registration
        // For now, placeholder logic: future enhancement will query backend summary endpoint.
        effect(() => {
            const fam = this.state.activeFamilyUser();
            if (!fam) {
                this.state.existingRegistrationAvailable.set(null);
                return;
            }
            // Placeholder heuristic: if startMode was previously 'edit', assume existing true.
            // Later: replace with API call or bootstrap summary check.
            const assumed = this.state.startMode() === 'edit';
            this.state.existingRegistrationAvailable.set(assumed);
        });
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
