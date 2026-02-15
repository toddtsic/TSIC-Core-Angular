import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '@shared-ui/toast.service';
import {
    ScheduleDivisionService,
    type AutoScheduleResponse,
    type AgegroupWithDivisionsDto,
    type DivisionSummaryDto,
    type PairingDto,
    type DivisionPairingsResponse,
    type DivisionTeamDto,
    type ScheduleGridResponse,
    type ScheduleGridRow,
    type ScheduleFieldColumn,
    type ScheduleGameDto
} from './services/schedule-division.service';

@Component({
    selector: 'app-schedule-division',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './schedule-division.component.html',
    styleUrl: './schedule-division.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScheduleDivisionComponent implements OnInit {
    private readonly svc = inject(ScheduleDivisionService);
    private readonly toast = inject(ToastService);

    // ── Navigator state ──
    readonly agegroups = signal<AgegroupWithDivisionsDto[]>([]);
    readonly expandedAgegroups = signal<Set<string>>(new Set());
    readonly selectedDivision = signal<DivisionSummaryDto | null>(null);
    readonly selectedAgegroupId = signal<string | null>(null);
    readonly isNavLoading = signal(false);

    // ── Pairings state ──
    readonly divisionResponse = signal<DivisionPairingsResponse | null>(null);
    readonly pairings = signal<PairingDto[]>([]);
    readonly isPairingsLoading = signal(false);

    // ── Teams state ──
    readonly divisionTeams = signal<DivisionTeamDto[]>([]);
    readonly showTeams = signal(false);

    // ── Schedule Grid state ──
    readonly gridResponse = signal<ScheduleGridResponse | null>(null);
    readonly isGridLoading = signal(false);

    // ── Placement workflow ──
    readonly selectedPairing = signal<PairingDto | null>(null);
    readonly isPlacing = signal(false);

    // ── Auto-schedule ──
    readonly isAutoScheduling = signal(false);
    readonly autoScheduleResult = signal<AutoScheduleResponse | null>(null);
    readonly showAutoScheduleConfirm = signal(false);

    // ── Delete confirmation ──
    readonly showDeleteDivConfirm = signal(false);
    readonly isDeletingDivGames = signal(false);

    // ── Computed helpers ──
    readonly gridColumns = computed(() => this.gridResponse()?.columns ?? []);
    readonly gridRows = computed(() => this.gridResponse()?.rows ?? []);

    readonly teamCount = computed(() => this.divisionResponse()?.teamCount ?? 0);

    // ── Conflict detection (3 types) ──

    /** BREAKING: Same team in 2+ games at the exact same time (same grid row, any division). */
    readonly timeClashGameIds = computed(() => {
        const rows = this.gridRows();
        const clashed = new Set<number>();

        for (const row of rows) {
            const teamGames = new Map<string, number[]>();
            for (const cell of row.cells) {
                if (!cell) continue;
                for (const tid of [cell.t1Id, cell.t2Id]) {
                    if (!tid) continue;
                    if (!teamGames.has(tid)) teamGames.set(tid, []);
                    teamGames.get(tid)!.push(cell.gid);
                }
            }
            for (const gids of teamGames.values()) {
                if (gids.length > 1) gids.forEach(g => clashed.add(g));
            }
        }
        return clashed;
    });

    /** NON-BREAKING: Same team in consecutive timeslot rows on the same day (any division). */
    readonly backToBackGameIds = computed(() => {
        const rows = this.gridRows();
        const b2b = new Set<number>();

        for (let i = 0; i < rows.length - 1; i++) {
            const curDay = new Date(rows[i].gDate).toDateString();
            const nextDay = new Date(rows[i + 1].gDate).toDateString();
            if (curDay !== nextDay) continue;

            const curTeams = new Map<string, number[]>();
            for (const cell of rows[i].cells) {
                if (!cell) continue;
                for (const tid of [cell.t1Id, cell.t2Id]) {
                    if (!tid) continue;
                    if (!curTeams.has(tid)) curTeams.set(tid, []);
                    curTeams.get(tid)!.push(cell.gid);
                }
            }

            for (const cell of rows[i + 1].cells) {
                if (!cell) continue;
                for (const tid of [cell.t1Id, cell.t2Id]) {
                    if (!tid) continue;
                    if (curTeams.has(tid)) {
                        curTeams.get(tid)!.forEach(g => b2b.add(g));
                        b2b.add(cell.gid);
                    }
                }
            }
        }
        return b2b;
    });

    /** Combined breaking conflict count (time clash + slot collision). */
    readonly breakingConflictCount = computed(() => {
        let count = this.timeClashGameIds().size;
        for (const row of this.gridRows()) {
            for (const cell of row.cells) {
                if (cell && (cell as any).isSlotCollision) count++;
            }
        }
        return count;
    });

    ngOnInit(): void {
        this.loadAgegroups();
    }

    // ── Navigator ──

    loadAgegroups(): void {
        this.isNavLoading.set(true);
        this.svc.getAgegroups().subscribe({
            next: (data) => {
                const filtered = data
                    .filter(ag => {
                        const name = (ag.agegroupName ?? '').toUpperCase();
                        return name !== 'DROPPED TEAMS' && !name.startsWith('WAITLIST');
                    })
                    .map(ag => ({
                        ...ag,
                        divisions: ag.divisions.filter(d =>
                            (d.divName ?? '').toUpperCase() !== 'UNASSIGNED'
                        )
                    }))
                    .filter(ag => ag.divisions.length > 0)
                    .sort((a, b) => (a.agegroupName ?? '').localeCompare(b.agegroupName ?? ''));
                this.agegroups.set(filtered);
                this.isNavLoading.set(false);
            },
            error: () => this.isNavLoading.set(false)
        });
    }

    toggleAgegroup(agId: string): void {
        const current = new Set(this.expandedAgegroups());
        if (current.has(agId)) current.delete(agId);
        else current.add(agId);
        this.expandedAgegroups.set(current);
    }

    isExpanded(agId: string): boolean {
        return this.expandedAgegroups().has(agId);
    }

    collapseAll(): void {
        this.expandedAgegroups.set(new Set());
    }

    selectDivision(div: DivisionSummaryDto, agegroupId: string): void {
        this.selectedDivision.set(div);
        this.selectedAgegroupId.set(agegroupId);
        this.selectedPairing.set(null);
        this.selectedGame.set(null);
        this.showDeleteDivConfirm.set(false);
        this.loadDivisionData(div.divId, agegroupId);
    }

    private loadDivisionData(divId: string, agegroupId: string): void {
        this.loadDivisionPairings(divId);
        this.loadDivisionTeams(divId);
        this.loadScheduleGrid(divId, agegroupId);
    }

    // ── Pairings ──

    loadDivisionPairings(divId: string): void {
        this.isPairingsLoading.set(true);
        this.svc.getDivisionPairings(divId).subscribe({
            next: (resp) => {
                this.divisionResponse.set(resp);
                this.pairings.set(resp.pairings);
                this.isPairingsLoading.set(false);
            },
            error: () => this.isPairingsLoading.set(false)
        });
    }

    loadDivisionTeams(divId: string): void {
        this.svc.getDivisionTeams(divId).subscribe({
            next: (teams) => this.divisionTeams.set(teams),
            error: () => this.divisionTeams.set([])
        });
    }

    toggleTeams(): void {
        this.showTeams.update(v => !v);
    }

    // ── Schedule Grid ──

    loadScheduleGrid(divId: string, agegroupId: string): void {
        this.isGridLoading.set(true);
        this.svc.getScheduleGrid(divId, agegroupId).subscribe({
            next: (grid) => {
                this.gridResponse.set(grid);
                this.isGridLoading.set(false);
            },
            error: () => {
                this.gridResponse.set(null);
                this.isGridLoading.set(false);
            }
        });
    }

    // ── Placement Workflow ──

    selectPairingForPlacement(pairing: PairingDto): void {
        this.selectedGame.set(null);
        if (this.selectedPairing()?.ai === pairing.ai) {
            this.selectedPairing.set(null);
        } else {
            this.selectedPairing.set(pairing);
        }
    }

    isPairingSelected(pairing: PairingDto): boolean {
        return this.selectedPairing()?.ai === pairing.ai;
    }

    placeGame(row: ScheduleGridRow, colIndex: number): void {
        const pairing = this.selectedPairing();
        const div = this.selectedDivision();
        const agId = this.selectedAgegroupId();
        if (!pairing || !div || !agId) return;

        const column = this.gridColumns()[colIndex];
        if (!column) return;

        // Pre-check: would placing this pairing create a time clash?
        const teamIds = this.resolvePairingTeamIds(pairing);
        const clash = this.findTimeClashInRow(row, teamIds);
        if (clash) {
            this.toast.show(`Time clash: ${clash} is already playing at this timeslot`, 'danger', 4000);
            return;
        }

        this.isPlacing.set(true);
        this.svc.placeGame({
            pairingAi: pairing.ai,
            gDate: row.gDate,
            fieldId: column.fieldId,
            agegroupId: agId,
            divId: div.divId
        }).subscribe({
            next: (game) => {
                // Update the grid cell in place
                this.gridResponse.update(grid => {
                    if (!grid) return grid;
                    const updatedRows = grid.rows.map((r: ScheduleGridRow) => {
                        if (r.gDate === row.gDate) {
                            const updatedCells = [...r.cells];
                            updatedCells[colIndex] = game;
                            return { ...r, cells: updatedCells };
                        }
                        return r;
                    });
                    return { ...grid, rows: updatedRows };
                });
                // Mark pairing as scheduled
                this.pairings.update(list =>
                    list.map(p => p.ai === pairing.ai ? { ...p, bAvailable: false } : p)
                );
                this.selectedPairing.set(null);
                this.isPlacing.set(false);
            },
            error: () => this.isPlacing.set(false)
        });
    }

    // ── Delete single game ──

    deleteGame(game: ScheduleGameDto, row: ScheduleGridRow, colIndex: number): void {
        // Clear move selection if deleting the selected game
        if (this.selectedGame()?.game.gid === game.gid) {
            this.selectedGame.set(null);
        }

        this.svc.deleteGame(game.gid).subscribe({
            next: () => {
                // Clear the grid cell
                this.gridResponse.update(grid => {
                    if (!grid) return grid;
                    const updatedRows = grid.rows.map((r: ScheduleGridRow) => {
                        if (r.gDate === row.gDate) {
                            const updatedCells = [...r.cells] as (ScheduleGameDto | null)[];
                            updatedCells[colIndex] = null;
                            return { ...r, cells: updatedCells as ScheduleGameDto[] };
                        }
                        return r;
                    });
                    return { ...grid, rows: updatedRows };
                });
                // Reload pairings to refresh availability
                const div = this.selectedDivision();
                if (div) this.loadDivisionPairings(div.divId);
            }
        });
    }

    // ── Delete all division games ──

    confirmDeleteDivGames(): void {
        this.showDeleteDivConfirm.set(true);
    }

    cancelDeleteDivGames(): void {
        this.showDeleteDivConfirm.set(false);
    }

    deleteDivGames(): void {
        const div = this.selectedDivision();
        if (!div) return;

        this.isDeletingDivGames.set(true);
        this.showDeleteDivConfirm.set(false);
        this.svc.deleteDivisionGames({ divId: div.divId }).subscribe({
            next: () => {
                this.isDeletingDivGames.set(false);
                // Reload grid and pairings
                const agId = this.selectedAgegroupId();
                if (agId) {
                    this.loadDivisionData(div.divId, agId);
                }
            },
            error: () => this.isDeletingDivGames.set(false)
        });
    }

    // ── Auto-schedule ──

    confirmAutoSchedule(): void {
        this.showAutoScheduleConfirm.set(true);
        this.autoScheduleResult.set(null);
    }

    cancelAutoSchedule(): void {
        this.showAutoScheduleConfirm.set(false);
    }

    autoScheduleDiv(): void {
        const div = this.selectedDivision();
        if (!div) return;

        this.isAutoScheduling.set(true);
        this.showAutoScheduleConfirm.set(false);
        this.autoScheduleResult.set(null);
        this.svc.autoScheduleDiv(div.divId).subscribe({
            next: (result) => {
                this.autoScheduleResult.set(result);
                this.isAutoScheduling.set(false);
                // Reload grid and pairings to reflect new schedule
                const agId = this.selectedAgegroupId();
                if (agId) {
                    this.loadDivisionData(div.divId, agId);
                }
            },
            error: () => this.isAutoScheduling.set(false)
        });
    }

    dismissAutoScheduleResult(): void {
        this.autoScheduleResult.set(null);
    }

    // ── Move/Swap (click game → click destination) ──

    readonly selectedGame = signal<{ game: ScheduleGameDto; row: ScheduleGridRow; colIndex: number } | null>(null);

    selectGameForMove(game: ScheduleGameDto, row: ScheduleGridRow, colIndex: number): void {
        this.selectedPairing.set(null);
        if (this.selectedGame()?.game.gid === game.gid) {
            this.selectedGame.set(null);
        } else {
            this.selectedGame.set({ game, row, colIndex });
        }
    }

    isGameSelected(game: ScheduleGameDto): boolean {
        return this.selectedGame()?.game.gid === game.gid;
    }

    /** Breaking: slot collision (2+ games in same cell) — from backend flag. */
    isSlotCollision(game: ScheduleGameDto): boolean {
        return (game as any).isSlotCollision === true;
    }

    /** Breaking: same team at same time on different fields. */
    isTimeClash(game: ScheduleGameDto): boolean {
        return this.timeClashGameIds().has(game.gid);
    }

    /** Non-breaking: same team in consecutive timeslot rows. */
    isBackToBack(game: ScheduleGameDto): boolean {
        return this.backToBackGameIds().has(game.gid);
    }

    /** Any breaking conflict (slot collision or time clash). */
    isBreaking(game: ScheduleGameDto): boolean {
        return this.isSlotCollision(game) || this.isTimeClash(game);
    }

    isOtherDivision(game: ScheduleGameDto): boolean {
        return game.divId !== this.selectedDivision()?.divId;
    }

    moveOrSwapGame(targetRow: ScheduleGridRow, targetColIndex: number): void {
        const source = this.selectedGame();
        if (!source) return;

        const targetColumn = this.gridColumns()[targetColIndex];
        if (!targetColumn) return;

        // Pre-check: would moving this game create a time clash?
        const teamIds = [source.game.t1Id, source.game.t2Id].filter((id): id is string => !!id);
        const targetCell = targetRow.cells[targetColIndex];
        if (!targetCell) {
            // Move to empty cell — check for clash in target row (exclude self)
            const clash = this.findTimeClashInRow(targetRow, teamIds, source.game.gid);
            if (clash) {
                this.toast.show(`Time clash: ${clash} is already playing at this timeslot`, 'danger', 4000);
                return;
            }
        }

        this.svc.moveGame({
            gid: source.game.gid,
            targetGDate: targetRow.gDate,
            targetFieldId: targetColumn.fieldId
        }).subscribe({
            next: () => {
                this.selectedGame.set(null);
                // Reload grid to reflect swap
                const div = this.selectedDivision();
                const agId = this.selectedAgegroupId();
                if (div && agId) this.loadScheduleGrid(div.divId, agId);
            }
        });
    }

    // ── Grid cell click handler (dispatches based on state) ──

    onGridCellClick(row: ScheduleGridRow, colIndex: number): void {
        const cell = row.cells[colIndex];

        if (this.selectedPairing()) {
            // Placement mode: clicking an empty slot places the game
            if (!cell) {
                this.placeGame(row, colIndex);
            }
        } else if (this.selectedGame()) {
            // Move mode: clicking any cell (empty or occupied) moves/swaps
            if (!cell || !this.isGameSelected(cell)) {
                this.moveOrSwapGame(row, colIndex);
            }
        }
    }

    // ── Time-clash prevention helpers ──

    /** Resolve a pairing's team ranks to actual team IDs (T-type only; bracket teams are TBD). */
    private resolvePairingTeamIds(pairing: PairingDto): string[] {
        const ids: string[] = [];
        const teams = this.divisionTeams();
        if (pairing.t1Type === 'T') {
            const t = teams.find(tm => tm.divRank === pairing.t1);
            if (t) ids.push(t.teamId);
        }
        if (pairing.t2Type === 'T') {
            const t = teams.find(tm => tm.divRank === pairing.t2);
            if (t) ids.push(t.teamId);
        }
        return ids;
    }

    /** Check if any team in `teamIds` already has a game in this row. Returns team name if clash found, null otherwise. */
    private findTimeClashInRow(row: ScheduleGridRow, teamIds: string[], excludeGid?: number): string | null {
        for (const cell of row.cells) {
            if (!cell) continue;
            if (excludeGid != null && cell.gid === excludeGid) continue;
            for (const tid of teamIds) {
                if (cell.t1Id === tid || cell.t2Id === tid) {
                    const team = this.divisionTeams().find(t => t.teamId === tid);
                    return team?.teamName ?? 'A team';
                }
            }
        }
        return null;
    }

    // ── Helpers ──

    agTeamCount(ag: AgegroupWithDivisionsDto): number {
        return ag.divisions.reduce((sum, d) => sum + d.teamCount, 0);
    }

    /** Returns an opaque tinted background: 12% agegroup color over solid body-bg. */
    agBg(hex: string | null | undefined): string {
        if (!hex || hex.length < 7 || hex[0] !== '#')
            return 'var(--bs-body-bg)';
        const r = parseInt(hex.slice(1, 3), 16);
        const g = parseInt(hex.slice(3, 5), 16);
        const b = parseInt(hex.slice(5, 7), 16);
        return `linear-gradient(rgba(${r}, ${g}, ${b}, 0.12), rgba(${r}, ${g}, ${b}, 0.12)), var(--bs-body-bg)`;
    }

    /** Returns '#fff' or '#000' for WCAG-compliant contrast against a hex background. */
    contrastText(hex: string | null | undefined): string {
        if (!hex || hex.length < 7 || hex[0] !== '#') return 'var(--bs-secondary-color)';
        const r = parseInt(hex.slice(1, 3), 16);
        const g = parseInt(hex.slice(3, 5), 16);
        const b = parseInt(hex.slice(5, 7), 16);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.55 ? '#000' : '#fff';
    }

    formatTime(gDate: string | Date): string {
        const d = new Date(gDate);
        return d.toLocaleString('en-US', {
            weekday: 'short',
            month: 'short',
            day: 'numeric',
            hour: 'numeric',
            minute: '2-digit'
        });
    }

    formatDate(gDate: string | Date): string {
        const d = new Date(gDate);
        return d.toLocaleDateString('en-US', {
            weekday: 'short',
            month: 'short',
            day: 'numeric'
        });
    }

    formatTimeOnly(gDate: string | Date): string {
        const d = new Date(gDate);
        return d.toLocaleTimeString('en-US', {
            hour: 'numeric',
            minute: '2-digit'
        });
    }

    /** Format team designator: pool play → "2", bracket → "Y1", "S4", "F1", etc. */
    teamDes(type: string, num: number | undefined | null): string {
        if (num == null) return type;
        return type === 'T' ? `${num}` : `${type}${num}`;
    }
}
