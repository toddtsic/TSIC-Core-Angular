import { Component, computed, input, output, signal, ElementRef, ViewChild, ChangeDetectionStrategy, NgZone, inject, OnDestroy, OnInit } from '@angular/core';
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
export class ScheduleGridComponent implements OnInit, OnDestroy {
    private readonly zone = inject(NgZone);

    @ViewChild('gridScroll') gridScrollEl?: ElementRef<HTMLElement>;
    @ViewChild('minimapCanvas') minimapCanvasRef?: ElementRef<HTMLCanvasElement>;

    ngOnInit(): void {
        this.zone.runOutsideAngular(() => {
            document.addEventListener('keydown', this.onKeyDown);
        });
    }

    // ── Inputs ──
    readonly gridResponse = input<ScheduleGridResponse | null>(null);
    readonly isLoading = input(false);
    readonly emptyMessage = input('No fields or timeslots configured');

    // Game card rendering options
    readonly showActionButtons = input(false);
    readonly showQaBadges = input(true);

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

    // Block shift selection (driven by parent)
    readonly blockSelection = input<{ rowStart: number; rowEnd: number; colStart: number; colEnd: number } | null>(null);

    // Shift preview ghost targets: Map<gid, targetGDate> (for rendering ghost cells at target positions)
    readonly shiftGhostMap = input<Map<number, string> | null>(null);

