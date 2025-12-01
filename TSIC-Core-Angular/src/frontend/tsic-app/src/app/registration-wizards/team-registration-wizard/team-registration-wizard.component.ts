import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { TwActionBarComponent } from '../shared/tw-action-bar.component';
import { FormFieldDataService, SelectOption } from '../../core/services/form-field-data.service';
import { JobService } from '../../core/services/job.service';
import { JobContextService } from '../../core/services/job-context.service';

@Component({
    selector: 'app-team-registration-wizard',
    templateUrl: './team-registration-wizard.component.html',
    styleUrls: ['./team-registration-wizard.component.scss'],
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, TwActionBarComponent]
})
export class TeamRegistrationWizardComponent implements OnInit {
    step = 1;
    hasClubRepAccount: boolean | null = null;
    stepLabels: Record<number, string> = {
        1: 'Login',
        2: 'Teams'
    };
    loginForm: FormGroup;
    registrationForm: FormGroup;
    private readonly route = inject(ActivatedRoute);
    private readonly jobService = inject(JobService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    statesOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');

    constructor(readonly fb: FormBuilder) {
        this.loginForm = this.fb.group({
            username: ['', Validators.required],
            password: ['', Validators.required]
        });
        this.registrationForm = this.fb.group({
            clubName: ['', Validators.required],
            firstName: ['', Validators.required],
            lastName: ['', Validators.required],
            street: ['', Validators.required],
            city: ['', Validators.required],
            state: ['', Validators.required],
            zip: ['', Validators.required],
            cellphone: ['', [Validators.required, Validators.pattern(/^[0-9-+\s()]+$/)]],
            username: ['', Validators.required],
            password: ['', [Validators.required, Validators.minLength(6)]],
            email: ['', [Validators.required, Validators.email]]
        });
    }

    ngOnInit(): void {
        // Get jobPath from JobContextService (extracted from URL)
        const jobPath = this.jobContext.jobPath();

        if (jobPath) {
            // Load job metadata and set JsonOptions for dropdowns
            this.jobService.fetchJobMetadata(jobPath).subscribe({
                next: (job) => {
                    this.fieldData.setJobOptions(job.jsonOptions);
                    // Job-specific dropdown options now available throughout the app
                },
                error: (err) => {
                    console.error('Failed to load job metadata:', err);
                    // Continue with static fallback options
                }
            });
        }
    }

    nextStep() {
        this.step++;
    }

    prevStep() {
        this.step--;
    }

    submitLogin() {
        if (this.loginForm.valid) {
            setTimeout(() => {
                this.nextStep();
            }, 300);
        }
    }

    submitRegistration() {
        if (this.registrationForm.valid) {
            setTimeout(() => {
                this.nextStep();
            }, 300);
        }
    }

    canContinue(): boolean {
        if (this.hasClubRepAccount === true) {
            return this.loginForm.valid;
        }
        if (this.hasClubRepAccount === false) {
            return this.registrationForm.valid;
        }
        return false;
    }

    continueLabel(): string {
        return this.step === 1 ? 'Continue' : 'Finish';
    }

    showContinueButton(): boolean {
        if (this.step === 1 && this.hasClubRepAccount === null) {
            return false;
        }
        return true;
    }

    currentStepLabel(): string {
        return this.stepLabels[this.step] || '';
    }
}