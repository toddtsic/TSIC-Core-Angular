import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TeamsStepComponent } from './teams-step/teams-step.component';
import { ClubTeamManagementComponent } from './club-team-management/club-team-management.component';
import { TwActionBarComponent } from './action-bar/tw-action-bar.component';
import { TwStepIndicatorComponent, WizardStep } from './step-indicator/tw-step-indicator.component';
import { ClubRepLoginStepComponent, LoginStepResult } from './login-step/club-rep-login-step.component';
import { FormFieldDataService } from '@infrastructure/services/form-field-data.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { AuthService } from '@infrastructure/services/auth.service';
import type { ClubRepClubDto } from '@core/api';

@Component({
    selector: 'app-team-registration-wizard',
    templateUrl: './team-registration-wizard.component.html',
    styleUrls: ['./team-registration-wizard.component.scss'],
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, FormsModule, TeamsStepComponent, ClubTeamManagementComponent, TwActionBarComponent, TwStepIndicatorComponent, ClubRepLoginStepComponent]
})
export class TeamRegistrationWizardComponent implements OnInit {
    step = 1;
    clubName: string | null = null;
    availableClubs: ClubRepClubDto[] = [];
    selectedClub: string | null = null;
    hasTeamsInLibrary = false;
    showClubSelectionModal = false;
    inlineError: string | null = null;

    stepLabels: Record<number, string> = {
        1: 'Login',
        2: 'Manage Club Teams',
        3: 'Register Teams',
        4: 'Payment',
        5: 'Confirmation'
    };

    wizardSteps: WizardStep[] = [
        { stepNumber: 1, label: 'Login' },
        { stepNumber: 2, label: 'Manage Teams' },
        { stepNumber: 3, label: 'Register' },
        { stepNumber: 4, label: 'Payment' },
        { stepNumber: 5, label: 'Confirmation' }
    ];

    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly jobService = inject(JobService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    readonly authService = inject(AuthService);

    ngOnInit(): void {
        // Get jobPath from route params using service
        const jobPath = this.jobContext.resolveFromRoute(this.route);

        if (jobPath) {
            // Load job metadata and set JsonOptions for dropdowns
            this.jobService.fetchJobMetadata(jobPath).subscribe({
                next: (job) => {
                    this.fieldData.setJobOptions(job.jsonOptions);
                },
                error: (err) => {
                    console.error('Failed to load job metadata:', err);
                }
            });
        }
    }

    handleLoginSuccess(result: LoginStepResult): void {
        this.availableClubs = result.availableClubs;

        // Auto-select if only one club, otherwise require selection
        if (result.availableClubs.length === 1) {
            this.selectedClub = result.availableClubs[0].clubName;
            this.clubName = result.availableClubs[0].clubName;
        } else {
            this.selectedClub = null;
        }

        // Show modal for club confirmation/selection
        this.showClubSelectionModal = true;
    }

    handleRegistrationSuccess(result: LoginStepResult): void {
        this.clubName = result.clubName;
        this.availableClubs = result.availableClubs;
        this.step = 2;
    }

    goBackToStep1() {
        this.step = 1;
    }

    prevStep() {
        this.step--;
    }

    selectClub(clubName: string): void {
        this.selectedClub = clubName;
        this.clubName = clubName;
    }

    continueFromStep1(): void {
        if (!this.selectedClub) {
            this.inlineError = 'Please select a club before continuing.';
            return;
        }
        this.clubName = this.selectedClub;
        this.inlineError = null;
        this.showClubSelectionModal = false;
        this.step = 2;
    }

    cancelClubSelection(): void {
        this.showClubSelectionModal = false;
        this.selectedClub = null;
        this.inlineError = null;
    }

    onTeamsLoaded(count: number): void {
        this.hasTeamsInLibrary = count > 0;
    }

    goToTeamsStep(): void {
        this.step = 3;
    }
}