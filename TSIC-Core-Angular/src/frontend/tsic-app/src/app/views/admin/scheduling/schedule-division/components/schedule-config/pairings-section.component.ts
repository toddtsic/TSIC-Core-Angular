/**
 * PairingsSectionComponent — Stepper Section ⑤ (Guaranteed Games)
 *
 * Shows per-agegroup game guarantee with inline editing.
 * Round counts are computed internally from the guarantee + team count —
 * the scheduler only sees agegroup names and their guaranteed game floor.
 */

import { Component, ChangeDetectionStrategy, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AgegroupWithDivisionsDto } from '@core/api';
import { contrastText, agTeamCount } from '../../../shared/utils/scheduling-helpers';

/** Payload emitted when the user wants to generate/regenerate pairings. */
export interface PairingsGenerateEvent {
    teamCounts: number[];
    roundsOverrides: Record<number, number>;
    forceRegenerate: boolean;
}

/** Payload emitted when the user saves guarantee changes. */
export interface GuaranteeSaveEvent {
    eventDefault: number | null;
    agegroupOverrides: Record<string, number | null>;
}

/** Row in the per-agegroup guarantee table. */
interface AgegroupRow {
    agegroupId: string;
    agegroupName: string;
    color: string | null;
    effectiveGuarantee: number;
    divisionCount: number;
    totalTeams: number;
    teamCounts: number[];
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
    readonly existingPairingRounds = input<Record<number, number>>({});
    readonly gameGuarantee = input<number | null>(null);
    readonly isExpanded = input(false);
    readonly isGenerating = input(false);
    readonly isSaving = input(false);

    // ── Outputs ──
    readonly toggleExpanded = output<void>();
    readonly generateRequested = output<PairingsGenerateEvent>();
    readonly guaranteeSaveRequested = output<GuaranteeSaveEvent>();

    // ── Local state: per-agegroup overrides ──
    readonly overrides = signal<Record<string, number>>({});

    /** Guarantee options: 2–8 games */
    readonly guaranteeOptions = [2, 3, 4, 5, 6, 7, 8];

    /** Contrast text helper for agegroup badge. */
    readonly contrastText = contrastText;

    // ── Computed: per-agegroup rows ──

    readonly agegroupRows = computed((): AgegroupRow[] => {
        const ags = this.agegroups();
        const guarantee = this.gameGuarantee();
        const ovr = this.overrides();

        return ags
            .filter(ag => ag.divisions.some(d => d.teamCount > 1))
            .map(ag => {
                const activeDivs = ag.divisions.filter(d => d.teamCount > 1);
                const tCnts = [...new Set(activeDivs.map(d => d.teamCount))];
                const effectiveGuarantee = ovr[ag.agegroupId]
                    ?? guarantee
                    ?? Math.min(...tCnts.map(t => t - 1));
                return {
                    agegroupId: ag.agegroupId,
                    agegroupName: ag.agegroupName,
                    color: ag.color ?? null,
                    effectiveGuarantee,
                    divisionCount: activeDivs.length,
                    totalTeams: agTeamCount(ag),
                    teamCounts: tCnts
                };
            });
    });

    /** Are there any team sizes that still need pairings? */
    readonly hasMissing = computed(() => this.missingPairingTCnts().length > 0);

    /** All pairings generated. */
    readonly allComplete = computed(() => {
        const missing = new Set(this.missingPairingTCnts());
        const ags = this.agegroups();
        if (ags.length === 0) return false;
        for (const ag of ags) {
            for (const div of ag.divisions) {
                if (div.teamCount > 1 && missing.has(div.teamCount)) return false;
            }
        }
        return true;
    });

    /** True when any agegroup guarantee differs from the event default. */
    readonly isDirty = computed((): boolean => {
        const ovr = this.overrides();
        const guarantee = this.gameGuarantee();
        for (const [, val] of Object.entries(ovr)) {
            if (val !== guarantee) return true;
        }
        return false;
    });

    /** Collapsed status label. */
    readonly summaryLabel = computed((): string => {
        const rows = this.agegroupRows();
        if (rows.length === 0) return 'No divisions';
        const guarantee = this.gameGuarantee();
        if (this.hasMissing()) return 'Pairings needed';
        if (guarantee != null && guarantee > 0) return `${guarantee}-game guarantee`;
        return 'All generated';
    });

    // ── Helpers ──

    /**
     * Compute rounds needed to meet guarantee for a team count.
     * Mirrors backend ComputeRoundCount exactly.
     */
    private computeRoundCount(tCnt: number, guarantee: number | null): number {
        const fullRr = tCnt % 2 === 0 ? tCnt - 1 : tCnt;
        if (guarantee == null || guarantee <= 0) return fullRr;
        const needed = tCnt % 2 === 0
            ? guarantee
            : guarantee + 1;
        return Math.max(1, Math.min(needed, fullRr));
    }

    // ── Actions ──

    onToggle(): void {
        this.toggleExpanded.emit();
    }

    /** Update guarantee for a specific agegroup. */
    setGuarantee(agegroupId: string, value: number): void {
        const current = this.overrides();
        this.overrides.set({ ...current, [agegroupId]: value });
    }

    /** Get the effective guarantee for an agegroup row. */
    getEffectiveValue(row: AgegroupRow): number {
        return this.overrides()[row.agegroupId] ?? row.effectiveGuarantee;
    }

    /** Save guarantee changes. */
    onSave(): void {
        const ovr = this.overrides();
        const agegroupOverrides: Record<string, number | null> = {};
        const guarantee = this.gameGuarantee();

        for (const [agId, val] of Object.entries(ovr)) {
            // Only send overrides that differ from event default
            if (val !== guarantee) {
                agegroupOverrides[agId] = val;
            }
        }

        this.guaranteeSaveRequested.emit({
            eventDefault: guarantee,
            agegroupOverrides
        });
    }

    /** Generate all missing pairings at guarantee-based round counts. */
    onGenerate(): void {
        const missing = new Set(this.missingPairingTCnts());
        if (missing.size === 0) return;

        const guarantee = this.gameGuarantee();
        const teamCounts = [...missing];
        const overrides: Record<number, number> = {};
        for (const tCnt of teamCounts) {
            overrides[tCnt] = this.computeRoundCount(tCnt, guarantee);
        }

        this.generateRequested.emit({
            teamCounts,
            roundsOverrides: overrides,
            forceRegenerate: false
        });
    }
}
