import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Shared step indicator component for multi-step wizards.
 * Supports both fixed step counts (Team: 4 steps) and dynamic step counts (Player: 7-9 steps).
 *
 * Responsive behavior (handled internally — no external d-none/d-md-block needed):
 * - Desktop (≥768px): Full step indicator with circles + labels + connectors
 * - Mobile (<768px): Compact "Step X of Y — Label" + thin progress bar
 *
 * Usage:
 * <app-step-indicator [steps]="steps()" [currentIndex]="currentIndex()" />
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
    styleUrls: ['./step-indicator.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
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

    /** Progress percentage (0–100) for the mobile progress bar. */
    readonly progressPercent = computed(() => {
        const total = this.steps().length;
        return total > 0 ? ((this.currentIndex() + 1) / total) * 100 : 0;
    });

    /** Label of the current step for the mobile compact display. */
    readonly currentLabel = computed(() => {
        const steps = this.steps();
        const idx = this.currentIndex();
        return steps[idx]?.label ?? '';
    });

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
