import {
  afterNextRender,
  ChangeDetectionStrategy, Component, computed,
  DestroyRef, ElementRef, inject, Injector, input, OnChanges,
  signal,
  SimpleChanges,
  output,
  viewChild
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import type { DivisionBracketResponse } from '@core/api';
import { agBg, formatTime } from '../../shared/utils/scheduling-helpers';
import { BRACKET_CARD_W, BRACKET_HEADER_H, layoutBracket, type BracketLayoutGame } from './bracket-layout';

/** What a pencil on a bracket card asks the host to open the score sheet with. */
export interface BracketScoreEdit {
    gid: number;
    t1Name: string;
    t2Name: string;
    t1Score: number | null;
    t2Score: number | null;
    /** GameRoundTypes letter (Z/Y/X/Q/S/F ladder, B bronze, C consolation). The score
     *  sheet uses it to decide whether re-scoring can strand a downstream game. */
    roundType: string;
}


// A game that is NOT part of the single-elimination ladder: the bronze (3rd-place) match, which
// has no parent to advance into, and consolation (placement) games, which were never in the tree.
// Both are rendered as flat cards in a strip beneath the ladder.
interface OutsideCard {
    kind: 'Bronze' | 'Consolation';
    gid: number;
    t1Name: string;
    t2Name: string;
    t1Id: string | null;
    t2Id: string | null;
    t1Score: number | null;
    t2Score: number | null;
    location: string | null;
    fieldId: string | null;
    bothScored: boolean;
    t1Win: boolean;
    t2Win: boolean;
}

// A ladder game (or a synthesized bye) as the layout and the card template see it.
interface BracketNode extends BracketLayoutGame {
    t1Name: string;
    t2Name: string;
    t1Id: string | null;
    t2Id: string | null;
    t1Score: number | null;
    t2Score: number | null;
    locationTime: string | null;
    fieldId: string | null;
    roundType: string;
    isBye: boolean;
}

interface LadderCard extends BracketNode {
    x: number;
    y: number;
    t1Win: boolean;
    t2Win: boolean;
}

/**
 * One division's bracket: the single-elimination ladder (fit-on-open, zoom, drag-to-pan) and the
 * bronze/consolation strip beneath it. Shared by the Brackets tab and the team panel's Bracket view;
 * the host chooses the division.
 */
@Component({
    selector: 'app-bracket-view',
    standalone: true,
    imports: [NgTemplateOutlet],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <!-- Ladder: cards positioned by bracket-layout.ts over one SVG of elbow connectors, in a
             window-height box that scrolls both ways. Opens fitted (see fitToView); zoom is the
             toolbar, Ctrl+wheel / trackpad pinch, or two-finger pinch; a mouse drags to pan.
             Absent for consolation-only divisions. -->
        @if (hasLadder()) {
            <div class="ladder-toolbar">
                <button type="button" class="ladder-zoom" title="Zoom out" aria-label="Zoom out"
                        (click)="zoomBy(1 / ZOOM_STEP)"><i class="bi bi-dash-lg" aria-hidden="true"></i></button>
                <button type="button" class="ladder-zoom ladder-zoom--fit" title="Fit to view"
                        (click)="fitToView()">Fit</button>
                <button type="button" class="ladder-zoom" title="Zoom in" aria-label="Zoom in"
                        (click)="zoomBy(ZOOM_STEP)"><i class="bi bi-plus-lg" aria-hidden="true"></i></button>
                <span class="ladder-zoom-pct" aria-live="polite">{{ zoomPct() }}%</span>
            </div>
            <div class="ladder-scroll" #scroll
                 [class.is-dragging]="dragging()"
                 (pointerdown)="onPointerDown($event)"
                 (wheel)="onWheel($event)"
                 (touchstart)="onTouchStart($event)"
                 (touchmove)="onTouchMove($event)"
                 (touchend)="onTouchEnd($event)"
                 (touchcancel)="onTouchEnd($event)">
                <!-- Round headers stay pinned while the ladder scrolls up beneath them. Scaled
                     with the canvas, and overlaid on the band the layout reserves for them. -->
                <div class="ladder-rounds"
                     [style.width.px]="layout().width * scale()"
                     [style.height.px]="headerH * scale()">
                    <div class="ladder-rounds__strip"
                         [style.width.px]="layout().width"
                         [style.height.px]="headerH"
                         [style.transform]="'scale(' + scale() + ')'">
                        @for (col of layout().columns; track col.x) {
                            <div class="ladder-round"
                                 [style.left.px]="col.x"
                                 [style.width.px]="cardW"
                                 [style.height.px]="headerH">{{ col.label }}</div>
                        }
                    </div>
                </div>
                <!-- sizer: gives native scrolling the SCALED extent (a transform does not change
                     layout size, so without it the scroll range would ignore the zoom) -->
                <div class="ladder-sizer"
                     [style.width.px]="layout().width * scale()"
                     [style.height.px]="layout().height * scale()"
                     [style.margin-top.px]="-headerH * scale()">
                <div class="ladder-canvas" #canvas
                     [style.width.px]="layout().width"
                     [style.height.px]="layout().height"
                     [style.transform]="'scale(' + scale() + ')'">
                    <svg class="ladder-connectors" aria-hidden="true"
                         [attr.width]="layout().width" [attr.height]="layout().height">
                        @for (d of layout().connectors; track $index) {
                            <path [attr.d]="d" />
                        }
                    </svg>

                    @for (card of ladderCards(); track card.gid) {
                        <div class="br-card"
                             [attr.data-gid]="card.gid"
                             [style.left.px]="card.x"
                             [style.top.px]="card.y"
                             [style.width.px]="cardW"
                             [style.background]="cardBg()"
                             [style.border-left-color]="stripeColor()">
                            @if (card.isBye) {
                                <!-- Bye: the advancing team and a BYE tag where the score sits.
                                     Nothing was played, so no location, score or pencil. -->
                                <div class="br-card__row">
                                    <ng-container *ngTemplateOutlet="teamName; context: { name: card.t1Name, id: card.t1Id }" />
                                    <span class="br-card__bye">BYE</span>
                                </div>
                            } @else {
                                @if (canScore()) {
                                    <button type="button" class="br-card__edit"
                                            title="Edit Score"
                                            (click)="emitLadderScoreEdit(card.gid)">
                                        <i class="bi bi-pencil-square" aria-hidden="true"></i>
                                    </button>
                                }
                                <div class="br-card__loc" [class.br-card__loc--edit]="canScore()">
                                    @if (card.locationTime) {
                                        @if (card.fieldId) {
                                            <button type="button" class="br-card__link"
                                                    (click)="viewFieldInfo.emit(card.fieldId!)">{{ card.locationTime }}</button>
                                        } @else {
                                            {{ card.locationTime }}
                                        }
                                    }
                                </div>
                                <!-- Black-tie: the gold trophy is the sole winner cue. The glyph
                                     slot is always rendered so scores align. -->
                                <div class="br-card__row">
                                    <ng-container *ngTemplateOutlet="teamName; context: { name: card.t1Name, id: card.t1Id }" />
                                    <span class="br-card__glyph" aria-hidden="true">
                                        @if (card.t1Win) { <i class="bi bi-trophy-fill"></i> }
                                    </span>
                                    <span class="br-card__score">{{ card.t1Score }}</span>
                                </div>
                                <div class="br-card__divider"></div>
                                <div class="br-card__row">
                                    <ng-container *ngTemplateOutlet="teamName; context: { name: card.t2Name, id: card.t2Id }" />
                                    <span class="br-card__glyph" aria-hidden="true">
                                        @if (card.t2Win) { <i class="bi bi-trophy-fill"></i> }
                                    </span>
                                    <span class="br-card__score">{{ card.t2Score }}</span>
                                </div>
                            }
                        </div>
                    }
                </div>
                </div>
            </div>
        }

        <!-- A team name: opens the team's results when it resolves to a team; bold when followed.
             Names WRAP rather than truncate — the card grows to fit and the layout re-centres. -->
        <ng-template #teamName let-name="name" let-id="id">
            @if (id) {
                <button type="button" class="br-card__team"
                        [class.followed]="isFollowed(id)"
                        (click)="viewTeamResults.emit(id)">{{ name }}</button>
            } @else {
                <span class="br-card__team">{{ name }}</span>
            }
        </ng-template>

        <!-- Games outside the ladder: bronze (no parent) + consolation (never in the tree) -->
        @if (outsideCards().length > 0) {
            <div class="outside-ladder">
                @for (group of outsideGroups(); track group.kind) {
                    <div class="ol-group">
                        <div class="ol-group__label">{{ group.kind }}</div>
                        <div class="ol-cards">
                            @for (card of group.cards; track card.gid) {
                                <div class="ol-card"
                                     [style.background]="cardBg()"
                                     [style.border-left-color]="stripeColor()">
                                    @if (canScore()) {
                                        <button type="button" class="ol-card__edit"
                                                title="Edit Score"
                                                (click)="emitScoreEdit(card)">
                                            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16"><path d="M15.502 1.94a.5.5 0 0 1 0 .706L14.459 3.69l-2-2L13.502.646a.5.5 0 0 1 .707 0l1.293 1.293zm-1.75 2.456-2-2L4.939 9.21a.5.5 0 0 0-.121.196l-.805 2.414a.25.25 0 0 0 .316.316l2.414-.805a.5.5 0 0 0 .196-.12l6.813-6.814z"/><path fill-rule="evenodd" d="M1 13.5A1.5 1.5 0 0 0 2.5 15h11a1.5 1.5 0 0 0 1.5-1.5v-6a.5.5 0 0 0-1 0v6a.5.5 0 0 1-.5.5h-11a.5.5 0 0 1-.5-.5v-11a.5.5 0 0 1 .5-.5H9a.5.5 0 0 0 0-1H2.5A1.5 1.5 0 0 0 1 2.5v11z"/></svg>
                                        </button>
                                    }
                                    @if (card.location) {
                                        <div class="ol-card__loc">
                                            @if (card.fieldId) {
                                                <button type="button" class="ol-card__link"
                                                        (click)="viewFieldInfo.emit(card.fieldId!)">{{ card.location }}</button>
                                            } @else {
                                                {{ card.location }}
                                            }
                                        </div>
                                    }
                                    <!-- Black-tie: the gold trophy is the sole winner cue;
                                         names and scores stay neutral for both teams. The
                                         glyph slot is always rendered so scores align. -->
                                    <div class="ol-card__row">
                                        @if (card.t1Id) {
                                            <button type="button" class="ol-card__team"
                                                    [class.followed]="isFollowed(card.t1Id)"
                                                    (click)="viewTeamResults.emit(card.t1Id!)">{{ card.t1Name }}</button>
                                        } @else {
                                            <span class="ol-card__team">{{ card.t1Name }}</span>
                                        }
                                        <span class="ol-card__glyph" aria-hidden="true">
                                            @if (card.t1Win) { <i class="bi bi-trophy-fill"></i> }
                                        </span>
                                        <span class="ol-card__score">{{ card.t1Score }}</span>
                                    </div>
                                    <div class="ol-card__divider"></div>
                                    <div class="ol-card__row">
                                        @if (card.t2Id) {
                                            <button type="button" class="ol-card__team"
                                                    [class.followed]="isFollowed(card.t2Id)"
                                                    (click)="viewTeamResults.emit(card.t2Id!)">{{ card.t2Name }}</button>
                                        } @else {
                                            <span class="ol-card__team">{{ card.t2Name }}</span>
                                        }
                                        <span class="ol-card__glyph" aria-hidden="true">
                                            @if (card.t2Win) { <i class="bi bi-trophy-fill"></i> }
                                        </span>
                                        <span class="ol-card__score">{{ card.t2Score }}</span>
                                    </div>
                                </div>
                            }
                        </div>
                    </div>
                }
            </div>
        }
    `,
    styles: [`
        :host { display: block; }

        /* ── Ladder ── */

        .ladder-toolbar {
            display: flex;
            align-items: center;
            justify-content: flex-end;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-top: none;
            border-bottom: none;
            background: var(--bs-body-bg);
        }

        .ladder-zoom {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 2rem;
            height: 2rem;
            padding: 0 var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            cursor: pointer;
        }
        .ladder-zoom:hover { background: var(--bs-secondary-bg); }
        .ladder-zoom--fit { font-weight: 600; }

        .ladder-zoom-pct {
            min-width: 3rem;
            text-align: right;
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
        }

        /* Capped at the window so BOTH scrollbars are always on screen; native two-axis scroll
           is the pan (a mouse also drags — see onPointerDown). touch-action keeps a pinch here
           from zooming the page: the component turns it into a ladder zoom instead. Text never
           starts a selection, because a press may become a drag. */
        .ladder-scroll {
            max-height: 80dvh;
            border: 1px solid var(--bs-border-color);
            border-radius: 0 0 var(--radius-sm) var(--radius-sm);
            background: var(--bs-body-bg);
            overflow: auto;
            touch-action: pan-x pan-y;
            user-select: none;
            cursor: grab;
        }
        .ladder-scroll.is-dragging { cursor: grabbing; }

        /* Pinned above the ladder; the cards scroll up beneath it. In flow (sticky), so the
           sizer's negative margin lays the canvas's own header band underneath it. */
        .ladder-rounds {
            position: sticky;
            top: 0;
            z-index: 1;
            background: var(--bs-body-bg);
        }
        .ladder-rounds__strip {
            position: relative;
            transform-origin: 0 0;
        }

        .ladder-sizer { position: relative; }

        /* Size comes from the layout (template bindings); scaled from the top-left corner. */
        .ladder-canvas {
            position: absolute;
            top: 0;
            left: 0;
            transform-origin: 0 0;
        }

        .ladder-zoom:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        .ladder-connectors {
            position: absolute;
            inset: 0;
            pointer-events: none;
        }
        .ladder-connectors path {
            fill: none;
            stroke: var(--bs-secondary-color);
            stroke-width: 1.5;
        }

        /* One header per round, over the band the layout reserves above the cards. */
        .ladder-round {
            position: absolute;
            top: 0;
            display: flex;
            align-items: flex-end;
            padding-left: var(--space-2);
            font-size: var(--font-size-xs);
            font-weight: 700;
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            letter-spacing: 0.04em;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        /* No height: the card sizes to its content, and the component measures it back into
           the layout so positions account for the real heights. */
        .br-card {
            position: absolute;
            box-sizing: border-box;
            display: flex;
            flex-direction: column;
            justify-content: center;
            border: 1px solid var(--bs-border-color);
            border-left: 5px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            padding: var(--space-1) var(--space-2) var(--space-1) var(--space-1);
        }

        /* Centred over both rows; padded on both sides when the pencil is present so the
           text stays centred and clear of it. */
        .br-card__loc {
            min-height: 1.2em;
            margin-bottom: var(--space-1);
            text-align: center;
            font-size: var(--font-size-2xs);
            line-height: 1.2;
            color: var(--bs-secondary-color);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }
        .br-card__loc--edit { padding-inline: var(--space-6); }

        .br-card__row {
            display: flex;
            align-items: center;
            gap: var(--space-1);
            padding: calc(var(--space-1) / 2) var(--space-1);
            color: var(--bs-body-color);
            font-size: var(--font-size-xs);
            line-height: 1.3;
        }

        .br-card__team {
            flex: 1;
            min-width: 0;
            overflow-wrap: anywhere;
            text-align: left;
            background: none;
            border: none;
            padding: 0;
            color: inherit;
            font: inherit;
        }
        .br-card__team.followed { font-weight: 700; }
        button.br-card__team { cursor: pointer; text-decoration: underline; }

        .br-card__glyph {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 14px;
            flex-shrink: 0;
            font-size: var(--font-size-2xs);
            color: var(--winner-gold);
        }

        .br-card__score {
            flex-shrink: 0;
            min-width: 1.2rem;
            text-align: right;
            font-weight: 700;
        }

        .br-card__divider {
            height: 1px;
            background: var(--bs-border-color);
            margin: calc(var(--space-1) / 2) 0;
        }

        .br-card__bye {
            flex-shrink: 0;
            font-size: var(--font-size-2xs);
            font-weight: 600;
            letter-spacing: 0.04em;
            color: var(--bs-secondary-color);
        }

        /* ── Outside-the-ladder strip (bronze + consolation) ── */

        .outside-ladder {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-4);
            padding: var(--space-3);
        }

        .ol-group { flex: 1 1 auto; min-width: 260px; }

        .ol-group__label {
            font-size: var(--font-size-sm);
            font-weight: 700;
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            letter-spacing: 0.04em;
            margin-bottom: var(--space-2);
        }

        .ol-cards {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-2);
        }

        .ol-card {
            position: relative;
            width: 260px;
            border: 1px solid var(--bs-border-color);
            border-left: 5px solid var(--bs-border-color);
            border-radius: 6px;
            padding: 6px 8px;
            box-sizing: border-box;
        }

        .ol-card__loc {
            text-align: center;
            font-size: 10px;
            color: var(--bs-secondary-color);
            margin-bottom: 3px;
            line-height: 1.2;
        }

        .ol-card__row {
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 6px;
            padding: 2px 4px;
            color: var(--bs-body-color);
            font-size: 12px;
        }
        /* Winner cue: gold trophy in a fixed slot (black-tie — the retired scheme
           was green/red scores + bold-winner/dim-loser rows). */
        .ol-card__glyph {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 14px;
            flex-shrink: 0;
            font-size: 11px;
            color: var(--winner-gold);
        }

        .ol-card__divider {
            height: 1px;
            background: var(--bs-border-color);
            margin: 2px 0;
        }

        .ol-card__team {
            flex: 1;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            text-align: left;
            font-size: 12px;
            background: none;
            border: none;
            padding: 0;
            color: inherit;
            font-family: inherit;
        }
        .ol-card__team.followed { font-weight: 700; }
        button.ol-card__team { cursor: pointer; text-decoration: underline; }

        .ol-card__score {
            font-weight: 700;
            font-size: 12px;
            min-width: 1.2rem;
            text-align: right;
        }

        .ol-card__link,
        .br-card__link {
            background: none;
            border: none;
            padding: 0;
            color: inherit;
            font: inherit;
            text-decoration: underline;
            cursor: pointer;
        }

        .ol-card__edit,
        .br-card__edit {
            position: absolute;
            top: 3px;
            right: 3px;
            width: 22px;
            height: 22px;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            border-radius: 4px;
            background: var(--bs-secondary-bg);
            border: 1px solid var(--bs-border-color);
            color: var(--bs-primary);
            cursor: pointer;
            line-height: 1;
        }

        .ol-card__team:focus-visible,
        .ol-card__link:focus-visible,
        .ol-card__edit:focus-visible,
        .br-card__team:focus-visible,
        .br-card__link:focus-visible,
        .br-card__edit:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }
    `]
})
export class BracketViewComponent implements OnChanges {
    private readonly injector = inject(Injector);
    private readonly destroyRef = inject(DestroyRef);
    private destroyed = false;

    bracket = input<DivisionBracketResponse | null>(null);
    canScore = input<boolean>(false);
    /** The division's age-group color (null for none). Tints the cards. */
    agColor = input<string | null>(null);
    followedTeamIds = input<readonly string[]>([]);

    private readonly followedSet = computed(() => new Set(this.followedTeamIds()));

    readonly editBracketScore = output<BracketScoreEdit>();
    readonly viewTeamResults = output<string>();
    readonly viewFieldInfo = output<string>();

    // Optional, not required: the canvas is absent for consolation-only divisions.
    // required() throws NG0951 on read.
    readonly canvas = viewChild<ElementRef<HTMLDivElement>>('canvas');

    // Agegroup tint — shared by the ladder cards and the strip cards.
    readonly cardBg = computed(() => agBg(this.agColor()));
    readonly stripeColor = computed(() => this.agColor() ?? 'var(--bs-border-color)');

    // The single-elimination tree excludes bronze (roundType 'B'): it has no parent to advance
    // into, so it would render as a second, disconnected root and distort the layout.
    readonly hasLadder = computed(() =>
        (this.bracket()?.matches ?? []).some(m => m.roundType !== 'B'));

    // Cards for games that are not in the ladder, grouped: bronze first, then consolation.
    readonly outsideGroups = computed<{ kind: OutsideCard['kind']; cards: OutsideCard[] }[]>(() => {
        const b = this.bracket();
        if (!b) return [];

        const bronze = b.matches
            .filter(m => m.roundType === 'B')
            .map(m => this.toCard('Bronze', m.gid, m.t1Name, m.t2Name, m.t1Id ?? null, m.t2Id ?? null,
                m.t1Score ?? null, m.t2Score ?? null,
                m.locationTime ?? this.composeLocation(m.fName, m.gDate), m.fieldId ?? null));

        const consolation = (b.consolationGames ?? [])
            // Consolation carries an address (item 6), not a fieldId — no field-info modal to wire.
            .map(c => this.toCard('Consolation', c.gid, c.t1Name, c.t2Name, c.t1Id ?? null, c.t2Id ?? null,
                c.t1Score ?? null, c.t2Score ?? null,
                this.composeLocation(c.fName, c.gDate), null));

        const groups: { kind: OutsideCard['kind']; cards: OutsideCard[] }[] = [];
        if (bronze.length) groups.push({ kind: 'Bronze', cards: bronze });
        if (consolation.length) groups.push({ kind: 'Consolation', cards: consolation });
        return groups;
    });

    readonly outsideCards = computed(() => this.outsideGroups().flatMap(g => g.cards));

    isFollowed(teamId: string | null): boolean {
        return !!teamId && this.followedSet().has(teamId);
    }

    emitScoreEdit(card: OutsideCard): void {
        this.editBracketScore.emit({
            gid: card.gid,
            t1Name: card.t1Name,
            t2Name: card.t2Name,
            t1Score: card.t1Score,
            t2Score: card.t2Score,
            roundType: card.kind === 'Bronze' ? 'B' : 'C'
        });
    }

    private composeLocation(fName?: string | null, gDate?: string | null): string | null {
        if (fName && gDate) return `${fName} — ${formatTime(gDate)}`;
        if (gDate) return formatTime(gDate);
        return fName ?? null;
    }

    private toCard(
        kind: OutsideCard['kind'], gid: number, t1Name: string, t2Name: string,
        t1Id: string | null, t2Id: string | null, t1Score: number | null, t2Score: number | null,
        location: string | null, fieldId: string | null): OutsideCard {
        const bothScored = t1Score != null && t2Score != null;
        return {
            kind, gid, t1Name, t2Name, t1Id, t2Id, t1Score, t2Score, location, fieldId,
            bothScored,
            t1Win: bothScored && t1Score! > t2Score!,
            t2Win: bothScored && t2Score! > t1Score!
        };
    }

    // ── Ladder (the single-elimination tree) ──

    // Ladder games for the active division, plus a synthesized bye for every parent with a
    // single feeder: the tree stays balanced and the advancing team is shown in its slot
    // (legacy placement — the bye sits ABOVE the real game). Bronze is excluded: it has no
    // parent to advance into, so it would become a second, disconnected root; it lives in
    // the strip below instead.
    private readonly ladderGames = computed<BracketNode[]>(() => {
        const b = this.bracket();
        if (!b) return [];

        const real: BracketNode[] = b.matches
            .filter(m => m.roundType !== 'B')
            .map(m => ({
                gid: m.gid,
                // legacy: Pgid == 0 ? null : Pgid
                parentGid: (m.parentGid && m.parentGid !== 0) ? m.parentGid : null,
                roundLabel: ROUND_LABELS[m.roundType] ?? '',
                t1Name: m.t1Name,
                t2Name: m.t2Name,
                t1Id: m.t1Id ?? null,
                t2Id: m.t2Id ?? null,
                t1Score: m.t1Score ?? null,
                t2Score: m.t2Score ?? null,
                locationTime: m.locationTime ?? null,
                fieldId: m.fieldId ?? null,
                roundType: m.roundType,
                isBye: false
            }));

        const games = [...real];
        for (const g of real) {
            if (g.parentGid == null) continue;
            if (real.filter(s => s.parentGid === g.parentGid).length !== 1) continue;
            const parent = real.find(p => p.gid === g.parentGid);
            const bye = parent ? byeTeamOf(parent, g) : { name: '', id: null };
            games.unshift({
                gid: -g.parentGid,
                parentGid: g.parentGid,
                roundLabel: g.roundLabel,
                t1Name: bye.name,
                t2Name: 'BYE',
                t1Id: bye.id,
                t2Id: null,
                t1Score: null,
                t2Score: null,
                locationTime: null,
                fieldId: null,
                roundType: g.roundType,
                isBye: true
            });
        }
        return games;
    });

    /** Measured card heights by gid. Names wrap, so the DOM is the only honest source. */
    private readonly cardHeights = signal(new Map<number, number>());

    readonly layout = computed(() =>
        layoutBracket(this.ladderGames(), gid => this.cardHeights().get(gid) ?? 0));

    readonly ladderCards = computed<LadderCard[]>(() =>
        this.layout().cards.map(({ game, x, y }) => {
            const bothScored = game.t1Score != null && game.t2Score != null;
            return {
                ...game,
                x,
                y,
                t1Win: bothScored && game.t1Score! > game.t2Score!,
                t2Win: bothScored && game.t2Score! > game.t1Score!
            };
        }));

    readonly cardW = BRACKET_CARD_W;
    readonly headerH = BRACKET_HEADER_H;

    // ── Zoom + pan ──

    readonly ZOOM_STEP = 1.25;
    readonly scroll = viewChild<ElementRef<HTMLDivElement>>('scroll');
    readonly scale = signal(1);
    readonly zoomPct = computed(() => Math.round(this.scale() * 100));
    readonly dragging = signal(false);

    /** set when a different ladder is shown; drained by the next measure pass */
    private fitPending = true;
    /** the scale that shows the WHOLE ladder; zoom-out may always reach it */
    private fitAllScale = 1;

    /**
     * Desktop opens fitted to WIDTH, never below DESKTOP_MIN_FIT (names stay readable; a wider
     * ladder scrolls instead) and never above 1. A touch device opens fitted to the whole ladder,
     * like TSIC-Events — an overview to pinch into. Resets the scroll to the top-left.
     */
    fitToView(): void {
        const el = this.scroll()?.nativeElement;
        const { width, height } = this.layout();
        if (!el || !width || !height || !el.clientWidth) return;
        // The box is capped at a share of the window; fit against the cap, not the box's current
        // height, which is the ladder's height whenever the ladder is shorter.
        const capH = parseFloat(getComputedStyle(el).maxHeight) || el.clientHeight;
        this.fitAllScale = Math.min(el.clientWidth / width, capH / height, 1);
        const touch = matchMedia('(pointer: coarse)').matches;
        this.scale.set(touch
            ? this.fitAllScale
            : Math.min(1, Math.max(DESKTOP_MIN_FIT, el.clientWidth / width)));
        el.scrollLeft = 0;
        el.scrollTop = 0;
    }

    /** Toolbar zoom, about the centre of the visible area. */
    zoomBy(factor: number): void {
        const el = this.scroll()?.nativeElement;
        if (!el) return;
        this.zoomAt(this.scale() * factor, el.clientWidth / 2, el.clientHeight / 2);
    }

    /**
     * Zoom to `target`, keeping the ladder point under (cx, cy) — viewport coordinates — still.
     * Scroll offsets scale with the content; they are applied after the render that resizes
     * the sizer, since the new scroll range does not exist until then.
     */
    private zoomAt(target: number, cx: number, cy: number): void {
        const el = this.scroll()?.nativeElement;
        if (!el) return;
        const next = this.clampScale(target);
        const factor = next / this.scale();
        if (factor === 1) return;
        const left = (el.scrollLeft + cx) * factor - cx;
        const top = (el.scrollTop + cy) * factor - cy;
        this.scale.set(next);
        this.scrollAfterRender(left, top);
    }

    private clampScale(s: number): number {
        return Math.min(MAX_SCALE, Math.max(Math.min(MIN_SCALE, this.fitAllScale), s));
    }

    private scrollAfterRender(left: number, top: number): void {
        afterNextRender(() => {
            const el = this.scroll()?.nativeElement;
            if (!el) return;
            el.scrollLeft = left;
            el.scrollTop = top;
        }, { injector: this.injector });
    }

    /** Ctrl+wheel — and a trackpad pinch, which browsers deliver as one — zooms the ladder,
     *  not the page. A plain wheel scrolls natively. */
    onWheel(e: WheelEvent): void {
        if (!e.ctrlKey) return;
        const el = this.scroll()?.nativeElement;
        if (!el) return;
        e.preventDefault();
        const rect = el.getBoundingClientRect();
        this.zoomAt(this.scale() * Math.exp(-e.deltaY * WHEEL_ZOOM_RATE),
            e.clientX - rect.left, e.clientY - rect.top);
    }

    // Two-finger pinch (touch). One finger stays native scroll. Ported from TSIC-Events: the
    // zoom is computed from the gesture's START state, so it never drifts mid-pinch.
    private pinch: { dist: number; scale: number; midX: number; midY: number; left: number; top: number } | null = null;

    onTouchStart(e: TouchEvent): void {
        const el = this.scroll()?.nativeElement;
        if (e.touches.length !== 2 || !el) return;
        const rect = el.getBoundingClientRect();
        this.pinch = {
            dist: touchDist(e),
            scale: this.scale(),
            midX: (e.touches[0].clientX + e.touches[1].clientX) / 2 - rect.left,
            midY: (e.touches[0].clientY + e.touches[1].clientY) / 2 - rect.top,
            left: el.scrollLeft,
            top: el.scrollTop
        };
    }

    onTouchMove(e: TouchEvent): void {
        const p = this.pinch;
        if (!p || e.touches.length !== 2) return;
        e.preventDefault(); // two fingers zoom; they must not also scroll
        const next = this.clampScale(p.scale * touchDist(e) / p.dist);
        const factor = next / p.scale;
        this.scale.set(next);
        this.scrollAfterRender((p.left + p.midX) * factor - p.midX, (p.top + p.midY) * factor - p.midY);
    }

    onTouchEnd(e: TouchEvent): void {
        if (e.touches.length < 2) this.pinch = null;
    }

    // Mouse drag-to-pan. A press becomes a drag only past DRAG_THRESHOLD, so a click on a team
    // name, the field link or the pencil still clicks. Touch is excluded: it already scrolls.
    private drag: { x: number; y: number; left: number; top: number; moved: boolean } | null = null;

    onPointerDown(e: PointerEvent): void {
        const el = this.scroll()?.nativeElement;
        if (!el || e.pointerType !== 'mouse' || e.button !== 0) return;
        // a press on the box's own scrollbar drives the scrollbar, not a drag
        if (e.target === el && (e.offsetX >= el.clientWidth || e.offsetY >= el.clientHeight)) return;
        this.drag = { x: e.clientX, y: e.clientY, left: el.scrollLeft, top: el.scrollTop, moved: false };
        window.addEventListener('pointermove', this.onDragMove);
        window.addEventListener('pointerup', this.onDragEnd, { once: true });
    }

    private readonly onDragMove = (e: PointerEvent): void => {
        const d = this.drag;
        const el = this.scroll()?.nativeElement;
        if (!d || !el) return;
        const dx = e.clientX - d.x;
        const dy = e.clientY - d.y;
        if (!d.moved) {
            if (Math.hypot(dx, dy) < DRAG_THRESHOLD) return;
            d.moved = true;
            this.dragging.set(true);
        }
        el.scrollLeft = d.left - dx;
        el.scrollTop = d.top - dy;
    };

    private readonly onDragEnd = (): void => {
        window.removeEventListener('pointermove', this.onDragMove);
        const moved = this.drag?.moved;
        this.drag = null;
        if (!moved) return;
        this.dragging.set(false);
        // The release lands as a click on whatever is under the cursor. Swallow that one click
        // (capture, before it reaches a team name or link); drop the guard if it never comes.
        const el = this.scroll()?.nativeElement;
        if (!el) return;
        const swallow = (ev: MouseEvent) => { ev.stopPropagation(); ev.preventDefault(); };
        el.addEventListener('click', swallow, { capture: true, once: true });
        setTimeout(() => el.removeEventListener('click', swallow, { capture: true }), 0);
    };

    emitLadderScoreEdit(gid: number): void {
        const match = this.bracket()?.matches.find(m => m.gid === gid);
        if (!match) return;
        this.editBracketScore.emit({
            gid: match.gid,
            t1Name: match.t1Name,
            t2Name: match.t2Name,
            t1Score: match.t1Score ?? null,
            t2Score: match.t2Score ?? null,
            roundType: match.roundType
        });
    }

    constructor() {
        // A web font that lands after first paint changes how names wrap; measure again then.
        void document.fonts?.ready.then(() => this.scheduleMeasure());
        this.destroyRef.onDestroy(() => {
            this.destroyed = true;
            window.removeEventListener('pointermove', this.onDragMove);
            window.removeEventListener('pointerup', this.onDragEnd);
        });
    }

    ngOnChanges(changes: SimpleChanges): void {
        if (changes['bracket']) this.fitPending = true;
        // Every input can change a card's content (names, pencil, bold) and so its height.
        this.scheduleMeasure();
    }

    /**
     * Measure every ladder card after the next render and feed the heights back into the
     * layout. Height is content-driven and does NOT depend on position, so one pass settles
     * it: re-positioning cannot change how tall a card is. The equality check skips the
     * write (and the extra render) when nothing changed.
     */
    private scheduleMeasure(): void {
        if (this.destroyed) return;
        afterNextRender(() => {
            const el = this.canvas()?.nativeElement;
            if (!el) return;
            const measured = new Map<number, number>();
            el.querySelectorAll<HTMLElement>('[data-gid]').forEach(node => {
                measured.set(Number(node.dataset['gid']), node.offsetHeight);
            });
            const current = this.cardHeights();
            const unchanged = measured.size === current.size
                && [...measured].every(([gid, h]) => current.get(gid) === h);
            if (!unchanged) this.cardHeights.set(measured);
            // Fit once the heights are in: the fit depends on the ladder's real height, and
            // layout() recomputes from the heights just written.
            if (this.fitPending && this.scroll()) {
                this.fitPending = false;
                this.fitToView();
            }
        }, { injector: this.injector });
    }
}

/** Desktop's opening fit never shrinks below this; past it the ladder scrolls instead. */
const DESKTOP_MIN_FIT = 0.75;
/** zoom-out floor (or the fit-all scale, when that is smaller) and zoom-in ceiling */
const MIN_SCALE = 0.35;
const MAX_SCALE = 2;
/** per unit of wheel deltaY: a mouse notch (~100) ≈ 18%, a trackpad pinch step a few % */
const WHEEL_ZOOM_RATE = 0.002;
/** px a mouse press must travel before it is a drag rather than a click */
const DRAG_THRESHOLD = 5;

function touchDist(e: TouchEvent): number {
    return Math.hypot(e.touches[0].clientX - e.touches[1].clientX,
        e.touches[0].clientY - e.touches[1].clientY);
}

/** Column headers, by GameRoundTypes ladder letter. */
const ROUND_LABELS: Record<string, string> = {
    Z: 'Round of 64', Y: 'Round of 32', X: 'Round of 16',
    Q: 'Quarterfinals', S: 'Semifinals', F: 'Final'
};

/**
 * The team in `parent` that did NOT come out of its single feeder game. Matched by team id
 * rather than assuming slot t1 (legacy/TSIC-Events assume t1): if the feeder's winner is
 * already placed, the bye team is the other slot. Before the feeder is played its slot
 * carries no team id, so the one slot that has an id is the bye team. Only when neither
 * slot is resolved does it fall back to t1's label.
 */
function byeTeamOf(parent: BracketNode, feeder: BracketNode): { name: string; id: string | null } {
    const feederIds = [feeder.t1Id, feeder.t2Id].filter((id): id is string => !!id);
    const t1 = { name: parent.t1Name, id: parent.t1Id };
    const t2 = { name: parent.t2Name, id: parent.t2Id };
    if (parent.t1Id && feederIds.includes(parent.t1Id)) return t2;
    if (parent.t2Id && feederIds.includes(parent.t2Id)) return t1;
    if (parent.t2Id && !parent.t1Id) return t2;
    return t1;
}
