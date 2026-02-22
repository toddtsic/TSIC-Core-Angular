import {
    Component,
    ChangeDetectionStrategy,
    input,
    signal,
    computed,
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
            <!-- Summary line -->
            <div class="games-summary">
                {{ games().length }} {{ games().length === 1 ? 'game' : 'games' }}
                @if (scoredCount() < games().length) {
                    <span class="summary-sep">&middot;</span>
                    {{ scoredCount() }} scored
                }
            </div>

            <!-- Flat games table -->
            <div class="games-table-wrap">
                <table class="games-table">
                    @for (game of games(); track game.gid; let i = $index) {
                        <tbody class="game-group" [class.game-even]="i % 2 === 1">
                            <!-- Row 1: date, time, teams + score -->
                            <tr class="game-row-main">
                                <td class="cell-date" rowspan="2">{{ formatDate(game.gDate) }}</td>
                                <td class="cell-time" rowspan="2">{{ formatTime(game.gDate) }}</td>
                                <td class="cell-team cell-team-home">
                                    @if (game.t1Id) {
                                        <span class="clickable" (click)="viewTeamResults.emit(game.t1Id!)">{{ game.t1Name }}</span>
                                    } @else {
                                        {{ game.t1Name }}
                                    }
                                </td>
                                <td class="cell-score" rowspan="2"
                                    [class.editable]="canScore()"
                                    (click)="onScoreCellClick(game)">
                                    @if (editingGid() === game.gid) {
                                        <div class="score-edit" (click)="$event.stopPropagation()">
                                            <input type="number" class="score-input"
                                                   [min]="0" [max]="99"
                                                   [ngModel]="editT1Score()"
                                                   (ngModelChange)="editT1Score.set($event)"
                                                   (keydown.enter)="saveScore(game.gid)"
                                                   (keydown.escape)="cancelEdit()"
                                                   #t1Input>
                                            <span class="score-dash">&ndash;</span>
                                            <input type="number" class="score-input"
                                                   [min]="0" [max]="99"
                                                   [ngModel]="editT2Score()"
                                                   (ngModelChange)="editT2Score.set($event)"
                                                   (keydown.enter)="saveScore(game.gid)"
                                                   (keydown.escape)="cancelEdit()">
                                        </div>
                                    } @else if (hasScore(game)) {
                                        <span class="score-val" [class.winner]="isT1Winner(game)" [class.loser]="isT2Winner(game)">{{ game.t1Score }}</span>
                                        <span class="score-dash">&ndash;</span>
                                        <span class="score-val" [class.winner]="isT2Winner(game)" [class.loser]="isT1Winner(game)">{{ game.t2Score }}</span>
                                    } @else if (game.t1Score != null) {
                                        <span class="score-val">{{ game.t1Score }}</span>
                                        <span class="score-dash">&ndash;</span>
                                        <span class="score-val no-score">&middot;</span>
                                    } @else if (game.t2Score != null) {
                                        <span class="score-val no-score">&middot;</span>
                                        <span class="score-dash">&ndash;</span>
                                        <span class="score-val">{{ game.t2Score }}</span>
                                    } @else {
                                        <span class="score-val no-score">&ndash;</span>
                                    }
                                </td>
                                <td class="cell-team">
                                    @if (game.t2Id) {
                                        <span class="clickable" (click)="viewTeamResults.emit(game.t2Id!)">{{ game.t2Name }}</span>
                                    } @else {
                                        {{ game.t2Name }}
                                    }
                                </td>
                                @if (canScore()) {
                                    <td class="cell-actions" rowspan="2">
                                        <button class="btn-edit-game"
                                                title="Edit game"
                                                (click)="editGame.emit(game.gid)">
                                            <i class="bi bi-pencil-square"></i>
                                        </button>
                                    </td>
                                }
                            </tr>
                            <!-- Row 2: field, badge, records -->
                            <tr class="game-row-sub">
                                <!-- date+time cells spanned from row 1 -->
                                <td class="cell-team cell-team-home cell-sub">
                                    <span class="clickable meta-field" (click)="viewFieldInfo.emit(game.fieldId)">{{ game.fName }}</span>
                                    <span class="meta-sep">&middot;</span>
                                    <span class="ag-badge"
                                          [style.background-color]="game.color"
                                          [style.color]="contrastColor(game.color)">{{ game.agDiv }}</span>
                                    @if (game.t1Record) {
                                        <span class="record ms-1">({{ game.t1Record }})</span>
                                    }
                                    @if (game.t1Ann) {
                                        <span class="annotation">{{ game.t1Ann }}</span>
                                    }
                                </td>
                                <!-- score cell spanned -->
                                <td class="cell-team cell-sub">
                                    @if (game.t2Record) {
                                        <span class="record">({{ game.t2Record }})</span>
                                    }
                                    @if (game.t2Ann) {
                                        <span class="annotation">{{ game.t2Ann }}</span>
                                    }
                                </td>
                                <!-- actions cell spanned -->
                            </tr>
                        </tbody>
                    }
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

        /* ── Summary line ── */
        .games-summary {
            padding: var(--space-2) var(--space-3);
            font-size: var(--font-size-sm);
            color: var(--bs-secondary-color);
            font-weight: 500;
        }

        .summary-sep {
            margin: 0 var(--space-1);
            opacity: 0.5;
        }

        /* ── Table ── */
        .games-table-wrap {
            overflow-x: auto;
            -webkit-overflow-scrolling: touch;
        }

        .games-table {
            width: 100%;
            border-collapse: collapse;
            font-size: var(--font-size-sm);
        }

        /* ── Date / Time columns ── */
        .cell-date,
        .cell-time {
            white-space: nowrap;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-xs);
            vertical-align: top;
            padding-top: var(--space-1);
            padding-bottom: var(--space-1);
        }

        .cell-date {
            font-weight: 600;
            color: var(--bs-body-color);
        }

        /* ── Game group (tbody per game) ── */
        .game-group {
            border-bottom: 1px solid var(--bs-border-color);
        }

        .game-group:first-child {
            border-top: 1px solid var(--bs-border-color);
        }

        .game-group td {
            padding: 0 var(--space-2);
            color: var(--bs-body-color);
        }

        .cell-score,
        .cell-actions {
            white-space: nowrap;
        }

        /* Row 1: no bottom border */
        .game-row-main td {
            padding-top: var(--space-1);
            padding-bottom: 0;
            vertical-align: bottom;
        }

        /* Row 2 */
        .game-row-sub td {
            padding-top: 0;
            padding-bottom: var(--space-1);
            vertical-align: top;
        }

        /* Rowspan cells */
        .game-row-main td[rowspan] {
            vertical-align: top;
            padding-top: var(--space-1);
            padding-bottom: var(--space-1);
        }

        /* Alternating group colors */
        .game-even td {
            background: var(--bs-tertiary-bg);
        }

        .game-group:hover td {
            background: var(--bs-secondary-bg);
        }

        /* ── Column styles ── */
        .cell-team-home {
            text-align: right;
        }

        .cell-team .clickable,
        .meta-field {
            color: var(--bs-primary);
            cursor: pointer;
            text-decoration: none;
        }

        .cell-team .clickable:hover,
        .meta-field:hover {
            text-decoration: underline;
        }

        .meta-sep {
            margin: 0 var(--space-1);
            opacity: 0.5;
        }

        /* ── Row 2 sub cells ── */
        .cell-sub {
            font-size: var(--font-size-xs, 0.75rem);
        }

        .record {
            color: var(--bs-secondary-color);
            font-variant-numeric: tabular-nums;
        }

        .annotation {
            font-style: italic;
            color: var(--bs-secondary-color);
            margin-left: var(--space-1);
        }

        /* ── Score column ── */
        .cell-score {
            text-align: center;
            font-variant-numeric: tabular-nums;
            font-family: var(--bs-font-monospace);
            border-left: 1px solid var(--bs-border-color);
            border-right: 1px solid var(--bs-border-color);
            padding-left: var(--space-3);
            padding-right: var(--space-3);
        }

        .cell-score.editable {
            cursor: pointer;
        }

        .cell-score.editable:hover {
            background: var(--bs-primary-bg-subtle) !important;
        }

        .score-val {
            font-size: var(--font-size-xl);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
        }

        .score-dash {
            margin: 0 var(--space-1);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
        }

        .winner {
            color: var(--bs-success);
        }

        .loser {
            color: var(--bs-danger);
        }

        .no-score {
            color: var(--bs-secondary-color);
        }

        /* ── Inline score editing ── */
        .score-edit {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
        }

        .score-input {
            width: 44px;
            padding: 2px var(--space-1);
            text-align: center;
            font-size: var(--font-size-base);
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
        }

        .btn-edit-game {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 24px;
            height: 24px;
            border: 1px solid var(--bs-border-color);
            background: var(--bs-secondary-bg);
            color: var(--bs-secondary-color);
            cursor: pointer;
            padding: 0;
            border-radius: var(--bs-border-radius-sm, 0.2rem);
            font-size: var(--font-size-sm);
            transition: color 0.15s, background-color 0.15s, border-color 0.15s;
        }

        .btn-edit-game:hover {
            color: var(--bs-primary);
            background: var(--bs-primary-bg-subtle);
        }

        /* ── Age-group color badge ── */
        .ag-badge {
            display: inline-block;
            padding: 0 var(--space-1);
            border-radius: var(--radius-sm, 4px);
            font-weight: 600;
            line-height: 1.4;
            white-space: nowrap;
        }

        .ms-1 {
            margin-left: var(--space-1);
        }

        /* ── Responsive: tablet ── */
        @media (max-width: 767px) {
            .games-table { font-size: var(--font-size-xs, 0.75rem); }
            .game-group td { padding: 0 var(--space-1); }
            .cell-score { padding-left: var(--space-2); padding-right: var(--space-2); }
            .score-val { font-size: var(--font-size-lg); }
        }

        /* ── Responsive: phone (card-like rows) ── */
        @media (max-width: 575px) {
            .games-table { display: block; }
            .games-table > tbody { display: block; }

            .game-group {
                display: block;
                border-bottom: 1px solid var(--bs-border-color);
                padding: var(--space-1) 0;
            }

            .game-row-main {
                display: flex;
                align-items: center;
            }

            .game-row-main .cell-date,
            .game-row-main .cell-time {
                display: none;
            }

            .game-row-main .cell-team {
                flex: 1;
                min-width: 0;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
                padding: var(--space-1);
                font-size: var(--font-size-xs);
            }

            .game-row-main .cell-team-home { text-align: right; }

            .game-row-main .cell-score {
                flex-shrink: 0;
                border: none;
                padding: var(--space-1);
            }

            .game-row-main .cell-actions {
                flex-shrink: 0;
                padding: 0 var(--space-1);
            }

            .score-val { font-size: var(--font-size-base); }
            .score-dash { margin: 0 2px; }

            .game-row-sub {
                display: flex;
                padding: 0 var(--space-1);
            }

            .game-row-sub td { border-bottom: none; }

            .game-row-sub .cell-sub {
                flex: 1;
                min-width: 0;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            }

            .game-row-sub .cell-team-home { text-align: left; }
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

    // ── Derived computeds ──

    readonly scoredCount = computed(() =>
        this.games().filter(g => g.t1Score != null && g.t2Score != null).length
    );

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

    // ── Color helpers ──

    private contrastCache = new Map<string, string>();

    /** Resolves any CSS color to RGB via canvas, returns black or white for contrast */
    contrastColor(cssColor: string | null | undefined): string {
        if (!cssColor) return 'inherit';
        const cached = this.contrastCache.get(cssColor);
        if (cached) return cached;

        const ctx = document.createElement('canvas').getContext('2d')!;
        ctx.fillStyle = cssColor;
        const hex = ctx.fillStyle.replace('#', '');
        const r = parseInt(hex.substring(0, 2), 16);
        const g = parseInt(hex.substring(2, 4), 16);
        const b = parseInt(hex.substring(4, 6), 16);
        const luminance = 0.299 * r + 0.587 * g + 0.114 * b;
        const result = luminance > 150 ? '#000' : '#fff';

        this.contrastCache.set(cssColor, result);
        return result;
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
