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

    // Division glow color (agegroup hex — persistent highlight for selected division cells)
    readonly highlightDivColor = input<string | null>(null);

    // Locate-game highlight (marching ants)
    readonly highlightGameGid = input<number | null>(null);

    // When true, ALL games matching highlightDivId get marching ants (division click)
    readonly highlightAllDiv = input(false);

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

    isGameHighlighted(game: ScheduleGameDto): boolean {
        if (game.gid === this.highlightGameGid()) return true;
        return this.highlightAllDiv() && this.isDivisionMatch(game);
    }

    isOtherDivision(game: ScheduleGameDto): boolean {
        const divId = this.highlightDivId();
        return divId != null && game.divId !== divId;
    }

    isDivisionMatch(game: ScheduleGameDto): boolean {
        const divId = this.highlightDivId();
        return divId != null && game.divId === divId;
    }

    /** Division glow applies when cell matches the div (yields to marching ants only). */
    showDivGlow(game: ScheduleGameDto): boolean {
        return this.isDivisionMatch(game)
            && !this.isGameHighlighted(game);
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

    /** Find the gid of the first game belonging to a division (null if none placed). */
    findFirstDivGameGid(divId: string): number | null {
        for (const row of this.gridRows()) {
            for (const cell of row.cells) {
                if (cell && cell.divId === divId) return cell.gid;
            }
        }
        return null;
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

    /** Scroll to a specific game by gid and center it in the viewport. */
    scrollToGame(gid: number): void {
        const rows = this.gridRows();
        for (let ri = 0; ri < rows.length; ri++) {
            const ci = rows[ri].cells.findIndex(c => c && c.gid === gid);
            if (ci >= 0) {
                const el = this.gridScrollEl?.nativeElement;
                if (!el) return;
                setTimeout(() => {
                    const cell = el.querySelector(`tbody tr:nth-child(${ri + 1}) td:nth-child(${ci + 2})`) as HTMLElement | null;
                    if (cell) cell.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'center' });
                }, 50);
                return;
            }
        }
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
