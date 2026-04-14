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
import { LadtTreeFilterComponent } from '../shared/components/ladt-tree-filter/ladt-tree-filter.component';
import { JobFilterTreeService } from '../../../core/services/job-filter-tree.service';
import type { CadtClubNode, LadtAgegroupNode } from '@core/api';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';

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
    private readonly jobFilterTreeSvc = inject(JobFilterTreeService);
    private readonly toast = inject(ToastService);

    @ViewChild('scheduleGrid') scheduleGrid?: ScheduleGridComponent;

    // ── Filter state ──
    readonly filterOptions = signal<ScheduleFilterOptionsDto | null>(null);
    readonly isFiltersLoading = signal(false);

    // ── Collapsible filter sections ──
    readonly ladtExpanded = signal(true);
    readonly cadtExpanded = signal(false);

    // LADT tree data + selection (3-level: Agegroup → Division → Team)
    readonly ladtTree = signal<LadtAgegroupNode[]>([]);
    readonly cadtTree = signal<CadtClubNode[]>([]);
    readonly ladtCheckedIds = signal<Set<string>>(new Set());
    private ladtLabelMap = new Map<string, string>(); // nodeId → display name
    private ladtParentMap = new Map<string, string>(); // nodeId → parentId

    // LADT-derived filter values
    readonly ladtAgegroupIds = signal<string[]>([]);
    readonly ladtDivisionIds = signal<string[]>([]);
    readonly ladtTeamIds = signal<string[]>([]);

    // CADT multi-select (raw node IDs from tree, e.g. "ag:ClubA|guid", "div:ClubA|guid")
    readonly cadtCheckedIds = signal<Set<string>>(new Set());
    readonly selectedGameDays = signal<string[]>([]);
    readonly selectedFieldIds = signal<string[]>([]);

    // ── Grid state ──
    readonly gridResponse = signal<ScheduleGridResponse | null>(null);
    readonly isGridLoading = signal(false);

    // ── Move/Swap workflow ──
    readonly selectedGame = signal<ScheduleGameDto | null>(null);
    readonly movedGameGid = signal<number | null>(null);
    private movedHighlightTimer: ReturnType<typeof setTimeout> | null = null;

    // ── Debounced filter → grid auto-load ──
    private filterDebounceTimer: ReturnType<typeof setTimeout> | null = null;
    private readonly FILTER_DEBOUNCE_MS = 350;

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
    readonly clubs = computed(() => this.cadtTree());
    readonly gameDays = computed(() => this.filterOptions()?.gameDays ?? []);
    readonly fields = computed(() => this.filterOptions()?.fields ?? []);

    readonly hasActiveFilters = computed(() =>
        this.ladtCheckedIds().size > 0 ||
        this.cadtCheckedIds().size > 0 ||
        this.selectedGameDays().length > 0 ||
        this.selectedFieldIds().length > 0
    );

    readonly isSingleDaySelected = computed(() => this.selectedGameDays().length === 1);

    // ── CADT label + parent maps (rebuilt when clubs data changes) ──
    private readonly cadtMaps = computed(() => {
        const labels = new Map<string, string>();
        const parents = new Map<string, string>();
        for (const club of this.clubs()) {
            const clubId = `club:${club.clubName}`;
            labels.set(clubId, club.clubName);
            for (const ag of club.agegroups) {
                const agId = `ag:${club.clubName}|${ag.agegroupId}`;
                labels.set(agId, ag.agegroupName);
                parents.set(agId, clubId);
                for (const div of ag.divisions) {
                    const divId = `div:${club.clubName}|${div.divId}`;
                    labels.set(divId, div.divName);
                    parents.set(divId, agId);
                    for (const team of div.teams) {
                        const teamId = `team:${team.teamId}`;
                        labels.set(teamId, team.teamName);
                        parents.set(teamId, divId);
                    }
                }
            }
        }
        return { labels, parents };
    });

    // ── Filter chips: only top-level selections (skip if parent is also checked) ──
    readonly filterChips = computed(() => {
        const chips: { label: string; source: 'ladt' | 'cadt' | 'day' | 'field'; id: string }[] = [];

        // LADT chips
        const ladtChecked = this.ladtCheckedIds();
        for (const id of ladtChecked) {
            const parentId = this.ladtParentMap.get(id);
            if (parentId && ladtChecked.has(parentId)) continue; // parent covers this
            const label = this.ladtLabelMap.get(id) ?? id;
            chips.push({ label, source: 'ladt', id });
        }

        // CADT chips
        const cadtChecked = this.cadtCheckedIds();
        const { labels: cadtLabels, parents: cadtParents } = this.cadtMaps();
        for (const id of cadtChecked) {
            const parentId = cadtParents.get(id);
            if (parentId && cadtChecked.has(parentId)) continue;
            const label = cadtLabels.get(id) ?? id;
            chips.push({ label, source: 'cadt', id });
        }

        // Day chips
        for (const day of this.selectedGameDays()) {
            chips.push({ label: formatGameDay(day), source: 'day', id: day });
        }

        // Field chips
        const allFields = this.fields();
        for (const fid of this.selectedFieldIds()) {
            const field = allFields.find(f => f.fieldId === fid);
            chips.push({ label: field?.fName ?? fid, source: 'field', id: fid });
        }

        return chips;
    });

    // ── Game / slot counts from grid response ──
    readonly gameCount = computed(() => {
        const rows = this.gridResponse()?.rows;
        if (!rows) return 0;
        let count = 0;
        for (const row of rows) {
            for (const cell of row.cells) {
                if (cell) count++;
            }
        }
        return count;
    });

    // ── RTE toolbar config ──
    readonly rteTools = {
        items: ['Bold', 'Italic', 'Underline', '|',
            'FontColor', 'BackgroundColor', '|',
            'OrderedList', 'UnorderedList', '|',
            'CreateLink', '|', 'Undo', 'Redo']
    };

    ngOnInit(): void {
        this.loadFilterOptions();
        this.loadJobFilterTree();
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

    // ── Unified CADT/LADT tree (single /api/job-filter-tree endpoint) ──

    private loadJobFilterTree(): void {
        this.jobFilterTreeSvc.getForJob().subscribe({
            next: (data) => {
                this.cadtTree.set(data.cadt);
                this.ladtTree.set(data.ladt);
                this.buildLadtMaps(data.ladt);
            }
        });
    }

    private buildLadtMaps(agegroups: LadtAgegroupNode[]): void {
        this.ladtLabelMap.clear();
        this.ladtParentMap.clear();
        for (const ag of agegroups) {
            const agId = `ag:${ag.agegroupId}`;
            this.ladtLabelMap.set(agId, ag.agegroupName);
            for (const div of ag.divisions ?? []) {
                const divId = `div:${div.divId}`;
                this.ladtLabelMap.set(divId, div.divName);
                this.ladtParentMap.set(divId, agId);
                for (const team of div.teams ?? []) {
                    const teamId = `team:${team.teamId}`;
                    this.ladtLabelMap.set(teamId, team.teamName);
                    this.ladtParentMap.set(teamId, divId);
                }
            }
        }
    }

    onLadtSelectionChange(checked: Set<string>): void {
        this.ladtCheckedIds.set(checked);
        const agIds: string[] = [];
        const divIds: string[] = [];
        const teamIds: string[] = [];
        for (const id of checked) {
            if (id.startsWith('ag:')) agIds.push(id.substring(3));
            else if (id.startsWith('div:')) divIds.push(id.substring(4));
            else if (id.startsWith('team:')) teamIds.push(id.substring(5));
        }
        this.ladtAgegroupIds.set(agIds);
        this.ladtDivisionIds.set(divIds);
        this.ladtTeamIds.set(teamIds);
        this.scheduleGridLoad();
    }

    toggleLadtSection(): void { this.ladtExpanded.update(v => !v); }
    toggleCadtSection(): void { this.cadtExpanded.update(v => !v); }

    // ── CADT tree selection handler ──

    onCadtSelectionChange(checked: Set<string>): void {
        this.cadtCheckedIds.set(checked);
        this.scheduleGridLoad();
    }

    // ── Filter toggles ──

    toggleGameDayFilter(day: string): void {
        const curr = [...this.selectedGameDays()];
        const idx = curr.indexOf(day);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(day);
        this.selectedGameDays.set(curr);
        this.scheduleGridLoad();
    }

    toggleFieldFilter(id: string): void {
        const curr = [...this.selectedFieldIds()];
        const idx = curr.indexOf(id);
        idx >= 0 ? curr.splice(idx, 1) : curr.push(id);
        this.selectedFieldIds.set(curr);
        this.scheduleGridLoad();
    }

    isGameDaySelected(day: string): boolean { return this.selectedGameDays().includes(day); }
    isFieldSelected(id: string): boolean { return this.selectedFieldIds().includes(id); }

    removeFilterChip(chip: { source: string; id: string }): void {
        if (chip.source === 'ladt') {
            const checked = new Set(this.ladtCheckedIds());
            this.pruneByAncestor(checked, chip.id, this.ladtParentMap);
            this.onLadtSelectionChange(checked);
        } else if (chip.source === 'cadt') {
            const checked = new Set(this.cadtCheckedIds());
            this.pruneByAncestor(checked, chip.id, this.cadtMaps().parents);
            this.onCadtSelectionChange(checked);
        } else if (chip.source === 'day') {
            this.selectedGameDays.set(this.selectedGameDays().filter(d => d !== chip.id));
            this.scheduleGridLoad();
        } else if (chip.source === 'field') {
            this.selectedFieldIds.set(this.selectedFieldIds().filter(f => f !== chip.id));
            this.scheduleGridLoad();
        }
    }

    /** Remove a node and every descendant from a checked set via parent-map traversal. */
    private pruneByAncestor(checked: Set<string>, nodeId: string, parents: Map<string, string>): void {
        checked.delete(nodeId);
        const toRemove: string[] = [];
        for (const id of checked) {
            let cur: string | undefined = id;
            while (cur) {
                if (cur === nodeId) { toRemove.push(id); break; }
                cur = parents.get(cur);
            }
        }
        for (const id of toRemove) checked.delete(id);
    }

    clearFilters(): void {
        this.ladtCheckedIds.set(new Set());
        this.ladtAgegroupIds.set([]);
        this.ladtDivisionIds.set([]);
        this.ladtTeamIds.set([]);
        this.cadtCheckedIds.set(new Set());
        this.selectedGameDays.set([]);
        this.selectedFieldIds.set([]);
        this.scheduleGridLoad();
    }

    // ══════════════════════════════════════════════════════════════
    // Grid Load
    // ══════════════════════════════════════════════════════════════

    private buildGridRequest(): ReschedulerGridRequest {
        const request: ReschedulerGridRequest = {};

        // Parse CADT node IDs (club-scoped: ag:{clubName}|{guid}, div:{clubName}|{guid})
        const cadtClubs: string[] = [];
        const cadtAgs: string[] = [];
        const cadtDivs: string[] = [];
        const cadtTeams: string[] = [];
        for (const id of this.cadtCheckedIds()) {
            if (id.startsWith('club:')) {
                cadtClubs.push(id.substring(5));
            } else if (id.startsWith('ag:')) {
                const raw = id.substring(3);
                const pipe = raw.indexOf('|');
                cadtAgs.push(pipe >= 0 ? raw.substring(pipe + 1) : raw);
            } else if (id.startsWith('div:')) {
                const raw = id.substring(4);
                const pipe = raw.indexOf('|');
                cadtDivs.push(pipe >= 0 ? raw.substring(pipe + 1) : raw);
            } else if (id.startsWith('team:')) {
                cadtTeams.push(id.substring(5));
            }
        }

        if (cadtClubs.length) request.clubNames = cadtClubs;

        // Merge LADT + CADT agegroup/division/team selections (OR-union)
        const agIds = [...new Set([...this.ladtAgegroupIds(), ...cadtAgs])];
        const divIds = [...new Set([...this.ladtDivisionIds(), ...cadtDivs])];
        const teamIds = [...new Set([...this.ladtTeamIds(), ...cadtTeams])];
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

    /** Debounced grid reload — call from any filter change handler */
    private scheduleGridLoad(): void {
        if (this.filterDebounceTimer) clearTimeout(this.filterDebounceTimer);
        this.filterDebounceTimer = setTimeout(() => this.loadGrid(), this.FILTER_DEBOUNCE_MS);
    }

    loadGrid(): void {
        if (!this.hasActiveFilters()) {
            this.gridResponse.set(null);
            this.selectedGame.set(null);
            return;
        }
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
