import { ChangeDetectionStrategy, Component, computed, effect, EventEmitter, input, Output, signal } from '@angular/core';
import type { StandingsByDivisionResponse } from '@core/api';

type StandingsMode = 'all' | 'rr';

@Component({
    selector: 'app-standings-tab',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (isLoading()) {
            <div class="loading-container">
                <span class="spinner-border spinner-border-sm" role="status"></span>
                Loading standings...
            </div>
        } @else if (!activeData() || activeData()!.divisions.length === 0) {
            <div class="empty-state">No standings data available.</div>
        } @else {
            <div class="standings-wrapper">
                <!-- Toolbar: age group tabs (left) + mode toggle (right) -->
                <div class="toolbar-row">
                    <div class="ag-tabs">
                        @for (tab of ageGroupTabs(); track tab; let i = $index) {
                            <button class="ag-tab"
                                    [class.active]="activeAgTabIndex() === i"
                                    (click)="activeAgTabIndex.set(i)">
                                {{ tab }}
                            </button>
                        }
                    </div>
                    <div class="mode-toggle" role="group" aria-label="Standings mode">
                        <button class="toggle-option"
                                [class.active]="standingsMode() === 'all'"
                                (click)="standingsMode.set('all')">
                            All Games
                        </button>
                        <button class="toggle-option"
                                [class.active]="standingsMode() === 'rr'"
                                (click)="standingsMode.set('rr')">
                            RR Only
                        </button>
                    </div>
                </div>

                <!-- Division cards for selected age group -->
                @for (div of activeDivisions(); track div.divId) {
                    <div class="division-block">
                        <div class="division-header">
                            {{ div.divName }} &mdash; {{ headerSuffix() }}
                        </div>

                        <table class="standings-table">
                            <thead>
                                <tr>
                                    <th class="col-rank">#</th>
                                    <th class="col-team">Team</th>
                                    <th class="col-num">GP</th>
                                    <th class="col-num">W</th>
                                    <th class="col-num">L</th>
                                    <th class="col-num">T</th>
                                    @if (!hidePointsCols()) {
                                        <th class="col-num">Pts</th>
                                    }
                                    <th class="col-num">GF</th>
                                    <th class="col-num">GA</th>
                                    <th class="col-num">GD</th>
                                    @if (!hidePointsCols()) {
                                        <th class="col-num">PPG</th>
                                    }
                                </tr>
                            </thead>
                            <tbody>
                                @for (team of div.teams; track team.teamId; let i = $index) {
                                    <tr [class.top-rank]="team.rankOrder === 1">
                                        <td class="col-rank">{{ team.rankOrder ?? (i + 1) }}</td>
                                        <td class="col-team">
                                            <span class="team-link" (click)="viewTeamResults.emit(team.teamId)">
                                                {{ team.teamName }}
                                            </span>
                                        </td>
                                        <td class="col-num">{{ team.games }}</td>
                                        <td class="col-num">{{ team.wins }}</td>
                                        <td class="col-num">{{ team.losses }}</td>
                                        <td class="col-num">{{ team.ties }}</td>
                                        @if (!hidePointsCols()) {
                                            <td class="col-num">{{ team.points }}</td>
                                        }
                                        <td class="col-num">{{ team.goalsFor }}</td>
                                        <td class="col-num">{{ team.goalsAgainst }}</td>
                                        <td class="col-num">{{ formatGoalDiff(team.goalDiffMax9) }}</td>
                                        @if (!hidePointsCols()) {
                                            <td class="col-num">{{ team.pointsPerGame.toFixed(2) }}</td>
                                        }
                                    </tr>
                                }
                            </tbody>
                        </table>
                    </div>
                }
            </div>
        }
    `,
    styles: [`
        .loading-container {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-4);
            color: var(--bs-secondary-color);
        }

        .empty-state {
            padding: var(--space-4);
            color: var(--bs-secondary-color);
            text-align: center;
        }

        .standings-wrapper {
            display: flex;
            flex-direction: column;
            gap: var(--space-4);
        }

        /* ── Toolbar: tabs + toggle ── */

        .toolbar-row {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: var(--space-3);
            flex-wrap: wrap;
        }

        /* ── Age Group Tabs ── */

        .ag-tabs {
            display: flex;
            gap: var(--space-1);
            overflow-x: auto;
            scrollbar-width: thin;
            flex: 1;
            min-width: 0;
        }

        .ag-tab {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
            font-weight: 500;
            cursor: pointer;
            white-space: nowrap;
            transition: background-color 0.15s, color 0.15s, border-color 0.15s;
        }

        .ag-tab:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        .ag-tab.active {
            background: var(--bs-primary);
            color: white;
            border-color: var(--bs-primary);
        }

        /* ── Mode Toggle (segmented control) ── */

        .mode-toggle {
            display: inline-flex;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            overflow: hidden;
            flex-shrink: 0;
        }

        .toggle-option {
            padding: var(--space-1) var(--space-3);
            border: none;
            background: var(--bs-body-bg);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
            font-weight: 500;
            cursor: pointer;
            white-space: nowrap;
            transition: background-color 0.15s, color 0.15s;
        }

        .toggle-option:not(:last-child) {
            border-right: 1px solid var(--bs-border-color);
        }

        .toggle-option:hover:not(.active) {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        .toggle-option.active {
            background: var(--bs-primary);
            color: white;
        }

        /* ── Division Cards ── */

        .division-block {
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            overflow: hidden;
        }

        .division-header {
            background: var(--bs-secondary-bg);
            padding: var(--space-2) var(--space-3);
            font-weight: 600;
            color: var(--bs-body-color);
        }

        /* ── Standings Table ── */

        .standings-table {
            width: 100%;
            border-collapse: collapse;
            table-layout: fixed;
            font-size: var(--font-size-sm);
        }

        .standings-table thead th {
            background: var(--bs-tertiary-bg);
            padding: var(--space-1) var(--space-2);
            border-bottom: 2px solid var(--bs-border-color);
            font-weight: 600;
            color: var(--bs-body-color);
            white-space: nowrap;
        }

        .standings-table tbody td {
            padding: var(--space-1) var(--space-2);
            border-bottom: 1px solid var(--bs-border-color);
            color: var(--bs-body-color);
        }

        .standings-table tbody tr:last-child td {
            border-bottom: none;
        }

        .col-rank {
            text-align: center;
            width: 2.5rem;
        }

        .col-team {
            text-align: left;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .col-num {
            text-align: right;
            width: 3.5rem;
        }

        .team-link {
            color: var(--bs-primary);
            cursor: pointer;
            text-decoration: none;
        }

        .team-link:hover {
            text-decoration: underline;
        }

        .top-rank td {
            font-weight: 600;
        }
    `]
})
export class StandingsTabComponent {
    standings = input<StandingsByDivisionResponse | null>(null);
    records = input<StandingsByDivisionResponse | null>(null);
    isLoading = input<boolean>(false);

    @Output() viewTeamResults = new EventEmitter<string>();

    readonly standingsMode = signal<StandingsMode>('all');
    readonly activeAgTabIndex = signal(0);

    readonly activeData = computed(() =>
        this.standingsMode() === 'rr' ? this.standings() : this.records()
    );

    readonly headerSuffix = computed(() =>
        this.standingsMode() === 'rr' ? 'Pool Play Standings' : 'Full Season Records'
    );

    /** Unique age group names from the active dataset, preserving backend sort order. */
    readonly ageGroupTabs = computed<string[]>(() => {
        const data = this.activeData();
        if (!data || data.divisions.length === 0) return [];
        const seen = new Set<string>();
        const result: string[] = [];
        for (const div of data.divisions) {
            if (!seen.has(div.agegroupName)) {
                seen.add(div.agegroupName);
                result.push(div.agegroupName);
            }
        }
        return result;
    });

    /** Divisions for the currently selected age group tab. */
    readonly activeDivisions = computed(() => {
        const data = this.activeData();
        const tabs = this.ageGroupTabs();
        const idx = this.activeAgTabIndex();
        if (!data || tabs.length === 0) return [];
        const agName = tabs[idx];
        return data.divisions.filter(d => d.agegroupName === agName);
    });

    private readonly isLacrosse = computed(() => {
        const sport = this.activeData()?.sportName ?? '';
        return sport.toLowerCase().includes('lacrosse');
    });

    readonly hidePointsCols = computed(() =>
        this.standingsMode() === 'rr' && this.isLacrosse()
    );

    constructor() {
        // Clamp tab index when available tabs change (e.g. mode switch)
        effect(() => {
            const tabs = this.ageGroupTabs();
            const idx = this.activeAgTabIndex();
            if (idx >= tabs.length && tabs.length > 0) {
                this.activeAgTabIndex.set(0);
            }
        });
    }

    formatGoalDiff(gd: number): string {
        if (gd > 0) return `+${gd}`;
        if (gd < 0) return `${gd}`;
        return '0';
    }
}
