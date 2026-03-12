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
import { findTimeClashInRow } from '../shared/utils/conflict-detection';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleGridComponent } from '../shared/components/schedule-grid/schedule-grid.component';
import { CadtTreeFilterComponent } from '../shared/components/cadt-tree-filter/cadt-tree-filter.component';
import { LadtTreeFilterComponent } from '../../registration-search/components/ladt-tree-filter.component';
import { LadtService } from '../../ladt-editor/services/ladt.service';
import type { LadtTreeNodeDto } from '@core/api';
import { TsicDialogComponent } from '../../../../shared-ui/components/tsic-dialog/tsic-dialog.component';

@Component({
    selector: 'app-rescheduler',
    standalone: true,
    imports: [CommonModule, FormsModule, RichTextEditorModule, ScheduleGridComponent, CadtTreeFilterComponent, LadtTreeFilterComponent, TsicDialogComponent],
    templateUrl: './rescheduler.component.html',
    styleUrl: './rescheduler.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReschedulerComponent implements OnInit {
    private readonly svc = inject(ReschedulerService);
    private readonly ladtSvc = inject(LadtService);
    private readonly toast = inject(ToastService);

    @ViewChild('scheduleGrid') scheduleGrid?: ScheduleGridComponent;

    // ── Filter state ──
    readonly filterOptions = signal<ScheduleFilterOptionsDto | null>(null);
    readonly isFiltersLoading = signal(false);

    // ── Collapsible filter sections ──
    readonly ladtExpanded = signal(true);
    readonly cadtExpanded = signal(false);

    // LADT tree data + selection
    readonly ladtTree = signal<LadtTreeNodeDto[]>([]);
    readonly ladtCheckedIds = signal<Set<string>>(new Set());
    private ladtLevelMap = new Map<string, number>(); // nodeId → level (0=League,1=AG,2=Div,3=Team)

    // LADT-derived filter values
    readonly ladtAgegroupIds = signal<string[]>([]);
    readonly ladtDivisionIds = signal<string[]>([]);
    readonly ladtTeamIds = signal<string[]>([]);

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
    readonly movedGameGid = signal<number | null>(null);
    private movedHighlightTimer: ReturnType<typeof setTimeout> | null = null;

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
        this.ladtCheckedIds().size > 0 ||
        this.selectedClubNames().length > 0 ||
        this.selectedAgegroupIds().length > 0 ||
        this.selectedDivisionIds().length > 0 ||
        this.selectedTeamIds().length > 0 ||
        this.selectedGameDays().length > 0 ||
        this.selectedFieldIds().length > 0
    );

    readonly isSingleDaySelected = computed(() => this.selectedGameDays().length === 1);

    // ── RTE toolbar config ──
    readonly rteTools = {
        items: ['Bold', 'Italic', 'Underline', '|',
            'FontColor', 'BackgroundColor', '|',
            'OrderedList', 'UnorderedList', '|',
            'CreateLink', '|', 'Undo', 'Redo']
    };

    ngOnInit(): void {
        this.loadFilterOptions();
        this.loadLadtTree();
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

    // ── LADT tree ──

    private loadLadtTree(): void {
        this.ladtSvc.getTree().subscribe({
            next: (root) => {
                const filtered = this.filterLadtTree(root.leagues);
                this.ladtTree.set(filtered);
                this.buildLadtLevelMap(filtered);
            }
        });
    }

    /** Strip scheduling-irrelevant nodes: Dropped/Waitlist agegroups, Unassigned divisions, empty divisions */
    private filterLadtTree(leagues: LadtTreeNodeDto[]): LadtTreeNodeDto[] {
        return leagues.map(league => {
            const agegroups = ((league.children ?? []) as LadtTreeNodeDto[])
                .filter(ag => {
                    const upper = ag.name.toUpperCase();
                    return !upper.startsWith('DROPPED') && !upper.startsWith('WAITLIST');
                })
                .map(ag => {
                    const divisions = ((ag.children ?? []) as LadtTreeNodeDto[])
                        .filter(div =>
                            div.name.toUpperCase() !== 'UNASSIGNED' && div.teamCount > 0
                        );
                    return { ...ag, children: divisions } as LadtTreeNodeDto;
                })
                .filter(ag => ((ag.children ?? []) as LadtTreeNodeDto[]).length > 0);
            return { ...league, children: agegroups } as LadtTreeNodeDto;
        }).filter(league => ((league.children ?? []) as LadtTreeNodeDto[]).length > 0);
    }

    private buildLadtLevelMap(nodes: LadtTreeNodeDto[]): void {
        this.ladtLevelMap.clear();
        const recurse = (items: LadtTreeNodeDto[]) => {
            for (const node of items) {
                this.ladtLevelMap.set(node.id, node.level);
                if (node.children?.length) recurse(node.children as LadtTreeNodeDto[]);
            }
        };
        recurse(nodes);
    }

    onLadtSelectionChange(checked: Set<string>): void {
        this.ladtCheckedIds.set(checked);
        const agIds: string[] = [];
        const divIds: string[] = [];
        const teamIds: string[] = [];
        for (const id of checked) {
            const level = this.ladtLevelMap.get(id);
            if (level === 1) agIds.push(id);
            else if (level === 2) divIds.push(id);
            else if (level === 3) teamIds.push(id);
            // level 0 (league) is ignored — its agegroups are already checked via cascade
        }
        this.ladtAgegroupIds.set(agIds);
        this.ladtDivisionIds.set(divIds);
        this.ladtTeamIds.set(teamIds);
    }

    toggleLadtSection(): void { this.ladtExpanded.update(v => !v); }
    toggleCadtSection(): void { this.cadtExpanded.update(v => !v); }

    // ── CADT tree selection handler ──

    onCadtSelectionChange(checked: Set<string>): void {
        const clubNames: string[] = [];
        const agegroupIds: string[] = [];
        const divisionIds: string[] = [];
        const teamIds: string[] = [];
        for (const id of checked) {
            if (id.startsWith('club:')) clubNames.push(id.substring(5));
            else if (id.startsWith('ag:')) agegroupIds.push(id.substring(3));
            else if (id.startsWith('div:')) divisionIds.push(id.substring(4));
            else if (id.startsWith('team:')) teamIds.push(id.substring(5));
        }
        this.selectedClubNames.set(clubNames);
        this.selectedAgegroupIds.set(agegroupIds);
        this.selectedDivisionIds.set(divisionIds);
        this.selectedTeamIds.set(teamIds);
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
        this.ladtCheckedIds.set(new Set());
        this.ladtAgegroupIds.set([]);
        this.ladtDivisionIds.set([]);
        this.ladtTeamIds.set([]);
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

    private buildGridRequest(): ReschedulerGridRequest {
        const request: ReschedulerGridRequest = {};
        if (this.selectedClubNames().length) request.clubNames = this.selectedClubNames();

        // Merge LADT + CADT agegroup/division/team selections (OR-union)
        const agIds = [...new Set([...this.ladtAgegroupIds(), ...this.selectedAgegroupIds()])];
        const divIds = [...new Set([...this.ladtDivisionIds(), ...this.selectedDivisionIds()])];
        const teamIds = [...new Set([...this.ladtTeamIds(), ...this.selectedTeamIds()])];
        if (agIds.length) request.agegroupIds = agIds;
        if (divIds.length) request.divisionIds = divIds;
        if (teamIds.length) request.teamIds = teamIds;

        if (this.selectedGameDays().length) request.gameDays = this.selectedGameDays();
        if (this.selectedFieldIds().length) request.fieldIds = this.selectedFieldIds();
        if (this.showAddTimeslot() && this.newTimeslotDate() && this.newTimeslotTime()) {
            request.additionalTimeslot = `${this.newTimeslotDate()}T${this.newTimeslotTime()}`;
        }
        return request;
    }

    loadGrid(): void {
        this.isGridLoading.set(true);
        this.selectedGame.set(null);

        this.svc.getGrid(this.buildGridRequest()).subscribe({
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

            // Time clash check (empty target only — swaps vacate the slot)
            if (!event.game) {
                const teamIds = [moving.t1Id, moving.t2Id].filter((id): id is string => !!id);
                const clash = findTimeClashInRow(event.row, teamIds, moving.gid);
                if (clash) {
                    this.toast.show(`Time clash: ${clash} is already playing at this timeslot`, 'danger', 4000);
                    return;
                }
            }

            const movedGid = moving.gid;
            this.svc.moveGame({
                gid: movedGid,
                targetGDate: event.row.gDate,
                targetFieldId: col.fieldId
            }).subscribe({
                next: () => {
                    this.selectedGame.set(null);
                    this.highlightMovedGame(movedGid);
                },
                error: () => this.selectedGame.set(null)
            });
        } else if (event.game) {
            // No game selected — pick this game for move
            this.selectGameForMove(event.game);
        }
    }

    private highlightMovedGame(gid: number): void {
        if (this.movedHighlightTimer) clearTimeout(this.movedHighlightTimer);
        this.movedGameGid.set(gid);
        this.isGridLoading.set(true);

        this.svc.getGrid(this.buildGridRequest()).subscribe({
            next: (data) => {
                this.gridResponse.set(data);
                this.isGridLoading.set(false);
                this.scheduleGrid?.scrollToGame(gid);
                this.movedHighlightTimer = setTimeout(() => this.movedGameGid.set(null), 3000);
            },
            error: () => this.isGridLoading.set(false)
        });
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
