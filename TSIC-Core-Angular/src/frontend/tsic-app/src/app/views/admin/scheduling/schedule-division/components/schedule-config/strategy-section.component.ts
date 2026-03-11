/**
 * StrategySectionComponent — Stepper Section ⑤
 *
 * Event-level game placement order (Horizontal/Vertical) and rest pattern (0/1/2 game break).
 * Extracted from event-summary-panel to reduce bloat.
 */

import { Component, ChangeDetectionStrategy, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

import type { DivisionStrategyEntry } from '@core/api';

@Component({
    selector: 'app-strategy-section',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './strategy-section.component.html',
    styleUrl: './strategy-section.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class StrategySectionComponent {
    // ── Inputs ──
    readonly strategies = input<DivisionStrategyEntry[]>([]);
    readonly isSaving = input(false);
    readonly isExpanded = input(false);

    // ── Outputs ──
    readonly toggleExpanded = output<void>();
    readonly saveRequested = output<{ placement: number; gapPattern: number }>();

    // ── Local state ──
    readonly localPlacement = signal<number | null>(null);
    readonly localGapPattern = signal<number | null>(null);

    // ── Computed ──

    /** Uniform placement value across all strategies, or null if mixed */
    readonly uniformPlacement = computed((): number | null => {
        const strats = this.strategies();
        if (strats.length === 0) return null;
        const first = strats[0].placement.toString();
        return strats.every(s => s.placement.toString() === first) ? Number(first) : null;
    });

    /** Uniform gap pattern value across all strategies, or null if mixed */
    readonly uniformGap = computed((): number | null => {
        const strats = this.strategies();
        if (strats.length === 0) return null;
        const first = strats[0].gapPattern.toString();
        return strats.every(s => s.gapPattern.toString() === first) ? Number(first) : null;
    });

    /** Effective placement: local override → server value → default */
    readonly effectivePlacement = computed(() =>
        this.localPlacement() ?? this.uniformPlacement() ?? 0
    );

    /** Effective gap pattern: local override → server value → default */
    readonly effectiveGapPattern = computed(() =>
        this.localGapPattern() ?? this.uniformGap() ?? 1
    );

    /** True when effective strategy differs from server/default values */
    readonly strategyDirty = computed(() => {
        if (this.localPlacement() === null && this.localGapPattern() === null) return false;
        const sp = this.uniformPlacement() ?? 0;
        const sg = this.uniformGap() ?? 1;
        return this.effectivePlacement() !== sp || this.effectiveGapPattern() !== sg;
    });

    // ── Actions ──

    onSave(): void {
        this.saveRequested.emit({
            placement: this.effectivePlacement(),
            gapPattern: this.effectiveGapPattern()
        });
    }

    onCancel(): void {
        this.localPlacement.set(null);
        this.localGapPattern.set(null);
        this.toggleExpanded.emit();
    }
}
