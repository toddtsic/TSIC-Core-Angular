import {
  Component,
  ChangeDetectionStrategy,
  input,
  signal,
  computed,
  output
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
            <!-- ═══════════════════════════════════════════════════════
                 DESKTOP GRID (≥768px) — div-based, CSS order anti-scrape
                 DOM order is intentionally scrambled; CSS order restores visual.
                 ═══════════════════════════════════════════════════════ -->
            <div class="games-grid desktop-view" role="table" aria-label="Games schedule">
                <!-- Header -->
                <div class="grid-header" role="row">
                    <span class="hdr hdr-num" role="columnheader" aria-label="Row number"></span>
                    <span class="hdr hdr-dt" role="columnheader">Date / Time</span>
                    <span class="hdr hdr-loc" role="columnheader">Location</span>
                    <span class="hdr hdr-pool" role="columnheader">Pool</span>
                    <span class="hdr hdr-home" role="columnheader">Home</span>
                    <span class="hdr hdr-score" role="columnheader">Score</span>
                    <span class="hdr hdr-away" role="columnheader">Away</span>
                    <span class="hdr hdr-status" role="columnheader">
                        Status
                        <span class="status-key" tabindex="0" role="button" aria-label="Show status key">
                            <i class="bi bi-info-circle" aria-hidden="true"></i>
                            <span class="status-key-popup" role="tooltip">
                                <span class="key-row"><span class="status-chip st-final">F</span>Final</span>
                                <span class="key-row"><span class="status-chip st-rescheduled">R</span>Rescheduled</span>
                                <span class="key-row"><span class="status-chip st-forfeit">X</span>Forfeit</span>
                                <span class="key-row"><span class="status-chip st-cancelled">C</span>Cancelled</span>
                            </span>
                        </span>
                    </span>
                </div>

                @for (game of games(); track game.gid; let i = $index) {
                    <div class="game-row" role="row"
                         [class.row-even]="i % 2 === 1"
                         [class.row-dimmed]="game.gStatusCode === 5">

                        <!-- ▼ Away team (DOM:1 Visual:7) -->
                        <span class="cell cell-away" role="cell" aria-colindex="7"
                              [class.is-won]="isT2Winner(game)"
                              [class.is-lost]="isT1Winner(game)">
                            <span class="decoy" aria-hidden="true">{{ decoyText(game.gid, 0) }}</span>
                            @if (game.t2SlotLabel) { <span class="seed-tag">{{ game.t2SlotLabel }}</span> }
                            @if (game.t2Id) {
                                <button type="button" class="team-star"
                                        [class.is-on]="isFollowed(game.t2Id)"
                                        [attr.aria-label]="(isFollowed(game.t2Id) ? 'Unfollow ' : 'Follow ') + game.t2Name"
                                        (click)="onStarClick(game.t2Id!)">
                                    <i class="bi" [class.bi-star-fill]="isFollowed(game.t2Id)" [class.bi-star]="!isFollowed(game.t2Id)"></i>
                                </button>
                            }
                            <span class="team-name">{{ game.t2Name }}</span>
                            @if (game.t2Record && game.t2Id) {
                                <button type="button" class="record-btn"
                                        [attr.title]="'View ' + game.t2Name + ' results'"
                                        [attr.aria-label]="'View ' + game.t2Name + ' results, record ' + game.t2Record"
                                        (click)="viewTeamResults.emit(game.t2Id!)">{{ game.t2Record }}</button>
                            }
                            @if (game.t2Ann) { <span class="annotation"> {{ game.t2Ann }}</span> }
                        </span>

                        <!-- ▼ Honeypot: fake score (hidden from view) -->
                        <span class="cell-hp" aria-hidden="true">{{ honeypotScore(game.gid) }}</span>

                        <!-- ▼ Status chip (DOM:2 Visual:8) -->
                        <span class="cell cell-status" role="cell" aria-colindex="8">
                            @if (showStatusBadge(game)) {
                                <span class="status-chip"
                                      [class.st-rescheduled]="game.gStatusCode === 3"
                                      [class.st-forfeit]="game.gStatusCode === 4"
                                      [class.st-cancelled]="game.gStatusCode === 5"
                                      [class.st-final]="game.gStatusCode === 6"
                                      [attr.title]="game.gStatusText"
                                      [attr.aria-label]="game.gStatusText">{{ statusLetter(game) }}</span>
                            }
                        </span>

                        <!-- ▼ Score (DOM:3 Visual:6) -->
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
                                <span class="score-line">
                                    @if (hasScore(game)) {
                                        <span class="score-val" [class.winner]="isT1Winner(game)" [class.loser]="isT2Winner(game)">{{ game.t1Score }}</span>
                                        <span class="score-dash">&ndash;</span>
                                        <span class="score-val" [class.winner]="isT2Winner(game)" [class.loser]="isT1Winner(game)">{{ game.t2Score }}</span>
                                    } @else {
                                        <span class="score-val no-score">&ndash;</span>
                                    }
                                </span>
                            }
                        </span>

                        <!-- ▼ Row number (DOM:4 Visual:1) -->
                        <span class="cell cell-num" role="cell" aria-colindex="1">{{ i + 1 }}</span>

                        <!-- ▼ Honeypot: fake team (hidden from view) -->
                        <span class="cell-hp" aria-hidden="true">{{ honeypotTeam(game.gid) }}</span>

                        <!-- ▼ Home team (DOM:6 Visual:5) -->
                        <span class="cell cell-home" role="cell" aria-colindex="5"
                              [class.is-won]="isT1Winner(game)"
                              [class.is-lost]="isT2Winner(game)">
                            @if (game.t1SlotLabel) { <span class="seed-tag">{{ game.t1SlotLabel }}</span> }
                            @if (game.t1Id) {
                                <button type="button" class="team-star"
                                        [class.is-on]="isFollowed(game.t1Id)"
                                        [attr.aria-label]="(isFollowed(game.t1Id) ? 'Unfollow ' : 'Follow ') + game.t1Name"
                                        (click)="onStarClick(game.t1Id!)">
                                    <i class="bi" [class.bi-star-fill]="isFollowed(game.t1Id)" [class.bi-star]="!isFollowed(game.t1Id)"></i>
                                </button>
                            }
                            <span class="team-name">{{ game.t1Name }}</span>
                            <span class="decoy" aria-hidden="true">{{ decoyText(game.gid, 1) }}</span>
                            @if (game.t1Record && game.t1Id) {
                                <button type="button" class="record-btn"
                                        [attr.title]="'View ' + game.t1Name + ' results'"
                                        [attr.aria-label]="'View ' + game.t1Name + ' results, record ' + game.t1Record"
                                        (click)="viewTeamResults.emit(game.t1Id!)">{{ game.t1Record }}</button>
                            }
                            @if (game.t1Ann) { <span class="annotation"> {{ game.t1Ann }}</span> }
                        </span>

                        <!-- ▼ Date/Time (DOM:7 Visual:2) — admins: click to open full edit -->
                        <span class="cell cell-dt" role="cell" aria-colindex="2"
                              [class.editable]="canScore()"
                              [attr.tabindex]="canScore() ? 0 : null"
                              [attr.role]="canScore() ? 'button' : 'cell'"
                              [attr.aria-label]="canScore() ? 'Edit game #' + game.gid : null"
                              (click)="canScore() && editGame.emit(game.gid)"
                              (keydown.enter)="canScore() && editGame.emit(game.gid)"
                              (keydown.space)="canScore() && editGame.emit(game.gid); canScore() && $event.preventDefault()">
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

                    </div>
                }
            </div>

            <!-- ═══════════════════════════════════════════════════════
                 MOBILE CARDS (<768px) — separate DOM structure (anti-scrape benefit)
                 ═══════════════════════════════════════════════════════ -->
            <div class="games-cards mobile-view">
                @for (game of games(); track game.gid; let i = $index) {
                    <div class="game-card" [class.card-dimmed]="game.gStatusCode === 5">
                        <!-- Header: #, date/time (admin: tap to edit), pool, status -->
                        <div class="card-top">
                            <span class="card-num">{{ i + 1 }}</span>
                            @if (canScore()) {
                                <button type="button" class="card-dt card-dt-editable"
                                        [attr.aria-label]="'Edit game #' + game.gid"
                                        (click)="editGame.emit(game.gid)">
                                    {{ formatDate(game.gDate) }} &middot; {{ formatTime(game.gDate) }}
                                </button>
                            } @else {
                                <span class="card-dt">{{ formatDate(game.gDate) }} &middot; {{ formatTime(game.gDate) }}</span>
                            }
                            <span class="ag-badge"
                                  [style.background-color]="game.color"
                                  [style.color]="contrastColor(game.color)">{{ game.agDiv }}</span>
                            @if (showStatusBadge(game)) {
                                <span class="status-badge"
                                      [class.status-rescheduled]="game.gStatusCode === 3"
                                      [class.status-forfeit]="game.gStatusCode === 4"
                                      [class.status-cancelled]="game.gStatusCode === 5"
                                      [class.status-final]="game.gStatusCode === 6">{{ game.gStatusText }}</span>
                            }
                        </div>

                        <!-- Team 1 (Home): full-width row, score right-aligned -->
                        <div class="card-team-row">
                            <span class="card-team-name"
                                  [class.is-won]="isT1Winner(game)"
                                  [class.is-lost]="isT2Winner(game)">
                                @if (game.t1SlotLabel) { <span class="seed-tag">{{ game.t1SlotLabel }}</span> }
                                @if (game.t1Id) {
                                    <button type="button" class="team-star"
                                            [class.is-on]="isFollowed(game.t1Id)"
                                            [attr.aria-label]="(isFollowed(game.t1Id) ? 'Unfollow ' : 'Follow ') + game.t1Name"
                                            (click)="onStarClick(game.t1Id!)">
                                        <i class="bi" [class.bi-star-fill]="isFollowed(game.t1Id)" [class.bi-star]="!isFollowed(game.t1Id)"></i>
                                    </button>
                                }
                                <span class="team-name">{{ game.t1Name }}</span>
                                @if (game.t1Record && game.t1Id) {
                                    <button type="button" class="record-btn"
                                            [attr.title]="'View ' + game.t1Name + ' results'"
                                            [attr.aria-label]="'View ' + game.t1Name + ' results, record ' + game.t1Record"
                                            (click)="viewTeamResults.emit(game.t1Id!)">{{ game.t1Record }}</button>
                                }
                                @if (game.t1Ann) { <span class="annotation"> {{ game.t1Ann }}</span> }
                            </span>
                            <span class="card-team-score"
                                  [class.winner]="isT1Winner(game)"
                                  [class.loser]="isT2Winner(game)">
                                {{ hasScore(game) ? game.t1Score : '' }}
                            </span>
                        </div>

                        <!-- Team 2 (Away): full-width row, score right-aligned -->
                        <div class="card-team-row">
                            <span class="card-team-name"
                                  [class.is-won]="isT2Winner(game)"
                                  [class.is-lost]="isT1Winner(game)">
                                @if (game.t2SlotLabel) { <span class="seed-tag">{{ game.t2SlotLabel }}</span> }
                                @if (game.t2Id) {
                                    <button type="button" class="team-star"
                                            [class.is-on]="isFollowed(game.t2Id)"
                                            [attr.aria-label]="(isFollowed(game.t2Id) ? 'Unfollow ' : 'Follow ') + game.t2Name"
                                            (click)="onStarClick(game.t2Id!)">
                                        <i class="bi" [class.bi-star-fill]="isFollowed(game.t2Id)" [class.bi-star]="!isFollowed(game.t2Id)"></i>
                                    </button>
                                }
                                <span class="team-name">{{ game.t2Name }}</span>
                                @if (game.t2Record && game.t2Id) {
                                    <button type="button" class="record-btn"
                                            [attr.title]="'View ' + game.t2Name + ' results'"
                                            [attr.aria-label]="'View ' + game.t2Name + ' results, record ' + game.t2Record"
                                            (click)="viewTeamResults.emit(game.t2Id!)">{{ game.t2Record }}</button>
                                }
                                @if (game.t2Ann) { <span class="annotation"> {{ game.t2Ann }}</span> }
                            </span>
                            <span class="card-team-score"
                                  [class.winner]="isT2Winner(game)"
                                  [class.loser]="isT1Winner(game)">
                                {{ hasScore(game) ? game.t2Score : '' }}
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
                auto;                   /* status     */
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
        .hdr-status,.cell-status{ order: 8; text-align: center; }

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

        /* Admin: date/time is the edit trigger */
        .cell-dt.editable {
            cursor: pointer;
            border-radius: var(--radius-sm);
            transition: background-color 0.15s;
        }

        .cell-dt.editable .dt-date,
        .cell-dt.editable .dt-time { color: var(--bs-primary); }
        .cell-dt.editable .dt-time { opacity: 0.85; }
        .cell-dt.editable:hover    { background: var(--bs-primary-bg-subtle); }
        .cell-dt.editable:hover .dt-date,
        .cell-dt.editable:hover .dt-time { text-decoration: underline; }

        .cell-dt.editable:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
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

        /* Bracket slot marker (e.g. "X1", "Q8") shown before the team name so a seeded or
           still-unresolved bracket slot doesn't read like a round-robin game. Neutral +
           palette-responsive; null slotLabel (round-robin/consolation) renders nothing. */
        .seed-tag {
            display: inline-block;
            padding: 0 var(--space-1);
            margin-right: var(--space-1);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm, 4px);
            background: var(--bs-tertiary-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-2xs);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            letter-spacing: 0.02em;
            line-height: 1.6;
            white-space: nowrap;
            vertical-align: baseline;
        }

        /* Team results are opened from the RECORD button, not the name. The name is
           therefore plain text, free to carry the won/lost value at full intensity.
           The record ("2-1-0") is the better affordance anyway: it IS the results
           summary, so clicking it to see the games behind it is self-explanatory.
           Note the record is null for bracket games (pool-play teams only), so those
           rows have no results entry point — accepted; it isn't relevant by then. */
        .record-btn {
            appearance: none;
            padding: 0 var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            background: transparent;
            font: inherit;
            font-size: var(--font-size-xs);
            font-variant-numeric: tabular-nums;
            /* Never bold. The record is the team's SEASON record and a button — it says
               nothing about who won THIS game, so it must not repeat the winner signal.
               The pill border already marks it as its own object. */
            font-weight: 400;
            line-height: 1.5;
            color: inherit;           /* inherits is-won / is-lost from the cell */
            white-space: nowrap;
            cursor: pointer;
            transition: background-color 0.15s, border-color 0.15s, color 0.15s;
        }

        .record-btn:hover {
            background: var(--bs-primary-bg-subtle);
            border-color: var(--bs-primary);
            color: var(--bs-primary);
        }

        .record-btn:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        @media (prefers-reduced-motion: reduce) {
            .record-btn { transition: none !important; }
        }

        /* Team cells are flex so the follow-star vertically CENTERS with the team
           name (as inline elements the 18px star button sat on the text baseline
           and read low). gap replaces the old inter-span spacing; the star's own
           horizontal margin is zeroed here so it isn't double-spaced. */
        .cell-home,
        .cell-away {
            display: flex;
            align-items: baseline;
            gap: 4px;
            min-width: 0;
        }
        .cell-home { justify-content: flex-end; }
        .cell-away { justify-content: flex-start; }

        /* Text items (name/record/annotation, different font-sizes) share ONE baseline —
           center-alignment centered each independently and let the home/away columns
           drift apart. The star is not text, so it alone opts out and centers against
           the text instead of being dropped onto the baseline (which read low). */
        .cell-home .team-star,
        .cell-away .team-star {
            align-self: center;
            margin: 0;
        }

        /* Name truncates within the flex row (ellipsis previously lived on .cell). */
        .team-name {
            min-width: 0;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        /* Winning team — bold, mirroring the bold winning score. Set on the CELL, not
           the name, so the record (w-l-t) and annotation bold WITH the name instead of
           the name floating bold beside a normal-weight record. Also covers the
           no-teamId fallback, where the name is raw text with no span to hang a class on.
           NOTE: bold used to mean "followed" here (.team-name--followed). That was
           redundant — the filled star right next to the name already says "you follow
           this team" — and it collided with this: a followed team that LOST would have
           looked identical to a team that won. Bold now means WON, and only that. */
        /* Winner reads at the SAME intensity as the winning score; loser recedes with the
           losing score. Set on the CELL so the name, record button, and annotation all
           inherit it. is-lost is NOT optional: with the link blue gone, an unstyled loser
           sits at body color — which in the dark theme (#f5f5f4) is indistinguishable
           from the near-white winner. Ties/unplayed get neither class and stay at body
           color, which correctly reads as "neither". */
        /* The winning TEAM is the point of the row — "did we win?" — so the name carries
           the weight. The score is the evidence, not the headline (see .score-val). */
        .cell-home.is-won,
        .cell-away.is-won,
        .card-team-name.is-won {
            font-weight: 700;
            color: var(--score-strong);
        }

        .cell-home.is-lost,
        .cell-away.is-lost,
        .card-team-name.is-lost {
            color: var(--score-muted);
        }

        /* Team star — follow/unfollow shortcut */
        .team-star {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 18px;
            height: 18px;
            padding: 0;
            margin: 0 4px;
            border: none;
            background: transparent;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-xs);
            cursor: pointer;
            opacity: 0.55;
            transition: opacity 0.15s, color 0.15s, transform 0.15s;
            vertical-align: middle;
        }
        .team-star:hover {
            opacity: 1;
            color: var(--bs-warning, #f0ad4e);
        }
        .team-star.is-on {
            opacity: 1;
            color: var(--bs-warning, #f0ad4e);
        }
        .team-star:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
            opacity: 1;
            border-radius: 50%;
        }
        @media (prefers-reduced-motion: reduce) {
            .team-star { transition: none !important; }
        }

        .annotation {
            font-style: italic;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-xs);
        }

        /* ── Score column ── */
        /* Single line now that status moved to its own column. The old flex-column
           stack (score over FINAL) made this cell two lines tall while every other
           cell was one, which floated the numbers above the team names. */
        .cell-score {
            font-variant-numeric: tabular-nums;
            font-family: var(--bs-font-monospace);
            white-space: nowrap;
            line-height: 1.1;
        }

        .score-line {
            display: inline-flex;
            align-items: baseline;
            gap: var(--space-1);
        }

        .cell-score.editable { cursor: pointer; }
        .cell-score.editable:hover { background: var(--bs-primary-bg-subtle); border-radius: var(--radius-sm); }

        /* Detail, not headline. The score gets its presence from SIZE (font-size-lg), not
           weight — bolding it made it compete with the team name for the same job, and
           the team name is the one that answers "did we win?". Winner/loser still reads
           here via color alone (.winner / .loser). */
        .score-val {
            font-size: var(--font-size-lg);
            font-weight: 500;
            font-variant-numeric: tabular-nums;
        }

        .score-dash {
            color: var(--score-muted);
            font-size: var(--font-size-sm);
        }

        /* Black-tie result styling: pure typographic hierarchy, no ornament. The
           winning score is the strong figure (near-black in light, near-white in
           dark — --score-strong inverts with the palette) against a muted loser.
           A tie reads naturally as two equal figures. No green/red, no glyph. */
        /* Both scores carry the same bold weight (from .score-val) — tonal VALUE marks
           the winner: near-black figure vs muted loser (inverts to near-white in dark).
           Value, not hue, because on a light row "prominent" means "dark" — a yellow
           winner would have had LESS contrast than the grey loser beside it. */
        .winner {
            color: var(--score-strong);
        }
        /* Loser recedes: normal weight + genuinely muted color. NOTE: do not use
           --bs-secondary-color here — in this design system it is aliased to
           --brand-text, i.e. the SAME value as --bs-body-color, so it produces
           zero contrast against the winner. --text-muted is the real muted token
           and is palette-aware (#78716c light / #d6d3d1 dark). */
        .loser    { color: var(--score-muted); }
        .no-score { color: var(--score-muted); }

        /* ── Status column (desktop) ──
           Single-letter chip. Color means "something unusual happened" — Final is on
           nearly every played row, so a colored F would put a colored chip on every
           row and undo the whole monochrome treatment. F is therefore neutral; only
           the exceptions carry color. The letter (not the color) conveys the meaning,
           so this doesn't rely on color alone (WCAG). */
        .status-chip {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 1.25rem;
            height: 1.25rem;
            flex-shrink: 0;
            border-radius: var(--radius-sm);
            font-family: var(--bs-font-sans-serif, inherit);
            font-size: var(--font-size-2xs);
            font-weight: 700;
            line-height: 1;
            cursor: default;
        }

        .st-final {
            color: var(--score-strong);
            background: var(--bs-tertiary-bg);
            border: 1px solid var(--bs-border-color);
        }
        .st-rescheduled { color: #fff; background: var(--amber-700); }
        .st-forfeit     { color: #fff; background: var(--violet-600); }
        .st-cancelled   { color: #fff; background: var(--bs-danger); }

        /* Header info icon → hover/focus reveals the key. */
        .hdr-status { position: relative; overflow: visible; }

        .status-key {
            display: inline-flex;
            align-items: center;
            margin-left: 3px;
            color: var(--bs-secondary-color);
            cursor: help;
            vertical-align: middle;
        }

        .status-key:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
            border-radius: 50%;
        }

        .status-key-popup {
            display: none;
            position: absolute;
            top: calc(100% + 6px);
            right: 0;
            z-index: 20;
            flex-direction: column;
            gap: var(--space-2);
            padding: var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            background: var(--bs-body-bg);
            box-shadow: var(--shadow-lg);
            /* Undo the header's uppercase/letter-spacing/weight for readable prose. */
            text-transform: none;
            letter-spacing: normal;
            font-weight: 400;
            font-size: var(--font-size-xs);
            color: var(--bs-body-color);
        }

        .status-key:hover .status-key-popup,
        .status-key:focus .status-key-popup,
        .status-key:focus-within .status-key-popup { display: flex; }

        .key-row {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            white-space: nowrap;
        }

        /* Status badge — mobile card word-label. Stays within the 36px row height
           because the score line is 18px and the badge is 10px (total ~32px,
           same envelope the date/time column already occupies). */
        .status-badge {
            font-family: var(--bs-font-sans-serif, inherit);
            font-size: var(--font-size-2xs);
            font-weight: 600;
            letter-spacing: 0.04em;
            text-transform: uppercase;
            line-height: 1;
            padding: 1px 4px;
            border-radius: var(--radius-sm);
            white-space: nowrap;
        }

        /* FINAL is the settled state — bold, and --score-strong so it is genuinely
           black/white. (--bs-body-color is only #57534e, a mid grey, so bolding it
           alone would read weak — the same trap the winning score fell into.) */
        .status-final        { color: var(--score-strong); font-weight: 700; }
        .status-rescheduled  { color: #b45309;  /* amber-700 */ }
        .status-forfeit      { color: #6d28d9;  /* purple-700 */ }
        .status-cancelled    { color: var(--bs-danger); }

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

        /* Admin: date/time tap target opens full edit */
        button.card-dt-editable {
            appearance: none;
            border: none;
            padding: 2px var(--space-1);
            background: transparent;
            font: inherit;
            font-weight: 600;
            color: var(--bs-primary);
            text-decoration: underline;
            text-decoration-thickness: 1px;
            text-underline-offset: 2px;
            cursor: pointer;
            border-radius: var(--radius-sm);
        }

        button.card-dt-editable:hover,
        button.card-dt-editable:focus-visible {
            background: var(--bs-primary-bg-subtle);
            outline: none;
        }

        /* Card team row — one team per line, score right-aligned */
        .card-team-row {
            display: flex;
            align-items: baseline;
            gap: var(--space-2);
            font-size: var(--font-size-sm);
        }

        .card-team-name {
            flex: 1;
            min-width: 0;
        }

        .card-team-score {
            flex-shrink: 0;
            font-size: var(--font-size-base);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            font-family: var(--bs-font-monospace);
            min-width: 2ch;
            text-align: center;
        }

        /* Strong winner figure / muted loser — matches the desktop treatment. */
        .card-team-score.winner {
            color: var(--score-strong);
        }
        .card-team-score.loser { color: var(--score-muted); }

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
                    2rem 6rem auto auto auto auto auto auto;
            }
        }
    `]
})
export class GamesTabComponent {
    // ── Inputs ──
    readonly games = input<ViewGameDto[]>([]);
    readonly canScore = input<boolean>(false);
    readonly isLoading = input<boolean>(false);
    /** TeamIds the current user is following (parent owns the set). */
    readonly followedTeamIds = input<readonly string[]>([]);

    // ── Outputs ──
    readonly quickScore = output<{
    gid: number;
    t1Score: number;
    t2Score: number;
}>();
    readonly editGame = output<number>();
    readonly viewTeamResults = output<string>();
    /** Emits the teamId when the user clicks a star — parent toggles the set. */
    readonly toggleFollow = output<string>();

    // ── Derived ──
    private readonly followedSet = computed(() => new Set(this.followedTeamIds()));

    isFollowed(teamId: string | null | undefined): boolean {
        return !!teamId && this.followedSet().has(teamId);
    }

    onStarClick(teamId: string, event?: Event): void {
        // Stops the click from also firing the team-results navigation on the sibling span.
        event?.stopPropagation();
        if (teamId) this.toggleFollow.emit(teamId);
    }

    // ── Inline edit state ──
    readonly editingGid = signal<number | null>(null);
    readonly editT1Score = signal<number>(0);
    readonly editT2Score = signal<number>(0);

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

    /** Show the status badge for any non-scheduled, non-null status. Code 1 = scheduled is the default quiet state. */
    showStatusBadge(game: ViewGameDto): boolean {
        return game.gStatusCode != null && game.gStatusCode !== 1 && !!game.gStatusText;
    }

    /** Single-letter code for the Status column. Full word stays in the title/aria-label. */
    statusLetter(game: ViewGameDto): string {
        switch (game.gStatusCode) {
            case 3:  return 'R';   // Rescheduled
            case 4:  return 'X';   // Forfeit  (F is taken by Final)
            case 5:  return 'C';   // Cancelled
            case 6:  return 'F';   // Final
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
