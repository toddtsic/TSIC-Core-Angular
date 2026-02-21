import { ChangeDetectionStrategy, Component, computed, EventEmitter, input, Output, signal } from '@angular/core';
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
        } @else {
            <div class="standings-wrapper">
                <!-- Mode toggle -->
                <div class="mode-toggle-row">
                    <div class="btn-group" role="group" aria-label="Standings mode">
                        <button class="btn btn-sm"
                                [class.btn-primary]="standingsMode() === 'all'"
                                [class.btn-outline-secondary]="standingsMode() !== 'all'"
                                (click)="standingsMode.set('all')">
                            All Games
                        </button>
                        <button class="btn btn-sm"
                                [class.btn-primary]="standingsMode() === 'rr'"
                                [class.btn-outline-secondary]="standingsMode() !== 'rr'"
                                (click)="standingsMode.set('rr')">
                            RR Games Only
                        </button>
                    </div>
                </div>

                @if (!activeData() || activeData()!.divisions.length === 0) {
                    <div class="empty-state">No standings data available.</div>
                } @else {
                    @for (div of activeData()!.divisions; track div.divId) {
                        <div class="division-block">
                            <div class="division-header">
                                {{ div.agegroupName }} {{ div.divName }} &mdash; {{ headerSuffix() }}
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

        .mode-toggle-row {
            display: flex;
            justify-content: flex-end;
        }

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

        .standings-table {
            width: 100%;
            border-collapse: collapse;
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
            width: 2rem;
        }

        .col-team {
            text-align: left;
        }

        .col-num {
            text-align: right;
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

    readonly activeData = computed(() =>
        this.standingsMode() === 'rr' ? this.standings() : this.records()
    );

    readonly headerSuffix = computed(() =>
        this.standingsMode() === 'rr' ? 'Pool Play Standings' : 'Full Season Records'
    );

    private readonly isLacrosse = computed(() => {
        const sport = this.activeData()?.sportName ?? '';
        return sport.toLowerCase().includes('lacrosse');
    });

    readonly hidePointsCols = computed(() =>
        this.standingsMode() === 'rr' && this.isLacrosse()
    );

    formatGoalDiff(gd: number): string {
        if (gd > 0) return `+${gd}`;
        if (gd < 0) return `${gd}`;
        return '0';
    }
}
