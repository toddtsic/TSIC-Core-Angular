/**
 * BuildSectionComponent — Stepper Section ⑥
 *
 * Readiness summary + build launch point.
 * Shows prerequisite status and launches the auto-schedule config modal.
 */

import { Component, ChangeDetectionStrategy, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-build-section',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './build-section.component.html',
    styleUrl: './build-section.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BuildSectionComponent {
    // ── Inputs (prerequisite statuses) ──
    readonly fieldsComplete = input(false);
    readonly datesComplete = input(false);
    readonly pairingsComplete = input(false);
    readonly assignedFieldCount = input(0);
    readonly configuredCount = input(0);
    readonly totalAgegroups = input(0);
    readonly totalGames = input(0);
    readonly totalExpected = input(0);
    readonly completionPct = input(0);
    readonly hasGamesInScope = input(false);
    readonly isExecuting = input(false);
    readonly isExpanded = input(false);

    // ── Outputs ──
    readonly toggleExpanded = output<void>();
    readonly buildRequested = output<void>();

    // ── Computed ──

    readonly allPrerequisitesMet = computed(() =>
        this.fieldsComplete() && this.datesComplete() && this.pairingsComplete()
    );

    readonly summaryLabel = computed((): string => {
        if (this.totalGames() > 0 && this.completionPct() >= 100) {
            return `Complete · ${this.totalGames()} games`;
        }
        if (this.totalGames() > 0) {
            return `${this.totalGames()}/${this.totalExpected()} games`;
        }
        if (this.allPrerequisitesMet()) {
            return 'Ready to build';
        }
        const missing: string[] = [];
        if (!this.fieldsComplete()) missing.push('fields');
        if (!this.datesComplete()) missing.push('dates');
        if (!this.pairingsComplete()) missing.push('pairings');
        return `Needs: ${missing.join(', ')}`;
    });

    readonly isComplete = computed(() =>
        this.totalGames() > 0 && this.completionPct() >= 100
    );

    // ── Actions ──

    onToggle(): void {
        this.toggleExpanded.emit();
    }

    onBuild(): void {
        this.buildRequested.emit();
    }
}
