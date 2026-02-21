import {
    ChangeDetectionStrategy, Component, inject, OnInit, signal, computed
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
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
        EditGameModalComponent
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="view-schedule-page">
            <!-- Header -->
            <div class="page-header">
                <h2 class="page-title">Schedule</h2>
            </div>

            <!-- Filter Bar (horizontal) -->
            <div class="filter-bar">
                <div class="filter-bar-row">
                    <i class="bi bi-funnel filter-icon"></i>

                    <!-- Game Days dropdown -->
                    @if (filterOptions()?.gameDays?.length) {
                        <select class="filter-select"
                                [ngModel]="selectedGameDay()"
                                (ngModelChange)="selectedGameDay.set($event); refreshTab()">
                            <option value="">All Game Days</option>
                            @for (day of filterOptions()!.gameDays; track day) {
                                <option [value]="day">{{ formatGameDay(day) }}</option>
                            }
                        </select>
                    }

                    <!-- Unscored checkbox -->
                    <label class="filter-checkbox-label">
                        <input type="checkbox"
                               [ngModel]="unscoredOnly()"
                               (ngModelChange)="unscoredOnly.set($event); refreshTab()" />
                        Unscored only
                    </label>

                    <!-- CADT toggle (only if tree has data) -->
                    @if (hasCadtData()) {
                        <button class="cadt-toggle-btn"
                                [class.active]="cadtTreeExpanded()"
                                (click)="cadtTreeExpanded.set(!cadtTreeExpanded())">
                            <i class="bi bi-diagram-3"></i>
                            Teams
                            @if (cadtFilterCount() > 0) {
                                <span class="filter-count">{{ cadtFilterCount() }}</span>
                            }
                            <span class="toggle-chevron" [class.expanded]="cadtTreeExpanded()"></span>
                        </button>
                    }

                    <!-- Clear -->
                    @if (hasActiveFilters()) {
                        <button class="clear-filters-btn" (click)="clearFilters()">
                            <i class="bi bi-x-circle"></i> Clear
                        </button>
                    }
                </div>

                <!-- CADT tree (collapsible) -->
                @if (cadtTreeExpanded() && hasCadtData()) {
                    <div class="cadt-section">
                        <app-cadt-tree-filter
                            [treeData]="filterOptions()?.clubs ?? []"
                            [checkedIds]="checkedIds"
                            (checkedIdsChange)="onCadtSelectionChange($event)" />
                    </div>
                }

                <!-- Filter chips strip -->
                @if (activeFilterChips().length > 0) {
                    <div class="filter-chips-strip">
                        @for (chip of activeFilterChips(); track chip.nodeId ?? chip.type) {
                            <span class="filter-chip">
                                <span class="chip-category">{{ chip.category }}:</span>
                                <span class="chip-label">{{ chip.label }}</span>
                                <button type="button" class="chip-remove"
                                        (click)="removeChip(chip)"
                                        aria-label="Remove filter">
                                    <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12"
                                         viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                                        <line x1="18" y1="6" x2="6" y2="18"></line>
                                        <line x1="6" y1="6" x2="18" y2="18"></line>
                                    </svg>
                                </button>
                            </span>
                        }
                        <button type="button" class="chip-clear-all" (click)="clearFilters()">Clear All</button>
                    </div>
                }
            </div>

            <!-- Tab Bar -->
            <div class="tab-bar">
                <button class="tab-btn" [class.active]="activeTab() === 'games'"
                        (click)="switchTab('games')">Games</button>
                <button class="tab-btn" [class.active]="activeTab() === 'standings'"
                        (click)="switchTab('standings')">Standings</button>
                <button class="tab-btn" [class.active]="activeTab() === 'brackets'"
                        (click)="switchTab('brackets')">Brackets</button>
                @if (!capabilities()?.hideContacts) {
                    <button class="tab-btn" [class.active]="activeTab() === 'contacts'"
                            (click)="switchTab('contacts')">Contacts</button>
                }
            </div>

            <!-- Tab content (full width) -->
            <div class="tab-content">
                @switch (activeTab()) {
                    @case ('games') {
                        <app-games-tab
                            [games]="games()"
                            [canScore]="capabilities()?.canScore ?? false"
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
                            [canScore]="capabilities()?.canScore ?? false"
                            [isLoading]="tabLoading()"
                            (editBracketScore)="onBracketScoreEdit($event)" />
                    }
                    @case ('contacts') {
                        <app-contacts-tab
                            [contacts]="contacts()"
                            [isLoading]="tabLoading()" />
                    }
                }
            </div>
        </div>

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
            display: flex;
            align-items: center;
            gap: var(--space-3);
        }

        .page-title {
            margin: 0;
            font-size: var(--font-size-xl, 1.5rem);
            font-weight: 700;
            color: var(--bs-body-color);
        }

        /* ── Filter Bar (horizontal) ── */
        .filter-bar {
            background: var(--bs-card-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            padding: var(--space-3);
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
        }

        .filter-bar-row {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            flex-wrap: wrap;
        }

        .filter-icon {
            color: var(--bs-secondary-color);
            font-size: var(--font-size-lg);
            flex-shrink: 0;
        }

        .filter-select {
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            cursor: pointer;
            min-width: 160px;
        }

        .filter-checkbox-label {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
            cursor: pointer;
            white-space: nowrap;
        }

        .filter-checkbox-label input {
            accent-color: var(--bs-primary);
        }

        .cadt-toggle-btn {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            font-weight: 600;
            cursor: pointer;
            transition: background 0.15s, border-color 0.15s;
        }

        .cadt-toggle-btn:hover {
            background: var(--bs-tertiary-bg);
        }

        .cadt-toggle-btn.active {
            border-color: var(--bs-primary);
            color: var(--bs-primary);
        }

        .filter-count {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 20px;
            height: 20px;
            padding: 0 var(--space-1);
            background: var(--bs-primary);
            color: white;
            border-radius: 10px;
            font-size: var(--font-size-xs);
            font-weight: 700;
        }

        .toggle-chevron {
            display: inline-block;
            width: 0;
            height: 0;
            border-left: 5px solid var(--bs-secondary-color);
            border-top: 4px solid transparent;
            border-bottom: 4px solid transparent;
            transition: transform 0.15s ease;
        }

        .toggle-chevron.expanded {
            transform: rotate(90deg);
        }

        .clear-filters-btn {
            display: flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: none;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
            cursor: pointer;
            margin-left: auto;
        }

        .clear-filters-btn:hover {
            background: var(--bs-tertiary-bg);
            color: var(--bs-body-color);
        }

        /* ── CADT Section (collapsible) ── */
        .cadt-section {
            border-top: 1px solid var(--bs-border-color);
            padding-top: var(--space-3);
            max-height: 300px;
            overflow-y: auto;
        }

        /* ── Filter Chips ── */
        .filter-chips-strip {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-2);
            padding-top: var(--space-2);
            border-top: 1px solid var(--bs-border-color);
            align-items: center;
        }

        .filter-chip {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-2);
            background: var(--bs-primary);
            color: var(--bs-white, #fff);
            border-radius: var(--radius-sm);
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
            margin-left: var(--space-1);
            background: transparent;
            border: none;
            border-radius: 50%;
            color: var(--bs-white, #fff);
            cursor: pointer;
            opacity: 0.7;
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
            border-radius: var(--radius-sm);
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

        /* ── Tab Bar ── */
        .tab-bar {
            display: flex;
            gap: 0;
            border-bottom: 2px solid var(--bs-border-color);
            overflow-x: auto;
        }

        .tab-btn {
            padding: var(--space-2) var(--space-4);
            border: none;
            background: none;
            color: var(--bs-secondary-color);
            font-weight: 600;
            font-size: var(--font-size-sm);
            cursor: pointer;
            white-space: nowrap;
            border-bottom: 2px solid transparent;
            margin-bottom: -2px;
            transition: color 0.15s, border-color 0.15s;
        }

        .tab-btn:hover {
            color: var(--bs-body-color);
        }

        .tab-btn.active {
            color: var(--bs-primary);
            border-bottom-color: var(--bs-primary);
        }

        /* ── Responsive ── */
        @media (max-width: 768px) {
            .filter-bar-row {
                flex-direction: column;
                align-items: stretch;
            }

            .clear-filters-btn {
                margin-left: 0;
            }

            .filter-select {
                width: 100%;
                min-width: unset;
            }
        }
    `]
})
export class ViewScheduleComponent implements OnInit {
    private readonly svc = inject(ViewScheduleService);
    private readonly route = inject(ActivatedRoute);

    // ── Route state ──
    private jobPath: string | undefined;

    // ── Filter state ──
    readonly filterOptions = signal<ScheduleFilterOptionsDto | null>(null);
    readonly capabilities = signal<ScheduleCapabilitiesDto | null>(null);

    // CADT selection (mutable Set shared with child)
    checkedIds = new Set<string>();
    readonly cadtSelectionSignal = signal<CadtSelectionEvent>({
        clubNames: [], agegroupIds: [], divisionIds: [], teamIds: []
    });
    readonly cadtTreeExpanded = signal(false);
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

    // ── Computed helpers ──

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
        });

        this.svc.getCapabilities(this.jobPath).subscribe(caps => {
            this.capabilities.set(caps);
        });

        // Load initial tab
        this.loadTabData('games');
    }

    // ── Filter handling ──

    onCadtSelectionChange(event: CadtSelectionEvent): void {
        this.cadtSelectionSignal.set(event);
        this.refreshTab();
    }

    clearFilters(): void {
        this.checkedIds = new Set<string>();
        this.cadtSelectionSignal.set({ clubNames: [], agegroupIds: [], divisionIds: [], teamIds: [] });
        this.selectedGameDay.set('');
        this.unscoredOnly.set(false);
        this.cadtTreeExpanded.set(false);
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
                this.svc.getGames(request, this.jobPath).subscribe({
                    next: data => { this.games.set(data); this.loadedTabs.add('games'); },
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
                const parts = [info.fName];
                if (info.address) parts.push(info.address);
                if (info.city) parts.push(info.city);
                if (info.directions) parts.push(`\nDirections: ${info.directions}`);
                alert(parts.join('\n'));
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
                if (this.checkedIds.has(`ag:${ag.agegroupId}`)) {
                    chips.push({ category: 'Agegroup', label: ag.agegroupName, type: 'cadt', nodeId: `ag:${ag.agegroupId}` });
                    continue;
                }
                for (const div of ag.divisions ?? []) {
                    if (this.checkedIds.has(`div:${div.divId}`)) {
                        chips.push({ category: 'Division', label: div.divName, type: 'cadt', nodeId: `div:${div.divId}` });
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
                    checked.delete(`ag:${ag.agegroupId}`);
                    for (const div of ag.divisions ?? []) {
                        checked.delete(`div:${div.divId}`);
                        for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                    }
                }
                return;
            }
            for (const ag of club.agegroups ?? []) {
                const agId = `ag:${ag.agegroupId}`;
                if (agId === nodeId) {
                    for (const div of ag.divisions ?? []) {
                        checked.delete(`div:${div.divId}`);
                        for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                    }
                    return;
                }
                for (const div of ag.divisions ?? []) {
                    if (`div:${div.divId}` === nodeId) {
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
                const agId = `ag:${ag.agegroupId}`;
                if (agId === nodeId) { checked.delete(clubId); return; }
                for (const div of ag.divisions ?? []) {
                    const divId = `div:${div.divId}`;
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

    /** Rebuild cadtSelectionSignal from current checkedIds */
    private deriveCadtSelection(): void {
        const clubNames: string[] = [];
        const agegroupIds: string[] = [];
        const divisionIds: string[] = [];
        const teamIds: string[] = [];

        for (const id of this.checkedIds) {
            if (id.startsWith('club:')) clubNames.push(id.substring(5));
            else if (id.startsWith('ag:')) agegroupIds.push(id.substring(3));
            else if (id.startsWith('div:')) divisionIds.push(id.substring(4));
            else if (id.startsWith('team:')) teamIds.push(id.substring(5));
        }

        this.cadtSelectionSignal.set({ clubNames, agegroupIds, divisionIds, teamIds });
    }
}
