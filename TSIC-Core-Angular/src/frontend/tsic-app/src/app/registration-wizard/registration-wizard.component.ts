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
import { AuthService } from '../core/services/auth.service';

@Component({
    selector: 'app-registration-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, StartChoiceComponent, EditLookupComponent, PlayerSelectionComponent, TeamSelectionComponent, ReviewComponent, ConstraintSelectionComponent, PlayerFormsComponent, PaymentComponent],
    templateUrl: './registration-wizard.component.html',
    styleUrls: ['./registration-wizard.component.scss']
})
export class RegistrationWizardComponent implements OnInit {
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    readonly state = inject(RegistrationWizardService);
    private readonly auth = inject(AuthService);

    // Dynamic steps:
    // 1 = start-choice
    // If mode==='edit': 2=edit-lookup, 3=forms, 4=review, 5=payment
    // If mode==='new'|'parent': 2=players, 3=constraint, 4=teams, 5=forms, 6=review, 7=payment
    currentStep = signal(1);
    totalSteps = computed(() => {
        const mode = this.state.startMode();
        if (!mode) return 1;
        return mode === 'edit' ? 5 : 7;
    });

    progressPercent = computed(() => Math.round((this.currentStep() / this.totalSteps()) * 100));

    ngOnInit(): void {
        // Initialize jobPath from route
        const jobPath = this.route.snapshot.paramMap.get('jobPath') ?? '';
        this.state.jobPath.set(jobPath);

        // Initialize start mode from query param (optional)
        const qpMode = this.route.snapshot.queryParamMap.get('mode') as StartChoice | null;
        if (qpMode === 'new' || qpMode === 'edit' || qpMode === 'parent') {
            this.state.startMode.set(qpMode);
            this.currentStep.set(2);
        }
    }

    next(): void {
        if (this.currentStep() < this.totalSteps()) {
            this.currentStep.update(s => s + 1);
        }
    }

    back(): void {
        this.currentStep.update(s => Math.max(1, s - 1));
    }

    // Handle selection from StartChoice step
    onStartChoice(choice: StartChoice): void {
        this.state.startMode.set(choice);
        if (choice === 'edit') {
            // Move to edit-lookup inside this wizard
            this.currentStep.set(2);
            return;
        }

        // For 'new' or 'parent' ensure user has a family account; if not, route to the family wizard
        const isAuthed = this.auth.isAuthenticated();
        if (!isAuthed) {
            const returnUrl = `/${this.state.jobPath()}/register-player?mode=${choice}`;
            this.router.navigate(['/tsic/family-account'], { queryParams: { returnUrl } });
            return;
        }

        // Otherwise proceed to Players step
        this.currentStep.set(2);
    }
}
