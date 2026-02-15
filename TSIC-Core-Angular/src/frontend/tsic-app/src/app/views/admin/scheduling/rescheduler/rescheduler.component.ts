import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorModule } from '@syncfusion/ej2-angular-richtexteditor';
import {
    ReschedulerService,
    type ScheduleFilterOptionsDto,
    type ScheduleGridResponse,
    type ScheduleGridRow,
    type ScheduleGameDto,
    type ReschedulerGridRequest,
    type AdjustWeatherResponse
} from './services/rescheduler.service';
import { formatGameDay } from '../shared/utils/scheduling-helpers';
import { ScheduleGridComponent } from '../shared/components/schedule-grid/schedule-grid.component';
import { CadtTreeFilterComponent, type CadtSelectionEvent } from '../shared/components/cadt-tree-filter/cadt-tree-filter.component';

@Component({
    selector: 'app-rescheduler',
    standalone: true,
    imports: [CommonModule, FormsModule, RichTextEditorModule, ScheduleGridComponent, CadtTreeFilterComponent],
    templateUrl: './rescheduler.component.html',
    styleUrl: './rescheduler.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReschedulerComponent implements OnInit {
    private readonly svc = inject(ReschedulerService);

    @ViewChild('scheduleGrid') scheduleGrid?: ScheduleGridComponent;

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

    // CADT tree: bridge between selection signals and shared component
    readonly cadtCheckedIds = computed(() => {
        const ids = new Set<string>();
        for (const n of this.selectedClubNames()) ids.add(`club:${n}`);
        for (const n of this.selectedAgegroupIds()) ids.add(`ag:${n}`);
        for (const n of this.selectedDivisionIds()) ids.add(`div:${n}`);
        for (const n of this.selectedTeamIds()) ids.add(`team:${n}`);
        return ids;
    });

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
    readonly clubs = computed(() => this.filterOptions()?.clubs ?? []);
    readonly gameDays = computed(() => this.filterOptions()?.gameDays ?? []);
    readonly fields = computed(() => this.filterOptions()?.fields ?? []);

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

    // ── CADT tree selection handler ──

    onCadtSelectionChange(event: CadtSelectionEvent): void {
        this.selectedClubNames.set(event.clubNames);
        this.selectedAgegroupIds.set(event.agegroupIds);
        this.selectedDivisionIds.set(event.divisionIds);
        this.selectedTeamIds.set(event.teamIds);
    }

    // ── Filter toggles ──

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

    onGridCellClick(event: { row: ScheduleGridRow; colIndex: number; game: ScheduleGameDto | null }): void {
        const moving = this.selectedGame();

        if (moving) {
            // Target cell clicked — execute move/swap
            const col = this.gridColumns()[event.colIndex];
            if (!col) return;

            // Don't move to the same slot
            if (event.game?.gid === moving.gid) {
                this.selectedGame.set(null);
                return;
            }

            this.svc.moveGame({
                gid: moving.gid,
                targetGDate: event.row.gDate,
                targetFieldId: col.fieldId
            }).subscribe({
                next: () => {
                    this.selectedGame.set(null);
                    this.loadGrid();
                },
                error: () => this.selectedGame.set(null)
            });
        } else if (event.game) {
            // No game selected — pick this game for move
            this.selectGameForMove(event.game);
        }
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

    // ── Helpers (delegated to shared utils) ──
    readonly formatGameDay = formatGameDay;
}
