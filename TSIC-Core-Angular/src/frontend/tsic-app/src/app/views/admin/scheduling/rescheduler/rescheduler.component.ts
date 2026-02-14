import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorModule } from '@syncfusion/ej2-angular-richtexteditor';
import {
    ReschedulerService,
    type ScheduleFilterOptionsDto,
    type ScheduleGridResponse,
    type ScheduleGridRow,
    type ScheduleFieldColumn,
    type ScheduleGameDto,
    type ReschedulerGridRequest,
    type AdjustWeatherResponse,
    type CadtClubNode,
    type CadtAgegroupNode,
    type CadtDivisionNode,
    type CadtTeamNode,
    type FieldSummaryDto
} from './services/rescheduler.service';

@Component({
    selector: 'app-rescheduler',
    standalone: true,
    imports: [CommonModule, FormsModule, RichTextEditorModule],
    templateUrl: './rescheduler.component.html',
    styleUrl: './rescheduler.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReschedulerComponent implements OnInit {
    private readonly svc = inject(ReschedulerService);

    // ── Filter state ──
    readonly filterOptions = signal<ScheduleFilterOptionsDto | null>(null);
    readonly isFiltersLoading = signal(false);

    // CADT multi-select
    readonly selectedClubNames = signal<string[]>([]);
    readonly selectedAgegroupIds = signal<string[]>([]);
    readonly selectedDivisionIds = signal<string[]>([]);
    readonly selectedTeamIds = signal<string[]>([]);
    readonly selectedGameDays = signal<string[]>([]);
    readonly selectedFieldIds = signal<string[]>([]);

    // CADT tree expand state
    readonly expandedClubs = signal<Set<string>>(new Set());
    readonly expandedAgegroups = signal<Set<string>>(new Set());
    readonly expandedDivisions = signal<Set<string>>(new Set());

    // ── Grid state ──
    readonly gridResponse = signal<ScheduleGridResponse | null>(null);
    readonly isGridLoading = signal(false);

    // ── Move/Swap workflow ──
    readonly selectedGame = signal<ScheduleGameDto | null>(null);

    // ── Add Timeslot ──
    readonly showAddTimeslot = signal(false);
    readonly newTimeslotDate = signal('');
    readonly newTimeslotTime = signal('');

    // ── Weather Modal ──
    readonly showWeatherModal = signal(false);
    readonly weatherPreFirstGame = signal('');
    readonly weatherPreGSI = signal(30);
    readonly weatherPostFirstGame = signal('');
    readonly weatherPostGSI = signal(30);
    readonly weatherFieldIds = signal<string[]>([]);
    readonly weatherAffectedCount = signal<number | null>(null);
    readonly isWeatherLoading = signal(false);
    readonly weatherResult = signal<AdjustWeatherResponse | null>(null);

    // ── Email Modal ──
    readonly showEmailModal = signal(false);
    readonly emailFirstGame = signal('');
    readonly emailLastGame = signal('');
    readonly emailFieldIds = signal<string[]>([]);
    readonly emailSubject = signal('');
    readonly emailBody = signal('');
    readonly emailRecipientCount = signal<number | null>(null);
    readonly isEmailLoading = signal(false);
    readonly emailSent = signal(false);
    readonly emailSentCount = signal(0);
    readonly emailFailedCount = signal(0);

    // ── Computed helpers ──
    readonly gridColumns = computed(() => this.gridResponse()?.columns ?? []);
    readonly gridRows = computed(() => this.gridResponse()?.rows ?? []);
    readonly clubs = computed(() => this.filterOptions()?.clubs ?? []);
    readonly gameDays = computed(() => this.filterOptions()?.gameDays ?? []);
    readonly fields = computed(() => this.filterOptions()?.fields ?? []);

    /** Conflict detection: same team in 2+ games at the same timeslot. */
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

    /** Back-to-back: same team in consecutive timeslot rows on the same day. */
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

    readonly breakingConflictCount = computed(() => {
        let count = this.timeClashGameIds().size;
        for (const row of this.gridRows()) {
            for (const cell of row.cells) {
                if (cell?.isSlotCollision) count++;
            }
        }
        return count;
    });

    readonly hasActiveFilters = computed(() =>
        this.selectedClubNames().length > 0 ||
        this.selectedAgegroupIds().length > 0 ||
        this.selectedDivisionIds().length > 0 ||
        this.selectedTeamIds().length > 0 ||
        this.selectedGameDays().length > 0 ||
        this.selectedFieldIds().length > 0
    );

    // ── RTE toolbar config ──
    readonly rteTools = {
        items: ['Bold', 'Italic', 'Underline', '|',
            'FontColor', 'BackgroundColor', '|',
            'OrderedList', 'UnorderedList', '|',
            'CreateLink', '|', 'Undo', 'Redo']
    };

    ngOnInit(): void {
        this.loadFilterOptions();
    }

    // ══════════════════════════════════════════════════════════════
    // Filter Options
    // ══════════════════════════════════════════════════════════════

    loadFilterOptions(): void {
        this.isFiltersLoading.set(true);
        this.svc.getFilterOptions().subscribe({
            next: (data) => {
                this.filterOptions.set(data);
                this.isFiltersLoading.set(false);
            },
            error: () => this.isFiltersLoading.set(false)
        });
    }

    // ── CADT tree toggle ──

    toggleClub(name: string): void {
        const s = new Set(this.expandedClubs());
        s.has(name) ? s.delete(name) : s.add(name);
        this.expandedClubs.set(s);
    }

    toggleAgegroup(id: string): void {
        const s = new Set(this.expandedAgegroups());
        s.has(id) ? s.delete(id) : s.add(id);
        this.expandedAgegroups.set(s);
    }

    toggleDivision(id: string): void {
        const s = new Set(this.expandedDivisions());
        s.has(id) ? s.delete(id) : s.add(id);
        this.expandedDivisions.set(s);
    }

    isClubExpanded(name: string): boolean { return this.expandedClubs().has(name); }
    isAgExpanded(id: string): boolean { return this.expandedAgegroups().has(id); }
    isDivExpanded(id: string): boolean { return this.expandedDivisions().has(id); }

    // ── Multi-select toggles ──

    toggleClubFilter(name: string): void {
        const curr = [...this.selectedClubNames()];
        const idx = curr.indexOf(name);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(name);
        this.selectedClubNames.set(curr);
    }

    toggleAgegroupFilter(id: string): void {
        const curr = [...this.selectedAgegroupIds()];
        const idx = curr.indexOf(id);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(id);
        this.selectedAgegroupIds.set(curr);
    }

    toggleDivisionFilter(id: string): void {
        const curr = [...this.selectedDivisionIds()];
        const idx = curr.indexOf(id);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(id);
        this.selectedDivisionIds.set(curr);
    }

    toggleTeamFilter(id: string): void {
        const curr = [...this.selectedTeamIds()];
        const idx = curr.indexOf(id);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(id);
        this.selectedTeamIds.set(curr);
    }

    toggleGameDayFilter(day: string): void {
        const curr = [...this.selectedGameDays()];
        const idx = curr.indexOf(day);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(day);
        this.selectedGameDays.set(curr);
    }

    toggleFieldFilter(id: string): void {
        const curr = [...this.selectedFieldIds()];
        const idx = curr.indexOf(id);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(id);
        this.selectedFieldIds.set(curr);
    }

    isClubSelected(name: string): boolean { return this.selectedClubNames().includes(name); }
    isAgSelected(id: string): boolean { return this.selectedAgegroupIds().includes(id); }
    isDivSelected(id: string): boolean { return this.selectedDivisionIds().includes(id); }
    isTeamSelected(id: string): boolean { return this.selectedTeamIds().includes(id); }
    isGameDaySelected(day: string): boolean { return this.selectedGameDays().includes(day); }
    isFieldSelected(id: string): boolean { return this.selectedFieldIds().includes(id); }

    clearFilters(): void {
        this.selectedClubNames.set([]);
        this.selectedAgegroupIds.set([]);
        this.selectedDivisionIds.set([]);
        this.selectedTeamIds.set([]);
        this.selectedGameDays.set([]);
        this.selectedFieldIds.set([]);
    }

    // ══════════════════════════════════════════════════════════════
    // Grid Load
    // ══════════════════════════════════════════════════════════════

    loadGrid(): void {
        this.isGridLoading.set(true);
        this.selectedGame.set(null);

        const request: ReschedulerGridRequest = {};
        if (this.selectedClubNames().length) request.clubNames = this.selectedClubNames();
        if (this.selectedAgegroupIds().length) request.agegroupIds = this.selectedAgegroupIds();
        if (this.selectedDivisionIds().length) request.divisionIds = this.selectedDivisionIds();
        if (this.selectedTeamIds().length) request.teamIds = this.selectedTeamIds();
        if (this.selectedGameDays().length) request.gameDays = this.selectedGameDays();
        if (this.selectedFieldIds().length) request.fieldIds = this.selectedFieldIds();

        // Inject additional timeslot if configured
        if (this.showAddTimeslot() && this.newTimeslotDate() && this.newTimeslotTime()) {
            request.additionalTimeslot = `${this.newTimeslotDate()}T${this.newTimeslotTime()}`;
        }

        this.svc.getGrid(request).subscribe({
            next: (data) => {
                this.gridResponse.set(data);
                this.isGridLoading.set(false);
            },
            error: () => this.isGridLoading.set(false)
        });
    }

    // ══════════════════════════════════════════════════════════════
    // Move/Swap Game
    // ══════════════════════════════════════════════════════════════

    selectGameForMove(game: ScheduleGameDto): void {
        if (this.selectedGame()?.gid === game.gid) {
            this.selectedGame.set(null);
        } else {
            this.selectedGame.set(game);
        }
    }

    onGridCellClick(row: ScheduleGridRow, colIndex: number): void {
        const cell = row.cells[colIndex];
        const moving = this.selectedGame();

        if (moving) {
            // Target cell clicked — execute move/swap
            const col = this.gridColumns()[colIndex];
            if (!col) return;

            // Don't move to the same slot
            if (cell?.gid === moving.gid) {
                this.selectedGame.set(null);
                return;
            }

            this.svc.moveGame({
                gid: moving.gid,
                targetGDate: row.gDate,
                targetFieldId: col.fieldId
            }).subscribe({
                next: () => {
                    this.selectedGame.set(null);
                    this.loadGrid();
                },
                error: () => this.selectedGame.set(null)
            });
        } else if (cell) {
            // No game selected — pick this game for move
            this.selectGameForMove(cell);
        }
    }

    isGameSelected(game: ScheduleGameDto): boolean {
        return this.selectedGame()?.gid === game.gid;
    }

    isBreaking(game: ScheduleGameDto): boolean {
        return (game.isSlotCollision === true) || this.timeClashGameIds().has(game.gid);
    }

    isBackToBack(game: ScheduleGameDto): boolean {
        return this.backToBackGameIds().has(game.gid);
    }

    isSlotCollision(game: ScheduleGameDto): boolean {
        return game.isSlotCollision === true;
    }

    isTimeClash(game: ScheduleGameDto): boolean {
        return this.timeClashGameIds().has(game.gid);
    }

    // ══════════════════════════════════════════════════════════════
    // Add Timeslot
    // ══════════════════════════════════════════════════════════════

    toggleAddTimeslot(): void {
        this.showAddTimeslot.update(v => !v);
    }

    addTimeslotAndReload(): void {
        if (this.newTimeslotDate() && this.newTimeslotTime()) {
            this.loadGrid();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Weather Adjustment Modal
    // ══════════════════════════════════════════════════════════════

    openWeatherModal(): void {
        this.weatherResult.set(null);
        this.weatherAffectedCount.set(null);
        this.weatherFieldIds.set([...this.selectedFieldIds()]);
        this.showWeatherModal.set(true);
    }

    closeWeatherModal(): void {
        this.showWeatherModal.set(false);
    }

    toggleWeatherField(id: string): void {
        const curr = [...this.weatherFieldIds()];
        const idx = curr.indexOf(id);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(id);
        this.weatherFieldIds.set(curr);
    }

    isWeatherFieldSelected(id: string): boolean {
        return this.weatherFieldIds().includes(id);
    }

    previewWeatherAffected(): void {
        if (!this.weatherPreFirstGame()) return;
        this.isWeatherLoading.set(true);
        this.svc.getAffectedCount(this.weatherPreFirstGame(), this.weatherFieldIds()).subscribe({
            next: (data) => {
                this.weatherAffectedCount.set(data.count);
                this.isWeatherLoading.set(false);
            },
            error: () => this.isWeatherLoading.set(false)
        });
    }

    executeWeatherAdjust(): void {
        this.isWeatherLoading.set(true);
        this.svc.adjustWeather({
            preFirstGame: this.weatherPreFirstGame(),
            preGSI: this.weatherPreGSI(),
            postFirstGame: this.weatherPostFirstGame(),
            postGSI: this.weatherPostGSI(),
            fieldIds: this.weatherFieldIds()
        }).subscribe({
            next: (result) => {
                this.weatherResult.set(result);
                this.isWeatherLoading.set(false);
                if (result.success) {
                    this.loadGrid();
                }
            },
            error: () => this.isWeatherLoading.set(false)
        });
    }

    // ══════════════════════════════════════════════════════════════
    // Email Modal
    // ══════════════════════════════════════════════════════════════

    openEmailModal(): void {
        this.emailSent.set(false);
        this.emailRecipientCount.set(null);
        this.emailFieldIds.set([...this.selectedFieldIds()]);
        this.showEmailModal.set(true);
    }

    closeEmailModal(): void {
        this.showEmailModal.set(false);
    }

    toggleEmailField(id: string): void {
        const curr = [...this.emailFieldIds()];
        const idx = curr.indexOf(id);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(id);
        this.emailFieldIds.set(curr);
    }

    isEmailFieldSelected(id: string): boolean {
        return this.emailFieldIds().includes(id);
    }

    previewRecipientCount(): void {
        if (!this.emailFirstGame() || !this.emailLastGame()) return;
        this.isEmailLoading.set(true);
        this.svc.getRecipientCount(this.emailFirstGame(), this.emailLastGame(), this.emailFieldIds()).subscribe({
            next: (data) => {
                this.emailRecipientCount.set(data.estimatedCount);
                this.isEmailLoading.set(false);
            },
            error: () => this.isEmailLoading.set(false)
        });
    }

    onRteChange(event: any): void {
        this.emailBody.set(event.value ?? '');
    }

    sendEmail(): void {
        if (!this.emailSubject() || !this.emailBody()) return;
        this.isEmailLoading.set(true);
        this.svc.emailParticipants({
            firstGame: this.emailFirstGame(),
            lastGame: this.emailLastGame(),
            emailSubject: this.emailSubject(),
            emailBody: this.emailBody(),
            fieldIds: this.emailFieldIds()
        }).subscribe({
            next: (result) => {
                this.emailSent.set(true);
                this.emailSentCount.set(result.recipientCount);
                this.emailFailedCount.set(result.failedCount);
                this.isEmailLoading.set(false);
            },
            error: () => this.isEmailLoading.set(false)
        });
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════

    formatDate(iso: string): string {
        const d = new Date(iso);
        return d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
    }

    formatTimeOnly(iso: string): string {
        const d = new Date(iso);
        return d.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
    }

    formatGameDay(iso: string): string {
        const d = new Date(iso);
        return d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
    }

    contrastText(color: string | null | undefined): string {
        if (!color) return 'var(--bs-body-color)';
        const hex = color.replace('#', '');
        if (hex.length < 6) return 'var(--bs-body-color)';
        const r = parseInt(hex.substring(0, 2), 16);
        const g = parseInt(hex.substring(2, 4), 16);
        const b = parseInt(hex.substring(4, 6), 16);
        const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
        return luminance > 0.5 ? '#000' : '#fff';
    }
}
