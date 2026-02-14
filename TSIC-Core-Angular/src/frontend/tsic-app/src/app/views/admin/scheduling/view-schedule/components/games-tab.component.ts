import {
    Component,
    ChangeDetectionStrategy,
    input,
    signal,
    Output,
    EventEmitter
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { ViewGameDto } from '@core/api';

@Component({
    selector: 'app-games-tab',
    standalone: true,
    imports: [CommonModule, FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (isLoading()) {
            <div class="games-loading">
                <div class="spinner-border spinner-border-sm text-primary" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
                <span class="ms-2 text-body-secondary">Loading games...</span>
            </div>
        } @else if (!games().length) {
            <div class="games-empty text-body-secondary">
                <i class="bi bi-calendar-x me-2"></i>No games match the current filters.
            </div>
        } @else {
            <div class="games-table-wrap">
                <table class="games-table">
                    <thead>
                        <tr>
                            <th class="col-date">Date</th>
                            <th class="col-time">Time</th>
                            <th class="col-field">Field</th>
                            <th class="col-div">Division</th>
                            <th class="col-team">Home</th>
                            <th class="col-team">Away</th>
                            <th class="col-score">Score</th>
                            @if (canScore()) {
                                <th class="col-actions"></th>
                            }
                        </tr>
                    </thead>
                    <tbody>
                        @for (game of games(); track game.gid) {
                            <tr class="game-row"
                                [style.border-left-color]="game.color ?? 'transparent'">

                                <!-- Date -->
                                <td class="cell-date">{{ formatDate(game.gDate) }}</td>

                                <!-- Time -->
                                <td class="cell-time">{{ formatTime(game.gDate) }}</td>

                                <!-- Field -->
                                <td class="cell-field">
                                    <span class="clickable" (click)="viewFieldInfo.emit(game.fieldId)">
                                        {{ game.fName }}
                                    </span>
                                </td>

                                <!-- Division -->
                                <td class="cell-div">{{ game.agDiv }}</td>

                                <!-- Home -->
                                <td class="cell-team">
                                    @if (game.t1Id) {
                                        <span class="clickable" (click)="viewTeamResults.emit(game.t1Id!)">
                                            {{ game.t1Name }}
                                        </span>
                                    } @else {
                                        {{ game.t1Name }}
                                    }
                                    @if (game.t1Ann) {
                                        <div class="annotation">{{ game.t1Ann }}</div>
                                    }
                                </td>

                                <!-- Away -->
                                <td class="cell-team">
                                    @if (game.t2Id) {
                                        <span class="clickable" (click)="viewTeamResults.emit(game.t2Id!)">
                                            {{ game.t2Name }}
                                        </span>
                                    } @else {
                                        {{ game.t2Name }}
                                    }
                                    @if (game.t2Ann) {
                                        <div class="annotation">{{ game.t2Ann }}</div>
                                    }
                                </td>

                                <!-- Score -->
                                <td class="cell-score"
                                    [class.editable]="canScore()"
                                    (click)="onScoreCellClick(game)">
                                    @if (editingGid() === game.gid) {
                                        <div class="score-edit" (click)="$event.stopPropagation()">
                                            <input type="number"
                                                   class="score-input"
                                                   [min]="0"
                                                   [max]="99"
                                                   [ngModel]="editT1Score()"
                                                   (ngModelChange)="editT1Score.set($event)"
                                                   (keydown.enter)="saveScore(game.gid)"
                                                   (keydown.escape)="cancelEdit()"
                                                   #t1Input>
                                            <span class="score-dash">&ndash;</span>
                                            <input type="number"
                                                   class="score-input"
                                                   [min]="0"
                                                   [max]="99"
                                                   [ngModel]="editT2Score()"
                                                   (ngModelChange)="editT2Score.set($event)"
                                                   (keydown.enter)="saveScore(game.gid)"
                                                   (keydown.escape)="cancelEdit()">
                                        </div>
                                    } @else {
                                        @if (hasScore(game)) {
                                            <span [class.winner]="isT1Winner(game)">{{ game.t1Score }}</span>
                                            <span class="score-dash">&ndash;</span>
                                            <span [class.winner]="isT2Winner(game)">{{ game.t2Score }}</span>
                                        } @else {
                                            <span class="no-score">&mdash;</span>
                                        }
                                    }
                                </td>

                                <!-- Actions -->
                                @if (canScore()) {
                                    <td class="cell-actions">
                                        <button class="btn-edit-game"
                                                title="Edit game"
                                                (click)="editGame.emit(game.gid)">
                                            <i class="bi bi-pencil-square"></i>
                                        </button>
                                    </td>
                                }
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        }
    `,
    styles: [`
        :host {
            display: block;
        }

        .games-loading,
        .games-empty {
            display: flex;
            align-items: center;
            justify-content: center;
            padding: var(--space-8) var(--space-4);
            font-size: var(--font-size-sm);
        }

        .games-table-wrap {
            overflow-x: auto;
            -webkit-overflow-scrolling: touch;
        }

        .games-table {
            width: 100%;
            border-collapse: collapse;
            font-size: var(--font-size-sm);
        }

        /* ── Header ── */
        thead th {
            background: var(--bs-tertiary-bg);
            color: var(--bs-body-color);
            font-weight: 600;
            font-size: var(--font-size-xs, 0.75rem);
            text-transform: uppercase;
            letter-spacing: 0.03em;
            padding: var(--space-1) var(--space-2);
            white-space: nowrap;
            border-bottom: 2px solid var(--bs-border-color);
            text-align: left;
        }

        .col-score,
        .col-actions {
            text-align: center;
        }

        /* ── Rows ── */
        .game-row {
            border-left: 3px solid transparent;
        }

        .game-row td {
            padding: var(--space-1) var(--space-2);
            white-space: nowrap;
            border-bottom: 1px solid var(--bs-border-color-translucent);
            color: var(--bs-body-color);
            vertical-align: top;
        }

        /* Alternating row colors */
        .game-row:nth-child(even) td {
            background: var(--bs-tertiary-bg);
        }

        .game-row:hover td {
            background: var(--bs-secondary-bg);
        }

        /* ── Column-specific styles ── */
        .cell-date,
        .cell-time {
            font-variant-numeric: tabular-nums;
        }

        .cell-field .clickable,
        .cell-team .clickable {
            color: var(--bs-primary);
            cursor: pointer;
            text-decoration: none;
        }

        .cell-field .clickable:hover,
        .cell-team .clickable:hover {
            text-decoration: underline;
        }

        .annotation {
            font-size: var(--font-size-xs, 0.75rem);
            font-style: italic;
            color: var(--bs-secondary-color);
            line-height: 1.2;
        }

        /* ── Score column ── */
        .cell-score {
            text-align: center;
            font-variant-numeric: tabular-nums;
            font-family: var(--bs-font-monospace);
        }

        .cell-score.editable {
            cursor: pointer;
        }

        .cell-score.editable:hover {
            background: var(--bs-primary-bg-subtle);
        }

        .winner {
            font-weight: 700;
        }

        .no-score {
            color: var(--bs-secondary-color);
        }

        .score-dash {
            margin: 0 var(--space-1);
            color: var(--bs-secondary-color);
        }

        /* ── Inline score editing ── */
        .score-edit {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
        }

        .score-input {
            width: 40px;
            padding: 1px var(--space-1);
            text-align: center;
            font-size: var(--font-size-sm);
            font-family: var(--bs-font-monospace);
            border: 1px solid var(--bs-primary);
            border-radius: var(--bs-border-radius-sm, 0.2rem);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            outline: none;
        }

        .score-input:focus {
            box-shadow: 0 0 0 2px var(--bs-primary-bg-subtle);
        }

        /* Hide number input spinners */
        .score-input::-webkit-inner-spin-button,
        .score-input::-webkit-outer-spin-button {
            -webkit-appearance: none;
            margin: 0;
        }
        .score-input {
            -moz-appearance: textfield;
        }

        /* ── Actions column ── */
        .cell-actions {
            text-align: center;
            padding: var(--space-1);
        }

        .btn-edit-game {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            border: none;
            background: transparent;
            color: var(--bs-secondary-color);
            cursor: pointer;
            padding: 2px var(--space-1);
            border-radius: var(--bs-border-radius-sm, 0.2rem);
            font-size: var(--font-size-sm);
            transition: color 0.15s, background-color 0.15s;
        }

        .btn-edit-game:hover {
            color: var(--bs-primary);
            background: var(--bs-primary-bg-subtle);
        }
    `]
})
export class GamesTabComponent {
    // ── Inputs ──
    readonly games = input<ViewGameDto[]>([]);
    readonly canScore = input<boolean>(false);
    readonly isLoading = input<boolean>(false);

    // ── Outputs ──
    @Output() quickScore = new EventEmitter<{ gid: number; t1Score: number; t2Score: number }>();
    @Output() editGame = new EventEmitter<number>();
    @Output() viewTeamResults = new EventEmitter<string>();
    @Output() viewFieldInfo = new EventEmitter<string>();

    // ── Inline edit state ──
    readonly editingGid = signal<number | null>(null);
    readonly editT1Score = signal<number>(0);
    readonly editT2Score = signal<number>(0);

    // ── Date / time formatting ──

    /** Formats ISO date string as "ddd M/d" (e.g., "Sat 3/1") */
    formatDate(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return '';
        const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
        return `${days[d.getDay()]} ${d.getMonth() + 1}/${d.getDate()}`;
    }

    /** Formats ISO date string as "h:mm a" (e.g., "9:30 AM") */
    formatTime(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return '';
        let hours = d.getHours();
        const minutes = d.getMinutes();
        const ampm = hours >= 12 ? 'PM' : 'AM';
        hours = hours % 12 || 12;
        const mm = minutes.toString().padStart(2, '0');
        return `${hours}:${mm} ${ampm}`;
    }

    // ── Score helpers ──

    hasScore(game: ViewGameDto): boolean {
        return game.t1Score != null && game.t2Score != null;
    }

    isT1Winner(game: ViewGameDto): boolean {
        return game.t1Score != null && game.t2Score != null && game.t1Score > game.t2Score;
    }

    isT2Winner(game: ViewGameDto): boolean {
        return game.t1Score != null && game.t2Score != null && game.t2Score > game.t1Score;
    }

    // ── Inline score editing ──

    onScoreCellClick(game: ViewGameDto): void {
        if (!this.canScore()) return;
        if (this.editingGid() === game.gid) return;
        this.editingGid.set(game.gid);
        this.editT1Score.set(game.t1Score ?? 0);
        this.editT2Score.set(game.t2Score ?? 0);
    }

    saveScore(gid: number): void {
        this.quickScore.emit({
            gid,
            t1Score: this.editT1Score(),
            t2Score: this.editT2Score()
        });
        this.editingGid.set(null);
    }

    cancelEdit(): void {
        this.editingGid.set(null);
    }
}
