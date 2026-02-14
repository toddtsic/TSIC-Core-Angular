import { ChangeDetectionStrategy, Component, EventEmitter, input, Output } from '@angular/core';
import type { StandingsByDivisionResponse, DivisionStandingsDto, StandingsDto } from '@core/api';

@Component({
    selector: 'app-records-tab',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (isLoading()) {
            <div class="loading-container">
                <span class="spinner-border spinner-border-sm" role="status"></span>
                Loading records...
            </div>
        } @else if (!records() || records()!.divisions.length === 0) {
            <div class="empty-state">No records data available.</div>
        } @else {
            <div class="records-wrapper">
                @for (div of records()!.divisions; track div.divId) {
                    <div class="division-block">
                        <div class="division-header">
                            {{ div.agegroupName }} {{ div.divName }} &mdash; Full Season Records
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
                                    <th class="col-num">Pts</th>
                                    <th class="col-num">GF</th>
                                    <th class="col-num">GA</th>
                                    <th class="col-num">GD</th>
                                    <th class="col-num">PPG</th>
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
                                        <td class="col-num">{{ team.points }}</td>
                                        <td class="col-num">{{ team.goalsFor }}</td>
                                        <td class="col-num">{{ team.goalsAgainst }}</td>
                                        <td class="col-num">{{ formatGoalDiff(team.goalDiffMax9) }}</td>
                                        <td class="col-num">{{ team.pointsPerGame.toFixed(2) }}</td>
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

        .records-wrapper {
            display: flex;
            flex-direction: column;
            gap: var(--space-4);
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
export class RecordsTabComponent {
    records = input<StandingsByDivisionResponse | null>(null);
    isLoading = input<boolean>(false);

    @Output() viewTeamResults = new EventEmitter<string>();

    formatGoalDiff(gd: number): string {
        if (gd > 0) return `+${gd}`;
        if (gd < 0) return `${gd}`;
        return '0';
    }
}
