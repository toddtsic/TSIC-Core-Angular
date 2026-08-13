import {
    AfterViewChecked, ChangeDetectionStrategy, Component, computed, ElementRef,
    input, OnChanges, output, signal, SimpleChanges, viewChild
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ResizablePanelDirective } from '../../../../shared-ui/directives/resizable-panel.directive';
import type { EditScoreRequest } from '@core/api';

/**
 * The slice of a game this sheet needs. Deliberately NOT ViewGameDto: the brackets tab
 * has no ViewGameDto to give (its cards carry gid + names + scores and nothing else) and
 * was previously synthesising a fake one — empty gDate, empty fName, invented status —
 * just to satisfy a type. A narrow shape lets every caller pass what it actually has.
 */
export interface ScoreEntryGame {
    readonly gid: number;
    readonly t1Name: string;
    readonly t2Name: string;
    readonly t1Score: number | null;
    readonly t2Score: number | null;
    readonly gStatusCode: number | null;
    /** Context strip — omitted by callers that have no schedule context (brackets). */
    readonly fName?: string | null;
    readonly gDate?: string | null;
    readonly agDiv?: string | null;
    /** Leagues.Schedules.T1Type — see LADDER_AND_BRONZE. */
    readonly t1Type?: string | null;
}

/**
 * Slot types whose result FEEDS FORWARD into a later game: the single-elimination ladder
 * rounds plus bronze. Editing one of these after it has been decided can strand games
 * downstream, which is what the caution below warns about.
 *
 * Spelled out as an allow-list rather than tested as `!== 'T'`. Consolation ('C') is not
 * round-robin but is never advanced either, so the shorthand would raise a warning about
 * downstream damage that consolation cannot cause. Mirrors GameRoundTypes.BracketTypes on
 * the backend; keep the two in step.
 */
const LADDER_AND_BRONZE: ReadonlySet<string> = new Set(['Z', 'Y', 'X', 'Q', 'S', 'F', 'B']);

/** Leagues.GameStatusCodes — only the two a scorer can set from here. */
const STATUS_FORFEIT = 4;
const STATUS_CANCELLED = 5;

/**
 * Score entry — the ONE surface for putting a result on a game.
 *
 * Ported from the TSIC-Events-2025 mobile app's score-entry sheet, which is the format
 * directors already know: each team gets its name, a big readout, and its own tap pad, so
 * there is never a "which box am I typing in?" moment and the on-screen keyboard never
 * covers the form. Status (cancelled / forfeit) rides along in the same sheet instead of
 * living behind a separate trip to the full edit panel.
 *
 * Two deliberate departures from the Ionic original:
 *   · Its readout is a hardcoded green gradient on a 2s infinite pulse with no
 *     reduced-motion escape. The readout here is a plain strong-ink figure on a token
 *     surface — same size and weight, no throb, palette-responsive, and it does not
 *     compete with the ledger's gold-means-won cue.
 *   · Its pad buttons are 18px tall, below any touch minimum. These are real buttons.
 *
 * The pad is not the only way in. Every keystroke a laptop director would reach for is
 * wired to the same signals the buttons write (see onKeydown), so `4 Tab 2 Enter` scores a
 * game without the trackpad — which is the actual hot path, since the pads exist for the
 * phone. The pad buttons are tabindex="-1" on purpose: they are pointer affordances, and
 * putting 24 of them in the tab order would bury the two controls that matter.
 */
