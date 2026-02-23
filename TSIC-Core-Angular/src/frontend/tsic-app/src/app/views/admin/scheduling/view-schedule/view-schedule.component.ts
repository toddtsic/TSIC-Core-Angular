import {
    ChangeDetectionStrategy, Component, inject, OnInit, signal, computed
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../../../infrastructure/services/auth.service';
import { JobService } from '../../../../infrastructure/services/job.service';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import type {
    ScheduleFilterOptionsDto,
    ScheduleFilterRequest,
    ScheduleCapabilitiesDto,
    CadtClubNode,
    ViewGameDto,
    StandingsByDivisionResponse,
    DivisionBracketResponse,
    ContactDto,
    TeamResultDto,
    FieldDisplayDto,
    EditScoreRequest,
    EditGameRequest,
    TeamResultsResponse
} from '@core/api';
import { ViewScheduleService } from './services/view-schedule.service';
import { CadtTreeFilterComponent, type CadtSelectionEvent } from '../shared/components/cadt-tree-filter/cadt-tree-filter.component';
import { GamesTabComponent } from './components/games-tab.component';
import { StandingsTabComponent } from './components/standings-tab.component';
import { BracketsTabComponent } from './components/brackets-tab.component';
import { ContactsTabComponent } from './components/contacts-tab.component';
import { TeamResultsModalComponent } from './components/team-results-modal.component';
import { EditGameModalComponent } from './components/edit-game-modal.component';
import { TsicDialogComponent } from '../../../../shared-ui/components/tsic-dialog/tsic-dialog.component';

type TabId = 'games' | 'standings' | 'brackets' | 'contacts';

interface FilterChip {
    category: string;
    label: string;
    type: 'cadt' | 'gameDay' | 'unscored';
    nodeId?: string;
}

@Component({
    selector: 'app-view-schedule',
    standalone: true,
    imports: [
        FormsModule,
        CadtTreeFilterComponent,
        GamesTabComponent,
        StandingsTabComponent,
        BracketsTabComponent,
        ContactsTabComponent,
        TeamResultsModalComponent,
        EditGameModalComponent,
        TsicDialogComponent
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="view-schedule-page">
            <!-- Header -->
            <div class="page-header">
                <h1 class="page-title">Schedule</h1>
                @if (eventName()) {
                    <p class="page-subtitle">{{ eventName() }}</p>
                }
            </div>

            <!-- Toolbar: filter trigger + segment tabs -->
            <div class="toolbar">
                <button class="filter-trigger"
                        (click)="filterModalVisible.set(true)"
                        aria-label="Open filters">
                    <i class="bi bi-funnel"></i>
                    @if (activeFilterCount() > 0) {
                        <span class="filter-badge">{{ activeFilterCount() }}</span>
                    }
                </button>

                <div class="segment-tabs" role="tablist">
                    <button class="segment-btn"
                            [class.active]="activeTab() === 'games'"
                            (click)="switchTab('games')" role="tab">Games</button>
                    <button class="segment-btn"
                            [class.active]="activeTab() === 'standings'"
                            (click)="switchTab('standings')" role="tab">Standings</button>
                    <button class="segment-btn"
                            [class.active]="activeTab() === 'brackets'"
                            (click)="switchTab('brackets')" role="tab">Brackets</button>
                    @if (!capabilities()?.hideContacts) {
                        <button class="segment-btn"
                                [class.active]="activeTab() === 'contacts'"
                                (click)="switchTab('contacts')" role="tab">Contacts</button>
                    }
                </div>
            </div>

            <!-- Filter chips -->
            @if (activeFilterChips().length > 0) {
                <div class="filter-chips-strip">
                    @for (chip of activeFilterChips(); track chip.nodeId ?? chip.type) {
                        <span class="filter-chip">
                            <span class="chip-category">{{ chip.category }}:</span>
                            <span class="chip-label">{{ chip.label }}</span>
                            <button type="button" class="chip-remove"
                                    (click)="removeChip(chip)"
                                    aria-label="Remove filter">&times;</button>
                        </span>
                    }
                    <button type="button" class="chip-clear-all" (click)="clearFilters()">Clear All</button>
                </div>
            }

            <!-- Tab content (full width) -->
            <div class="tab-content">
                @switch (activeTab()) {
                    @case ('games') {
                        <app-games-tab
                            [games]="games()"
                            [canScore]="auth.isAdmin()"
                            [isLoading]="tabLoading()"
                            (quickScore)="onQuickScore($event)"
                            (editGame)="onEditGameOpen($event)"
                            (viewTeamResults)="onViewTeamResults($event)"
                            (viewFieldInfo)="onViewFieldInfo($event)" />
                    }
                    @case ('standings') {
                        <app-standings-tab
                            [standings]="standings()"
                            [records]="records()"
                            [isLoading]="tabLoading()"
                            (viewTeamResults)="onViewTeamResults($event)" />
                    }
                    @case ('brackets') {
                        <app-brackets-tab
                            [brackets]="brackets()"
                            [canScore]="auth.isAdmin()"
                            [isLoading]="tabLoading()"
                            (editBracketScore)="onBracketScoreEdit($event)"
                            (viewTeamResults)="onViewTeamResults($event)"
                            (viewFieldInfo)="onViewFieldInfo($event)" />
                    }
                    @case ('contacts') {
                        <app-contacts-tab
                            [contacts]="contacts()"
                            [isLoading]="tabLoading()" />
                    }
                }
            </div>
        </div>

        <!-- Filter Modal -->
        @if (filterModalVisible()) {
            <tsic-dialog size="sm" (requestClose)="closeFilterModal()">
                <div class="modal-content filter-modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">
                            <i class="bi bi-funnel me-2"></i>Filters
                        </h5>
                        <button class="btn-close" (click)="closeFilterModal()"></button>
                    </div>
                    <div class="modal-body filter-modal-body">
                        <!-- Game Days -->
                        @if (filterOptions()?.gameDays?.length) {
                            <div class="filter-group">
                                <label class="filter-group-label">Game Day</label>
                                <select class="form-select form-select-sm"
                                        [ngModel]="selectedGameDay()"
                                        (ngModelChange)="selectedGameDay.set($event); refreshTab()">
                                    <option value="">All Game Days</option>
                                    @for (day of filterOptions()!.gameDays; track day) {
                                        <option [value]="day">{{ formatGameDay(day) }}</option>
                                    }
                                </select>
                            </div>
                        }

                        <!-- Unscored toggle -->
                        <div class="filter-group">
                            <label class="filter-check">
                                <input type="checkbox"
                                       [ngModel]="unscoredOnly()"
                                       (ngModelChange)="unscoredOnly.set($event); refreshTab()" />
                                Show unscored games only
                            </label>
                        </div>

                        <!-- CADT Tree -->
                        @if (hasCadtData()) {
                            <div class="filter-group filter-group-tree">
                                <label class="filter-group-label">Teams</label>
                                <app-cadt-tree-filter
                                    [treeData]="filterOptions()?.clubs ?? []"
                                    [checkedIds]="checkedIds"
                                    (checkedIdsChange)="onCadtSelectionChange($event)" />
                            </div>
                        }
                    </div>
                    <div class="modal-footer">
                        <button class="btn btn-sm btn-outline-danger me-auto"
                                [disabled]="!hasActiveFilters()"
                                (click)="clearFilters()">
                            <i class="bi bi-x-circle me-1"></i>Reset
                        </button>
                        <button class="btn btn-sm btn-primary"
                                (click)="closeFilterModal()">Done</button>
                    </div>
                </div>
            </tsic-dialog>
        }

        <!-- Team Results Modal -->
        <app-team-results-modal
            [results]="teamResults()"
            [teamName]="teamResultsName()"
            [visible]="teamResultsVisible()"
            (close)="teamResultsVisible.set(false)"
            (viewOpponent)="onViewTeamResults($event)" />

        <!-- Edit Game Modal -->
        <app-edit-game-modal
            [game]="editingGame()"
            [visible]="editGameVisible()"
            (close)="editGameVisible.set(false)"
            (save)="onEditGameSave($event)" />

        <!-- Field Info Modal -->
        @if (fieldInfoVisible()) {
            <tsic-dialog size="sm" (requestClose)="fieldInfoVisible.set(false)">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">{{ fieldInfo()?.fName }}</h5>
                        <button class="btn-close" (click)="fieldInfoVisible.set(false)"></button>
                    </div>
                    <div class="modal-body">
                        @if (fieldInfo()?.address) {
                            <p class="mb-1">{{ fieldInfo()!.address }}</p>
                        }
                        @if (fieldInfo()?.city) {
                            <p class="mb-1">{{ fieldInfo()!.city }}</p>
                        }
                        @if (fieldInfo()?.state || fieldInfo()?.zip) {
                            <p class="mb-1">{{ fieldInfo()!.state ?? '' }} {{ fieldInfo()!.zip ?? '' }}</p>
                        }
                        @if (fieldInfo()?.directions) {
                            <p class="mb-0 text-muted" style="white-space:pre-wrap;">{{ fieldInfo()!.directions }}</p>
                        }
                        @if (fieldInfo()?.latitude && fieldInfo()?.longitude) {
                            <a href="https://www.google.com/maps?q={{ fieldInfo()!.latitude }},{{ fieldInfo()!.longitude }}"
                               target="_blank" rel="noopener"
                               class="btn btn-sm btn-outline-primary mt-2">
                                <i class="bi bi-geo-alt"></i> View Map
                            </a>
                        }
                    </div>
                </div>
            </tsic-dialog>
        }
    `,
    styles: [`
        .view-schedule-page {
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
            padding: var(--space-3);
            max-width: 1400px;
            margin: 0 auto;
        }

        /* ── Header ── */
        .page-header {
            text-align: center;
            padding: var(--space-2) 0;
        }

        .page-title {
            margin: 0;
            font-size: var(--font-size-3xl);
            font-weight: 700;
            color: var(--bs-body-color);
        }

        .page-subtitle {
            margin: var(--space-1) 0 0;
            font-size: var(--font-size-sm);
            color: var(--bs-secondary-color);
            font-weight: 400;
        }

        /* ── Toolbar ── */
        .toolbar {
            display: flex;
            align-items: center;
            gap: var(--space-3);
        }

        .filter-trigger {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            position: relative;
            width: 40px;
            height: 40px;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            background: var(--bs-card-bg);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-lg);
            cursor: pointer;
            flex-shrink: 0;
            transition: background 0.15s, border-color 0.15s, color 0.15s;
        }

        .filter-trigger:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        .filter-badge {
            position: absolute;
            top: -4px;
            right: -4px;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 18px;
            height: 18px;
            padding: 0 4px;
            background: var(--bs-primary);
            color: white;
            border-radius: var(--radius-full);
            font-size: 10px;
            font-weight: 700;
            line-height: 1;
        }

        /* ── Segment Tabs ── */
        .segment-tabs {
            display: inline-flex;
            background: var(--bs-tertiary-bg);
            border-radius: var(--radius-full);
            padding: 3px;
            gap: 2px;
        }

        .segment-btn {
            padding: var(--space-2) var(--space-4);
            border: none;
            border-radius: var(--radius-full);
            background: transparent;
            color: var(--bs-secondary-color);
            font-weight: 600;
            font-size: var(--font-size-sm);
            cursor: pointer;
            white-space: nowrap;
            transition: background 0.15s, color 0.15s, box-shadow 0.15s;
        }

        .segment-btn:hover:not(.active) {
            color: var(--bs-body-color);
            background: rgba(0, 0, 0, 0.04);
        }

        .segment-btn.active {
            background: var(--bs-primary);
            color: white;
            box-shadow: var(--shadow-sm);
        }

        /* ── Filter Chips ── */
        .filter-chips-strip {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-2);
            align-items: center;
        }

        .filter-chip {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-2);
            background: var(--bs-primary);
            color: var(--bs-white, #fff);
            border-radius: var(--radius-full);
            font-size: var(--font-size-xs);
            font-weight: 500;
            line-height: 1.2;
        }

        .chip-category {
            opacity: 0.8;
            font-weight: 600;
        }

        .chip-remove {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 16px;
            height: 16px;
            padding: 0;
            margin-left: 2px;
            background: transparent;
            border: none;
            border-radius: 50%;
            color: var(--bs-white, #fff);
            cursor: pointer;
            opacity: 0.7;
            font-size: var(--font-size-sm);
            line-height: 1;
            transition: opacity 0.15s, background 0.15s;
        }

        .chip-remove:hover {
            opacity: 1;
            background: rgba(255, 255, 255, 0.2);
        }

        .chip-clear-all {
            padding: var(--space-1) var(--space-2);
            background: transparent;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            font-size: var(--font-size-xs);
            font-weight: 500;
            color: var(--bs-secondary-color);
            cursor: pointer;
            transition: all 0.15s;
        }

        .chip-clear-all:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        /* ── Filter Modal ── */
        .filter-modal-content {
            display: flex;
            flex-direction: column;
            max-height: 80vh;
        }

        .filter-modal-body {
            display: flex;
            flex-direction: column;
            gap: var(--space-4);
            overflow-y: auto;
            flex: 1;
            min-height: 0;
        }

        .filter-group-tree {
            min-height: 0;
            flex-shrink: 1;
        }

        .filter-group {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
        }

        .filter-group-label {
            font-weight: 600;
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
        }

        .filter-check {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
            cursor: pointer;
        }

        .filter-check input {
            accent-color: var(--bs-primary);
        }

        /* ── Responsive ── */
        @media (max-width: 575px) {
            .view-schedule-page {
                padding: var(--space-2);
                gap: var(--space-2);
            }

            .page-title {
                font-size: var(--font-size-2xl);
            }

            .toolbar {
                flex-wrap: wrap;
            }

            .segment-tabs {
                flex: 1;
                min-width: 0;
                overflow-x: auto;
                scrollbar-width: none;
            }

            .segment-tabs::-webkit-scrollbar {
                display: none;
            }

            .segment-btn {
                padding: var(--space-1) var(--space-3);
                font-size: var(--font-size-xs);
            }
        }
    `]
})
export class ViewScheduleComponent implements OnInit {
    private readonly svc = inject(ViewScheduleService);
    private readonly route = inject(ActivatedRoute);
    protected readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);

    // ── Route state ──
    private jobPath: string | undefined;

    // ── Filter state ──
    readonly filterOptions = signal<ScheduleFilterOptionsDto | null>(null);
    readonly capabilities = signal<ScheduleCapabilitiesDto | null>(null);
    readonly filterModalVisible = signal(false);

    // CADT selection (mutable Set shared with child)
    checkedIds = new Set<string>();
    readonly cadtSelectionSignal = signal<CadtSelectionEvent>({
        clubNames: [], agegroupIds: [], divisionIds: [], teamIds: []
    });
    readonly selectedGameDay = signal('');
    readonly unscoredOnly = signal(false);

    // ── Tab state ──
    readonly activeTab = signal<TabId>('games');
    readonly tabLoading = signal(false);

    // Per-tab data
    readonly games = signal<ViewGameDto[]>([]);
    readonly standings = signal<StandingsByDivisionResponse | null>(null);
    readonly records = signal<StandingsByDivisionResponse | null>(null);
    readonly brackets = signal<DivisionBracketResponse[]>([]);
    readonly contacts = signal<ContactDto[]>([]);

    // Track which tabs have been loaded with current filters
    private loadedTabs = new Set<TabId>();

    // ── Modal state ──
    readonly teamResults = signal<TeamResultDto[]>([]);
    readonly teamResultsName = signal('');
    readonly teamResultsVisible = signal(false);

    readonly editingGame = signal<ViewGameDto | null>(null);
    readonly editGameVisible = signal(false);

    readonly fieldInfo = signal<FieldDisplayDto | null>(null);
    readonly fieldInfoVisible = signal(false);

    // ── Computed helpers ──

    readonly eventName = computed(() => this.jobService.currentJob()?.jobName ?? '');

    readonly hasCadtData = computed(() => (this.filterOptions()?.clubs?.length ?? 0) > 0);

    readonly cadtFilterCount = computed(() => {
        const s = this.cadtSelectionSignal();
        return s.clubNames.length + s.agegroupIds.length
            + s.divisionIds.length + s.teamIds.length;
    });

    readonly hasActiveFilters = computed(() => {
        return this.cadtFilterCount() > 0
            || this.selectedGameDay() !== ''
            || this.unscoredOnly();
    });

    readonly activeFilterCount = computed(() => {
        let count = this.cadtFilterCount();
        if (this.selectedGameDay()) count++;
        if (this.unscoredOnly()) count++;
        return count;
    });

    readonly activeFilterChips = computed<FilterChip[]>(() => {
        const chips: FilterChip[] = [];
        const opts = this.filterOptions();

        // CADT chips — show only the highest-level checked nodes
        if (opts?.clubs) {
            this.buildCadtChips(opts.clubs, chips);
        }

        // Game Day chip
        if (this.selectedGameDay()) {
            chips.push({
                category: 'Day',
                label: this.formatGameDay(this.selectedGameDay()),
                type: 'gameDay'
            });
        }

        // Unscored chip
        if (this.unscoredOnly()) {
            chips.push({ category: 'Filter', label: 'Unscored only', type: 'unscored' });
        }

        return chips;
    });

    ngOnInit(): void {
        // Determine if this is public mode (route data) and extract jobPath
        const data = this.route.snapshot.data;
        if (data['publicMode']) {
            this.jobPath = this.route.snapshot.params['jobPath'];
        }

        // Load filter options and capabilities in parallel
        this.svc.getFilterOptions(this.jobPath).subscribe(opts => {
            this.filterOptions.set(opts);
            // Debug: check if tree contains teams
            const totalTeams = (opts.clubs ?? []).reduce((sum, club) =>
                sum + (club.agegroups ?? []).reduce((s2, ag) =>
                    s2 + (ag.divisions ?? []).reduce((s3, div) =>
                        s3 + (div.teams?.length ?? 0), 0), 0), 0);
            console.log('[CADT] filter options loaded:',
                'clubs:', opts.clubs?.length ?? 0,
                'totalTeams in tree:', totalTeams);
        });

        this.svc.getCapabilities(this.jobPath).subscribe(caps => {
            this.capabilities.set(caps);
        });

        // Load initial tab
        this.loadTabData('games');
    }

    // ── Filter handling ──

    onCadtSelectionChange(checked: Set<string>): void {
        this.checkedIds = checked;
        this.deriveCadtSelection();
        console.log('[CADT] checkedIds:', [...checked]);
        console.log('[CADT] derived signal:', this.cadtSelectionSignal());
    }

    closeFilterModal(): void {
        this.filterModalVisible.set(false);
        const req = this.buildFilterRequest();
        console.log('[FILTER] closeFilterModal → request:', JSON.stringify(req));
        this.refreshTab();
    }

    clearFilters(): void {
        this.checkedIds = new Set<string>();
        this.cadtSelectionSignal.set({ clubNames: [], agegroupIds: [], divisionIds: [], teamIds: [] });
        this.selectedGameDay.set('');
        this.unscoredOnly.set(false);
        this.refreshTab();
    }

    removeChip(chip: FilterChip): void {
        switch (chip.type) {
            case 'cadt':
                if (chip.nodeId) {
                    const next = new Set(this.checkedIds);
                    next.delete(chip.nodeId);
                    this.removeCadtDescendants(chip.nodeId, next);
                    this.removeCadtAncestors(chip.nodeId, next);
                    this.checkedIds = next;
                    this.deriveCadtSelection();
                    this.refreshTab();
                }
                break;
            case 'gameDay':
                this.selectedGameDay.set('');
                this.refreshTab();
                break;
            case 'unscored':
                this.unscoredOnly.set(false);
                this.refreshTab();
                break;
        }
    }

    refreshTab(): void {
        this.loadedTabs.clear();
        this.loadTabData(this.activeTab());
    }

    // ── Tab switching ──

    switchTab(tab: TabId): void {
        this.activeTab.set(tab);
        if (!this.loadedTabs.has(tab)) {
            this.loadTabData(tab);
        }
    }

    private buildFilterRequest(): ScheduleFilterRequest {
        const req: ScheduleFilterRequest = {};
        const s = this.cadtSelectionSignal();
        if (s.clubNames.length > 0) req.clubNames = s.clubNames;
        if (s.agegroupIds.length > 0) req.agegroupIds = s.agegroupIds;
        if (s.divisionIds.length > 0) req.divisionIds = s.divisionIds;
        if (s.teamIds.length > 0) req.teamIds = s.teamIds;
        if (this.selectedGameDay()) req.gameDays = [this.selectedGameDay()];
        if (this.unscoredOnly()) req.unscoredOnly = true;
        return req;
    }

    private loadTabData(tab: TabId): void {
        this.tabLoading.set(true);
        const request = this.buildFilterRequest();

        switch (tab) {
            case 'games':
                console.log('[FILTER] POST games with:', JSON.stringify(request));
                this.svc.getGames(request, this.jobPath).subscribe({
                    next: data => {
                        console.log('[FILTER] games response:', data.length, 'games');
                        this.games.set(data);
                        this.loadedTabs.add('games');
                    },
                    error: err => console.error('[FILTER] games ERROR:', err),
                    complete: () => this.tabLoading.set(false)
                });
                break;
            case 'standings':
                forkJoin({
                    standings: this.svc.getStandings(request, this.jobPath),
                    records: this.svc.getTeamRecords(request, this.jobPath)
                }).subscribe({
                    next: ({ standings, records }) => {
                        this.standings.set(standings);
                        this.records.set(records);
                        this.loadedTabs.add('standings');
                    },
                    complete: () => this.tabLoading.set(false)
                });
                break;
            case 'brackets':
                this.svc.getBrackets(request, this.jobPath).subscribe({
                    next: data => { this.brackets.set(data); this.loadedTabs.add('brackets'); },
                    complete: () => this.tabLoading.set(false)
                });
                break;
            case 'contacts':
                this.svc.getContacts(request).subscribe({
                    next: data => { this.contacts.set(data); this.loadedTabs.add('contacts'); },
                    complete: () => this.tabLoading.set(false)
                });
                break;
        }
    }

    // ── Team Results Modal ──

    onViewTeamResults(teamId: string): void {
        this.teamResultsVisible.set(true);
        this.teamResultsName.set('');
        this.svc.getTeamResults(teamId, this.jobPath).subscribe(response => {
            this.teamResults.set(response.games);
            // Build team label: "Agegroup — Club — Team" (club may be absent)
            const parts = [response.agegroupName, response.clubName, response.teamName]
                .filter(Boolean);
            this.teamResultsName.set(parts.join(' — '));
        });
    }

    // ── Score Editing ──

    onQuickScore(event: { gid: number; t1Score: number; t2Score: number }): void {
        const request: EditScoreRequest = {
            gid: event.gid,
            t1Score: event.t1Score,
            t2Score: event.t2Score
        };
        this.svc.quickEditScore(request).subscribe(() => {
            this.loadedTabs.delete('games');
            this.loadedTabs.delete('standings');
            this.loadedTabs.delete('brackets');
            this.loadTabData(this.activeTab());
        });
    }

    onBracketScoreEdit(event: { gid: number; t1Name: string; t2Name: string; t1Score: number | null; t2Score: number | null }): void {
        const mockGame: ViewGameDto = {
            gid: event.gid,
            gDate: '',
            fName: '',
            fieldId: '',
            agDiv: '',
            t1Name: event.t1Name,
            t2Name: event.t2Name,
            t1Score: event.t1Score ?? undefined,
            t2Score: event.t2Score ?? undefined,
            t1Type: 'F',
            t2Type: 'F',
            rnd: 0,
            gStatusCode: event.t1Score != null ? 2 : 1
        };
        this.editingGame.set(mockGame);
        this.editGameVisible.set(true);
    }

    onEditGameOpen(gid: number): void {
        const game = this.games().find(g => g.gid === gid);
        if (game) {
            this.editingGame.set(game);
            this.editGameVisible.set(true);
        }
    }

    onEditGameSave(request: EditGameRequest): void {
        this.svc.editGame(request).subscribe(() => {
            this.editGameVisible.set(false);
            this.loadedTabs.clear();
            this.loadTabData(this.activeTab());
        });
    }

    // ── Field Info ──

    onViewFieldInfo(fieldId: string): void {
        this.svc.getFieldInfo(fieldId).subscribe(info => {
            if (info) {
                this.fieldInfo.set(info);
                this.fieldInfoVisible.set(true);
            }
        });
    }

    // ── Formatting ──

    formatGameDay(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return isoDate;
        const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
        const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
        return `${days[d.getDay()]} ${months[d.getMonth()]} ${d.getDate()}`;
    }

    // ── CADT chip helpers (private) ──

    /** Build chips for highest-level checked nodes only (don't show children if parent is checked) */
    private buildCadtChips(clubs: CadtClubNode[], chips: FilterChip[]): void {
        for (const club of clubs) {
            if (this.checkedIds.has(`club:${club.clubName}`)) {
                chips.push({ category: 'Club', label: club.clubName, type: 'cadt', nodeId: `club:${club.clubName}` });
                continue; // skip descendants — parent covers them
            }
            for (const ag of club.agegroups ?? []) {
                if (this.checkedIds.has(`ag:${club.clubName}|${ag.agegroupId}`)) {
                    chips.push({ category: 'Agegroup', label: ag.agegroupName, type: 'cadt', nodeId: `ag:${club.clubName}|${ag.agegroupId}` });
                    continue;
                }
                for (const div of ag.divisions ?? []) {
                    if (this.checkedIds.has(`div:${club.clubName}|${div.divId}`)) {
                        chips.push({ category: 'Division', label: div.divName, type: 'cadt', nodeId: `div:${club.clubName}|${div.divId}` });
                        continue;
                    }
                    for (const team of div.teams ?? []) {
                        if (this.checkedIds.has(`team:${team.teamId}`)) {
                            chips.push({ category: 'Team', label: team.teamName, type: 'cadt', nodeId: `team:${team.teamId}` });
                        }
                    }
                }
            }
        }
    }

    /** Remove a CADT node's descendants from the checked set */
    private removeCadtDescendants(nodeId: string, checked: Set<string>): void {
        const clubs = this.filterOptions()?.clubs;
        if (!clubs) return;

        for (const club of clubs) {
            const clubId = `club:${club.clubName}`;
            if (clubId === nodeId) {
                for (const ag of club.agegroups ?? []) {
                    checked.delete(`ag:${club.clubName}|${ag.agegroupId}`);
                    for (const div of ag.divisions ?? []) {
                        checked.delete(`div:${club.clubName}|${div.divId}`);
                        for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                    }
                }
                return;
            }
            for (const ag of club.agegroups ?? []) {
                const agId = `ag:${club.clubName}|${ag.agegroupId}`;
                if (agId === nodeId) {
                    for (const div of ag.divisions ?? []) {
                        checked.delete(`div:${club.clubName}|${div.divId}`);
                        for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                    }
                    return;
                }
                for (const div of ag.divisions ?? []) {
                    if (`div:${club.clubName}|${div.divId}` === nodeId) {
                        for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                        return;
                    }
                }
            }
        }
    }

    /** Remove all ancestors of a node from the checked set */
    private removeCadtAncestors(nodeId: string, checked: Set<string>): void {
        const clubs = this.filterOptions()?.clubs;
        if (!clubs) return;

        for (const club of clubs) {
            const clubId = `club:${club.clubName}`;
            for (const ag of club.agegroups ?? []) {
                const agId = `ag:${club.clubName}|${ag.agegroupId}`;
                if (agId === nodeId) { checked.delete(clubId); return; }
                for (const div of ag.divisions ?? []) {
                    const divId = `div:${club.clubName}|${div.divId}`;
                    if (divId === nodeId) { checked.delete(agId); checked.delete(clubId); return; }
                    for (const team of div.teams ?? []) {
                        if (`team:${team.teamId}` === nodeId) {
                            checked.delete(divId); checked.delete(agId); checked.delete(clubId);
                            return;
                        }
                    }
                }
            }
        }
    }

    /**
     * Rebuild cadtSelectionSignal from current checkedIds.
     *
     * Always resolves to team IDs for maximum precision — the tree
     * contains the full hierarchy, so checking a club/agegroup/division
     * just means collecting all teams beneath it. This avoids the
     * bubble-up problem where checking a single team auto-checks its
     * parent division, which would then filter by division (too broad).
     */
    private deriveCadtSelection(): void {
        const teamIdSet = new Set<string>();
        const clubs = this.filterOptions()?.clubs ?? [];

        for (const club of clubs) {
            // Club checked → all its teams
            if (this.checkedIds.has(`club:${club.clubName}`)) {
                for (const ag of club.agegroups ?? []) {
                    for (const div of ag.divisions ?? []) {
                        for (const team of div.teams ?? []) {
                            teamIdSet.add(team.teamId);
                        }
                    }
                }
                continue;
            }

            for (const ag of club.agegroups ?? []) {
                for (const div of ag.divisions ?? []) {
                    // Division checked → all its teams
                    if (this.checkedIds.has(`div:${club.clubName}|${div.divId}`)) {
                        for (const team of div.teams ?? []) {
                            teamIdSet.add(team.teamId);
                        }
                        continue;
                    }
                    // Individual teams
                    for (const team of div.teams ?? []) {
                        if (this.checkedIds.has(`team:${team.teamId}`)) {
                            teamIdSet.add(team.teamId);
                        }
                    }
                }
            }
        }

        console.log('[CADT] deriveCadtSelection → teams:', teamIdSet.size);

        this.cadtSelectionSignal.set({
            clubNames: [],
            agegroupIds: [],
            divisionIds: [],
            teamIds: [...teamIdSet]
        });
    }
}