    // Shift conflict gids (for red highlight on target cells)
    readonly shiftConflictGids = input<Set<number> | null>(null);

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
            label: new Date(day).toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' }),
            rowIndex
        }));
    });

    // ── Parking zone ──

    private static readonly PARKING_HOUR = 23;
    private static readonly PARKING_MIN = 45;

    /** Split grid rows into regular + parking (time >= 23:45). */
    readonly regularRows = computed(() =>
        this.gridRows().filter(r => !this.isParkingRow(r))
    );

    readonly parkingRows = computed(() =>
        this.gridRows().filter(r => this.isParkingRow(r))
    );

    readonly parkedGameCount = computed(() => {
        let count = 0;
        for (const row of this.parkingRows()) {
            for (const cell of row.cells) {
                if (cell) count++;
            }
        }
        return count;
    });

    readonly parkingExpanded = signal(false);

    isParkingRow(row: ScheduleGridRow): boolean {
        const d = new Date(row.gDate);
        return d.getHours() === ScheduleGridComponent.PARKING_HOUR
            && d.getMinutes() >= ScheduleGridComponent.PARKING_MIN;
    }

    // ── Block selection helpers ──

    isInBlockSelection(rowIndex: number, colIndex: number): boolean {
        const sel = this.blockSelection();
        if (!sel) return false;
        return rowIndex >= sel.rowStart && rowIndex <= sel.rowEnd
            && colIndex >= sel.colStart && colIndex <= sel.colEnd;
    }

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

    // ══════════════════════════════════════════════════════════════
    // Minimap — bird's-eye grid navigator (SSMS diagram style)
    // ══════════════════════════════════════════════════════════════

    readonly minimapOpen = signal(false);
    private isMinimapDragging = false;
    private _diagLogged = false;
    private scrollHandler?: () => void;

    private readonly onKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Escape' && this.minimapOpen()) {
            this.zone.run(() => this.closeMinimap());
        }
        if (e.key === 'm' || e.key === 'M') {
            const el = e.target as HTMLElement;
            const tag = el?.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
            if (el?.isContentEditable) return;
            this.zone.run(() => this.toggleMinimap());
        }
    };

    toggleMinimap(): void {
        if (this.minimapOpen()) {
            this.closeMinimap();
        } else {
            this.minimapOpen.set(true);
            setTimeout(() => this.openMinimap());
        }
    }

    private scrollParent?: HTMLElement | null;

    private openMinimap(): void {
        this.renderMinimap();
        const el = this.gridScrollEl?.nativeElement;
        if (el) {
            this.scrollHandler = () => this.renderMinimap();
            el.addEventListener('scroll', this.scrollHandler, { passive: true });
            // Also listen on the parent scroll container if grid-scroll doesn't scroll vertically
            this.scrollParent = this.findScrollParent(el);
            if (this.scrollParent) {
                this.scrollParent.addEventListener('scroll', this.scrollHandler, { passive: true });
            }
        }
    }

    private closeMinimap(): void {
        this.minimapOpen.set(false);
        if (this.scrollHandler) {
            this.gridScrollEl?.nativeElement.removeEventListener('scroll', this.scrollHandler);
            this.scrollParent?.removeEventListener('scroll', this.scrollHandler);
            this.scrollHandler = undefined;
            this.scrollParent = undefined;
        }
    }

    renderMinimap(): void {
        const canvas = this.minimapCanvasRef?.nativeElement;
        const el = this.gridScrollEl?.nativeElement;
        if (!canvas || !el) return;

        const gridW = el.scrollWidth;
        const gridH = el.scrollHeight;

        // DIAG: remove after fixing
        if (!this._diagLogged) {
            this._diagLogged = true;
            const parent = this.findScrollParent(el);
            console.log('[minimap-diag]', {
                elTag: el.tagName, elClass: el.className,
                scrollW: gridW, scrollH: gridH,
                clientW: el.clientWidth, clientH: el.clientHeight,
                elScrollsVertically: gridH > el.clientHeight + 1,
                parentTag: parent?.tagName, parentClass: parent?.className,
                parentScrollH: parent?.scrollHeight, parentClientH: parent?.clientHeight,
            });
        }

        // Scale to fit in max 260x180 bounding box
        const maxW = 260, maxH = 180;
        const scale = Math.min(maxW / gridW, maxH / gridH);
        canvas.width = Math.round(gridW * scale);
        canvas.height = Math.round(gridH * scale);

        const ctx = canvas.getContext('2d')!;
        const style = getComputedStyle(document.documentElement);

        // Background
        ctx.fillStyle = style.getPropertyValue('--bs-tertiary-bg').trim() || '#f5f5f4';
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        // Draw game cells
        const rows = this.gridRows();
        const cols = this.gridColumns();
        if (rows.length === 0 || cols.length === 0) return;

        const timeColW = 100 * scale;
        const fieldColW = (canvas.width - timeColW) / cols.length;
        const rowH = canvas.height / rows.length;

        const clashIds = this.timeClashGameIds();
        const b2bIds = this.backToBackGameIds();
        const colorFallback = style.getPropertyValue('--bs-primary').trim() || '#0d6efd';
        const colorDanger = style.getPropertyValue('--bs-danger').trim() || '#dc3545';
        const colorWarning = style.getPropertyValue('--bs-warning').trim() || '#ffc107';

        for (let ri = 0; ri < rows.length; ri++) {
            const cells = rows[ri].cells;
            for (let ci = 0; ci < cells.length; ci++) {
                const cell = cells[ci];
                if (!cell) continue;

                if (cell.isSlotCollision || clashIds.has(cell.gid)) {
                    ctx.fillStyle = colorDanger;
                } else if (b2bIds.has(cell.gid)) {
                    ctx.fillStyle = colorWarning;
                } else {
                    ctx.fillStyle = cell.color || colorFallback;
                }

                const x = timeColW + ci * fieldColW + 0.5;
                const y = ri * rowH + 0.5;
                ctx.fillRect(x, y, fieldColW - 1, rowH - 1);
            }
        }

        // Viewport rectangle — use grid-scroll if it scrolls vertically, else find parent scroller
        let vpY: number;
        let vpH: number;
        if (el.scrollHeight > el.clientHeight + 1) {
            vpY = el.scrollTop * scale;
            vpH = el.clientHeight * scale;
        } else {
            const parent = this.findScrollParent(el);
            if (parent) {
                const gridAbsTop = el.getBoundingClientRect().top
                    - parent.getBoundingClientRect().top + parent.scrollTop;
                vpY = (parent.scrollTop - gridAbsTop) * scale;
                vpH = parent.clientHeight * scale;
            } else {
                vpY = 0;
                vpH = canvas.height;
            }
        }
        const vpX = el.scrollLeft * scale;
        const vpW = el.clientWidth * scale;

        ctx.fillStyle = 'rgba(13, 110, 253, 0.1)';
        ctx.fillRect(vpX, vpY, vpW, vpH);
        ctx.strokeStyle = colorFallback;
        ctx.lineWidth = 2;
        ctx.strokeRect(vpX, vpY, vpW, vpH);
    }

    private readonly boundMinimapMove = (e: MouseEvent) => this.onMinimapMove(e);
    private readonly boundMinimapUp = () => this.onMinimapUp();

    onMinimapDown(e: MouseEvent): void {
        this.isMinimapDragging = true;
        this.scrollFromMinimap(e);
        e.preventDefault();
        document.addEventListener('mousemove', this.boundMinimapMove);
        document.addEventListener('mouseup', this.boundMinimapUp);
    }

    onMinimapMove(e: MouseEvent): void {
        if (!this.isMinimapDragging) return;
        this.scrollFromMinimap(e);
    }

    onMinimapUp(): void {
        this.isMinimapDragging = false;
        document.removeEventListener('mousemove', this.boundMinimapMove);
        document.removeEventListener('mouseup', this.boundMinimapUp);
    }

    private scrollFromMinimap(e: MouseEvent): void {
        const canvas = this.minimapCanvasRef?.nativeElement;
        const el = this.gridScrollEl?.nativeElement;
        if (!canvas || !el) return;

        const rect = canvas.getBoundingClientRect();
        const x = e.clientX - rect.left;
        const y = e.clientY - rect.top;
        const scale = canvas.width / el.scrollWidth;

        // Horizontal: grid scrolls itself
        el.scrollLeft = (x / scale) - (el.clientWidth / 2);

        // Vertical: grid-scroll may or may not be the vertical scroller.
        // If grid-scroll has no vertical overflow, find the nearest scrollable ancestor.
        if (el.scrollHeight > el.clientHeight + 1) {
            el.scrollTop = (y / scale) - (el.clientHeight / 2);
        } else {
            const parent = this.findScrollParent(el);
            if (parent) {
                const gridAbsTop = el.getBoundingClientRect().top
                    - parent.getBoundingClientRect().top + parent.scrollTop;
                parent.scrollTop = gridAbsTop + (y / scale) - (parent.clientHeight / 2);
            }
        }
    }

    /** Walk up the DOM to find the nearest vertically-scrollable ancestor. */
    private findScrollParent(el: HTMLElement): HTMLElement | null {
        let node = el.parentElement;
        while (node) {
            if (node.scrollHeight > node.clientHeight + 1) {
                const style = getComputedStyle(node);
                const ov = style.overflowY;
                if (ov === 'auto' || ov === 'scroll') return node;
            }
            node = node.parentElement;
        }
        return null;
    }

    ngOnDestroy(): void {
        this.closeMinimap();
        document.removeEventListener('mousemove', this.boundMinimapMove);
        document.removeEventListener('mouseup', this.boundMinimapUp);
        document.removeEventListener('keydown', this.onKeyDown);
    }
}
