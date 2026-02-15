import {
    ChangeDetectionStrategy, Component, inject, OnInit, signal, computed
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import type {
    ScheduleFilterOptionsDto,
    ScheduleFilterRequest,
    ScheduleCapabilitiesDto,
    ViewGameDto,
    StandingsByDivisionResponse,
    DivisionBracketResponse,
    ContactDto,
    TeamResultDto,
    FieldDisplayDto,
    EditScoreRequest,
    EditGameRequest
} from '@core/api';
import { ViewScheduleService } from './services/view-schedule.service';
import { CadtTreeFilterComponent, type CadtSelectionEvent } from '../shared/components/cadt-tree-filter/cadt-tree-filter.component';
import { GamesTabComponent } from './components/games-tab.component';
import { StandingsTabComponent } from './components/standings-tab.component';
import { RecordsTabComponent } from './components/records-tab.component';
import { BracketsTabComponent } from './components/brackets-tab.component';
import { ContactsTabComponent } from './components/contacts-tab.component';
import { TeamResultsModalComponent } from './components/team-results-modal.component';
import { EditGameModalComponent } from './components/edit-game-modal.component';

type TabId = 'games' | 'standings' | 'records' | 'brackets' | 'contacts';

@Component({
    selector: 'app-view-schedule',
    standalone: true,
    imports: [
        FormsModule,
        CadtTreeFilterComponent,
        GamesTabComponent,
        StandingsTabComponent,
        RecordsTabComponent,
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
                @if (capabilities()?.sportName; as sport) {
                    <span class="sport-badge">{{ sport }}</span>
                }
            </div>

            <div class="page-body">
                <!-- Filter panel (collapsible) -->
                <div class="filter-panel" [class.collapsed]="!filtersExpanded()">
                    <button class="filter-toggle" (click)="toggleFilters()">
                        <i class="bi" [class.bi-funnel-fill]="filtersExpanded()" [class.bi-funnel]="!filtersExpanded()"></i>
                        Filters
                        @if (hasActiveFilters()) {
                            <span class="filter-count">{{ activeFilterCount() }}</span>
                        }
                        <span class="toggle-chevron" [class.expanded]="filtersExpanded()"></span>
                    </button>

                    @if (filtersExpanded()) {
                        <div class="filter-content">
                            <!-- CADT Tree -->
                            <app-cadt-tree-filter
                                [treeData]="filterOptions()?.clubs ?? []"
                                [checkedIds]="checkedIds"
                                (checkedIdsChange)="onCadtSelectionChange($event)" />

                            <!-- Game Days -->
                            @if (filterOptions()?.gameDays?.length) {
                                <div class="filter-section">
                                    <div class="filter-section-label">Game Days</div>
                                    <select class="filter-select"
                                            [ngModel]="selectedGameDay()"
                                            (ngModelChange)="selectedGameDay.set($event); refreshTab()">
                                        <option value="">All Days</option>
                                        @for (day of filterOptions()!.gameDays; track day) {
                                            <option [value]="day">{{ formatGameDay(day) }}</option>
                                        }
                                    </select>
                                </div>
                            }

                            <!-- Unscored Only -->
                            <div class="filter-section">
                                <label class="filter-checkbox-label">
                                    <input type="checkbox"
                                           [ngModel]="unscoredOnly()"
                                           (ngModelChange)="unscoredOnly.set($event); refreshTab()" />
                                    Unscored games only
                                </label>
                            </div>

                            <!-- Clear filters -->
                            @if (hasActiveFilters()) {
                                <button class="clear-filters-btn" (click)="clearFilters()">
                                    Clear all filters
                                </button>
                            }
                        </div>
                    }
                </div>

                <!-- Tabs -->
                <div class="tabs-section">
                    <div class="tab-bar">
                        <button class="tab-btn" [class.active]="activeTab() === 'games'"
                                (click)="switchTab('games')">Games</button>
                        <button class="tab-btn" [class.active]="activeTab() === 'standings'"
                                (click)="switchTab('standings')">Standings</button>
                        <button class="tab-btn" [class.active]="activeTab() === 'records'"
                                (click)="switchTab('records')">Records</button>
                        <button class="tab-btn" [class.active]="activeTab() === 'brackets'"
                                (click)="switchTab('brackets')">Brackets</button>
                        @if (!capabilities()?.hideContacts) {
                            <button class="tab-btn" [class.active]="activeTab() === 'contacts'"
                                    (click)="switchTab('contacts')">Contacts</button>
                        }
                    </div>

                    <!-- Tab content -->
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
                                    [isLoading]="tabLoading()"
                                    (viewTeamResults)="onViewTeamResults($event)" />
                            }
                            @case ('records') {
                                <app-records-tab
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

        .sport-badge {
            padding: var(--space-1) var(--space-2);
            background: var(--bs-primary-bg-subtle);
            color: var(--bs-primary);
            border-radius: var(--bs-border-radius);
            font-size: var(--font-size-xs);
            font-weight: 600;
            text-transform: uppercase;
        }

        /* ── Page body ── */
        .page-body {
            display: flex;
            gap: var(--space-4);
        }

        /* ── Filter panel ── */
        .filter-panel {
            flex-shrink: 0;
            width: 280px;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            background: var(--bs-body-bg);
            align-self: flex-start;
            position: sticky;
            top: var(--space-3);
        }

        .filter-panel.collapsed {
            width: auto;
        }

        .filter-toggle {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            width: 100%;
            padding: var(--space-2) var(--space-3);
            border: none;
            background: none;
            font-weight: 600;
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
            cursor: pointer;
            text-align: left;
        }

        .filter-toggle:hover {
            background: var(--bs-tertiary-bg);
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
            margin-left: auto;
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

        .filter-content {
            padding: 0 var(--space-3) var(--space-3);
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
            border-top: 1px solid var(--bs-border-color);
            padding-top: var(--space-3);
        }

        .filter-section {
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
        }

        .filter-section-label {
            font-weight: 600;
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }

        .filter-select {
            width: 100%;
            padding: var(--space-1) var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            cursor: pointer;
        }

        .filter-checkbox-label {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
            cursor: pointer;
        }

        .filter-checkbox-label input {
            accent-color: var(--bs-primary);
        }

        .clear-filters-btn {
            padding: var(--space-1) var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            background: none;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-xs);
            cursor: pointer;
            text-align: center;
        }

        .clear-filters-btn:hover {
            background: var(--bs-tertiary-bg);
            color: var(--bs-body-color);
        }

        /* ── Tabs section ── */
        .tabs-section {
            flex: 1;
            min-width: 0;
        }

        .tab-bar {
            display: flex;
            gap: 0;
            border-bottom: 2px solid var(--bs-border-color);
            margin-bottom: var(--space-3);
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

        /* ── Responsive: stack on small screens ── */
        @media (max-width: 768px) {
            .page-body {
                flex-direction: column;
            }

            .filter-panel {
                width: 100%;
                position: static;
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
    readonly filtersExpanded = signal(true);

    // CADT selection (mutable Set shared with child)
    checkedIds = new Set<string>();
    private cadtSelection: CadtSelectionEvent = { clubNames: [], agegroupIds: [], divisionIds: [], teamIds: [] };
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
    readonly hasActiveFilters = computed(() => {
        return this.cadtSelection.clubNames.length > 0
            || this.cadtSelection.agegroupIds.length > 0
            || this.cadtSelection.divisionIds.length > 0
            || this.cadtSelection.teamIds.length > 0
            || this.selectedGameDay() !== ''
            || this.unscoredOnly();
    });

    readonly activeFilterCount = computed(() => {
        let count = 0;
        if (this.cadtSelection.clubNames.length > 0) count++;
        if (this.cadtSelection.agegroupIds.length > 0) count++;
        if (this.cadtSelection.divisionIds.length > 0) count++;
        if (this.cadtSelection.teamIds.length > 0) count++;
        if (this.selectedGameDay() !== '') count++;
        if (this.unscoredOnly()) count++;
        return count;
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

    // ── UI helpers ──

    toggleFilters(): void {
        this.filtersExpanded.set(!this.filtersExpanded());
    }

    // ── Filter handling ──

    onCadtSelectionChange(event: CadtSelectionEvent): void {
        this.cadtSelection = event;
        this.refreshTab();
    }

    clearFilters(): void {
        this.checkedIds = new Set<string>();
        this.cadtSelection = { clubNames: [], agegroupIds: [], divisionIds: [], teamIds: [] };
        this.selectedGameDay.set('');
        this.unscoredOnly.set(false);
        this.refreshTab();
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
        if (this.cadtSelection.clubNames.length > 0) req.clubNames = this.cadtSelection.clubNames;
        if (this.cadtSelection.agegroupIds.length > 0) req.agegroupIds = this.cadtSelection.agegroupIds;
        if (this.cadtSelection.divisionIds.length > 0) req.divisionIds = this.cadtSelection.divisionIds;
        if (this.cadtSelection.teamIds.length > 0) req.teamIds = this.cadtSelection.teamIds;
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
                this.svc.getStandings(request, this.jobPath).subscribe({
                    next: data => { this.standings.set(data); this.loadedTabs.add('standings'); },
                    complete: () => this.tabLoading.set(false)
                });
                break;
            case 'records':
                this.svc.getTeamRecords(request, this.jobPath).subscribe({
                    next: data => { this.records.set(data); this.loadedTabs.add('records'); },
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
        this.teamResultsName.set(''); // Will be set from results
        this.svc.getTeamResults(teamId, this.jobPath).subscribe(results => {
            this.teamResults.set(results);
            // Infer team name from first result opponent context
            if (results.length > 0) {
                // The team name isn't directly in TeamResultDto — use the first game's context
                // For now, display "Team Results" and the game list
                this.teamResultsName.set('Team Results');
            }
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
            // Refresh games tab to show updated scores
            this.loadedTabs.delete('games');
            this.loadedTabs.delete('standings');
            this.loadedTabs.delete('records');
            this.loadedTabs.delete('brackets');
            this.loadTabData(this.activeTab());
        });
    }

    onBracketScoreEdit(event: { gid: number; t1Name: string; t2Name: string; t1Score: number | null; t2Score: number | null }): void {
        // Open edit game modal for bracket match
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
            // Refresh all loaded tabs
            this.loadedTabs.clear();
            this.loadTabData(this.activeTab());
        });
    }

    // ── Field Info ──

    onViewFieldInfo(fieldId: string): void {
        this.svc.getFieldInfo(fieldId).subscribe(info => {
            if (info) {
                // Simple alert for now; could be a modal in future
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
}
