import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { TwActionBarComponent } from '../shared/tw-action-bar.component';

@Component({
    selector: 'app-team-registration-wizard',
    templateUrl: './team-registration-wizard.component.html',
    styleUrls: ['./team-registration-wizard.component.scss'],
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, TwActionBarComponent]
})
export class TeamRegistrationWizardComponent {
    step = 1;
    hasClubRepAccount: boolean | null = null;
    stepLabels: Record<number, string> = {
        1: 'Login',
        2: 'Teams'
    };
    loginForm: FormGroup;
    registrationForm: FormGroup;

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