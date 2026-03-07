/**
 * PairingsSectionComponent — Stepper Section ⑤ (Pairings)
 *
 * Inline expandable section for configuring rounds per division size.
 * Shows each distinct team count across all divisions with a dropdown
 * to set the number of rounds (defaults to full round-robin).
 *
 * Supports both initial generation (missing pairings) and editing
 * rounds for already-generated pairings (delete + regenerate).
 */

import { Component, ChangeDetectionStrategy, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AgegroupWithDivisionsDto } from '@core/api';

/** Payload emitted when the user wants to generate/regenerate pairings. */
export interface PairingsGenerateEvent {
    teamCounts: number[];
    roundsOverrides: Record<number, number>;
    forceRegenerate: boolean;
}

/** Row in the rounds-per-size table. */
interface SizeRow {
    tCnt: number;
    fullRR: number;
    hasPairings: boolean;
    currentRounds: number | null;
}

@Component({
    selector: 'app-pairings-section',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './pairings-section.component.html',
    styleUrl: './pairings-section.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class PairingsSectionComponent {
    // ── Inputs ──
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly missingPairingTCnts = input<number[]>([]);
    /** Current rounds per tCnt from the DB (from prerequisite check). */
    readonly existingPairingRounds = input<Record<number, number>>({});
    readonly isExpanded = input(false);
    readonly isGenerating = input(false);

    // ── Outputs ──
    readonly toggleExpanded = output<void>();
    readonly generateRequested = output<PairingsGenerateEvent>();

    // ── Local state: user's desired rounds per tCnt ──
    readonly roundsConfig = signal<Record<number, number>>({});

    // ── Computed: distinct team sizes across all divisions ──

    readonly sizeRows = computed((): SizeRow[] => {
        const ags = this.agegroups();
        const missing = new Set(this.missingPairingTCnts());
        const existing = this.existingPairingRounds();
        const tCnts = new Set<number>();

        for (const ag of ags) {
            for (const div of ag.divisions) {
                if (div.teamCount > 1) {
                    tCnts.add(div.teamCount);
                }
            }
        }

        return [...tCnts]
            .sort((a, b) => a - b)
            .map(tCnt => ({
                tCnt,
                fullRR: this.fullRR(tCnt),
                hasPairings: !missing.has(tCnt),
                currentRounds: existing[tCnt] ?? null
            }));
    });

    /** Are there any team sizes that still need pairings? */
    readonly hasMissing = computed(() =>
        this.sizeRows().some(r => !r.hasPairings)
    );

    /** All pairings generated. */
    readonly allComplete = computed(() =>
        this.sizeRows().length > 0 && this.sizeRows().every(r => r.hasPairings)
    );

    /** True when any dropdown differs from the DB-stored rounds (or fullRR for missing). */
    readonly isDirty = computed((): boolean => {
        const config = this.roundsConfig();
        for (const row of this.sizeRows()) {
            const desired = config[row.tCnt];
            if (desired == null) continue; // not touched
            const baseline = row.currentRounds ?? this.fullRR(row.tCnt);
            if (desired !== baseline) return true;
        }
        return false;
    });

    /** Collapsed status label. */
    readonly summaryLabel = computed((): string => {
        const rows = this.sizeRows();
        if (rows.length === 0) return 'No divisions';
        const missing = rows.filter(r => !r.hasPairings);
        if (missing.length === 0) {
            // Show rounds from DB: "3×2rds · 4×3rds"
            const parts = rows.map(r => {
                const rounds = r.currentRounds ?? this.fullRR(r.tCnt);
                return `${r.tCnt}×${rounds}rd${rounds !== 1 ? 's' : ''}`;
            });
            return parts.join(' · ');
        }
        return `Missing for ${missing.map(r => r.tCnt).join(', ')} teams`;
    });

    // ── Helpers ──

    /** Full round-robin rounds for a given team count. */
    fullRR(tCnt: number): number {
        return tCnt % 2 === 0 ? tCnt - 1 : tCnt;
    }

    /** Array of options [1..fullRR] for the rounds dropdown. */
    roundsOptions(tCnt: number): number[] {
        const max = this.fullRR(tCnt);
        return Array.from({ length: max }, (_, i) => i + 1);
    }

    /** Effective rounds for a tCnt: user override → DB value → fullRR. */
    effectiveRounds(tCnt: number): number {
        return this.roundsConfig()[tCnt]
            ?? this.existingPairingRounds()[tCnt]
            ?? this.fullRR(tCnt);
    }

    /** Update rounds for a specific tCnt. */
    setRounds(tCnt: number, rounds: number): void {
        const current = this.roundsConfig();
        this.roundsConfig.set({ ...current, [tCnt]: rounds });
    }

    // ── Actions ──

    onToggle(): void {
        this.toggleExpanded.emit();
    }

    /** Save: generate missing + regenerate changed existing. */
    onSave(): void {
        const config = this.roundsConfig();
        const allRows = this.sizeRows();
        const teamCounts: number[] = [];
        const overrides: Record<number, number> = {};
        let needsForce = false;

        for (const row of allRows) {
            const desired = config[row.tCnt] ?? (row.currentRounds ?? this.fullRR(row.tCnt));

            if (!row.hasPairings) {
                // Missing — always generate
                teamCounts.push(row.tCnt);
                overrides[row.tCnt] = desired;
            } else if (config[row.tCnt] != null && config[row.tCnt] !== row.currentRounds) {
                // Existing but rounds changed — regenerate
                teamCounts.push(row.tCnt);
                overrides[row.tCnt] = desired;
                needsForce = true;
            }
        }

        if (teamCounts.length === 0) return;

        this.generateRequested.emit({
            teamCounts,
            roundsOverrides: overrides,
            forceRegenerate: needsForce
        });
    }
}
