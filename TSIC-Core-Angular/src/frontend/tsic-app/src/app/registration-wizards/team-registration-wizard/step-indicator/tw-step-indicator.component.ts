import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface WizardStep {
    label: string;
    stepNumber: number;
}

@Component({
    selector: 'app-tw-step-indicator',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './tw-step-indicator.component.html',
    styleUrls: ['./tw-step-indicator.component.scss']
})
export class TwStepIndicatorComponent {
    @Input() steps: WizardStep[] = [];
    @Input() currentStep: number = 1;

    isCompleted(stepNumber: number): boolean {
        return stepNumber < this.currentStep;
    }

    isActive(stepNumber: number): boolean {
        return stepNumber === this.currentStep;
    }

    isFuture(stepNumber: number): boolean {
        return stepNumber > this.currentStep;
    }
}
