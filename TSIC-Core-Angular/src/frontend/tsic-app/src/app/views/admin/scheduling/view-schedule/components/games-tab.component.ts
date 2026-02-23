import {
    Component,
    ChangeDetectionStrategy,
    input,
    signal,
    computed,
    Output,
    EventEmitter
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { ViewGameDto } from '@core/api';

@Component({
    selector: 'app-games-tab',
    standalone: true,
    imports: [FormsModule],
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
            <!-- Summary -->
            <div class="games-summary">
                {{ games().length }} {{ games().length === 1 ? 'game' : 'games' }}
                @if (scoredCount() < games().length) {
                    <span class="summary-sep">&middot;</span>
                    {{ scoredCount() }} scored
                }
            </div>

            <!-- ═══════════════════════════════════════════════════════
                 DESKTOP GRID (≥768px) — div-based, CSS order anti-scrape
                 DOM order is intentionally scrambled; CSS order restores visual.
                 ═══════════════════════════════════════════════════════ -->
            <div class="games-grid desktop-view" role="table" aria-label="Games schedule">
                <!-- Header -->
                <div class="grid-header" role="row">
                    <span class="hdr hdr-num" role="columnheader">#</span>
                    <span class="hdr hdr-dt" role="columnheader">Date / Time</span>
                    <span class="hdr hdr-loc" role="columnheader">Location</span>
                    <span class="hdr hdr-pool" role="columnheader">Pool</span>
                    <span class="hdr hdr-home" role="columnheader">Home</span>
                    <span class="hdr hdr-score" role="columnheader">Score</span>
                    <span class="hdr hdr-away" role="columnheader">Away</span>
                    @if (canScore()) {
                        <span class="hdr hdr-edit" role="columnheader"></span>
                    }
                </div>

                @for (game of games(); track game.gid; let i = $index) {
                    <div class="game-row" role="row"
                         [class.row-even]="i % 2 === 1"
                         [class.row-dimmed]="game.gStatusCode === 5">

                        <!-- ▼ Away team (DOM:1 Visual:7) -->
                        <span class="cell cell-away" role="cell" aria-colindex="7">
                            <span class="decoy" aria-hidden="true">{{ decoyText(game.gid, 0) }}</span>
                            @if (game.t2Id) {
                                <span class="clickable" (click)="viewTeamResults.emit(game.t2Id!)">{{ game.t2Name }}</span>
                            } @else {
                                <span>{{ game.t2Name }}</span>
                            }
                            @if (game.t2Record) { <span class="record"> ({{ game.t2Record }})</span> }
                            @if (game.t2Ann) { <span class="annotation"> {{ game.t2Ann }}</span> }
                        </span>

                        <!-- ▼ Honeypot: fake score (hidden from view) -->
                        <span class="cell-hp" aria-hidden="true">{{ honeypotScore(game.gid) }}</span>

                        <!-- ▼ Score + status dot (DOM:3 Visual:6) -->
                        <span class="cell cell-score" role="cell" aria-colindex="6"
                              [class.editable]="canScore()"
                              (click)="onScoreCellClick(game)">
                            @if (editingGid() === game.gid) {
                                <span class="score-edit" (click)="$event.stopPropagation()">
                                    <input type="number" class="score-input" [min]="0" [max]="99"
                                           [ngModel]="editT1Score()"
                                           (ngModelChange)="editT1Score.set($event)"
                                           (keydown.enter)="saveScore(game.gid)"
                                           (keydown.escape)="cancelEdit()">
                                    <span class="score-dash">&ndash;</span>
                                    <input type="number" class="score-input" [min]="0" [max]="99"
                                           [ngModel]="editT2Score()"
                                           (ngModelChange)="editT2Score.set($event)"
                                           (keydown.enter)="saveScore(game.gid)"
                                           (keydown.escape)="cancelEdit()">
                                </span>
                            } @else {
                                @if (hasScore(game)) {
                                    <span class="score-val" [class.winner]="isT1Winner(game)" [class.loser]="isT2Winner(game)">{{ game.t1Score }}</span>
                                    <span class="score-dash">&ndash;</span>
                                    <span class="score-val" [class.winner]="isT2Winner(game)" [class.loser]="isT1Winner(game)">{{ game.t2Score }}</span>
                                } @else {
                                    <span class="score-val no-score">&ndash;</span>
                                }
                                @if (game.gStatusCode === 3 || game.gStatusCode === 4 || game.gStatusCode === 5) {
                                    <span class="status-dot"
                                          [class.dot-amber]="game.gStatusCode === 3"
                                          [class.dot-purple]="game.gStatusCode === 4"
                                          [class.dot-red]="game.gStatusCode === 5"
                                          [title]="statusTooltip(game.gStatusCode)"></span>
                                }
                            }
                        </span>

                        <!-- ▼ Row number (DOM:4 Visual:1) -->
                        <span class="cell cell-num" role="cell" aria-colindex="1">{{ i + 1 }}</span>

                        <!-- ▼ Honeypot: fake team (hidden from view) -->
                        <span class="cell-hp" aria-hidden="true">{{ honeypotTeam(game.gid) }}</span>

                        <!-- ▼ Home team (DOM:6 Visual:5) -->
                        <span class="cell cell-home" role="cell" aria-colindex="5">
                            @if (game.t1Id) {
                                <span class="clickable" (click)="viewTeamResults.emit(game.t1Id!)">{{ game.t1Name }}</span>
                            } @else {
                                <span>{{ game.t1Name }}</span>
                            }
                            <span class="decoy" aria-hidden="true">{{ decoyText(game.gid, 1) }}</span>
                            @if (game.t1Record) { <span class="record"> ({{ game.t1Record }})</span> }
                            @if (game.t1Ann) { <span class="annotation"> {{ game.t1Ann }}</span> }
                        </span>

                        <!-- ▼ Date/Time (DOM:7 Visual:2) -->
                        <span class="cell cell-dt" role="cell" aria-colindex="2">
                            <span class="dt-date">{{ formatDate(game.gDate) }}</span>
                            <span class="dt-time">{{ formatTime(game.gDate) }}</span>
                        </span>

                        <!-- ▼ Pool badge (DOM:8 Visual:4) -->
                        <span class="cell cell-pool" role="cell" aria-colindex="4">
                            <span class="ag-badge"
                                  [style.background-color]="game.color"
                                  [style.color]="contrastColor(game.color)">{{ game.agDiv }}</span>
                        </span>

                        <!-- ▼ Location (DOM:9 Visual:3) -->
                        <span class="cell cell-loc" role="cell" aria-colindex="3">
                            @if (mapsUrl(game)) {
                                <a [href]="mapsUrl(game)" target="_blank" rel="noopener"
                                   class="loc-link" [title]="game.fAddress || game.fName">
                                    {{ game.fName }} <i class="bi bi-box-arrow-up-right loc-icon"></i>
                                </a>
                            } @else {
                                <span>{{ game.fName }}</span>
                            }
                        </span>

                        <!-- ▼ Edit button (DOM:10 Visual:8) — admin only -->
                        @if (canScore()) {
                            <span class="cell cell-edit" role="cell" aria-colindex="8">
                                <button class="btn-edit-game" title="Edit game"
                                        (click)="editGame.emit(game.gid)">
                                    <i class="bi bi-pencil-square"></i>
                                </button>
                            </span>
                        }
                    </div>
                }
            </div>

            <!-- ═══════════════════════════════════════════════════════
                 MOBILE CARDS (<768px) — separate DOM structure (anti-scrape benefit)
                 ═══════════════════════════════════════════════════════ -->
            <div class="games-cards mobile-view">
                @for (game of games(); track game.gid; let i = $index) {
                    <div class="game-card" [class.card-dimmed]="game.gStatusCode === 5">
                        <!-- Header: #, date/time, pool, status, edit -->
                        <div class="card-top">
                            <span class="card-num">{{ i + 1 }}</span>
                            <span class="card-dt">{{ formatDate(game.gDate) }} &middot; {{ formatTime(game.gDate) }}</span>
                            <span class="ag-badge"
                                  [style.background-color]="game.color"
                                  [style.color]="contrastColor(game.color)">{{ game.agDiv }}</span>
                            @if (game.gStatusCode === 3 || game.gStatusCode === 4 || game.gStatusCode === 5) {
                                <span class="status-dot"
                                      [class.dot-amber]="game.gStatusCode === 3"
                                      [class.dot-purple]="game.gStatusCode === 4"
                                      [class.dot-red]="game.gStatusCode === 5"
                                      [title]="statusTooltip(game.gStatusCode)"></span>
                            }
                            @if (canScore()) {
                                <button class="btn-edit-game ms-auto" title="Edit game"
                                        (click)="editGame.emit(game.gid)">
                                    <i class="bi bi-pencil-square"></i>
                                </button>
                            }
                        </div>

                        <!-- Matchup: Home  score  Away -->
                        <div class="card-matchup">
                            <span class="card-team card-team-home">
                                @if (game.t1Id) {
                                    <span class="clickable" (click)="viewTeamResults.emit(game.t1Id!)">{{ game.t1Name }}</span>
                                } @else {
                                    {{ game.t1Name }}
                                }
                                @if (game.t1Record) { <span class="record"> ({{ game.t1Record }})</span> }
                            </span>

                            <span class="card-score"
                                  [class.editable]="canScore()"
                                  (click)="onScoreCellClick(game)">
                                @if (editingGid() === game.gid) {
                                    <span class="score-edit" (click)="$event.stopPropagation()">
                                        <input type="number" class="score-input" [min]="0" [max]="99"
                                               [ngModel]="editT1Score()"
                                               (ngModelChange)="editT1Score.set($event)"
                                               (keydown.enter)="saveScore(game.gid)"
                                               (keydown.escape)="cancelEdit()">
                                        <span class="score-dash">&ndash;</span>
                                        <input type="number" class="score-input" [min]="0" [max]="99"
                                               [ngModel]="editT2Score()"
                                               (ngModelChange)="editT2Score.set($event)"
                                               (keydown.enter)="saveScore(game.gid)"
                                               (keydown.escape)="cancelEdit()">
                                    </span>
                                } @else if (hasScore(game)) {
                                    <span class="score-val" [class.winner]="isT1Winner(game)" [class.loser]="isT2Winner(game)">{{ game.t1Score }}</span>
                                    <span class="score-dash">&ndash;</span>
                                    <span class="score-val" [class.winner]="isT2Winner(game)" [class.loser]="isT1Winner(game)">{{ game.t2Score }}</span>
                                } @else {
                                    <span class="score-val no-score">&ndash;</span>
                                }
                            </span>

                            <span class="card-team card-team-away">
                                @if (game.t2Id) {
                                    <span class="clickable" (click)="viewTeamResults.emit(game.t2Id!)">{{ game.t2Name }}</span>
                                } @else {
                                    {{ game.t2Name }}
                                }
                                @if (game.t2Record) { <span class="record"> ({{ game.t2Record }})</span> }
                            </span>
                        </div>

                        <!-- Location -->
                        <div class="card-location">
                            @if (mapsUrl(game)) {
                                <a [href]="mapsUrl(game)" target="_blank" rel="noopener" class="loc-link">
                                    <i class="bi bi-geo-alt"></i> {{ game.fName }}
                                </a>
                            } @else {
                                <i class="bi bi-geo-alt text-body-secondary"></i>
                                <span class="text-body-secondary">{{ game.fName }}</span>
                            }
                        </div>
                    </div>
                }
            </div>
        }
    `,
    styles: [`
        :host { display: block; }

        .games-loading,
        .games-empty {
            display: flex;
            align-items: center;
            justify-content: center;
            padding: var(--space-8) var(--space-4);
            font-size: var(--font-size-sm);
        }

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

        /* ═══ Anti-scraping: honeypot & decoy ═══ */

        .cell-hp {
            display: none;
            position: absolute;
            left: -9999px;
        }

        .decoy {
            position: absolute;
            width: 1px;
            height: 1px;
            overflow: hidden;
            clip: rect(0, 0, 0, 0);
            opacity: 0;
            user-select: none;
            pointer-events: none;
        }

        /* ═══ DESKTOP GRID ═══ */

        /* Container grid — columns sized across ALL rows (table-like alignment).
           subgrid on each row inherits these tracks so every cell in a column
           shares the exact same left/right edges, just like a <table>. */
        .games-grid {
            display: none;
            grid-template-columns:
                2rem                    /* #          */
                5.5rem                  /* date/time  */
                auto                    /* location   */
                auto                    /* pool       */
                auto                    /* home →     */
                auto                    /* score      */
                auto                    /* ← away     */
                2rem;                   /* edit       */
            column-gap: var(--space-2);
            margin: 0 var(--space-2);
        }

        /* Each row inherits parent column sizing via subgrid */
        .grid-header,
        .game-row {
            grid-column: 1 / -1;
            display: grid;
            grid-template-columns: subgrid;
            align-items: center;
        }

        /* ── Header ── */
        .grid-header {
            border-bottom: 2px solid var(--bs-border-color);
            padding-top: var(--space-1);
            padding-bottom: var(--space-1);
        }

        .hdr {
            font-size: var(--font-size-xs);
            font-weight: 600;
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            letter-spacing: 0.03em;
            white-space: nowrap;
        }

        /* Anti-scraping: CSS order restores visual column positions */
        .hdr-num,   .cell-num   { order: 1; }
        .hdr-dt,    .cell-dt    { order: 2; }
        .hdr-loc,   .cell-loc   { order: 3; }
        .hdr-pool,  .cell-pool  { order: 4; text-align: left; }
        .hdr-home,  .cell-home  { order: 5; text-align: right; }
        .hdr-score, .cell-score { order: 6; text-align: center; }
        .hdr-away,  .cell-away  { order: 7; text-align: left; }
        .hdr-edit,  .cell-edit  { order: 8; }

        /* ── Game row ── */
        .game-row {
            border-bottom: 1px solid var(--bs-border-color);
            padding-top: var(--space-1);
            padding-bottom: var(--space-1);
            min-height: 36px;
        }

        .row-even { background: var(--bs-tertiary-bg); }
        .game-row:hover { background: var(--bs-secondary-bg); }
        .row-dimmed { opacity: 0.5; }

        /* Cell common */
        .cell {
            font-size: var(--font-size-sm);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            position: relative; /* for decoy positioning */
        }

        /* Row number */
        .cell-num {
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
            font-variant-numeric: tabular-nums;
            text-align: center;
        }

        /* Date/Time */
        .cell-dt {
            display: flex;
            flex-direction: column;
            line-height: 1.3;
        }

        .dt-date {
            font-weight: 600;
            font-size: var(--font-size-xs);
            color: var(--bs-body-color);
        }

        .dt-time {
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
        }

        /* Location */
        .loc-link {
            color: var(--bs-primary);
            text-decoration: none;
            font-size: var(--font-size-xs);
        }

        .loc-link:hover { text-decoration: underline; }

        .loc-icon {
            font-size: 0.6rem;
            vertical-align: super;
            opacity: 0.6;
        }

        /* Pool badge */
        .ag-badge {
            display: inline-block;
            padding: 0 var(--space-1);
            border-radius: var(--radius-sm, 4px);
            font-weight: 600;
            font-size: var(--font-size-xs);
            line-height: 1.5;
            white-space: nowrap;
        }

        .clickable {
            color: var(--bs-primary);
            cursor: pointer;
            text-decoration: none;
        }

        .clickable:hover { text-decoration: underline; }

        .record {
            color: var(--bs-secondary-color);
            font-size: var(--font-size-xs);
            font-variant-numeric: tabular-nums;
        }

        .annotation {
            font-style: italic;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-xs);
        }

        /* ── Score column ── */
        .cell-score {
            font-variant-numeric: tabular-nums;
            font-family: var(--bs-font-monospace);
            white-space: nowrap;
            /* text-align: center inherited from order rule — centres the inline-block group */
        }

        .cell-score.editable { cursor: pointer; }
        .cell-score.editable:hover { background: var(--bs-primary-bg-subtle); border-radius: var(--radius-sm); }

        .score-val {
            font-size: var(--font-size-lg);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
        }

        .score-dash {
            margin: 0 2px;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
        }

        .winner { color: var(--bs-success); }
        .loser  { color: var(--bs-danger); }
        .no-score { color: var(--bs-secondary-color); }

        /* Status dots */
        .status-dot {
            display: inline-block;
            width: 8px;
            height: 8px;
            border-radius: var(--radius-full);
            margin-left: var(--space-1);
            vertical-align: middle;
        }

        .dot-amber  { background: #f59e0b; }
        .dot-purple { background: #8b5cf6; }
        .dot-red    { background: var(--bs-danger); }

        /* Inline score editing */
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
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            outline: none;
        }

        .score-input:focus { box-shadow: 0 0 0 2px var(--bs-primary-bg-subtle); }

        .score-input::-webkit-inner-spin-button,
        .score-input::-webkit-outer-spin-button { -webkit-appearance: none; margin: 0; }
        .score-input { -moz-appearance: textfield; }

        /* Edit button */
        .cell-edit { text-align: center; justify-self: center; }

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
            border-radius: var(--radius-sm);
            font-size: var(--font-size-sm);
            transition: color 0.15s, background-color 0.15s;
        }

        .btn-edit-game:hover {
            color: var(--bs-primary);
            background: var(--bs-primary-bg-subtle);
        }

        /* ═══ MOBILE CARDS ═══ */

        .games-cards {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
        }

        .game-card {
            background: var(--bs-card-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            padding: var(--space-2) var(--space-3);
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
        }

        .card-dimmed { opacity: 0.5; }

        /* Card top row */
        .card-top {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--font-size-xs);
        }

        .card-num {
            color: var(--bs-secondary-color);
            font-variant-numeric: tabular-nums;
            min-width: 20px;
        }

        .card-dt {
            color: var(--bs-body-color);
            font-weight: 600;
        }

        .ms-auto { margin-left: auto; }

        /* Card matchup row */
        .card-matchup {
            display: flex;
            align-items: center;
            gap: var(--space-2);
        }

        .card-team {
            flex: 1;
            min-width: 0;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            font-size: var(--font-size-sm);
        }

        .card-team-home { text-align: right; }
        .card-team-away { text-align: left; }

        .card-score {
            flex-shrink: 0;
            text-align: center;
            font-variant-numeric: tabular-nums;
            font-family: var(--bs-font-monospace);
            padding: 0 var(--space-1);
        }

        .card-score.editable { cursor: pointer; }

        .card-score .score-val { font-size: var(--font-size-base); }
        .card-score .score-dash { margin: 0 2px; }

        /* Card location row */
        .card-location {
            font-size: var(--font-size-xs);
            padding-top: var(--space-1);
            border-top: 1px solid var(--bs-border-color);
        }

        .card-location .loc-link {
            font-size: var(--font-size-xs);
        }

        /* ═══ Responsive — must be AFTER all base rules for correct cascade ═══ */
        @media (min-width: 768px) {
            .games-grid  { display: grid; }
            .games-cards { display: none; }
        }

        @media (min-width: 1200px) {
            .games-grid {
                grid-template-columns:
                    2rem 6rem auto auto auto auto auto 2rem;
            }
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

    // ── Inline edit state ──
    readonly editingGid = signal<number | null>(null);
    readonly editT1Score = signal<number>(0);
    readonly editT2Score = signal<number>(0);

    // ── Derived ──
    readonly scoredCount = computed(() =>
        this.games().filter(g => g.t1Score != null && g.t2Score != null).length
    );

    // ══════════════════════════════════════════════════════════════════
    // Date / time formatting
    // ══════════════════════════════════════════════════════════════════

    formatDate(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return '';
        const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
        return `${days[d.getDay()]} ${d.getMonth() + 1}/${d.getDate()}`;
    }

    formatTime(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return '';
        let hours = d.getHours();
        const minutes = d.getMinutes();
        const ampm = hours >= 12 ? 'PM' : 'AM';
        hours = hours % 12 || 12;
        return `${hours}:${minutes.toString().padStart(2, '0')} ${ampm}`;
    }

    // ══════════════════════════════════════════════════════════════════
    // Color / contrast helpers
    // ══════════════════════════════════════════════════════════════════

    private contrastCache = new Map<string, string>();

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

    // ══════════════════════════════════════════════════════════════════
    // Maps URL
    // ══════════════════════════════════════════════════════════════════

    mapsUrl(game: ViewGameDto): string | null {
        if (game.fAddress) {
            return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(game.fAddress)}`;
        }
        if (game.latitude && game.longitude) {
            return `https://www.google.com/maps/search/?api=1&query=${game.latitude},${game.longitude}`;
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════════════
    // Status dots
    // ══════════════════════════════════════════════════════════════════

    statusTooltip(code: number | null | undefined): string {
        switch (code) {
            case 3: return 'Rescheduled';
            case 4: return 'Forfeit';
            case 5: return 'Cancelled';
            default: return '';
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Score helpers
    // ══════════════════════════════════════════════════════════════════

    hasScore(game: ViewGameDto): boolean {
        return game.t1Score != null && game.t2Score != null;
    }

    isT1Winner(game: ViewGameDto): boolean {
        return game.t1Score != null && game.t2Score != null && game.t1Score > game.t2Score;
    }

    isT2Winner(game: ViewGameDto): boolean {
        return game.t1Score != null && game.t2Score != null && game.t2Score > game.t1Score;
    }

    onScoreCellClick(game: ViewGameDto): void {
        if (!this.canScore()) return;
        if (this.editingGid() === game.gid) return;
        this.editingGid.set(game.gid);
        this.editT1Score.set(game.t1Score ?? 0);
        this.editT2Score.set(game.t2Score ?? 0);
    }

    saveScore(gid: number): void {
        this.quickScore.emit({ gid, t1Score: this.editT1Score(), t2Score: this.editT2Score() });
        this.editingGid.set(null);
    }

    cancelEdit(): void {
        this.editingGid.set(null);
    }

    // ══════════════════════════════════════════════════════════════════
    // Anti-scraping: honeypot & decoy generators
    // ══════════════════════════════════════════════════════════════════

    private readonly fakeTeams = [
        'Eagles FC', 'Thunder SC', 'Lightning', 'Storm United',
        'Hawks Elite', 'Blazers SC', 'Dynamo FC', 'Rapids United',
        'Strikers SC', 'Wolves FC', 'Phoenix SC', 'Atlas FC'
    ];

    honeypotTeam(gid: number): string {
        return this.fakeTeams[gid % this.fakeTeams.length];
    }

    honeypotScore(gid: number): string {
        return `${(gid * 3 + 2) % 7}-${(gid * 2 + 1) % 5}`;
    }

    decoyText(gid: number, slot: number): string {
        const base = (gid * 17 + slot * 31) % this.fakeTeams.length;
        return this.fakeTeams[base];
    }
}
