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
import { contrastText, agBg, formatTime } from '../../shared/utils/scheduling-helpers';
import { AgeGroupPickerComponent, type AgePickerItem } from '../../shared/components/age-group-picker/age-group-picker.component';
import { BRACKET_CARD_W, BRACKET_HEADER_H, layoutBracket, type BracketLayoutGame } from './bracket-layout';

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

@Component({
    selector: 'app-brackets-tab',
    standalone: true,
    imports: [AgeGroupPickerComponent, NgTemplateOutlet],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (isLoading()) {
            <div class="loading-container">
                <span class="spinner-border spinner-border-sm" role="status"></span>
                Loading brackets...
            </div>
        } @else if (brackets().length === 0) {
            <div class="empty-state">No bracket data available.</div>
        } @else {
            <!-- Age-group nav: scrollable pill strip on desktop, compact dot+name
                 dropdown on mobile. Both drive the same selectTab. -->
            <div class="ag-picker-bar">
                <app-age-group-picker
                    [items]="agePickerItems()"
                    [selectedId]="activeAgId()"
                    (selectionChange)="onAgePicked($event)" />
            </div>
            <div class="ag-tabs">
                @for (tab of tabItems(); track tab.index) {
                    <button class="ag-tab"
                            [class.active]="activeTabIndex() === tab.index"
                            (click)="selectTab(tab.index)">
                        <span class="ag-dot" [class.ag-dot--empty]="!tab.color"
                              [style.background]="tab.color || null"></span>
                        {{ tab.label }}
                    </button>
                }
            </div>

            <!-- Ladder: cards positioned by bracket-layout.ts over one SVG of elbow connectors.
                 Scrolls horizontally when wider than the page. Absent for consolation-only divisions. -->
            @if (hasLadder()) {
                <div class="ladder-scroll">
                    <div class="ladder-canvas" #canvas
                         [style.width.px]="layout().width"
                         [style.height.px]="layout().height">
                        <svg class="ladder-connectors" aria-hidden="true"
                             [attr.width]="layout().width" [attr.height]="layout().height">
                            @for (d of layout().connectors; track $index) {
                                <path [attr.d]="d" />
                            }
                        </svg>

                        @for (col of layout().columns; track col.x) {
                            <div class="ladder-round"
                                 [style.left.px]="col.x"
                                 [style.width.px]="cardW"
                                 [style.height.px]="headerH">{{ col.label }}</div>
                        }

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
        }
    `,
    styles: [`
        :host { display: block; }

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

        /* ── Tab bar ── */

        /* Mobile-only dropdown bar; the pill strip owns desktop (see mobile @media). */
        .ag-picker-bar { display: none; }

        .ag-tabs {
            display: flex;
            gap: var(--space-1);
            padding: var(--space-2) var(--space-3);
            border-bottom: 1px solid var(--bs-border-color);
            overflow-x: auto;
            scrollbar-width: thin;
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
            transition: background-color 0.15s, color 0.15s, border-color 0.15s, box-shadow 0.15s, opacity 0.15s;
        }

        .ag-tab:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        /* Age-group identity is carried by a quiet color DOT, not a flooded pill — one
           affordance language shared with the mobile picker and the standings strip.
           Every chip is neutral; the active one gets a primary border + bold. */
        .ag-tab.active {
            background: var(--bs-primary-bg-subtle);
            border-color: var(--bs-primary);
            box-shadow: inset 0 0 0 1px var(--bs-primary);
            color: var(--bs-body-color);
            font-weight: 700;
        }

        /* Small filled dot of the age-group's color; inset hairline ring keeps light
           dots visible; dashed neutral ring when the group has no color set. */
        .ag-dot {
            flex-shrink: 0;
            display: inline-block;
            width: 10px;
            height: 10px;
            border-radius: 50%;
            background: var(--bs-secondary-bg);
            box-shadow: inset 0 0 0 1px var(--bs-border-color);
        }
        .ag-dot--empty {
            background: transparent;
            box-shadow: none;
            border: 1px dashed var(--bs-border-color);
        }

        .ag-tab:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        @media (prefers-reduced-motion: reduce) {
            .ag-tab { transition: none !important; }
        }

        /* Swap the pill strip for the dropdown at phone width. */
        @media (max-width: 767px) {
            .ag-tabs { display: none; }
            .ag-picker-bar {
                display: flex;
                justify-content: flex-start;
                padding: var(--space-2) var(--space-3);
                border-bottom: 1px solid var(--bs-border-color);
            }
        }

        /* ── Ladder ── */

        /* Native horizontal scroll is the pan: a bracket wider than the page scrolls; the page
           itself scrolls vertically, since the canvas is as tall as the ladder. */
        .ladder-scroll {
            border: 1px solid var(--bs-border-color);
            border-top: none;
            border-radius: 0 0 var(--radius-sm) var(--radius-sm);
            background: var(--bs-body-bg);
            overflow-x: auto;
        }

        /* Size comes from the layout (template bindings); cards are positioned inside it. */
        .ladder-canvas { position: relative; }

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

        /* One header per round, in the band the layout reserves above the cards. */
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
export class BracketsTabComponent implements OnChanges {
    private readonly injector = inject(Injector);
    private readonly destroyRef = inject(DestroyRef);
    private destroyed = false;

    brackets = input<DivisionBracketResponse[]>([]);
    canScore = input<boolean>(false);
    isLoading = input<boolean>(false);
    agegroupColors = input<Record<string, string | null>>({});
    followedTeamIds = input<readonly string[]>([]);

    private readonly followedSet = computed(() => new Set(this.followedTeamIds()));

    readonly editBracketScore = output<{
    gid: number;
    t1Name: string;
    t2Name: string;
    t1Score: number | null;
    t2Score: number | null;
    /** GameRoundTypes letter (Z/Y/X/Q/S/F ladder, B bronze, C consolation). The score
     *  sheet uses it to decide whether re-scoring can strand a downstream game. */
    roundType: string;
}>();

    readonly viewTeamResults = output<string>();
    readonly viewFieldInfo = output<string>();

    // Optional, not required: the canvas lives inside the @else/@if branches, so it is absent
    // while loading, when the job has no brackets, and for consolation-only divisions.
    // required() throws NG0951 on read.
    readonly canvas = viewChild<ElementRef<HTMLDivElement>>('canvas');

    // ── Tab state ──
    readonly activeTabIndex = signal(0);

    readonly tabItems = computed(() => {
        const data = this.brackets();
        const colors = this.agegroupColors();
        return data.map((b, i) => {
            const color = colors[b.agegroupName] ?? null;
            return {
                index: i,
                label: b.divName ? `${b.agegroupName} — ${b.divName}` : b.agegroupName,
                champion: b.champion ?? null,
                color,
                contrastColor: contrastText(color)
            };
        });
    });

    readonly activeBracket = computed(() => {
        const data = this.brackets();
        const idx = this.activeTabIndex();
        return data[idx] ?? null;
    });

    // Agegroup tint for the active division — shared by the ladder cards and the strip cards.
    private readonly activeAgColor = computed(() =>
        this.agegroupColors()[this.activeBracket()?.agegroupName ?? ''] ?? null);
    readonly cardBg = computed(() => agBg(this.activeAgColor()));
    readonly stripeColor = computed(() => this.activeAgColor() ?? 'var(--bs-border-color)');

    // The single-elimination tree excludes bronze (roundType 'B'): it has no parent to advance
    // into, so it would render as a second, disconnected root and distort the layout.
    readonly hasLadder = computed(() =>
        (this.activeBracket()?.matches ?? []).some(m => m.roundType !== 'B'));

    // Cards for games that are not in the ladder, grouped: bronze first, then consolation.
    readonly outsideGroups = computed<{ kind: OutsideCard['kind']; cards: OutsideCard[] }[]>(() => {
        const b = this.activeBracket();
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
        const b = this.activeBracket();
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

    emitLadderScoreEdit(gid: number): void {
        const match = this.activeBracket()?.matches.find(m => m.gid === gid);
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
        this.destroyRef.onDestroy(() => (this.destroyed = true));
    }

    ngOnChanges(changes: SimpleChanges): void {
        if (changes['brackets']) this.activeTabIndex.set(0);
        // Every input can change a card's content (names, pencil, bold) and so its height.
        this.scheduleMeasure();
    }

    selectTab(index: number): void {
        if (index === this.activeTabIndex()) return;
        this.activeTabIndex.set(index);
        this.scheduleMeasure();
    }

    /** Mobile dropdown feed — same age groups as the pill strip, id = tab index. */
    readonly agePickerItems = computed<AgePickerItem[]>(() =>
        this.tabItems().map(t => ({ id: String(t.index), label: t.label, color: t.color }))
    );
    readonly activeAgId = computed(() => String(this.activeTabIndex()));
    onAgePicked(id: string): void {
        this.selectTab(Number(id));
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
        }, { injector: this.injector });
    }
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