@Component({
    selector: 'app-score-entry-modal',
    standalone: true,
    imports: [DatePipe, ResizablePanelDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (visible()) {
            <div class="detail-backdrop" (click)="close.emit()"></div>
        }
        <div class="detail-panel score-panel" [class.open]="visible()"
             appResizablePanel storageKey="scoreEntryPanelWidth" panelSide="right"
             role="dialog" aria-modal="true" aria-labelledby="score-panel-title"
             (keydown)="onKeydown($event)">

            <div class="panel-header">
                <div class="header-top-row">
                    <h3 class="panel-title" id="score-panel-title">Enter Score</h3>
                    <button type="button" class="btn-close" (click)="close.emit()"
                            aria-label="Close">&times;</button>
                </div>
                @if (game(); as g) {
                    @if (g.fName || g.gDate || g.agDiv) {
                        <div class="score-context">
                            @if (g.fName) { <span class="ctx-field">{{ g.fName }}</span> }
                            @if (g.gDate) { <span class="ctx-when">{{ g.gDate | date: 'EEE M/d · h:mm a' }}</span> }
                            @if (g.agDiv) { <span class="ctx-ag">{{ g.agDiv }}</span> }
                        </div>
                    }
                }
            </div>

            <div class="panel-body">
                @if (game(); as g) {
                    @if (showDecidedWarning()) {
                        <div class="bracket-warning" role="alert">
                            <i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i>
                            <span>This bracket game already has a result. Later games that have
                                  already been played will <strong>not</strong> update
                                  automatically — you may need to correct them by hand.</span>
                        </div>
                    }

                    <!-- One column per team below 768px (the panel is full-bleed there and six
                         pad columns across a phone would fall under the touch minimum); side by
                         side above it, where the 560px panel reads as a scoreboard. -->
                    <div class="score-teams">
                        <!-- ── Team 1 ── -->
                        <section class="team-pane" [class.is-active]="activeSide() === 1"
                                 aria-labelledby="score-t1-name">
                            <div class="team-head">
                                <span class="team-name" id="score-t1-name">{{ g.t1Name }}</span>
                                <button type="button" class="score-readout" #readout1
                                        role="spinbutton"
                                        [attr.aria-valuenow]="t1() === '' ? null : +t1()"
                                        [attr.aria-valuetext]="t1() === '' ? 'No score' : t1()"
                                        aria-valuemin="0" aria-valuemax="99"
                                        [attr.aria-label]="g.t1Name + ' score'"
                                        (focus)="activeSide.set(1)"
                                        (click)="activeSide.set(1)">{{ display(t1()) }}</button>
                            </div>
                            <div class="number-pad" role="group"
                                 [attr.aria-label]="g.t1Name + ' score pad'">
                                @for (n of DIGITS; track n) {
                                    <button type="button" class="pad-btn" tabindex="-1"
                                            (click)="append(1, n)">{{ n }}</button>
                                }
                                <button type="button" class="pad-btn pad-word" tabindex="-1"
                                        (click)="clear(1)">Clear</button>
                                <button type="button" class="pad-btn" tabindex="-1"
                                        (click)="append(1, 0)">0</button>
                                <button type="button" class="pad-btn pad-word" tabindex="-1"
                                        aria-label="Backspace" (click)="backspace(1)">
                                    <i class="bi bi-backspace" aria-hidden="true"></i>
                                </button>
                            </div>
                        </section>

                        <!-- ── Team 2 ── -->
                        <section class="team-pane" [class.is-active]="activeSide() === 2"
                                 aria-labelledby="score-t2-name">
                            <div class="team-head">
                                <span class="team-name" id="score-t2-name">{{ g.t2Name }}</span>
                                <button type="button" class="score-readout" #readout2
                                        role="spinbutton"
                                        [attr.aria-valuenow]="t2() === '' ? null : +t2()"
                                        [attr.aria-valuetext]="t2() === '' ? 'No score' : t2()"
                                        aria-valuemin="0" aria-valuemax="99"
                                        [attr.aria-label]="g.t2Name + ' score'"
                                        (focus)="activeSide.set(2)"
                                        (click)="activeSide.set(2)">{{ display(t2()) }}</button>
                            </div>
                            <div class="number-pad" role="group"
                                 [attr.aria-label]="g.t2Name + ' score pad'">
                                @for (n of DIGITS; track n) {
                                    <button type="button" class="pad-btn" tabindex="-1"
                                            (click)="append(2, n)">{{ n }}</button>
                                }
                                <button type="button" class="pad-btn pad-word" tabindex="-1"
                                        (click)="clear(2)">Clear</button>
                                <button type="button" class="pad-btn" tabindex="-1"
                                        (click)="append(2, 0)">0</button>
                                <button type="button" class="pad-btn pad-word" tabindex="-1"
                                        aria-label="Backspace" (click)="backspace(2)">
                                    <i class="bi bi-backspace" aria-hidden="true"></i>
                                </button>
                            </div>
                        </section>
                    </div>

                    <!-- Status — collapsed by default. A game gets a score; cancelled and
                         forfeit are the exceptions, so they cost one tap and no vertical
                         space on the common path. Opens pre-expanded when one is already
                         set, so an existing exception is never hidden behind a chevron. -->
                    <div class="status-block">
                        <button type="button" class="status-toggle"
                                [attr.aria-expanded]="statusOpen()"
                                (click)="statusOpen.set(!statusOpen())">
                            <i class="bi" [class.bi-chevron-right]="!statusOpen()"
                               [class.bi-chevron-down]="statusOpen()" aria-hidden="true"></i>
                            <span>Game status</span>
                            @if (status() === null) {
                                <span class="status-hint">optional</span>
                            } @else {
                                <span class="status-set">{{ statusLabel() }}</span>
                            }
                        </button>
                        @if (statusOpen()) {
                            <div class="status-choices">
                                <button type="button" class="status-btn"
                                        [class.is-on]="status() === STATUS_CANCELLED"
                                        [attr.aria-pressed]="status() === STATUS_CANCELLED"
                                        (click)="toggleStatus(STATUS_CANCELLED)">Cancelled</button>
                                <button type="button" class="status-btn"
                                        [class.is-on]="status() === STATUS_FORFEIT"
                                        [attr.aria-pressed]="status() === STATUS_FORFEIT"
                                        (click)="toggleStatus(STATUS_FORFEIT)">Forfeit</button>
                            </div>
                        }
                    </div>

                    <!-- Only ever shown for the one state Save refuses: exactly one box
                         filled. Silence on a disabled button is the thing that makes a form
                         feel broken. -->
                    @if (!canSave()) {
                        <p class="save-hint" role="status">
                            Enter both scores, or clear both to return the game to unscored.
                        </p>
                    }

                    <!-- The server rejected the write. There is no global error surface in
                         this app (auth is the only interceptor), so a refusal shown nowhere
                         is a refusal that looks like a hang — and the backend genuinely does
                         refuse real entries, e.g. a tie on a single-elimination game. -->
                    @if (error()) {
                        <p class="save-error" role="alert">
                            <i class="bi bi-x-octagon-fill" aria-hidden="true"></i>
                            <span>{{ error() }}</span>
                        </p>
                    }
                }
            </div>

            <div class="panel-footer">
                <button type="button" class="btn btn-outline-secondary btn-sm"
                        [disabled]="saving()" (click)="close.emit()">Cancel</button>
                <button type="button" class="btn btn-primary btn-sm"
                        [disabled]="!canSave() || saving()" (click)="onSave()">
                    @if (saving()) {
                        <span class="spinner-border spinner-border-sm me-1" role="status"
                              aria-hidden="true"></span>Saving…
                    } @else {
                        {{ isClearing() ? 'Clear Score' : 'Save Score' }}
                    }
                </button>
            </div>
        </div>
    `,
    styles: [`
        /* The panel is the app's canonical fly-in (styles/_flyin.scss) — same surface,
           motion and mobile takeover as every other detail panel, so this sheet needs no
           positional CSS of its own and cannot drift from the contract. */

        /* ── Context strip ── field · when · age group, under the title. */
        .score-context {
            display: flex;
            flex-wrap: wrap;
            align-items: baseline;
            gap: var(--space-2) var(--space-3);
            margin-top: var(--space-2);
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
        }

        .ctx-field {
            padding: 1px var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            background: var(--bs-tertiary-bg);
            color: var(--bs-body-color);
            font-weight: 600;
        }

        .ctx-ag { font-weight: 600; color: var(--bs-body-color); }

        /* ── Decided-bracket caution ── */
        .bracket-warning {
            display: flex;
            gap: var(--space-2);
            padding: var(--space-3);
            margin-bottom: var(--space-4);
            border: 1px solid var(--bs-danger);
            border-radius: var(--radius-lg);
            background: color-mix(in srgb, var(--bs-danger) 12%, var(--surface-elevated-bg));
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            line-height: 1.4;
        }

        .bracket-warning .bi {
            flex: none;
            color: var(--bs-danger);
        }

        /* ── Team panes ──
           Two abreast at EVERY width, phone included: home | away is a scoreboard, and
           stacking them was the mistake. A stacked pair is roughly 500px of pads, so on a
           phone the away team's pad sat below the fold — the scorer had to scroll to reach
           half the control, on the one surface where the whole point is entering two numbers
           without thinking. Side by side the whole sheet fits with the footer in view.
           Width is not the constraint it looked like: the buttons were ~100px wide stacked,
           more than twice what a digit needs. Halved, they are still ~50px on a 390px phone
           (see the narrow-viewport block below for the arithmetic).
           Single column only under 380px, where two columns really would fall below the
           touch minimum. */
        .score-teams {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: var(--space-3);
        }

        @media (max-width: 379.98px) {
            .score-teams { grid-template-columns: 1fr; }
        }

        /* Flex column with the pad pushed to the bottom (see .number-pad). Grid stretches
           both panes to a common height, but the two team NAMES wrap to different line
           counts — "Hero's Lax:2029 green" beside "Sky Walkers Lacrosse Program:Blue" —
           which would start the two pads at different heights and leave the 1-4-7 columns
           out of register across the sheet. Bottom-aligning the pads makes name length
           irrelevant to where the digits sit. */
        .team-pane {
            display: flex;
            flex-direction: column;
            padding: var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-lg);
            background: var(--surface-elevated-bg, var(--bs-body-bg));
            /* Only the border colour changes on activation — no width change, or the pads
               would shift by a pixel every time focus moved between the two panes. */
            transition: border-color 0.15s, background-color 0.15s;
        }

        /* The pane the keyboard is aimed at. Paired with the focus ring on the readout
           itself, so this is reinforcement rather than the sole channel. */
        .team-pane.is-active {
            border-color: var(--bs-primary);
            background: color-mix(in srgb, var(--bs-primary) 4%, var(--surface-elevated-bg, var(--bs-body-bg)));
        }

        @media (prefers-reduced-motion: reduce) {
            .team-pane { transition: none !important; }
        }

        .team-head {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            margin-bottom: var(--space-3);
        }

        .team-name {
            flex: 1;
            min-width: 0;
            font-size: var(--font-size-sm);
            font-weight: 600;
            line-height: 1.3;
            color: var(--bs-body-color);
            /* Wraps rather than ellipsizes: these are "{club}:{team}" strings and the tail
               is the half that names the team. Two lines cost nothing here. */
            overflow-wrap: break-word;
        }

        /* The readout is the score. Big, tabular, strong ink on a plain token surface —
           no gradient, no pulse (see the class comment). It is also the keyboard's target,
           which is why it is a real focusable control and not a <span>. */
        .score-readout {
            flex: none;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 4.5rem;
            min-height: 3rem;
            padding: 0 var(--space-2);
            border: 2px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            background: var(--bs-body-bg);
            color: var(--score-strong);
            font-family: var(--bs-font-monospace);
            font-size: var(--font-size-2xl);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            line-height: 1;
            cursor: pointer;
        }

        .team-pane.is-active .score-readout { border-color: var(--bs-primary); }

        .score-readout:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
            border-color: var(--bs-primary);
        }

        /* ── Number pad ── */
        /* margin-top: auto — bottom-aligns the pad within its stretched pane so both pads
           sit on the same line regardless of how far each team's name wrapped. */
        .number-pad {
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            gap: var(--space-2);
            margin-top: auto;
        }

        .pad-btn {
            appearance: none;
            min-height: 44px;
            padding: 0;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-family: var(--bs-font-monospace);
            font-size: var(--font-size-lg);
            font-weight: 600;
            font-variant-numeric: tabular-nums;
            line-height: 1;
            cursor: pointer;
            transition: background-color 0.12s, border-color 0.12s;
        }

        .pad-word {
            font-family: var(--bs-font-sans-serif, inherit);
            font-size: var(--font-size-sm);
            font-weight: 600;
            color: var(--bs-secondary-color);
        }

        .pad-btn:hover {
            background: var(--bs-primary-bg-subtle);
            border-color: var(--bs-primary);
        }

        .pad-btn:active { background: var(--bs-secondary-bg); }

        @media (prefers-reduced-motion: reduce) {
            .pad-btn { transition: none !important; }
        }

        /* ── Status ── */
        .status-block { margin-top: var(--space-4); }

        .status-toggle {
            appearance: none;
            display: flex;
            align-items: center;
            gap: var(--space-2);
            width: 100%;
            padding: var(--space-2);
            border: none;
            border-radius: var(--radius-sm);
            background: transparent;
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            font-weight: 600;
            text-align: left;
            cursor: pointer;
        }

        .status-toggle:hover { background: var(--bs-tertiary-bg); }

        .status-toggle:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        .status-hint {
            font-weight: 400;
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
        }

        /* An exception IS set. Kept to ink + weight rather than a colour chip: cancelled
           and forfeit already read as exceptional by being the only thing in this row. */
        .status-set {
            padding: 1px var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            background: var(--bs-tertiary-bg);
            font-size: var(--font-size-2xs);
            font-weight: 700;
            letter-spacing: 0.04em;
            text-transform: uppercase;
            color: var(--score-strong);
        }

        .status-choices {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: var(--space-2);
            padding: var(--space-2) var(--space-2) 0;
        }

        .status-btn {
            appearance: none;
            min-height: 40px;
            padding: 0 var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            font-weight: 600;
            cursor: pointer;
            transition: background-color 0.12s, border-color 0.12s, color 0.12s;
        }

        .status-btn:hover { border-color: var(--bs-primary); }

        /* Selected state carries a check glyph as well as the fill, so it is never
           colour-alone. */
        /* --neutral-0, not --brand-primary-contrast. Two neighbours in this feature reach
           for the latter, but it is defined nowhere in the token sheet — both are silently
           riding their own hex fallback. --neutral-0 is real (styles/_tokens.scss). */
        .status-btn.is-on {
            border-color: var(--bs-primary);
            background: var(--bs-primary);
            color: var(--neutral-0);
        }

        .status-btn.is-on::before {
            content: '\\2713\\00a0';
            font-weight: 700;
        }

        .status-btn:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        @media (prefers-reduced-motion: reduce) {
            .status-btn { transition: none !important; }
        }

        .save-hint {
            margin: var(--space-3) 0 0;
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
        }

        .save-error {
            display: flex;
            align-items: flex-start;
            gap: var(--space-2);
            margin: var(--space-3) 0 0;
            padding: var(--space-3);
            border: 1px solid var(--bs-danger);
            border-radius: var(--radius-md);
            background: color-mix(in srgb, var(--bs-danger) 12%, var(--surface-elevated-bg));
            font-size: var(--font-size-sm);
            line-height: 1.4;
            color: var(--bs-body-color);
        }

        .save-error .bi {
            flex: none;
            color: var(--bs-danger);
        }

        /* ── Narrow viewport (the panel is full-bleed here) ──
           Everything tightens so both pads plus the status row and footer land on one
           screen. Worked from a 390px phone: 390 − 24 body padding = 366; two panes with a
           12px gap = 177 each; − 16 pane padding = 161 inner; three columns with two 4px
           gaps = 51px per button, against a 44px touch minimum. Raising any padding in
           this block eats that margin — check the arithmetic before you do.

           The panel-body override needs .detail-panel.score-panel, not .score-panel alone:
           the global rule it is beating (styles/_flyin.scss) is .detail-panel .panel-body,
           equal specificity, and which of two equal rules wins would then come down to
           stylesheet injection order. */
        @media (max-width: 767.98px) {
            .detail-panel.score-panel .panel-body { padding: var(--space-3); }

            .team-pane { padding: var(--space-2); }

            .team-head {
                gap: var(--space-2);
                margin-bottom: var(--space-2);
            }

            .team-name { font-size: var(--font-size-xs); }

            .score-readout {
                min-width: 3.25rem;
                min-height: 2.5rem;
                font-size: var(--font-size-xl);
            }

            .number-pad { gap: var(--space-1); }

            /* 42px, not 44: the pad is a grid of nine same-sized targets with no
               neighbouring hazard, and 2px per row is 8px of height back. Still inside the
               spirit of the minimum, and the buttons are ~51px WIDE. */
            .pad-btn {
                min-height: 42px;
                font-size: var(--font-size-base);
            }

            .pad-word { font-size: var(--font-size-xs); }
        }
    `]
})
export class ScoreEntryModalComponent implements OnChanges, AfterViewChecked {
    // ── Inputs ──
    readonly game = input<ScoreEntryGame | null>(null);
    readonly visible = input<boolean>(false);
    /** Host owns the in-flight state so the sheet stays open until the write lands. */
    readonly saving = input<boolean>(false);
    /** Server's refusal message, or null. Owned by the host because only it sees the
     *  HTTP response; the sheet just renders it and stays open for a retry. */
    readonly error = input<string | null>(null);

    // ── Outputs ──
    readonly close = output<void>();
    readonly save = output<EditScoreRequest>();

    // Template constants
    protected readonly DIGITS = [1, 2, 3, 4, 5, 6, 7, 8, 9] as const;
    protected readonly STATUS_FORFEIT = STATUS_FORFEIT;
    protected readonly STATUS_CANCELLED = STATUS_CANCELLED;

    private readonly readout1 = viewChild<ElementRef<HTMLButtonElement>>('readout1');
    private readonly readout2 = viewChild<ElementRef<HTMLButtonElement>>('readout2');

    /**
     * Scores are held as STRINGS, not numbers. '' is "no score" and is distinct from '0' —
     * a number signal cannot express that difference without a second "is set" flag, and a
     * 0-0 draw is a real result that must survive a round trip. Strings also make the pad's
     * append/backspace trivial (they are literally character operations) and cap the value
     * at two digits by length rather than by arithmetic.
     */
    readonly t1 = signal<string>('');
    readonly t2 = signal<string>('');

    /** Which pane the keyboard writes to. Follows focus; a pad tap sets it too. */
    readonly activeSide = signal<1 | 2>(1);

    /** STATUS_CANCELLED, STATUS_FORFEIT, or null for "no exception". */
    readonly status = signal<number | null>(null);
    readonly statusOpen = signal(false);

    /** Set by ngOnChanges, drained by ngAfterViewChecked — the readout does not exist in
     *  the DOM until the panel renders, so focus cannot be taken in the same tick. */
    private pendingFocus = false;

    // ── Derived ──

    private readonly bothFilled = computed(() => this.t1() !== '' && this.t2() !== '');
    private readonly bothEmpty = computed(() => this.t1() === '' && this.t2() === '');

    /**
     * Both filled, or both empty. The refused state is exactly one box filled, which is
     * always a half-finished entry rather than an intention — the old inline editor
     * silently wrote the blank side as 0 and there was no way to tell a real 3-0 from a
     * distracted 3-and-walked-away.
     *
     * Status does not unlock a half entry: a forfeit is either scored (1-0) or unscored,
     * never half.
     */
    readonly canSave = computed(() => this.bothFilled() || this.bothEmpty());

    /** Saving with both boxes empty on a game that HAS a score is an unscore — say so on
     *  the button rather than letting "Save" quietly wipe a result. */
    readonly isClearing = computed(() => {
        const g = this.game();
        return this.bothEmpty() && !!g && (g.t1Score != null || g.t2Score != null);
    });

    readonly statusLabel = computed(() =>
        this.status() === STATUS_CANCELLED ? 'Cancelled'
            : this.status() === STATUS_FORFEIT ? 'Forfeit'
                : '');

    /** Caution when re-scoring a bracket game that already has a result — its winner has
     *  already been written forward and later games will not re-derive. */
    readonly showDecidedWarning = computed(() => {
        const g = this.game();
        if (!g) return false;
        return LADDER_AND_BRONZE.has(g.t1Type ?? '')
            && g.t1Score != null && g.t2Score != null;
    });

    // ── Lifecycle ──

    /**
     * Seed the pads from the game the host handed us. Driven by ngOnChanges, not an
     * effect: these signals are edited by the user immediately afterwards, and an effect
     * that both reads the input and writes the local copy re-seeds itself on every
     * unrelated dependency — it would wipe a half-typed score.
     */
    ngOnChanges(changes: SimpleChanges): void {
        if (!changes['visible'] && !changes['game']) return;

        const g = this.game();
        if (!this.visible() || !g) return;

        this.t1.set(g.t1Score == null ? '' : String(g.t1Score));
        this.t2.set(g.t2Score == null ? '' : String(g.t2Score));

        const s = g.gStatusCode;
        const exception = s === STATUS_CANCELLED || s === STATUS_FORFEIT ? s : null;
        this.status.set(exception);
        this.statusOpen.set(exception !== null);

        this.activeSide.set(1);
        this.pendingFocus = true;
    }

    ngAfterViewChecked(): void {
        if (!this.pendingFocus) return;
        this.pendingFocus = false;
        this.readout1()?.nativeElement.focus();
    }

    // ── Score editing ──

    display(v: string): string {
        return v === '' ? '–' : v;
    }

    private read(side: 1 | 2): string {
        return side === 1 ? this.t1() : this.t2();
    }

    private write(side: 1 | 2, v: string): void {
        (side === 1 ? this.t1 : this.t2).set(v);
    }

    /**
     * Returning focus to the readout after every pad tap does two jobs: it keeps the
     * active-pane ring where the eye expects it, and it takes focus OFF the pad button —
     * a clicked button keeps focus in Chrome, so a subsequent Enter would have re-fired
     * that digit instead of submitting.
     */
    private focusActive(): void {
        const ref = this.activeSide() === 1 ? this.readout1() : this.readout2();
        ref?.nativeElement.focus();
    }

    /** Third digit is refused, not rolled — scores cap at 99 and a silent shift would
     *  turn a mis-tap into a plausible wrong score. */
    append(side: 1 | 2, digit: number): void {
        this.activeSide.set(side);
        const cur = this.read(side);
        if (cur === '') {
            this.write(side, String(digit));
        } else if (cur.length < 2) {
            this.write(side, cur + String(digit));
        }
        this.focusActive();
    }

    backspace(side: 1 | 2): void {
        this.activeSide.set(side);
        const cur = this.read(side);
        this.write(side, cur.length <= 1 ? '' : cur.slice(0, -1));
        this.focusActive();
    }

    clear(side: 1 | 2): void {
        this.activeSide.set(side);
        this.write(side, '');
        this.focusActive();
    }

    /** Arrow keys on a spinbutton. Empty counts as 0 so ↑ from blank gives 1. */
    private bump(side: 1 | 2, delta: number): void {
        const cur = this.read(side);
        const n = cur === '' ? 0 : Number(cur);
        const next = Math.min(99, Math.max(0, n + delta));
        this.write(side, String(next));
    }

    toggleStatus(code: number): void {
        this.status.set(this.status() === code ? null : code);
    }

    // ── Keyboard ──

    /**
     * The laptop path. Digits, backspace and the arrows write to whichever pane holds
     * focus; Tab moves between the two readouts natively (the pads are out of the tab
     * order); Enter saves and Escape closes.
     */
    onKeydown(e: KeyboardEvent): void {
        const target = e.target as HTMLElement | null;
        const onReadout = !!target?.classList?.contains('score-readout');

        if (e.key === 'Escape') {
            e.preventDefault();
            this.close.emit();
            return;
        }

        // Enter/Space anywhere but a readout belongs to whatever control has focus —
        // the status toggles and the footer buttons are all real buttons.
        if (!onReadout && (e.key === 'Enter' || e.key === ' ')) return;

        if (e.key === 'Enter') {
            e.preventDefault();
            this.onSave();
            return;
        }

        if (!onReadout) return;

        const side = this.activeSide();

        if (e.key >= '0' && e.key <= '9') {
            e.preventDefault();
            this.append(side, Number(e.key));
            return;
        }

        switch (e.key) {
            case 'Backspace':
                e.preventDefault();
                this.backspace(side);
                break;
            case 'Delete':
                e.preventDefault();
                this.clear(side);
                break;
            case 'ArrowUp':
                e.preventDefault();
                this.bump(side, 1);
                break;
            case 'ArrowDown':
                e.preventDefault();
                this.bump(side, -1);
                break;
        }
    }

    // ── Commit ──

    onSave(): void {
        const g = this.game();
        if (!g || !this.canSave() || this.saving()) return;

        const toScore = (v: string): number | null => (v === '' ? null : Number(v));

        // gStatusCode is left UNDEFINED when no exception is set, so the backend applies
        // its own rule (a scored game becomes Final(6); a cleared one falls back to
        // Scheduled(1)). Sending a status here would override that derivation — and would
        // also be how a previously-cancelled game silently stayed cancelled after being
        // scored.
        this.save.emit({
            gid: g.gid,
            t1Score: toScore(this.t1()),
            t2Score: toScore(this.t2()),
            gStatusCode: this.status() ?? undefined
        });
    }
}
