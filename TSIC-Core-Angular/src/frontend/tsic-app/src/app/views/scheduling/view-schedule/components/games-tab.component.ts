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

/**
 * Broadsheet grouping — the flat chronological game list (server orders by
 * GDate, then field, then gid) is broken into day "chapters". A `day` row is a
 * full-width dateline injected before the first game of each calendar day; a
 * `game` row carries the original list index so the row number and zebra stay
 * keyed to the underlying game position, unaffected by the interleaved headers.
 */
type ScheduleRow =
    | { readonly kind: 'day'; readonly key: string; readonly label: string; readonly count: number }
    | { readonly kind: 'game'; readonly game: ViewGameDto; readonly index: number };

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
                 DESKTOP GRID (≥768px) — subgrid "table"; DOM order = visual order.
                 ═══════════════════════════════════════════════════════ -->
            <div class="games-grid desktop-view" role="table" aria-label="Games schedule">
                <!-- Header -->
                <div class="grid-header" role="row">
                    <span class="hdr hdr-dt" role="columnheader">Date / Time</span>
                    <span class="hdr hdr-loc" role="columnheader">Location</span>
                    <span class="hdr hdr-pool" role="columnheader">Pool</span>
                    <span class="hdr hdr-home" role="columnheader">Home</span>
                    <span class="hdr hdr-score" role="columnheader">Score</span>
                    <span class="hdr hdr-away" role="columnheader">Away</span>
                    <!-- Header is the info icon ALONE, no "Status" word. The column shows
                         exceptions only — status 6 (Final) is auto-derived from score entry
                         and suppressed, so every cell is empty on a normal row and the
                         widest thing this column ever holds is a one-letter chip. Spelling
                         out "Status" made the HEADER the widest thing in a max-content
                         track, so a label was setting the width of a column that renders
                         nothing 99% of the time. The word is carried by aria-label and the
                         icon's key popup; an actual chip carries its full text in title +
                         aria-label. -->
                    <span class="hdr hdr-status" role="columnheader" aria-label="Status">
                        <span class="status-key" tabindex="0" role="button" aria-label="Show status key">
                            <i class="bi bi-info-circle" aria-hidden="true"></i>
                            <span class="status-key-popup" role="tooltip">
                                <span class="key-row"><span class="status-chip">F</span>Final</span>
                                <span class="key-row"><span class="status-chip">R</span>Rescheduled</span>
                                <span class="key-row"><span class="status-chip">X</span>Forfeit</span>
                                <span class="key-row"><span class="status-chip">C</span>Cancelled</span>
                            </span>
                        </span>
                    </span>
                </div>

                @for (row of rows(); track row.kind === 'day' ? 'day-' + row.key : row.game.gid) {
                    @if (row.kind === 'day') {
                        <!-- Broadsheet dateline — a full-width day "chapter" break -->
                        <div class="day-divider" role="row">
                            <span class="day-dateline">{{ row.label }}</span>
                            <span class="day-rule" aria-hidden="true"></span>
                            <span class="day-count">{{ row.count }} {{ row.count === 1 ? 'game' : 'games' }}</span>
                        </div>
                    } @else {
                        @let game = row.game;
                        @let i = row.index;
                    <div class="game-row" role="row"
                         [class.row-even]="i % 2 === 1"
                         [class.row-tinted]="!!game.color"
                         [style.--ag-tint]="game.color"
                         [class.row-dimmed]="game.gStatusCode === 5">

                        <!-- Date/Time — admins: click to open the full edit modal.
                             The trigger is a REAL button nested inside the cell rather than
                             ARIA bolted onto the cell itself: the span has to keep role="cell"
                             for the grid's table semantics, and an element cannot be both a
                             cell and a button. Nesting also hands us Enter/Space/focus/
                             announcement for free — this used to be four hand-rolled
                             attribute bindings plus two keydown handlers. -->
                        <span class="cell cell-dt" role="cell" aria-colindex="1">
                            @if (canScore()) {
                                <button type="button" class="dt-edit"
                                        [attr.aria-label]="'Edit game #' + game.gid"
                                        (click)="editGame.emit(game.gid)">
                                    <span class="dt-date">{{ formatDate(game.gDate) }}</span>
                                    <span class="dt-time">{{ formatTime(game.gDate) }}</span>
                                </button>
                            } @else {
                                <!-- .dt-stack mirrors .dt-edit's box so both permission
                                     branches sit on the row's line identically. Without a
                                     wrapper the two spans are loose inlines and would sit
                                     side by side instead of stacking. -->
                                <span class="dt-stack">
                                    <span class="dt-date">{{ formatDate(game.gDate) }}</span>
                                    <span class="dt-time">{{ formatTime(game.gDate) }}</span>
                                </span>
                            }
                        </span>

                        <!-- Location -->
                        <span class="cell cell-loc" role="cell" aria-colindex="2">
                            @if (mapsUrl(game)) {
                                <a [href]="mapsUrl(game)" target="_blank" rel="noopener"
                                   class="loc-link" [title]="game.fAddress || game.fName">
                                    {{ game.fName }} <i class="bi bi-box-arrow-up-right loc-icon"></i>
                                </a>
                            } @else {
                                <span>{{ game.fName }}</span>
                            }
                        </span>

                        <!-- Pool — age-group swatch + label. The swatch is decorative (the label
                             names the age group), so color is never the sole carrier. -->
                        <span class="cell cell-pool" role="cell" aria-colindex="3">
                            <span class="ag-dot" aria-hidden="true"
                                  [class.ag-dot--empty]="!game.color"></span>
                            <span class="ag-label">{{ game.agDiv }}</span>
                        </span>

                        <!-- Home season record — its OWN cell, outboard of the name. It is a
                             fixed-width object and the name beside it is not, so sharing a
                             box made the pill's position a function of name length (see
                             .cell-home-rec in the styles). -->
                        <span class="cell cell-home-rec" role="cell" aria-colindex="4">
                            @if (game.t1Record && game.t1Id) {
                                <button type="button" class="record-btn"
                                        [attr.title]="'View ' + game.t1Name + ' results'"
                                        [attr.aria-label]="'View ' + game.t1Name + ' results, record ' + game.t1Record"
                                        (click)="viewTeamResults.emit(game.t1Id!)">{{ game.t1Record }}</button>
                            }
                        </span>

                        <!-- Home team. The cell is ONE INLINE TEXT RUN (see .cell-home in the
                             styles), so star, name and annotation flow and wrap together.
                             That is what keeps the star beside its team: as a flex sibling it
                             anchored to the name ELEMENT's edge, and a wrapped name's element
                             fills the whole column while its text sits ragged inside it,
                             stranding the star ~90px away. Inline content has no element edge
                             to anchor to.

                             The win cue is not here at all — it is a gold underline on the
                             name itself (.team-name.is-winner), which is why this cell no
                             longer needs a marker child on either side. -->
                        <span class="cell cell-home" role="cell" aria-colindex="5">
                            @if (game.t1SlotLabel) { <span class="seed-tag">{{ game.t1SlotLabel }}</span> }
                            @if (game.t1Id) {
                                <button type="button" class="team-star"
                                        [class.is-on]="isFollowed(game.t1Id)"
                                        [attr.aria-label]="(isFollowed(game.t1Id) ? 'Unfollow ' : 'Follow ') + game.t1Name"
                                        (click)="onStarClick(game.t1Id!)">
                                    <i class="bi" [class.bi-star-fill]="isFollowed(game.t1Id)" [class.bi-star]="!isFollowed(game.t1Id)"></i>
                                </button>
                            }
                            @if (game.t1Id) {
                                <span class="team-name team-link" role="button" tabindex="0"
                                      [class.is-winner]="isT1Winner(game)"
                                      [attr.title]="'View ' + game.t1Name + ' results'"
                                      [attr.aria-label]="'View ' + game.t1Name + ' results' + (isT1Winner(game) ? ', winner' : '')"
                                      (click)="viewTeamResults.emit(game.t1Id!)"
                                      (keydown.enter)="viewTeamResults.emit(game.t1Id!)"
                                      (keydown.space)="$event.preventDefault(); viewTeamResults.emit(game.t1Id!)">{{ teamLabel(game.t1Name) }}</span>
                            } @else {
                                <span class="team-name" [class.is-winner]="isT1Winner(game)">{{ teamLabel(game.t1Name) }}</span>
                            }
                            @if (game.t1Ann) { <span class="annotation"> {{ game.t1Ann }}</span> }
                        </span>

                        <!-- Score — three real columns so home/away numbers each stack on
                             their own right edge and the dash sits isolated in the centre
                             track, unable to nudge either number. While editing, one cell
                             spans all three for the input pair. -->
                        @if (editingGid() === game.gid) {
                            <span class="cell cell-score-edit" role="cell" aria-colindex="6"
                                  (click)="$event.stopPropagation()">
                                <span class="score-edit">
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
                                    @if (hasScore(game)) {
                                        <button type="button" class="score-clear"
                                                title="Clear score — return game to unscored"
                                                aria-label="Clear score, return game to unscored"
                                                (click)="clearScore(game.gid)">
                                            <i class="bi bi-eraser" aria-hidden="true"></i>
                                        </button>
                                    }
                                </span>
                            </span>
                        } @else {
                            <!-- Home score -->
                            <span class="cell cell-t1-score" role="cell" aria-colindex="6"
                                  [class.editable]="canScore()"
                                  (click)="onScoreCellClick(game)">
                                @if (hasScore(game)) {
                                    <span class="score-val">{{ game.t1Score }}</span>
                                }
                            </span>
                            <!-- Dash — isolated centre track -->
                            <span class="cell cell-dash" role="cell" aria-colindex="7"
                                  [class.editable]="canScore()"
                                  (click)="onScoreCellClick(game)">
                                <span class="score-dash">&ndash;</span>
                            </span>
                            <!-- Away score -->
                            <span class="cell cell-t2-score" role="cell" aria-colindex="8"
                                  [class.editable]="canScore()"
                                  (click)="onScoreCellClick(game)">
                                @if (hasScore(game)) {
                                    <span class="score-val">{{ game.t2Score }}</span>
                                }
                            </span>
                        }

                        <!-- Away team. The star PRECEDES the name here exactly as it does on
                             the home side — deliberately NOT mirrored.

                             Inline flow puts "first" at the start of line one and "last" at
                             the end of the last line. A mirrored away cell therefore put its
                             star after the final character of a wrapped name, and when the
                             name filled the track (which fit-content guarantees for whichever
                             name is longest) there was no room left, so the star dropped to a
                             line of its own below the team. The home side never showed this
                             because its star was already first.

                             There is no inline arrangement that is both mirrored and always
                             on the first line, so first-line wins: "the star precedes the
                             team name" is one rule for both sides, and the star is invisible
                             at rest anyway unless the team is followed. -->
                        <span class="cell cell-away" role="cell" aria-colindex="9">
                            @if (game.t2SlotLabel) { <span class="seed-tag">{{ game.t2SlotLabel }}</span> }
                            @if (game.t2Id) {
                                <button type="button" class="team-star"
                                        [class.is-on]="isFollowed(game.t2Id)"
                                        [attr.aria-label]="(isFollowed(game.t2Id) ? 'Unfollow ' : 'Follow ') + game.t2Name"
                                        (click)="onStarClick(game.t2Id!)">
                                    <i class="bi" [class.bi-star-fill]="isFollowed(game.t2Id)" [class.bi-star]="!isFollowed(game.t2Id)"></i>
                                </button>
                            }
                            @if (game.t2Id) {
                                <span class="team-name team-link" role="button" tabindex="0"
                                      [class.is-winner]="isT2Winner(game)"
                                      [attr.title]="'View ' + game.t2Name + ' results'"
                                      [attr.aria-label]="'View ' + game.t2Name + ' results' + (isT2Winner(game) ? ', winner' : '')"
                                      (click)="viewTeamResults.emit(game.t2Id!)"
                                      (keydown.enter)="viewTeamResults.emit(game.t2Id!)"
                                      (keydown.space)="$event.preventDefault(); viewTeamResults.emit(game.t2Id!)">{{ teamLabel(game.t2Name) }}</span>
                            } @else {
                                <span class="team-name" [class.is-winner]="isT2Winner(game)">{{ teamLabel(game.t2Name) }}</span>
                            }
                            @if (game.t2Ann) { <span class="annotation"> {{ game.t2Ann }}</span> }
                        </span>

                        <!-- Away season record — own cell, outboard of the name (see home). -->
                        <span class="cell cell-away-rec" role="cell" aria-colindex="10">
                            @if (game.t2Record && game.t2Id) {
                                <button type="button" class="record-btn"
                                        [attr.title]="'View ' + game.t2Name + ' results'"
                                        [attr.aria-label]="'View ' + game.t2Name + ' results, record ' + game.t2Record"
                                        (click)="viewTeamResults.emit(game.t2Id!)">{{ game.t2Record }}</button>
                            }
                        </span>

                        <!-- Status chip -->
                        <span class="cell cell-status" role="cell" aria-colindex="11">
                            @if (showStatusBadge(game)) {
                                <span class="status-chip"
                                      [attr.title]="game.gStatusText"
                                      [attr.aria-label]="game.gStatusText">{{ statusLetter(game) }}</span>
                            }
                        </span>

                    </div>
                    }
                }
            </div>

            <!-- ═══════════════════════════════════════════════════════
                 MOBILE CARDS (<768px) — card-per-game layout for narrow screens.
                 ═══════════════════════════════════════════════════════ -->
            <div class="games-cards mobile-view">
                @for (row of rows(); track row.kind === 'day' ? 'day-' + row.key : row.game.gid) {
                    @if (row.kind === 'day') {
                        <!-- Broadsheet dateline — day chapter break (mobile) -->
                        <div class="card-day-divider">
                            <span class="day-dateline">{{ row.label }}</span>
                            <span class="day-count">{{ row.count }} {{ row.count === 1 ? 'game' : 'games' }}</span>
                        </div>
                    } @else {
                        @let game = row.game;
                    <div class="game-card"
                         [class.card-tinted]="!!game.color"
                         [style.--ag-tint]="game.color"
                         [class.card-dimmed]="game.gStatusCode === 5">
                        <!-- Header: date/time (admin: tap to edit), pool, status -->
                        <div class="card-top">
                            @if (canScore()) {
                                <button type="button" class="card-dt card-dt-editable"
                                        [attr.aria-label]="'Edit game #' + game.gid"
                                        (click)="editGame.emit(game.gid)">
                                    {{ formatDate(game.gDate) }} &middot; {{ formatTime(game.gDate) }}
                                </button>
                            } @else {
                                <span class="card-dt">{{ formatDate(game.gDate) }} &middot; {{ formatTime(game.gDate) }}</span>
                            }
                            <span class="ag-chip">
                                <span class="ag-dot" aria-hidden="true"
                                      [class.ag-dot--empty]="!game.color"></span>
                                <span class="ag-label">{{ game.agDiv }}</span>
                            </span>
                            @if (showStatusBadge(game)) {
                                <span class="status-badge">{{ game.gStatusText }}</span>
                            }
                        </div>

                        <!-- Team 1 (Home): full-width row, score right-aligned.
                             Black-tie: the name never carries the result — the gold
                             underline under the winning score is the sole win cue. -->
                        <div class="card-team-row">
                            <span class="card-team-name">
                                @if (game.t1SlotLabel) { <span class="seed-tag">{{ game.t1SlotLabel }}</span> }
                                @if (game.t1Id) {
                                    <button type="button" class="team-star"
                                            [class.is-on]="isFollowed(game.t1Id)"
                                            [attr.aria-label]="(isFollowed(game.t1Id) ? 'Unfollow ' : 'Follow ') + game.t1Name"
                                            (click)="onStarClick(game.t1Id!)">
                                        <i class="bi" [class.bi-star-fill]="isFollowed(game.t1Id)" [class.bi-star]="!isFollowed(game.t1Id)"></i>
                                    </button>
                                }
                                @if (game.t1Id) {
                                    <button type="button" class="team-name team-link"
                                            [attr.title]="'View ' + game.t1Name + ' results'"
                                            [attr.aria-label]="'View ' + game.t1Name + ' results'"
                                            (click)="viewTeamResults.emit(game.t1Id!)">{{ teamLabel(game.t1Name) }}</button>
                                } @else {
                                    <span class="team-name">{{ teamLabel(game.t1Name) }}</span>
                                }
                                @if (game.t1Record && game.t1Id) {
                                    <button type="button" class="record-btn"
                                            [attr.title]="'View ' + game.t1Name + ' results'"
                                            [attr.aria-label]="'View ' + game.t1Name + ' results, record ' + game.t1Record"
                                            (click)="viewTeamResults.emit(game.t1Id!)">{{ game.t1Record }}</button>
                                }
                                @if (game.t1Ann) { <span class="annotation"> {{ game.t1Ann }}</span> }
                            </span>
                            <span class="card-team-score" [class.is-winner]="isT1Winner(game)">
                                {{ hasScore(game) ? game.t1Score : '' }}
                            </span>
                        </div>

                        <!-- Team 2 (Away): full-width row, score right-aligned -->
                        <div class="card-team-row">
                            <span class="card-team-name">
                                @if (game.t2SlotLabel) { <span class="seed-tag">{{ game.t2SlotLabel }}</span> }
                                @if (game.t2Id) {
                                    <button type="button" class="team-star"
                                            [class.is-on]="isFollowed(game.t2Id)"
                                            [attr.aria-label]="(isFollowed(game.t2Id) ? 'Unfollow ' : 'Follow ') + game.t2Name"
                                            (click)="onStarClick(game.t2Id!)">
                                        <i class="bi" [class.bi-star-fill]="isFollowed(game.t2Id)" [class.bi-star]="!isFollowed(game.t2Id)"></i>
                                    </button>
                                }
                                @if (game.t2Id) {
                                    <button type="button" class="team-name team-link"
                                            [attr.title]="'View ' + game.t2Name + ' results'"
                                            [attr.aria-label]="'View ' + game.t2Name + ' results'"
                                            (click)="viewTeamResults.emit(game.t2Id!)">{{ teamLabel(game.t2Name) }}</button>
                                } @else {
                                    <span class="team-name">{{ teamLabel(game.t2Name) }}</span>
                                }
                                @if (game.t2Record && game.t2Id) {
                                    <button type="button" class="record-btn"
                                            [attr.title]="'View ' + game.t2Name + ' results'"
                                            [attr.aria-label]="'View ' + game.t2Name + ' results, record ' + game.t2Record"
                                            (click)="viewTeamResults.emit(game.t2Id!)">{{ game.t2Record }}</button>
                                }
                                @if (game.t2Ann) { <span class="annotation"> {{ game.t2Ann }}</span> }
                            </span>
                            <span class="card-team-score" [class.is-winner]="isT2Winner(game)">
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

        /* ═══ DESKTOP GRID ═══ */

        /* Container grid — columns sized across ALL rows (table-like alignment).
           subgrid on each row inherits these tracks so every cell in a column
           shares the exact same left/right edges, just like a <table>. */
        /* Track sizing: content-packed, then CENTRED as a block (justify-content).
           Every track was "auto" before, and CSS Grid hands leftover width to auto tracks
           in EQUAL shares — so ~250px of slack at a 1490px window got sprayed into seven
           separate ~36px voids. Two of them landed either side of the score (marooning the
           record pill and the away star off the numbers) and two more collided into a
           chasm between the pool label and the home names.

           Fix is typographic, not arithmetic: give the ledger a MEASURE. No track stretches
           now — the row packs to its own content and justify-content puts the leftover
           OUTSIDE the row, split evenly, where it reads as a margin instead of a hole.
           No magic max-width to keep in sync with the content; the measure follows the data.

           "safe" centring matters: plain center would overflow equally in both directions
           on a narrow desktop and push the row number off the left edge, unreachable.
           Safe falls back to start-alignment the moment content exceeds the container.

           fit-content(--name-col) on the two name tracks, NOT minmax(0, --name-col). The
           difference is the whole ballgame and it is easy to get wrong: minmax sets a base
           of 0 and a growth limit of --name-col, and Grid's "maximize tracks" step grows
           every track toward its limit out of available free space BEFORE justify-content
           runs. So minmax(0, 17rem) is not "content, capped at 17rem" — it is "17rem
           whenever the window has room," regardless of how short the names are. That is
           what put a fixed ~17rem box around a median 24-character name and pooled the
           slack on the ragged side, which is the empty band this grid keeps regrowing.

           fit-content(17rem) is min(max-content, 17rem): the track is exactly as wide as
           the widest name present and never wider than the cap. It still shrinks under a
           tight window, so these remain the only two shrinkable tracks and still absorb the
           squeeze first — which is right, they are the only cells whose content can WRAP
           instead of being clipped.

           NOTE: no backticks anywhere in this styles block — it is a template literal. */
        .games-grid {
            display: none;
            /* Cap on the two team-name tracks — THE tuning knob for this grid.
               Measured over LFTC's 2,556 names (after the club-prefix collapse): p50 = 24
               characters, p75 = 29, p90 = 34, p99 = 40, max = 45. The track also carries the
               star and one gap (~1.6rem), so 17rem leaves roughly 31 characters of name —
               about the 85th percentile. Names past that wrap rather than clip. (The record
               pill used to sit in here too; it has its own track now.)

               Uncapped (max-content) the track sized to the single longest name in the whole
               event, so a median row carried ~21 characters of dead space, and because the
               home names are right-aligned all of it pooled on their left — the band of
               emptiness between the pool label and the home names. Capping near the median
               is only safe BECAUSE names now wrap; capping while they still ellipsized would
               have traded a layout problem for a data-loss one.

               Raise it to widen the ledger and wrap fewer names; lower it to tighten the
               band and wrap more. Nothing else depends on the value. */
            --name-col: 17rem;
            grid-template-columns:
                max-content             /* date/time   */
                minmax(0, max-content)  /* location    */
                max-content             /* pool        */
                max-content                 /* home record */
                fit-content(var(--name-col))/* home →      */
                max-content                 /* home score  */
                min-content                 /* dash        */
                max-content                 /* away score  */
                fit-content(var(--name-col))/* ← away      */
                max-content                 /* away record */
                max-content;                /* status      */
            justify-content: safe center;
            column-gap: var(--space-2);
            margin: 0 var(--space-2);
        }

        /* Each row inherits parent column sizing via subgrid.

           align-items: BASELINE, not center. Cells in this row are not all one line tall:
           date/time is always two, and a team name past the --name-col cap wraps to two.
           Under center, every one-line cell floated to the middle of whatever the tallest
           cell happened to be, so on a wrapped row the score, both records and the opposing
           team all sank half a line while the pool label — a flex box centring its own dot
           and text — sank further still. The row lost its line.

           Baseline pins the FIRST baseline of every cell to one line: the date's first line,
           the pool label, both names' first lines, the scores, the records and the status
           chip all sit on it, and only genuine overflow (the time, a name's second line)
           hangs below. That is what a ledger does, and it means row height can vary without
           the scoreline ever moving.

           Baseline rather than start: start aligns box TOPS, and these cells run different
           font sizes (xs furniture, sm names, base scores), so their tops do not correspond
           to anything the eye reads. Baselines do. */
        .grid-header,
        .game-row {
            grid-column: 1 / -1;
            display: grid;
            grid-template-columns: subgrid;
            align-items: baseline;
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

        /* Column alignment (DOM order already matches visual order).

           Home and Away each span TWO tracks — the record pill's track plus the name's —
           because one word ("Home") heads both. Spanning keeps the header honest without
           inventing a "Record" column label for a pill that is self-describing. The span
           inherits the side's alignment, so "Home" still lands on the names' right edge
           beside the score and "Away" on their left edge, exactly where they were. */
        .hdr-pool,  .cell-pool  { text-align: left; }
        .hdr-home   { grid-column: span 2; text-align: right; }
        .cell-home  { text-align: right; }
        .hdr-score  { grid-column: span 3; text-align: center; }
        .hdr-away   { grid-column: span 2; text-align: left; }
        .cell-away  { text-align: left; }
        .hdr-status,.cell-status{ text-align: center; }

        /* The record pill owns a TRACK of its own, on the OUTER side of each name.

           It used to be the last inline child of the team cell, which made its position a
           function of the name's length: once names could wrap, a name at or near the
           17rem cap left no room on its last line and the pill dropped onto a line of its
           own — on roughly half the rows, since the median name is 24 characters. Pinning
           the cell to nowrap only traded that for silent clipping (.cell is overflow:
           hidden), so the pill needed to stop sharing a box with text that resizes.

           OUTER, not inner, deliberately: the inner slot would stack a column of season
           records flush against the score column, and "2-2-0  4 - 14  3-1-0" reads as one
           run of numbers. Bookending the row keeps the two kinds of number apart and
           leaves the names themselves adjacent to the score, which is the pairing that
           has to survive. Each pill hugs the name it belongs to. */
        .cell-home-rec { text-align: right; }
        .cell-away-rec { text-align: left; }

        /* ── Broadsheet dateline ──
           A full-width day "chapter" break. Spans every column (grid-column 1/-1) and
           opts out of the row subgrid, reading as an editorial section head: an uppercase
           dateline, a hairline rule that carries the eye across, and a quiet per-day count.
           Not a data row — role="row" is a11y structure only; it holds no cells. */
        .day-divider {
            grid-column: 1 / -1;
            display: flex;
            align-items: center;
            gap: var(--space-3);
            padding: var(--space-5) var(--space-1) var(--space-2);
        }

        .day-dateline {
            font-size: var(--font-size-base);
            font-weight: 700;
            letter-spacing: 0.06em;
            text-transform: uppercase;
            color: var(--bs-body-color);
            white-space: nowrap;
        }

        .day-rule {
            flex: 1 1 auto;
            height: 1px;
            background: var(--bs-border-color);
        }

        .day-count {
            font-size: var(--font-size-xs);
            font-weight: 600;
            letter-spacing: 0.04em;
            text-transform: uppercase;
            color: var(--bs-secondary-color);
            font-variant-numeric: tabular-nums;
            white-space: nowrap;
        }

        /* ── Game row ── */
        /* Zebra and hover own the row's BACKGROUND. The age-group color owns a rail down the
           left edge and never touches the surface behind the data.
           An earlier pass washed the entire row instead. It grouped correctly, but it drowned
           the content and it flattened the zebra to nothing — every row in a run became one
           flat hue, so the alternating step that lets you track a row across eight columns
           simply vanished. The row-number gutter is the lowest-information real estate in the
           grid (a pure 1..N counter), so a rail there costs no data and reads just as well:
           a run of six same-age rows is still an unmissable solid block down the left edge. */
        .game-row {
            /* transparent, NOT --bs-body-bg: odd rows were previously unpainted and let the
               tab panel behind them show through. Naming a concrete color here would have
               quietly repainted every odd row with the page background. */
            --row-surface: transparent;
            background: var(--row-surface);
            border-bottom: 1px solid var(--bs-border-color);
            padding-top: var(--space-1);
            padding-bottom: var(--space-1);
            min-height: 36px;
        }

        /* ── Win cue: the winner's NAME is underlined gold and SOLID ──
           This reverses the old black-tie rule that names never carry the result. That rule
           produced six separate marks over time (bold/mute, trophy, dot, star, superscript,
           offset underline, edge bar, caret) because every one of them was a BOX that had to
           be positioned next to ragged, wrapping text — and boxes strand. A text decoration
           is not a box. It is the text. It cannot detach, cannot be ordered wrong, needs no
           left/right variant, and one rule covers both sides.

           COLOUR ONLY — the pattern is identical to the resting affordance (.team-link:
           dotted, 1px, 2px offset). The winner's rule is the same rule in gold. Keep it that
           way: a thicker or solid variant reads as a different object and shouts over a
           ledger where every other name is already underlined.

           Known trade-off, accepted deliberately: hue is then the sole channel separating
           "clickable" from "won", so the cue does not survive greyscale or colour-blindness.
           The digits beside it still state the result unambiguously, so nothing is lost that
           was only available here.

           Values are duplicated from .team-link rather than inherited because .team-name is
           also used bare — an unresolved bracket slot renders a plain span with no link
           affordance, and it still deserves the cue. */
        .team-name.is-winner {
            text-decoration: underline dotted;
            text-decoration-color: var(--winner-gold);
            text-decoration-thickness: 1px;
            text-underline-offset: 2px;
        }

        /* Hover promotes dotted → solid exactly as it does for any other name; this only
           holds the gold, which .team-link:hover would otherwise repaint to currentColor.
           A longhand at higher specificity beats the shorthand's implied value, so the
           promotion still happens — only the hue is pinned. */
        .team-name.is-winner:hover,
        .team-name.is-winner:focus-visible {
            text-decoration-color: var(--winner-gold);
        }

        .row-even       { --row-surface: var(--bs-tertiary-bg); }
        .game-row:hover { --row-surface: var(--bs-secondary-bg); }
        .row-dimmed     { opacity: 0.5; }

        /* Age-group rail. An INSET SHADOW, not border-left: the row is a subgrid whose tracks
           align to the parent grid's, so a real border would inset the row's content box and
           throw every column out of register with the header. A shadow paints inside the box
           and costs no layout. */
        .game-row.row-tinted {
            box-shadow: inset var(--ag-rail-w) 0 0 0 var(--ag-ink);
        }

        /* --ag-ink is the ONE derived color an age group is drawn with — rail and dot both —
           so the two can never disagree. --ag-tint is the director's stored hex
           (Leagues.agegroups.color) and remains the source of truth: hue and chroma pass
           through UNTOUCHED, at full saturation. Only LIGHTNESS is clamped, and only far
           enough to guarantee the color is visible against the row at all.
           That clamp is load bearing, not cosmetic: the stored palette is dominated by
           near-white. #FFFFFF is the second most common value (787 age groups) and #F0F8FF
           another 438 — left raw, those paint an invisible rail beside an invisible dot.
           For the mid-range colors most directors actually pick the clamp is a no-op —
           #FF0000, #808080, #1E90FF all pass straight through untouched.
           Achromatic input carries no chroma, so white and black come out as greys of a
           legible lightness rather than being handed a fake hue (oklch reads hue 0 — red —
           off a colorless input, so a fixed chroma would have painted every white age group
           pink). */
        .game-row,
        .game-card {
            --ag-rail-w: 4px;
            --ag-ink-lo: 0.35;
            --ag-ink-hi: 0.75;
            --ag-ink: oklch(from var(--ag-tint) clamp(var(--ag-ink-lo), l, var(--ag-ink-hi)) c h);
        }

        /* Dark: the legible band shifts UP, because the surface the color must read against
           is now near-black rather than near-white. Same structure, different window. */
        :host-context([data-bs-theme='dark']) .game-row,
        :host-context([data-bs-theme='dark']) .game-card {
            --ag-ink-lo: 0.45;
            --ag-ink-hi: 0.85;
        }

        /* Cell common */
        .cell {
            font-size: var(--font-size-sm);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        /* Date/Time */
        /* Date and time are two stacked lines of ONE datum, so neither owns the row's
           baseline — the pair should straddle the row's line, centred on it.

           align-self: center does NOT do that. It centres this cell against the ROW BOX, so
           the moment a team name wraps and the row grows taller, the date sinks toward the
           middle of the taller row and drifts off the line every other column sits on. The
           target is the row's LINE, which does not move; the row's box does.

           So the cell stays a plain block and baseline-aligns like everything else, and the
           stacking moves to an INLINE-FLEX child with vertical-align: middle. The cell's
           line box then takes its baseline from the strut — landing exactly where the other
           columns' baselines land — and the two-line stack is centred on that baseline. Row
           height can now change without the date moving at all.

           It also makes the two permission branches behave identically. .dt-edit is a real
           <button>, an atomic inline box whose baseline is its LAST line; under plain
           baseline alignment that would have put the TIME on the row's line for admins and
           the DATE on it for everyone else. vertical-align: middle overrides the baseline
           question for both, so admin and parent see the same column. */
        .cell-dt {
            display: block;
            line-height: 1.3;
        }

        .cell-dt .dt-edit,
        .cell-dt .dt-stack {
            display: inline-flex;
            flex-direction: column;
            align-items: flex-start;
            vertical-align: middle;
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

        /* Admin: date/time is the edit trigger.
           At REST it is indistinguishable from a non-admin's date/time — plain ink, no
           colour. It used to paint both lines --bs-primary, which put 323 blue anchors down
           the leftmost column: the loudest colour in the row, spent on its RAREST action,
           and the only non-doctrinal colour in a layout where colour means age-group
           identity or a win and nothing else. Blue now belongs solely to the location link,
           which actually navigates.

           The affordance arrives on hover instead — same bargain as the follow stars. Safe
           here because this is not the hot path: scoring is a click on the score cells
           (onScoreCellClick); this opens the FULL edit modal (reschedule / field / status),
           which is rare and which you are already pointing at by the time it lights up. */
        .dt-edit {
            appearance: none;
            display: flex;
            flex-direction: column;
            align-items: flex-start;
            width: 100%;
            padding: 0;
            border: none;
            background: transparent;
            font: inherit;
            line-height: inherit;
            text-align: left;
            color: inherit;
            cursor: pointer;
            border-radius: var(--radius-sm);
            transition: background-color 0.15s;
        }

        .dt-edit:hover { background: var(--bs-primary-bg-subtle); }
        .dt-edit:hover .dt-date,
        .dt-edit:hover .dt-time { text-decoration: underline; }

        .dt-edit:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        @media (prefers-reduced-motion: reduce) {
            .dt-edit { transition: none !important; }
        }

        /* Location */
        .loc-link {
            color: var(--bs-primary);
            text-decoration: none;
            font-size: var(--font-size-xs);
        }

        .loc-link:hover { text-decoration: underline; }

        /* Raised by a relative offset, NOT by vertical-align: super.
           "super" shifts the glyph's BOX, and a raised box grows the line box upward. The
           cell is a grid item under align-items: center, so a box that got taller on top is
           re-centred by pushing its text baseline DOWN — the location link sat ~2px below
           the pool label and the team names on every row that HAD a maps link (rows without
           one render a plain span with no icon and sat correctly, which is what made it read
           as the link being low rather than the icon being tall).

           position: relative offsets paint only: the line box is still measured from the
           un-offset position, so the row's baseline is untouched. -0.35em of the icon's own
           0.6rem is ~3px — tune that one number to taste, it cannot move anything else. */
        .loc-icon {
            font-size: 0.6rem;
            vertical-align: baseline;
            position: relative;
            top: -0.35em;
            opacity: 0.6;
        }

        /* Pool cell — age-group swatch + label. Replaces a solid chip flooded with the
           director's color: that chip needed a luminance test to pick black-or-white text
           for an arbitrary hex, and it still failed at the extremes (a #FFFFFF age group
           rendered a white chip on a white row — invisible; #FFFF00 was unreadable-adjacent
           at any weight). A dot carries the same color in a spot where contrast is not load
           bearing, and hands legibility back to plain body text. Same language as the LADT /
           CADT tree filters (.tree-color-dot) and the roster swapper (.color-dot). */
        /* align-items: BASELINE, with the dot opting out to align-self: center.

           This cell is a flex container, and a flex container only exposes a real baseline
           if one of its items participates in baseline alignment. Under align-items: center
           none did, so the row's baseline alignment had nothing to grab and fell back to a
           baseline synthesised from this cell's bottom border edge — which is why the
           agegroup:division label sat lower than everything else on the row while the score
           and both names stayed on the line.

           Baseline here makes .ag-label the first participating item, so the CELL's baseline
           is the label's baseline and the label lands on the row's line. The dot has no text
           and would sit on that baseline like a full stop, so it opts out with align-self:
           center and stays optically centred against the label — unchanged from before. */
        .cell-pool {
            display: flex;
            align-items: baseline;
            gap: var(--space-2);
            min-width: 0;
        }

        /* Drawn from --ag-ink, the same derived color as the row's rail, so the dot beside the
           label and the rail in the gutter are always the identical color for an age group. */
        .ag-dot {
            display: inline-block;
            box-sizing: border-box;
            width: 10px;
            height: 10px;
            flex-shrink: 0;
            align-self: center;
            border-radius: 50%;
            border: 1px solid var(--bs-border-color);
            background: var(--ag-ink);
        }

        /* No color set on the age group — an empty dashed ring, so "unset" can never
           masquerade as a deliberate color choice. */
        .ag-dot--empty {
            background: transparent;
            border-style: dashed;
        }

        .ag-label {
            min-width: 0;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            font-size: var(--font-size-xs);
            font-weight: 600;
        }

        /* Mobile card: dot + label travel together inside the card's top row. */
        .ag-chip {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
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
        /* The pill border STAYS. It is what marks the record as its own object — a control
           you can click, distinct from the team name it sits beside — and without it the
           W-L-T degrades into a second, dimmer piece of the name. It is also the token's
           shared identity across surfaces: the same pill appears in the team-results
           fly-in (.record-chip there), and the two must not drift apart.

           Briefly made transparent-at-rest in cbebc999 to thin out the row's furniture.
           That was the wrong economy — it bought quiet by dissolving an affordance. If the
           row needs calming, take it out of something that is not carrying meaning. */
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
            /* Always muted, for BOTH teams — deliberately not inheriting is-won. The record
               is a season stat and a control; it says nothing about who won this game, so
               it stays out of the winner's ink budget. The border keeps the affordance. */
            color: var(--score-muted);
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

        /* ONE INLINE TEXT RUN, not a flex row. Everything in these cells — seed tag, star,
           name, annotation — flows and wraps together as text. (The record pill is NOT in
           here; it has its own track. See .cell-home-rec.)

           Flex was the wrong container the moment names started wrapping, and it failed twice
           the same way. A flex item anchors to the NAME ELEMENT's edge, but a wrapped name's
           element fills the entire column while its text sits ragged inside it. So whichever
           sibling sat on the ragged side stranded itself ~90px from the team: first the win
           caret, then (after moving the caret inline) the follow star. Reordering only ever
           moved the problem to the other side, because each cell has a flush edge and a
           ragged one and flex siblings can only ever hug the flush one.

           Inline flow has no element edge to anchor to. The star lands immediately beside the
           name's TEXT and stays there through any amount of wrapping. Both sides read
           star · name — see the away cell in the template for why that is not mirrored.

           The star keeps vertical-align: middle from its base rule, which centres it on the
           baseline of whatever line it sits on. Since it is always first, that is always the
           FIRST line — so it tracks the row's line rather than drifting with row height.

           Alignment is unchanged: .cell-home is text-align: right, .cell-away left (set with
           the column rules), and .team-link is text-align: inherit.
           .team-link also needs display: inline so its text participates in the run rather
           than forming an atomic inline-block that fills the column all over again.
           .team-star keeps vertical-align: middle from its base rule — that is what stopped
           it reading low on the baseline, which is the reason flex was reached for. */
        .cell-home,
        .cell-away {
            display: block;
            min-width: 0;
            line-height: 1.35;
            /* MUST be normal, and must stay that way. .cell sets nowrap + overflow: hidden +
               text-overflow: ellipsis for the single-line cells; inheriting that nowrap here
               re-arms the ellipsis and a long name collapses to a bare "...". Wrapping is the
               whole point of these two cells. There was briefly a reason to want nowrap — it
               stopped the W-L-T pill breaking onto its own line — but the pill now has its
               own track (.cell-home-rec), so nothing in this cell needs it. */
            white-space: normal;
            /* .cell's overflow: hidden + text-overflow: ellipsis are for the SINGLE-LINE
               cells. On a wrapping cell the ellipsis is dead (it only ever renders on an
               unwrapped line) and the hidden overflow does nothing but silently swallow
               text. Turning both off here means a mistake in this cell shows up as an
               overhang you can see rather than a name that quietly disappears. */
            overflow: visible;
            text-overflow: clip;
        }

        /* HANGING INDENT — away side only.

           The star is the first thing in this cell, so it occupies the start of line one.
           Line two has no star and so started at the cell's left edge, i.e. LEFT of where
           line one's text began: a left-aligned column with a ragged LEFT edge, which reads
           as broken ("Spirit" outdented under "Philly Blast Lacrosse:2034").

           The padding pushes every line in by the star's footprint; the negative text-indent
           pulls the FIRST line back out so the star still begins at the cell edge and nothing
           moves on unwrapped rows. Continuation lines then align under the name's first
           character.

           Home needs none of this and must not have it: home is right-aligned, so every line
           is already flush on the right and the star simply extends line one further left —
           which is what a ragged left edge is supposed to do.

           Two known imperfections, both rare and both better than the outdent: a bracket row
           with a seed tag puts an extra token on line one, so continuation lines sit slightly
           left of that row's first character; and an unresolved feed renders no star at all,
           leaving its line one outdented by the footprint. */
        .cell-away {
            /* .team-star is an 18px box with margin: 0 4px */
            --star-footprint: 26px;
            padding-inline-start: var(--star-footprint);
            text-indent: calc(-1 * var(--star-footprint));
        }

        /* There is deliberately NO "display: inline" rule for .team-link here any more. There
           was one, and it never did anything: in the desktop grid the name used to be a
           <button>, and a button is an ATOMIC inline-level box that no display value can
           make flow as text. So the name was a single unbreakable brick sharing a line with
           the star. Whenever star + brick exceeded the track the brick dropped to a line of
           its own, leaving line 1 holding nothing but an invisible star — which read as a
           name sitting a line lower than its own row, wrapped its text inside a box that was
           already on the wrong line, and under nowrap collapsed the whole name to "...".
           Three separate symptoms, one cause.

           The desktop name is now a span with role="button" + tabindex, so its text really
           does join this cell's inline run. The mobile cards keep real <button>s: each team
           there owns its own line, so nothing competes for space and atomicity is free. */


        /* Long names WRAP; they no longer ellipsize.
           An ellipsis on "North Bay Lacrosse Club:North Bay Lacrosse Club-Riptides" eats the
           tail — which is the only part that identifies the TEAM, the club having already
           been said. Wrapping keeps every character and costs only row height, on the ~15%
           of names long enough to need it.

           Justification follows the side automatically: .cell-home is text-align: right and
           .cell-away is left, and .team-link declares text-align: inherit, so wrapped lines
           stack flush against the score on both sides and the ragged edge stays on the
           outside where the names' outer edge already was.

           This only WORKS because the name tracks are capped (see .games-grid). An
           uncapped max-content track is by definition never narrower than its own text, so
           no wrap opportunity ever arises — capping the track is the change, this rule just
           says what to do when the cap bites. break-word rather than anywhere: only break
           inside a word when the word alone will not fit, which for these names is never. */
        .team-name {
            min-width: 0;
            white-space: normal;
            overflow-wrap: break-word;
        }

        /* Team name → team-results modal, the same viewTeamResults target the record
           badge fires. Black-tie doctrine meets touch reality: the name RESTS at body ink
           with a SOFT DOTTED UNDERLINE — a persistent affordance (no hover dependency, so
           touch shows it too) that stays calm (ink, not loud blue) so gold remains the
           only resting accent and the ledger reads even. Hover/focus promotes it to solid
           primary. This is the mobile-app treatment, and what was flagged missing at the
           start of the refresh. A <button>, not a bare span like standings, so it's
           keyboard-operable and gets a focus ring (design system requires focus states on
           interactive elements). Reuses .team-name for truncation/layout; only rendered
           when the slot has a resolved team id (unresolved bracket feeds stay plain text). */
        .team-link {
            appearance: none;
            border: none;
            padding: 0;
            background: transparent;
            font: inherit;
            color: inherit;
            text-align: inherit;
            cursor: pointer;
            text-decoration: underline dotted;
            text-decoration-color: color-mix(in srgb, currentColor 35%, transparent);
            text-decoration-thickness: 1px;
            text-underline-offset: 2px;
        }
        .team-link:hover {
            color: var(--bs-primary);
            text-decoration: underline solid;
            text-decoration-color: currentColor;
        }
        .team-link:focus-visible {
            outline: none;
            color: var(--bs-primary);
            box-shadow: var(--shadow-focus);
            border-radius: var(--radius-sm);
        }

        /* Result typography is RETIRED (was: bold winner name / muted loser name).
           Black-tie doctrine (ported from TSIC-Events-2025 visual-refresh): color means
           age-group identity, gold means WON — the gold underline under the winning score
           is the sole result cue. Names stay at constant weight and body color so a run of
           rows reads as an even, formal ledger; the eye finds winners by scanning for
           gold, not by comparing font weights. Ties/unplayed: no gold anywhere. */

        /* Team star — follow/unfollow shortcut. Black-tie: the filled (followed) state
           is strong ink (black in light, white in dark — --score-strong flips with the
           theme), NOT gold. Gold now means "won" and nothing else; the fill-vs-outline
           shape alone carries the followed state. */
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
        /* Bootstrap Icons ships .bi::before with vertical-align: -.125em (verified in
           bootstrap-icons.css, alongside line-height: 1). That nudge assumes the icon sits
           INLINE BESIDE TEXT at the same size, where dropping the ink is what makes a glyph
           optically centre on a lowercase run.

           These three buttons are inline-flex with align-items: center, so flex has already
           centred the icon's BOX. Bootstrap's nudge then lands on top of a centring that
           already happened and drives the ink ~0.125em below the middle — about 1.5px at
           --font-size-xs, which is exactly the amount the follow stars were reading low by.
           Cancel it wherever we do the centring ourselves.

           Deliberately NOT applied to .loc-icon: that one really is inline beside text, and
           it carries its own tuned offset on top. It still wants Bootstrap's behaviour. */
        .team-star .bi::before,
        .status-key .bi::before,
        .score-clear .bi::before {
            vertical-align: 0;
        }

        .team-star:hover {
            opacity: 1;
            color: var(--score-strong);
        }
        .team-star.is-on {
            opacity: 1;
            color: var(--score-strong);
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

        /* Stars rest INVISIBLE in the desktop ledger and appear on row hover. Two per row
           over 20 rows is 40 outlines competing with the score; at rest they carry no
           information, since the state that matters (this team is one of mine) is the
           FILLED star, which stays lit via .is-on below.

           Gated on (hover: hover) so a touch device — which never fires row hover — keeps
           them visible, and scoped to .game-row so the mobile cards are untouched.
           Opacity only, never display/visibility: the button must stay focusable, and
           :focus-visible brings it back for keyboard users mid-tab-order. */
        @media (hover: hover) {
            .game-row .team-star { opacity: 0; }
            .game-row:hover .team-star,
            .game-row .team-star:focus-visible,
            .game-row .team-star.is-on { opacity: 1; }
        }

        /* Wraps internally for the same reason the name does: it is the other
           variable-length child of the team cell, and the cell's nowrap means it cannot
           start a line of its own to escape a narrow track — without this it would run
           past the cap and be clipped by .cell's overflow: hidden. */
        .annotation {
            font-style: italic;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-xs);
            white-space: normal;
            overflow-wrap: break-word;
        }

        /* ── Score columns (three real subgrid tracks) ── */
        /* Home number · dash · away number. The two numbers align TOWARD the dash — home
           right, away left — so each butts the centre track from its own side and the dash
           reads as an unbroken vertical spine down the ledger. The dash lives in an isolated
           track and can never nudge a number. The "Score" header spans all three
           (.hdr-score → grid-column: span 3). */
        .cell-t1-score,
        .cell-dash,
        .cell-t2-score {
            font-variant-numeric: tabular-nums;
            font-family: var(--bs-font-monospace);
            white-space: nowrap;
            line-height: 1.1;
            display: flex;
            align-items: baseline;
            gap: var(--space-1);
        }

        /* Both numbers hug the dash — MIRRORED, not both right-aligned. Right-aligning the
           away number made it hug the away TEAM NAME instead, so a one-digit away score sat
           a character further right than a two-digit one and the pair read lopsided
           ("4 - 14" tight, "4 -  9" loose). Aligning each number toward the centre track
           makes every row a symmetric xx-y unit; the ragged edges fall on the OUTSIDE, next
           to the record pills, where slack reads as breathing room.

           min-width equalises the two tracks' min-content contributions, so they stay the
           same width even when (say) every home score is one digit and every away score is
           two — otherwise the 3-track span's geometric centre drifts off the dash and takes
           the centred "Score" header with it. Two digits at --font-size-base monospace is
           ~1.2rem and the editor caps scores at 99, so 1.75rem is the ceiling plus slack.
           A floor, not a fixed width: the inline editor spans these tracks and still needs
           to size them intrinsically. */
        .cell-t1-score,
        .cell-t2-score { min-width: 1.75rem; }
        .cell-t1-score { justify-content: flex-end; }
        .cell-t2-score { justify-content: flex-start; }

        /* Tight scoreline — the grid's column-gap (--space-2) flanks the dash on BOTH
           sides, which reads as "1  -  2". Cancel it with negative inline margins so the
           dash butts against the digits, leaving only a hair of padding: "1-2". Purely a
           visual pull; the dash's own min-content track stays put, so nothing else shifts. */
        .cell-dash {
            justify-content: center;
            margin-inline: calc(-1 * var(--space-2));
            padding-inline: var(--space-1);
        }

        /* Editing swaps all three score tracks for the input pair. */
        .cell-score-edit {
            grid-column: span 3;
            display: flex;
            align-items: center;
            justify-content: center;
        }

        .cell-t1-score.editable,
        .cell-dash.editable,
        .cell-t2-score.editable { cursor: pointer; }
        .cell-t1-score.editable:hover,
        .cell-dash.editable:hover,
        .cell-t2-score.editable:hover { background: var(--bs-primary-bg-subtle); border-radius: var(--radius-sm); }

        /* Both scores are plain bold strong figures — SAME weight and ink for winner and
           loser, and now with NO decoration on either. The result is carried entirely by the
           gold underline on the winning team's NAME (.team-name.is-winner, further up),
           which leaves the digits as pure data. Muting the loser would be a second, redundant
           channel — the retired scheme's whole failure mode — and the losing score is real
           information that deserves full legibility. */
        /* base/600, stepped down from lg/700.
           At lg the score ran 1.5x the body of the row (most cells are xs) and 1.29x the
           team names, with a 300-point weight step on top — two channels pushed at once. It
           earned that while it carried the win cue and was the result-bearing element; the
           name underline took that job, leaving the score as two numbers still emphasised for
           the old one. Everything around it got quieter in the same pass (status chips suppressed,
           stars hidden, date de-blued), so its relative loudness rose without the value ever
           changing. base also matches .card-team-score, so the same datum is finally one
           size across both layouts.

           Still the row's numeric focal point: base (1rem) outranks the names at sm and the
           rest at xs. Weight is the knob to reach for first if it needs more or less. */
        .score-val {
            font-size: var(--font-size-base);
            font-weight: 600;
            font-variant-numeric: tabular-nums;
            color: var(--score-strong);
        }

        .score-dash {
            color: var(--score-muted);
            font-size: var(--font-size-sm);
        }


        /* ── Status column (desktop) ──
           Single-letter chip, ONE neutral treatment for every status (black-tie: color
           is reserved for age-group identity and the winner's gold; amber/violet/red
           status coding is retired). The LETTER carries the meaning — which also means
           the chip never relied on color alone (WCAG) — and cancelled rows are further
           dimmed by .row-dimmed. Full word stays in title/aria-label + the header key. */
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
            color: var(--score-strong);
            background: var(--bs-tertiary-bg);
            border: 1px solid var(--bs-border-color);
        }

        /* Header info icon → hover/focus reveals the key.
           align-self: center because this header cell holds no text — under the header row's
           baseline alignment it would otherwise synthesise a baseline from its box edge and
           hang below the lettered headers beside it. */
        .hdr-status { position: relative; overflow: visible; align-self: center; }

        /* No margin-left: it existed only to hold the icon off the word "Status", and the
           icon is now the entire header. Any margin here is width added to a max-content
           track that this column cannot spend. */
        .status-key {
            display: inline-flex;
            align-items: center;
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

        /* Black-tie: every status word gets the same strong-ink treatment (the WORD
           carries the meaning; amber/purple/red coding retired with the doctrine).
           --score-strong so it is genuinely black/white — --bs-body-color is only
           #57534e, a mid grey, and would read weak at this size. */
        .status-badge { color: var(--score-strong); font-weight: 700; }

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

        /* Clear-score (unscore) button — only shown while editing an already-scored game.
           Neutral by default; reads as a destructive-ish action on hover (danger tint). */
        .score-clear {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 24px;
            height: 24px;
            margin-left: var(--space-1);
            padding: 0;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: transparent;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
            cursor: pointer;
            transition: background-color 0.15s, border-color 0.15s, color 0.15s;
        }
        .score-clear:hover {
            background: var(--bs-danger-bg-subtle);
            border-color: var(--bs-danger);
            color: var(--bs-danger);
        }
        .score-clear:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }
        @media (prefers-reduced-motion: reduce) {
            .score-clear { transition: none !important; }
        }

        /* ═══ MOBILE CARDS ═══ */

        .games-cards {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
        }

        /* Broadsheet dateline, mobile flavour — reuses .day-dateline / .day-count.
           No hairline rule (the card gap already separates chapters); dateline left,
           count right, a little air above each new day. */
        .card-day-divider {
            display: flex;
            align-items: baseline;
            justify-content: space-between;
            gap: var(--space-2);
            padding: var(--space-3) var(--space-1) var(--space-1);
        }

        .game-card {
            --row-surface: var(--bs-card-bg);
            background: var(--row-surface);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            padding: var(--space-2) var(--space-3);
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
        }

        /* Same rail as the desktop row, so the two views present identically. The inset shadow
           follows the card's border-radius, which reads as a spine rather than a stripe. */
        .game-card.card-tinted {
            box-shadow: inset var(--ag-rail-w) 0 0 0 var(--ag-ink);
        }

        .card-dimmed { opacity: 0.5; }

        /* Card top row */
        .card-top {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--font-size-xs);
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

        /* Plain bold strong figure for BOTH teams; the gold underline under the winning
           NUMBER is the sole result cue here (.is-winner).
           Desktop underlines the winning NAME instead. Both are gold underlines, so the
           language is shared — they differ only in what they sit under, and deliberately.
           A card gives each team its own line with its score at the end of it, so the number
           IS that team's line and underlining it says who won without ambiguity. The desktop
           row mirrors two teams around a shared scoreline, where an underlined number would
           only be re-stating which digit is larger. */
        .card-team-score {
            flex-shrink: 0;
            font-size: var(--font-size-base);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            font-family: var(--bs-font-monospace);
            min-width: 2ch;
            text-align: center;
            color: var(--score-strong);
        }

        .card-team-score.is-winner {
            text-decoration: underline;
            text-decoration-color: color-mix(in srgb, var(--winner-gold) 80%, transparent);
            text-decoration-thickness: 2px;
            text-underline-offset: 4px;
        }

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

        /* There is deliberately NO wide-viewport override of grid-template-columns. There used
           to be one, and it existed for a single purpose: to widen a hand-picked date/time
           track from 5.5rem to 6rem. That track is now max-content, so it is already exactly
           as wide as "Wed 12/31" needs at every viewport and the override became a verbatim
           copy of the base rule. Every track in this grid is content-sized; nothing about the
           measure is viewport-dependent, so nothing here needs restating. */

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
    t1Score: number | null;
    t2Score: number | null;
}>();
    readonly editGame = output<number>();
    readonly viewTeamResults = output<string>();
    /** Emits the teamId when the user clicks a star — parent toggles the set. */
    readonly toggleFollow = output<string>();

    // ── Derived ──
    private readonly followedSet = computed(() => new Set(this.followedTeamIds()));

    /** Games interleaved with day datelines — the Broadsheet grouping (see ScheduleRow).
     *  Two passes: count games per day, then emit a `day` row at each date change. */
    readonly rows = computed<ScheduleRow[]>(() => {
        const gs = this.games();
        const counts = new Map<string, number>();
        for (const g of gs) {
            const k = this.dateKey(g.gDate);
            counts.set(k, (counts.get(k) ?? 0) + 1);
        }
        const out: ScheduleRow[] = [];
        let prevKey: string | null = null;
        for (let i = 0; i < gs.length; i++) {
            const g = gs[i];
            const k = this.dateKey(g.gDate);
            if (k !== prevKey) {
                out.push({ kind: 'day', key: k, label: this.formatDayline(g.gDate), count: counts.get(k) ?? 0 });
                prevKey = k;
            }
            out.push({ kind: 'game', game: g, index: i });
        }
        return out;
    });

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
    // Nullable: an emptied number input yields null. A blank box is treated as 0 on
    // save (see saveScore); an explicit unscore goes through clearScore, not here.
    readonly editT1Score = signal<number | null>(0);
    readonly editT2Score = signal<number | null>(0);

    // ══════════════════════════════════════════════════════════════════
    // Team labels
    // ══════════════════════════════════════════════════════════════════

    /**
     * Display label for a stored team name, with a repeated club prefix collapsed out.
     *
     * Schedules.T1Name / T2Name are denormalized as "{club}:{team}" — "Metro:2034/35 Blue",
     * "All Lax Select:2034". Some clubs then name the team after the club again, so the
     * stored string doubles back on itself: "North Bay Lacrosse Club:North Bay Lacrosse
     * Club-Riptide". Those are the longest labels in the ledger and the first to ellipsize,
     * and the half that gets cut is the half that identifies the team.
     *
     * Collapsing to "North Bay Lacrosse Club:Riptide" keeps the club:team shape every other
     * row uses. DISPLAY ONLY — title and aria-label keep the stored value, so hovering,
     * screen readers, and anyone matching against what the club typed still see it verbatim.
     * Fixing it in the data would touch every consumer of T1Name (brackets, standings, team
     * results) and rewrite what directors entered; this stays in the one view that suffers.
     */
    teamLabel(name: string | null | undefined): string {
        const raw = name ?? '';
        const sep = raw.indexOf(':');
        if (sep <= 0) return raw;
        const club = raw.slice(0, sep);
        const team = raw.slice(sep + 1);
        if (!team.toLowerCase().startsWith(club.toLowerCase())) return raw;
        // Drop the echoed club plus whatever joins it to the real name ("-", " ", "/", ":").
        const rest = team.slice(club.length).replace(/^[\s\-–—:_/|]+/, '').trim();
        // Team named EXACTLY after its club has nothing left to show — keep it verbatim.
        return rest ? `${club}:${rest}` : raw;
    }

    // ══════════════════════════════════════════════════════════════════
    // Date / time formatting
    // ══════════════════════════════════════════════════════════════════

    formatDate(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return '';
        const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
        return `${days[d.getDay()]} ${d.getMonth() + 1}/${d.getDate()}`;
    }

    /** Local calendar-day key (y-m-d) for grouping — matches formatDate's local-time basis. */
    private dateKey(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return '';
        return `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`;
    }

    /** Full editorial dateline for a day chapter, e.g. "Saturday · June 20". */
    formatDayline(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return '';
        const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
        const months = ['January', 'February', 'March', 'April', 'May', 'June',
            'July', 'August', 'September', 'October', 'November', 'December'];
        return `${days[d.getDay()]} · ${months[d.getMonth()]} ${d.getDate()}`;
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

    /**
     * Show the status badge only when the status says something the row does not already.
     *
     * Code 1 (scheduled) is the quiet default. Code 6 (final) is a TAUTOLOGY on a scored
     * game: ViewScheduleService derives it from the score itself — entering a score sets 6,
     * clearing one resets to 1 — so an F chip on a scored row repeats what the two numbers
     * beside it already said, once per row, all the way down a 1,278-game ledger. Muting it
     * is what lets the column mean something again: what survives is R / X / C, and for a
     * parent scanning her kid's four games "Cancelled" is the most important word on the
     * page. It was previously indistinguishable at a glance from 322 F's.
     *
     * Final WITHOUT a score still shows — that pairing is contradictory (the service
     * actively prevents creating it), so if legacy data has one it is worth surfacing,
     * not hiding.
     */
    showStatusBadge(game: ViewGameDto): boolean {
        if (game.gStatusCode == null || !game.gStatusText) return false;
        if (game.gStatusCode === 1) return false;
        if (game.gStatusCode === 6 && this.hasScore(game)) return false;
        return true;
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
        // Blank box = 0 (typical "3–0" entry where the loser's box is left empty).
        // Unscoring is a deliberate act via clearScore, never an accidental blank here.
        this.quickScore.emit({
            gid,
            t1Score: this.editT1Score() ?? 0,
            t2Score: this.editT2Score() ?? 0
        });
        this.editingGid.set(null);
    }

    /** Return the game to unscored — both scores null. The backend resets a cleared
     *  game's status from Final(6) back to Scheduled(1). */
    clearScore(gid: number): void {
        this.quickScore.emit({ gid, t1Score: null, t2Score: null });
        this.editingGid.set(null);
    }

    cancelEdit(): void {
        this.editingGid.set(null);
    }
}
