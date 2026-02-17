import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
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
import { formatTime, teamDes } from '../shared/utils/scheduling-helpers';
import { DivisionNavigatorComponent } from '../shared/components/division-navigator/division-navigator.component';
import { ScheduleGridComponent } from '../shared/components/schedule-grid/schedule-grid.component';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';

@Component({
    selector: 'app-schedule-division',
    standalone: true,
    imports: [CommonModule, FormsModule, TsicDialogComponent, DivisionNavigatorComponent, ScheduleGridComponent],
    templateUrl: './schedule-division.component.html',
    styleUrl: './schedule-division.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScheduleDivisionComponent implements OnInit {
    private readonly svc = inject(ScheduleDivisionService);
    private readonly toast = inject(ToastService);

    @ViewChild('scheduleGrid') scheduleGrid?: ScheduleGridComponent;
    @ViewChild('rapidFieldInput') rapidFieldInputEl?: ElementRef<HTMLInputElement>;

    // ── Navigator state ──
    readonly agegroups = signal<AgegroupWithDivisionsDto[]>([]);
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
    readonly placementMode = signal<'mouse' | 'keyboard'>(this.loadPlacementMode());
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
    readonly allPairingsScheduled = computed(() => this.pairings().length > 0 && this.pairings().every(p => !p.bAvailable));
    readonly remainingPairingsCount = computed(() => this.pairings().filter(p => p.bAvailable).length);

    // ── Rapid-placement modal ──

    readonly showRapidModal = signal(false);
    readonly rapidPairing = signal<PairingDto | null>(null);
    readonly rapidFieldFilter = signal('');
    readonly rapidTimeFilter = signal('');
    readonly rapidFieldIndex = signal(-1);
    readonly rapidTimeIndex = signal(-1);
    readonly rapidFieldOpen = signal(false);
    readonly rapidTimeOpen = signal(false);
    readonly rapidSelectedField = signal<ScheduleFieldColumn | null>(null);
    readonly rapidSelectedTime = signal<{ gDate: string; label: string; rowIndex: number } | null>(null);

    readonly rapidFieldsFiltered = computed(() => {
        const filter = this.rapidFieldFilter().toLowerCase();
        const fields = this.gridColumns();
        if (!filter) return fields;
        return fields.filter(f => f.fName.toLowerCase().includes(filter));
    });

    readonly rapidOpenSlots = computed(() => {
        const rows = this.gridRows();
        const fields = this.gridColumns();
        const selectedFieldId = this.rapidSelectedField()?.fieldId;
        if (!selectedFieldId) return [];

        const colIdx = fields.findIndex(f => f.fieldId === selectedFieldId);
        if (colIdx < 0) return [];

        return rows
            .map((r, i) => ({ row: r, rowIndex: i }))
            .filter(({ row }) => !row.cells[colIdx])
            .map(({ row, rowIndex }) => ({
                gDate: row.gDate,
                label: this.formatTime(row.gDate),
                rowIndex
            }));
    });

    readonly rapidTimesFiltered = computed(() => {
        const filter = this.rapidTimeFilter().toLowerCase();
        const slots = this.rapidOpenSlots();
        if (!filter) return slots;
        return slots.filter(s => s.label.toLowerCase().includes(filter));
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

    onDivisionSelected(event: { division: DivisionSummaryDto; agegroupId: string }): void {
        this.selectedDivision.set(event.division);
        this.selectedAgegroupId.set(event.agegroupId);
        this.selectedPairing.set(null);
        this.selectedGame.set(null);
        this.showDeleteDivConfirm.set(false);
        this.loadDivisionData(event.division.divId, event.agegroupId);
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
                this.scheduleGrid?.scrollToFirstRelevant(divId);
            },
            error: () => {
                this.gridResponse.set(null);
                this.isGridLoading.set(false);
            }
        });
    }

    // ── Placement Workflow ──

    private loadPlacementMode(): 'mouse' | 'keyboard' {
        const stored = localStorage.getItem(LocalStorageKey.SchedulePlacementMode);
        return stored === 'keyboard' ? 'keyboard' : 'mouse';
    }

    setPlacementMode(mode: 'mouse' | 'keyboard'): void {
        this.placementMode.set(mode);
        localStorage.setItem(LocalStorageKey.SchedulePlacementMode, mode);
    }

    onPairingClick(pairing: PairingDto): void {
        if (this.placementMode() === 'keyboard') {
            this.openRapidModalFor(pairing);
        } else {
            this.selectPairingForPlacement(pairing);
        }
    }

    selectPairingForPlacement(pairing: PairingDto): void {
        this.selectedGame.set(null);
        if (this.selectedPairing()?.ai === pairing.ai) {
            this.selectedPairing.set(null);
        } else {
            this.selectedPairing.set(pairing);
            this.scheduleGrid?.scrollToNextOpenSlot(0);
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

        // Pre-check: bracket enforcement (single-bracket agegroups)
        const bracketBlock = this.checkBracketPlacement(pairing);
        if (bracketBlock) {
            this.toast.show(bracketBlock, 'danger', 5000);
            return;
        }

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
                this.isPlacing.set(false);

                // Auto-advance to next unscheduled pairing
                const nextPairing = this.pairings().find(p => p.ai !== pairing.ai && p.bAvailable);
                this.selectedPairing.set(nextPairing ?? null);

                // Scroll to next open slot forward in time
                if (nextPairing) {
                    const rows = this.gridRows();
                    const placedRowIdx = rows.findIndex(r => r.gDate === row.gDate);
                    this.scheduleGrid?.scrollToNextOpenSlot(placedRowIdx + 1);
                }
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

    onGridCellClick(event: { row: ScheduleGridRow; colIndex: number; game: ScheduleGameDto | null }): void {
        if (this.selectedPairing()) {
            // Placement mode: clicking an empty slot places the game
            if (!event.game) {
                this.placeGame(event.row, event.colIndex);
            }
        } else if (this.selectedGame()) {
            // Move mode: clicking any cell (empty or occupied) moves/swaps
            if (!event.game || event.game.gid !== this.selectedGame()!.game.gid) {
                this.moveOrSwapGame(event.row, event.colIndex);
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

    // ── Bracket enforcement ──

    /**
     * Checks whether placing a bracket pairing from the current division is allowed.
     * When bChampionsByDivision is false/null (traditional single-bracket):
     *   ALL championship games for the agegroup must come from the SAME pool.
     *   If any bracket game already exists from a different division → block.
     * When bChampionsByDivision is true (per-division brackets):
     *   Each division independently owns its own bracket — no cross-division restriction.
     * Returns an error message string if blocked, null if allowed.
     */
    private checkBracketPlacement(pairing: PairingDto): string | null {
        const isBracket = pairing.t1Type !== 'T' || pairing.t2Type !== 'T';
        if (!isBracket) return null;

        const agId = this.selectedAgegroupId();
        const ag = this.agegroups().find(a => a.agegroupId === agId);
        if (!ag) return null;

        // Per-division brackets: each division manages its own bracket independently
        if (ag.bChampionsByDivision) return null;

        // Traditional single-bracket: all championship games must come from one pool
        const currentDivId = this.selectedDivision()?.divId;
        for (const row of this.gridRows()) {
            for (const cell of row.cells) {
                if (!cell) continue;
                if (cell.t1Type === 'T' && cell.t2Type === 'T') continue; // pool-play game
                if (cell.divId !== currentDivId) {
                    const ownerDiv = ag.divisions.find(d => d.divId === cell.divId);
                    return `Championship games for this agegroup are already being scheduled from ${ownerDiv?.divName ?? 'another pool'}. All bracket games must come from the same pool.`;
                }
            }
        }
        return null;
    }

    // ── Rapid-placement modal methods ──

    openRapidModal(): void {
        const first = this.pairings().find(p => p.bAvailable);
        if (!first) {
            this.toast.show('All pairings are already scheduled', 'info', 3000);
            return;
        }
        this.openRapidModalFor(first);
    }

    openRapidModalFor(pairing: PairingDto): void {
        this.rapidPairing.set(pairing);
        this.resetRapidSelections();
        this.showRapidModal.set(true);
        setTimeout(() => this.rapidFieldInputEl?.nativeElement.focus(), 100);
    }

    closeRapidModal(): void {
        this.showRapidModal.set(false);
        this.rapidPairing.set(null);
    }

    private resetRapidSelections(): void {
        this.rapidFieldFilter.set('');
        this.rapidTimeFilter.set('');
        this.rapidSelectedField.set(null);
        this.rapidSelectedTime.set(null);
        this.rapidFieldIndex.set(-1);
        this.rapidTimeIndex.set(-1);
        this.rapidFieldOpen.set(false);
        this.rapidTimeOpen.set(false);
    }

    onRapidFieldInput(event: Event): void {
        const val = (event.target as HTMLInputElement).value;
        this.rapidFieldFilter.set(val);
        this.rapidSelectedField.set(null);
        this.rapidSelectedTime.set(null);
        this.rapidTimeFilter.set('');
        this.rapidFieldIndex.set(0);
        this.rapidFieldOpen.set(true);
    }

    selectRapidField(field: ScheduleFieldColumn): void {
        this.rapidSelectedField.set(field);
        this.rapidFieldFilter.set(field.fName);
        this.rapidFieldOpen.set(false);
        // Auto-default time to first open slot for this field
        setTimeout(() => {
            const slots = this.rapidOpenSlots();
            if (slots.length > 0) {
                this.rapidSelectedTime.set(slots[0]);
                this.rapidTimeFilter.set(slots[0].label);
            }
        }, 0);
    }

    onRapidTimeInput(event: Event): void {
        const val = (event.target as HTMLInputElement).value;
        this.rapidTimeFilter.set(val);
        this.rapidSelectedTime.set(null);
        this.rapidTimeIndex.set(0);
        this.rapidTimeOpen.set(true);
    }

    selectRapidTime(slot: { gDate: string; label: string; rowIndex: number }): void {
        this.rapidSelectedTime.set(slot);
        this.rapidTimeFilter.set(slot.label);
        this.rapidTimeOpen.set(false);
    }

    onRapidFieldKeydown(event: KeyboardEvent): void {
        const items = this.rapidFieldsFiltered();
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            this.rapidFieldOpen.set(true);
            this.rapidFieldIndex.update(i => Math.min(i + 1, items.length - 1));
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            this.rapidFieldIndex.update(i => Math.max(i - 1, 0));
        } else if (event.key === 'Enter' && this.rapidFieldOpen()) {
            event.preventDefault();
            const idx = this.rapidFieldIndex();
            if (idx >= 0 && idx < items.length) {
                this.selectRapidField(items[idx]);
            }
        } else if (event.key === 'Tab' && this.rapidFieldOpen()) {
            const idx = this.rapidFieldIndex();
            if (idx >= 0 && idx < items.length) {
                this.selectRapidField(items[idx]);
            }
        }
    }

    onRapidTimeKeydown(event: KeyboardEvent): void {
        const items = this.rapidTimesFiltered();
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            this.rapidTimeOpen.set(true);
            this.rapidTimeIndex.update(i => Math.min(i + 1, items.length - 1));
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            this.rapidTimeIndex.update(i => Math.max(i - 1, 0));
        } else if (event.key === 'Enter') {
            event.preventDefault();
            if (this.rapidTimeOpen() && this.rapidTimeIndex() >= 0) {
                const idx = this.rapidTimeIndex();
                if (idx < items.length) {
                    this.selectRapidTime(items[idx]);
                }
            } else {
                this.rapidPlaceGame();
            }
        }
    }

    rapidFieldBlur(): void {
        setTimeout(() => this.rapidFieldOpen.set(false), 150);
    }

    rapidTimeBlur(): void {
        setTimeout(() => this.rapidTimeOpen.set(false), 150);
    }

    rapidPlaceGame(): void {
        const pairing = this.rapidPairing();
        const field = this.rapidSelectedField();
        const time = this.rapidSelectedTime();
        const div = this.selectedDivision();
        const agId = this.selectedAgegroupId();
        if (!pairing || !field || !time || !div || !agId) return;

        // Bracket enforcement check
        const bracketBlock = this.checkBracketPlacement(pairing);
        if (bracketBlock) {
            this.toast.show(bracketBlock, 'danger', 5000);
            return;
        }

        // Time-clash check
        const row = this.gridRows().find(r => r.gDate === time.gDate);
        if (row) {
            const teamIds = this.resolvePairingTeamIds(pairing);
            const clash = this.findTimeClashInRow(row, teamIds);
            if (clash) {
                this.toast.show(`Time clash: ${clash} is already playing at this timeslot`, 'danger', 4000);
                return;
            }
        }

        this.isPlacing.set(true);
        this.svc.placeGame({
            pairingAi: pairing.ai,
            gDate: time.gDate,
            fieldId: field.fieldId,
            agegroupId: agId,
            divId: div.divId
        }).subscribe({
            next: (game) => {
                // Update grid cell in place
                const colIdx = this.gridColumns().findIndex(c => c.fieldId === field.fieldId);
                this.gridResponse.update(grid => {
                    if (!grid) return grid;
                    const updatedRows = grid.rows.map((r: ScheduleGridRow) => {
                        if (r.gDate === time.gDate) {
                            const updatedCells = [...r.cells];
                            if (colIdx >= 0) updatedCells[colIdx] = game;
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
                this.isPlacing.set(false);

                // Auto-advance to next unscheduled pairing
                const nextPairing = this.pairings().find(p => p.bAvailable);
                if (nextPairing) {
                    this.rapidPairing.set(nextPairing);
                    this.resetRapidSelections();
                    // Smart default: keep same field, advance time
                    this.selectRapidField(field);
                    // Re-focus field input for next round
                    setTimeout(() => this.rapidFieldInputEl?.nativeElement.focus(), 100);
                } else {
                    this.toast.show('All pairings scheduled!', 'success', 3000);
                    this.closeRapidModal();
                }
            },
            error: () => this.isPlacing.set(false)
        });
    }

    // ── Helpers (delegated to shared utils) ──

    readonly formatTime = formatTime;
    readonly teamDes = teamDes;
}
