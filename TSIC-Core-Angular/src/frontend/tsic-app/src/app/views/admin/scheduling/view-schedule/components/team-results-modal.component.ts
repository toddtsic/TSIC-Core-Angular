import { ChangeDetectionStrategy, Component, computed, EventEmitter, input, Output } from '@angular/core';
import { DatePipe } from '@angular/common';
import type { TeamResultDto } from '@core/api';

@Component({
    selector: 'app-team-results-modal',
    standalone: true,
    imports: [DatePipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (visible()) {
            <div class="modal-backdrop" (click)="close.emit()">
                <div class="modal-card" (click)="$event.stopPropagation()">
                    <!-- Header -->
                    <div class="modal-header">
                        <h3 class="modal-title">Game History: {{ teamName() }}</h3>
                        <button class="modal-close" (click)="close.emit()" aria-label="Close">&times;</button>
                    </div>

                    <!-- Body -->
                    <div class="modal-body">
                        @if (results().length === 0) {
                            <div class="empty-state">No game results found.</div>
                        } @else {
                            <table class="results-table">
                                <thead>
                                    <tr>
                                        <th>Date</th>
                                        <th>Opponent</th>
                                        <th class="col-center">Score</th>
                                        <th class="col-center">Result</th>
                                        <th>Location</th>
                                        <th>Type</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    @for (r of results(); track r.gid) {
                                        <tr [class.row-win]="r.outcome === 'W'"
                                            [class.row-loss]="r.outcome === 'L'">
                                            <td class="col-date">{{ r.gDate | date:'EEE M/d' }}</td>
                                            <td>
                                                @if (r.opponentTeamId) {
                                                    <span class="opponent-link"
                                                          (click)="viewOpponent.emit(r.opponentTeamId!)">
                                                        {{ r.opponentName }}
                                                    </span>
                                                } @else {
                                                    {{ r.opponentName }}
                                                }
                                            </td>
                                            <td class="col-center">
                                                @if (r.teamScore != null && r.opponentScore != null) {
                                                    {{ r.teamScore }} - {{ r.opponentScore }}
                                                } @else {
                                                    <span class="no-data">&mdash;</span>
                                                }
                                            </td>
                                            <td class="col-center">
                                                @if (r.outcome === 'W') {
                                                    <span class="badge badge-win">W</span>
                                                } @else if (r.outcome === 'L') {
                                                    <span class="badge badge-loss">L</span>
                                                } @else if (r.outcome === 'T') {
                                                    <span class="badge badge-tie">T</span>
                                                } @else {
                                                    <span class="no-data">&mdash;</span>
                                                }
                                            </td>
                                            <td>{{ r.location }}</td>
                                            <td>
                                                <span class="badge badge-type">{{ r.gameType }}</span>
                                            </td>
                                        </tr>
                                    }
                                </tbody>
                            </table>

                            <!-- Summary row -->
                            <div class="summary-row">
                                <strong>Record:</strong>
                                {{ record().wins }}-{{ record().losses }}-{{ record().ties }}
                                ({{ results().length }} games)
                            </div>
                        }
                    </div>
                </div>
            </div>
        }
    `,
    styles: [`
        .modal-backdrop {
            position: fixed;
            inset: 0;
            z-index: 1050;
            background: rgba(0, 0, 0, 0.5);
            display: flex;
            align-items: center;
            justify-content: center;
            padding: var(--space-4);
        }

        .modal-card {
            background: var(--bs-body-bg);
            border-radius: var(--bs-border-radius-lg);
            max-width: 700px;
            width: 100%;
            max-height: 80vh;
            overflow-y: auto;
            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
            display: flex;
            flex-direction: column;
        }

        .modal-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: var(--space-3) var(--space-4);
            border-bottom: 1px solid var(--bs-border-color);
            flex-shrink: 0;
        }

        .modal-title {
            margin: 0;
            font-size: var(--font-size-lg, 1.125rem);
            font-weight: 600;
            color: var(--bs-body-color);
        }

        .modal-close {
            background: none;
            border: none;
            font-size: 1.5rem;
            line-height: 1;
            color: var(--bs-secondary-color);
            cursor: pointer;
            padding: 0 var(--space-1);
        }

        .modal-close:hover {
            color: var(--bs-body-color);
        }

        .modal-body {
            padding: var(--space-3) var(--space-4);
            overflow-y: auto;
        }

        .empty-state {
            padding: var(--space-4);
            color: var(--bs-secondary-color);
            text-align: center;
        }

        .results-table {
            width: 100%;
            border-collapse: collapse;
            font-size: var(--font-size-sm);
        }

        .results-table thead th {
            background: var(--bs-tertiary-bg);
            padding: var(--space-1) var(--space-2);
            border-bottom: 2px solid var(--bs-border-color);
            font-weight: 600;
            color: var(--bs-body-color);
            white-space: nowrap;
            text-align: left;
        }

        .results-table tbody td {
            padding: var(--space-1) var(--space-2);
            border-bottom: 1px solid var(--bs-border-color);
            color: var(--bs-body-color);
            vertical-align: middle;
        }

        .results-table tbody tr:last-child td {
            border-bottom: none;
        }

        .col-center {
            text-align: center;
        }

        .col-date {
            white-space: nowrap;
        }

        .row-win {
            background: color-mix(in srgb, var(--bs-success) 8%, transparent);
        }

        .row-loss {
            background: color-mix(in srgb, var(--bs-danger) 8%, transparent);
        }

        .opponent-link {
            color: var(--bs-primary);
            cursor: pointer;
            text-decoration: none;
        }

        .opponent-link:hover {
            text-decoration: underline;
        }

        .badge {
            display: inline-block;
            padding: 1px 6px;
            border-radius: var(--bs-border-radius);
            font-size: var(--font-size-xs, 0.75rem);
            font-weight: 600;
            line-height: 1.4;
        }

        .badge-win {
            background: color-mix(in srgb, var(--bs-success) 20%, transparent);
            color: var(--bs-success);
        }

        .badge-loss {
            background: color-mix(in srgb, var(--bs-danger) 20%, transparent);
            color: var(--bs-danger);
        }

        .badge-tie {
            background: var(--bs-secondary-bg);
            color: var(--bs-secondary-color);
        }

        .badge-type {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
            font-weight: 500;
        }

        .no-data {
            color: var(--bs-secondary-color);
        }

        .summary-row {
            margin-top: var(--space-3);
            padding: var(--space-2) var(--space-3);
            background: var(--bs-tertiary-bg);
            border-radius: var(--bs-border-radius);
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
        }
    `]
})
export class TeamResultsModalComponent {
    results = input<TeamResultDto[]>([]);
    teamName = input<string>('');
    visible = input<boolean>(false);

    @Output() close = new EventEmitter<void>();
    @Output() viewOpponent = new EventEmitter<string>();

    readonly record = computed(() => {
        const items = this.results();
        let wins = 0;
        let losses = 0;
        let ties = 0;
        for (const r of items) {
            if (r.outcome === 'W') wins++;
            else if (r.outcome === 'L') losses++;
            else if (r.outcome === 'T') ties++;
        }
        return { wins, losses, ties };
    });
}
