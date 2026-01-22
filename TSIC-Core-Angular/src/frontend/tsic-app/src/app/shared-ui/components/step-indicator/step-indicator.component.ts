import { Component, input } from '@angular/core';

/**
 * Shared step indicator component for multi-step wizards.
 * Supports both fixed step counts (Team: 4 steps) and dynamic step counts (Player: 7-9 steps).
 * 
 * Usage:
 * <app-step-indicator [steps]="steps()" [currentIndex]="currentIndex()" />
 * 
 * - Displays circular badges with step numbers (or checkmark if completed)
 * - Connectors between steps
 * - Light/dark mode support via CSS variables
 * - Responsive design (hides labels on mobile)
 */

export interface StepDefinition {
    id: string;
    label: string;
    stepNumber: number;
}

@Component({
    selector: 'app-step-indicator',
    standalone: true,
    imports: [],
    templateUrl: './step-indicator.component.html',
    styleUrls: ['./step-indicator.component.scss']
})
export class StepIndicatorComponent {
    /**
     * Array of step definitions. Can be fixed (e.g., [step1, step2, step3, step4])
     * or dynamic with conditional filtering (e.g., exclude waivers if not required).
     */
    readonly steps = input.required<StepDefinition[]>();

    /**
     * Zero-based index of the current step in the steps array.
     * Example: 0 = first step, 1 = second step, etc.
     */
    readonly currentIndex = input.required<number>();

    /**
     * Check if a step has been completed (past steps).
     */
    isCompleted(stepIndex: number): boolean {
        return stepIndex < this.currentIndex();
    }

    /**
     * Check if a step is currently active (current step).
     */
    isActive(stepIndex: number): boolean {
        return stepIndex === this.currentIndex();
    }

    /**
     * Check if a step is in the future (not yet reached).
     */
    isFuture(stepIndex: number): boolean {
        return stepIndex > this.currentIndex();
    }
}
