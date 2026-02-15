import { Component, computed, input, output, signal, ElementRef, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import type { ScheduleGridResponse, ScheduleGridRow, ScheduleGameDto } from '@core/api';
import { GameCardComponent } from '../game-card/game-card.component';
import { formatDate, formatTimeOnly } from '../../utils/scheduling-helpers';
import {
    computeTimeClashGameIds, computeBackToBackGameIds, computeBreakingConflictCount,
    isSlotCollision, isTimeClash, isBackToBack, isBreaking
} from '../../utils/conflict-detection';

@Component({
    selector: 'app-schedule-grid',
    standalone: true,
    imports: [GameCardComponent],
    templateUrl: './schedule-grid.component.html',
    styleUrl: './schedule-grid.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScheduleGridComponent {

    @ViewChild('gridScroll') gridScrollEl?: ElementRef<HTMLElement>;

    // ── Inputs ──
    readonly gridResponse = input<ScheduleGridResponse | null>(null);
    readonly isLoading = input(false);
    readonly emptyMessage = input('No fields or timeslots configured');

    // Game card rendering options
    readonly showMatchupCodes = input(false);
    readonly showGameId = input(false);
    readonly showTeamDesignators = input(false);
    readonly showActionButtons = input(false);

    // Division highlighting (null = no dimming)
    readonly highlightDivId = input<string | null>(null);

    // Selection state (driven by parent)
    readonly selectedGameGid = input<number | null>(null);

    // Open slot hints
    readonly showOpenSlotHints = input(false);
    readonly showMoveSlotHints = input(false);

    // ── Outputs ──
    readonly cellClicked = output<{ row: ScheduleGridRow; colIndex: number; game: ScheduleGameDto | null }>();
    readonly gameMoveRequested = output<{ game: ScheduleGameDto; row: ScheduleGridRow; colIndex: number }>();
    readonly gameDeleteRequested = output<{ game: ScheduleGameDto; row: ScheduleGridRow; colIndex: number }>();

    // ── Internal computed ──
    readonly gridColumns = computed(() => this.gridResponse()?.columns ?? []);
    readonly gridRows = computed(() => this.gridResponse()?.rows ?? []);

    readonly timeClashGameIds = computed(() => computeTimeClashGameIds(this.gridRows()));
    readonly backToBackGameIds = computed(() => computeBackToBackGameIds(this.gridRows()));
    readonly breakingConflictCount = computed(() => computeBreakingConflictCount(this.gridRows(), this.timeClashGameIds()));

    readonly gridDays = computed(() => {
        const rows = this.gridRows();
        const seen = new Map<string, number>();
        rows.forEach((r, i) => {
            const key = new Date(r.gDate).toDateString();
            if (!seen.has(key)) seen.set(key, i);
        });
        return Array.from(seen.entries()).map(([day, rowIndex]) => ({
            label: new Date(day).toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' }),
            rowIndex
        }));
    });

    // ── Helpers bound for template ──
    readonly formatDate = formatDate;
    readonly formatTimeOnly = formatTimeOnly;

    // ── Cell state methods ──

    isGameSelected(game: ScheduleGameDto): boolean {
        return game.gid === this.selectedGameGid();
    }

    isOtherDivision(game: ScheduleGameDto): boolean {
        const divId = this.highlightDivId();
        return divId != null && game.divId !== divId;
    }

    isSlotCollision(game: ScheduleGameDto): boolean {
        return isSlotCollision(game);
    }

    isTimeClash(game: ScheduleGameDto): boolean {
        return isTimeClash(game, this.timeClashGameIds());
    }

    isBackToBack(game: ScheduleGameDto): boolean {
        return isBackToBack(game, this.backToBackGameIds());
    }

    isBreaking(game: ScheduleGameDto): boolean {
        return isBreaking(game, this.timeClashGameIds());
    }

    // ── Cell click ──

    onCellClick(row: ScheduleGridRow, colIndex: number): void {
        const game = row.cells[colIndex] ?? null;
        this.cellClicked.emit({ row, colIndex, game });
    }

    // ── Scroll methods (called by parent via ViewChild) ──

    scrollToRow(index: number): void {
        const el = this.gridScrollEl?.nativeElement;
        if (!el) return;
        setTimeout(() => {
            const rows = el.querySelectorAll('tbody tr');
            const target = rows[index] as HTMLElement | undefined;
            if (target) target.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }, 50);
    }

    scrollToFirstRelevant(divId?: string): void {
        const rows = this.gridRows();
        if (divId) {
            const divIdx = rows.findIndex(r => r.cells.some(c => c && c.divId === divId));
            if (divIdx >= 0) { this.scrollToRow(divIdx); return; }
        }
        // Fallback: first row with an open slot, then row 0
        const openIdx = rows.findIndex(r => r.cells.some(c => !c));
        this.scrollToRow(Math.max(openIdx, 0));
    }

    scrollToNextOpenSlot(startFromRow: number): void {
        const rows = this.gridRows();
        for (let i = startFromRow; i < rows.length; i++) {
            if (rows[i].cells.some(c => !c)) {
                this.scrollToRow(i);
                return;
            }
        }
    }
}
