import { Component, ChangeDetectionStrategy, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '@environments/environment';
import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import { contrastText, agTeamCount } from '../../../shared/utils/scheduling-helpers';
import type { AgegroupCanvasReadinessDto, DivisionStrategyEntry, GameDayDto, GameSummaryResponse } from '@core/api';

export type AgStage = 'unconfigured' | 'configured' | 'scheduled-partial' | 'scheduled-complete';

/** Formatted game day line for hero display. */
export interface GameDayLine {
    dayNumber: number;
    dow: string;
    dateFormatted: string;
    fieldCount: number;
    startTime: string;
    endTime: string;
    gsi: number;
    totalSlots: number;
}


@Component({
    selector: 'app-event-summary-panel',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink],
    templateUrl: './event-summary-panel.component.html',
    styleUrl: './event-summary-panel.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class EventSummaryPanelComponent {
    // ── Inputs ──
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly gameSummary = input<GameSummaryResponse | null>(null);
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});
    readonly strategies = input<DivisionStrategyEntry[]>([]);
    readonly assignedFieldCount = input(0);
    readonly hasGamesInScope = input(false);
    readonly scopeGameCount = input(0);
    readonly isExecuting = input(false);
    readonly isDeletingGames = input(false);
    readonly showDeleteConfirm = input(false);
    readonly isDevResetting = input(false);

    // ── Outputs ──
    readonly autoScheduleRequested = output<void>();
    readonly deleteRequested = output<void>();
    readonly deleteConfirmed = output<void>();
    readonly deleteCancelled = output<void>();
    readonly agegroupClicked = output<string>();
    readonly manageFieldsClicked = output<void>();
    readonly devResetConfirmed = output<void>();

    // ── Local state ──
    readonly deleteConfirmText = signal('');
    readonly showDevResetConfirm = signal(false);
    readonly devResetConfirmText = signal('');
    readonly isDevMode = !environment.production;

    // ── Helpers ──
    readonly contrastText = contrastText;
    readonly agTeamCount = agTeamCount;

    // ── Event-level computed ──

    readonly totalExpected = computed(() => {
        const summary = this.gameSummary();
        if (!summary) return 0;
        return summary.divisions.reduce((sum, d) => sum + d.expectedRrGames, 0);
    });

    readonly totalGames = computed(() => this.gameSummary()?.totalGames ?? 0);

    readonly completionPct = computed(() => {
        const expected = this.totalExpected();
        if (expected === 0) return 0;
        return Math.round((this.totalGames() / expected) * 100);
    });

    readonly configuredCount = computed(() =>
        Object.values(this.readinessMap()).filter(r => r.isConfigured).length
    );

    readonly totalAgegroups = computed(() => this.agegroups().length);

    /** True when there's nothing for dev reset to clear. */
    readonly isResetEmpty = computed(() =>
        this.configuredCount() === 0 && this.totalGames() === 0
    );

    /** True when agegroups exist but no fields are assigned to the event. */
    readonly hasNoFieldsAssigned = computed(() =>
        this.agegroups().length > 0 && this.assignedFieldCount() === 0
    );

    // ── Tournament-level game days (union of ALL dates across agegroups) ──

    readonly eventGameDays = computed((): GameDayLine[] => {
        const map = this.readinessMap();
        const configured = Object.values(map).filter(r => r.isConfigured);
        if (configured.length === 0) return [];

        // Aggregate ALL unique dates across ALL configured agegroups
        const dateMap = new Map<string, GameDayDto>(); // ISO date key → GameDayDto
        for (const r of configured) {
            if (!r.gameDays) continue;
            for (const gd of r.gameDays) {
                const dateKey = gd.date.substring(0, 10); // YYYY-MM-DD
                if (!dateMap.has(dateKey)) {
                    dateMap.set(dateKey, gd);
                }
            }
        }

        // Sort by date and renumber as tournament days
        const sorted = [...dateMap.values()].sort((a, b) =>
            new Date(a.date).getTime() - new Date(b.date).getTime()
        );

        return sorted.map((gd, i) => ({
            ...this.toGameDayLine(gd),
            dayNumber: i + 1
        }));
    });

    /**
     * If ALL configured agegroups play the same dates, return a hero-level "Plays:" string.
     * Otherwise null — per-tile dropdowns handle it.
     */
    readonly heroPlaysLabel = computed((): string | null => {
        const map = this.readinessMap();
        const configured = Object.values(map).filter(r => r.isConfigured && r.gameDays?.length > 0);
        if (configured.length === 0) return null;

        // Build a canonical date signature for each agegroup
        const sigs = configured.map(r =>
            [...r.gameDays].sort((a, b) => a.date.localeCompare(b.date))
                .map(gd => gd.date.substring(0, 10))
                .join(',')
        );

        // Check if all are identical
        if (!sigs.every(s => s === sigs[0])) return null;

        // All the same — format using the first agegroup's data
        const gameDayList = this.agGameDayList(configured[0].agegroupId);
        if (gameDayList.length === 0) return null;
        return `Plays: ${gameDayList.join(' & ')}`;
    });

    /** Scheduling style — uniform across all divisions. Structured for badged rendering. */
    readonly schedulingStyle = computed((): { placement: string | null; gap: string | null } | null => {
        // Don't show strategy defaults on a blank slate — they're just noise
        if (this.configuredCount() === 0) return null;
        const strats = this.strategies();
        if (strats.length === 0) return null;

        const placement = this.uniformValue(strats, s => s.placement.toString());
        const gap = this.uniformValue(strats, s => s.gapPattern.toString());

        if (placement === null && gap === null) return null;

        return {
            placement: placement !== null ? this.placementLabel(Number(placement)) : null,
            gap: gap !== null ? this.gapLabel(Number(gap)) : null
        };
    });

    // ── Per-agegroup helpers ──

    agGameCount(agegroupId: string): number {
        const summary = this.gameSummary();
        if (!summary) return 0;
        return summary.divisions
            .filter(d => d.agegroupId === agegroupId)
            .reduce((sum, d) => sum + d.gameCount, 0);
    }

    agExpectedGames(agegroupId: string): number {
        const summary = this.gameSummary();
        if (!summary) return 0;
        return summary.divisions
            .filter(d => d.agegroupId === agegroupId)
            .reduce((sum, d) => sum + d.expectedRrGames, 0);
    }

    agStage(agegroupId: string): AgStage {
        const r = this.readinessMap()[agegroupId];
        if (!r?.isConfigured) return 'unconfigured';
        const games = this.agGameCount(agegroupId);
        if (games === 0) return 'configured';
        const expected = this.agExpectedGames(agegroupId);
        return games >= expected ? 'scheduled-complete' : 'scheduled-partial';
    }

    /**
     * Per-card game day list — returns formatted date strings for the plays dropdown.
     * e.g. ["Sat 06/06/2026", "Sun 06/07/2026"]
     * Returns empty array if unconfigured.
     */
    agGameDayList(agegroupId: string): string[] {
        const r = this.readinessMap()[agegroupId];
        if (!r?.isConfigured || !r.gameDays || r.gameDays.length === 0) return [];

        return [...r.gameDays]
            .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
            .map(gd => {
                const d = new Date(gd.date);
                const mm = String(d.getMonth() + 1).padStart(2, '0');
                const dd = String(d.getDate()).padStart(2, '0');
                const yyyy = d.getFullYear();
                return `${gd.dow} ${mm}/${dd}/${yyyy}`;
            });
    }

    /**
     * Per-tile game day list — returns empty if hero already shows a uniform "Plays:" line.
     * This avoids redundant dropdowns when all agegroups play the same dates.
     */
    agTileGameDayList(agegroupId: string): string[] {
        if (this.heroPlaysLabel()) return []; // hero covers it
        return this.agGameDayList(agegroupId);
    }

    // ── Labels ──

    placementLabel(val: number): string {
        return val === 1 ? 'vertically' : 'horizontally';
    }

    gapLabel(val: number): string {
        switch (val) {
            case 0: return 'back-to-back';
            case 2: return '1on/2off';
            default: return '1on/1off';
        }
    }

    // ── Private helpers ──

    private uniformValue<T>(items: T[], extract: (item: T) => string): string | null {
        if (items.length === 0) return null;
        const first = extract(items[0]);
        return items.every(item => extract(item) === first) ? first : null;
    }

    /** Convert API GameDayDto to display-ready GameDayLine. */
    private toGameDayLine(gd: GameDayDto): GameDayLine {
        const d = new Date(gd.date);
        const mm = String(d.getMonth() + 1).padStart(2, '0');
        const dd = String(d.getDate()).padStart(2, '0');
        const yyyy = d.getFullYear();

        return {
            dayNumber: gd.dayNumber,
            dow: gd.dow,
            dateFormatted: `${mm}/${dd}/${yyyy}`,
            fieldCount: gd.fieldCount,
            startTime: gd.startTime,
            endTime: gd.endTime,
            gsi: gd.gsi,
            totalSlots: gd.totalSlots
        };
    }

    /** Convert MM/DD/YYYY back to YYYY-MM-DD for comparison. */
    private toIsoDate(formatted: string): string {
        const [mm, dd, yyyy] = formatted.split('/');
        return `${yyyy}-${mm}-${dd}`;
    }

    onDeleteConfirmed(): void {
        this.deleteConfirmed.emit();
        this.deleteConfirmText.set('');
    }

    onDeleteCancelled(): void {
        this.deleteCancelled.emit();
        this.deleteConfirmText.set('');
    }

    requestDevReset(): void {
        this.showDevResetConfirm.set(true);
        this.devResetConfirmText.set('');
    }

    onDevResetConfirmed(): void {
        this.showDevResetConfirm.set(false);
        this.devResetConfirmText.set('');
        this.devResetConfirmed.emit();
    }

    onDevResetCancelled(): void {
        this.showDevResetConfirm.set(false);
        this.devResetConfirmText.set('');
    }
}
