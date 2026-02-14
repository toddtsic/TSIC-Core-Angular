import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
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

    readonly availablePairings = computed(() =>
        this.pairings().filter(p => p.bAvailable)
    );

    readonly scheduledPairings = computed(() =>
        this.pairings().filter(p => !p.bAvailable)
    );

    /** Set of Gids that have a same-team-same-date conflict in the grid. */
    readonly conflictedGameIds = computed(() => {
        const rows = this.gridRows();
        // Build map: dateKey → list of { teamId, gid }
        const dateTeams = new Map<string, { teamId: string; gid: number }[]>();

        for (const row of rows) {
            const dateKey = new Date(row.gDate).toDateString();
            for (const cell of row.cells) {
                if (!cell) continue;
                if (!dateTeams.has(dateKey)) dateTeams.set(dateKey, []);
                const entries = dateTeams.get(dateKey)!;
                if (cell.t1Id) entries.push({ teamId: cell.t1Id, gid: cell.gid });
                if (cell.t2Id) entries.push({ teamId: cell.t2Id, gid: cell.gid });
            }
        }

        const conflicted = new Set<number>();
        for (const entries of dateTeams.values()) {
            const seen = new Map<string, number[]>();
            for (const { teamId, gid } of entries) {
                if (!seen.has(teamId)) seen.set(teamId, []);
                seen.get(teamId)!.push(gid);
            }
            for (const gids of seen.values()) {
                if (gids.length > 1) gids.forEach(g => conflicted.add(g));
            }
        }
        return conflicted;
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

    selectDivision(div: DivisionSummaryDto, agegroupId: string): void {
        this.selectedDivision.set(div);
        this.selectedAgegroupId.set(agegroupId);
        this.selectedPairing.set(null);
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
        // If a pairing is selected for placement, ignore game clicks
        if (this.selectedPairing()) return;

        if (this.selectedGame()?.game.gid === game.gid) {
            this.selectedGame.set(null);
        } else {
            this.selectedGame.set({ game, row, colIndex });
        }
    }

    isGameSelected(game: ScheduleGameDto): boolean {
        return this.selectedGame()?.game.gid === game.gid;
    }

    isConflicted(game: ScheduleGameDto): boolean {
        return this.conflictedGameIds().has(game.gid);
    }

    moveOrSwapGame(targetRow: ScheduleGridRow, targetColIndex: number): void {
        const source = this.selectedGame();
        if (!source) return;

        const targetColumn = this.gridColumns()[targetColIndex];
        if (!targetColumn) return;

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
            // Move mode: clicking any cell moves/swaps
            this.moveOrSwapGame(row, colIndex);
        } else if (cell) {
            // No active selection: clicking a game selects it for move
            this.selectGameForMove(cell, row, colIndex);
        }
    }

    // ── Helpers ──

    agTeamCount(ag: AgegroupWithDivisionsDto): number {
        return ag.divisions.reduce((sum, d) => sum + d.teamCount, 0);
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
}
