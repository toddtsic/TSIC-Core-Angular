import { ChangeDetectionStrategy, Component, computed, EventEmitter, input, Output, signal } from '@angular/core';
import type { DivisionBracketResponse, BracketMatchDto } from '@core/api';

/** Canonical round ordering for single-elimination brackets (left to right). */
const ROUND_ORDER: string[] = ['Z', 'Y', 'X', 'Q', 'S', 'F'];

/** Human-readable labels per round code. */
const ROUND_LABELS: Record<string, string> = {
    Z: 'Round of 64',
    Y: 'Round of 32',
    X: 'Round of 16',
    Q: 'Quarterfinals',
    S: 'Semifinals',
    F: 'Finals'
};

interface GridMatch {
    match: BracketMatchDto;
    gridRow: string;
    gridCol: number;
    isFinal: boolean;
}

interface GridConnector {
    gridRow: string;
    gridCol: number;
}

interface RoundLabel {
    label: string;
    gridCol: number;
}

interface ParsedBracket {
    agegroupName: string;
    divName: string;
    headerLabel: string;
    champion: string | null;
    matches: GridMatch[];
    connectors: GridConnector[];
    labels: RoundLabel[];
    gridCols: string;
    gridRows: string;
}

@Component({
    selector: 'app-brackets-tab',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (isLoading()) {
            <div class="loading-container">
                <span class="spinner-border spinner-border-sm" role="status"></span>
                Loading brackets...
            </div>
        } @else if (parsed().length === 0) {
            <div class="empty-state">No bracket data available.</div>
        } @else {
            <!-- Single shared viewport for all brackets -->
            <div class="brackets-viewport"
                 (wheel)="onWheel($event)"
                 (pointerdown)="onPanStart($event)"
                 (pointermove)="onPanMove($event)"
                 (pointerup)="onPanEnd()"
                 (pointerleave)="onPanEnd()"
                 [style.cursor]="isPanning() ? 'grabbing' : 'grab'">

                <div class="zoom-controls">
                    <button class="zoom-btn" (click)="zoomIn()" title="Zoom in"
                            (pointerdown)="$event.stopPropagation()">
                        <i class="bi bi-plus-lg"></i>
                    </button>
                    <button class="zoom-btn" (click)="zoomOut()" title="Zoom out"
                            (pointerdown)="$event.stopPropagation()">
                        <i class="bi bi-dash-lg"></i>
                    </button>
                    <button class="zoom-btn" (click)="resetZoom()" title="Reset zoom"
                            (pointerdown)="$event.stopPropagation()">
                        <i class="bi bi-arrows-angle-contract"></i>
                    </button>
                    <span class="zoom-label">{{ Math.round(scale() * 100) }}%</span>
                </div>

                <div class="brackets-layout"
                     [style.transform]="'scale(' + scale() + ') translate(' + translateX() + 'px, ' + translateY() + 'px)'">

                    @for (bracket of parsed(); track bracket.agegroupName + ':' + bracket.divName) {
                        <div class="bracket-section">
                            <div class="section-header">
                                <span class="section-title">{{ bracket.headerLabel }}</span>
                                @if (bracket.champion) {
                                    <span class="champion-badge">
                                        <i class="bi bi-trophy-fill"></i>
                                        {{ bracket.champion }}
                                    </span>
                                }
                            </div>

                            <!-- Round labels -->
                            <div class="label-row" [style.grid-template-columns]="bracket.gridCols">
                                @for (lbl of bracket.labels; track lbl.gridCol) {
                                    <div class="round-label" [style.grid-column]="lbl.gridCol">{{ lbl.label }}</div>
                                }
                            </div>

                            <!-- Bracket grid -->
                            <div class="bracket-grid"
                                 [style.grid-template-columns]="bracket.gridCols"
                                 [style.grid-template-rows]="bracket.gridRows">

                                @for (m of bracket.matches; track m.match.gid) {
                                    <div class="grid-match"
                                         [class.is-final]="m.isFinal"
                                         [style.grid-row]="m.gridRow"
                                         [style.grid-column]="m.gridCol">
                                        <div class="bracket-match"
                                             [class.clickable]="canScore()"
                                             (click)="onMatchClick(m.match)"
                                             (pointerdown)="canScore() && $event.stopPropagation()">
                                            <div class="match-team"
                                                 [class.winner]="m.match.t1Css === 'winner'"
                                                 [class.loser]="m.match.t1Css === 'loser'">
                                                <span class="team-name">{{ m.match.t1Name }}</span>
                                                <span class="team-score">{{ m.match.t1Score ?? '' }}</span>
                                            </div>
                                            <div class="match-divider"></div>
                                            <div class="match-team"
                                                 [class.winner]="m.match.t2Css === 'winner'"
                                                 [class.loser]="m.match.t2Css === 'loser'">
                                                <span class="team-name">{{ m.match.t2Name }}</span>
                                                <span class="team-score">{{ m.match.t2Score ?? '' }}</span>
                                            </div>
                                            @if (m.match.locationTime) {
                                                <div class="match-info">{{ m.match.locationTime }}</div>
                                            }
                                        </div>
                                    </div>
                                }

                                @for (c of bracket.connectors; track $index) {
                                    <div class="grid-connector"
                                         [style.grid-row]="c.gridRow"
                                         [style.grid-column]="c.gridCol"></div>
                                }
                            </div>
                        </div>
                    }
                </div>
            </div>
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

        /* ── Shared viewport (pan & zoom container for ALL brackets) ── */

        .brackets-viewport {
            position: relative;
            overflow: hidden;
            background: var(--bs-body-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            min-height: 400px;
            user-select: none;
            touch-action: none;
        }

        .zoom-controls {
            position: sticky;
            top: var(--space-2);
            float: right;
            margin-right: var(--space-2);
            margin-top: var(--space-2);
            z-index: 10;
            display: flex;
            align-items: center;
            gap: var(--space-1);
            background: var(--bs-body-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            padding: var(--space-1);
            box-shadow: var(--shadow-sm);
        }

        .zoom-btn {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 28px;
            height: 28px;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            cursor: pointer;
            font-size: var(--font-size-sm);
            transition: background-color 0.15s;
        }

        .zoom-btn:hover {
            background: var(--bs-secondary-bg);
        }

        .zoom-label {
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
            min-width: 36px;
            text-align: center;
            font-variant-numeric: tabular-nums;
        }

        /* ── Transformed layout container ── */

        .brackets-layout {
            padding: var(--space-6);
            transform-origin: 0 0;
            will-change: transform;
        }

        /* ── Per-bracket section ── */

        .bracket-section {
            margin-bottom: var(--space-8);
        }

        .bracket-section:last-child {
            margin-bottom: 0;
        }

        .section-header {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            padding: var(--space-2) var(--space-3);
            background: var(--bs-secondary-bg);
            border-radius: var(--bs-border-radius);
            margin-bottom: var(--space-3);
        }

        .section-title {
            font-weight: 600;
            color: var(--bs-body-color);
        }

        .champion-badge {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-2);
            background: rgba(var(--bs-warning-rgb), 0.15);
            color: var(--bs-warning);
            border-radius: var(--bs-border-radius);
            font-size: var(--font-size-xs);
            font-weight: 600;
        }

        .champion-badge i {
            font-size: var(--font-size-sm);
        }

        /* ── Round labels row ── */

        .label-row {
            display: grid;
            margin-bottom: var(--space-3);
        }

        .round-label {
            font-size: var(--font-size-xs);
            font-weight: 600;
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            letter-spacing: 0.05em;
            white-space: nowrap;
            text-align: center;
        }

        /* ── Bracket grid ── */

        .bracket-grid {
            display: grid;
        }

        /* ── Match cell ── */

        .grid-match {
            display: flex;
            align-items: center;
            padding: var(--space-2) var(--space-1);
        }

        /* ── Match card ── */

        .bracket-match {
            width: 100%;
            background: var(--bs-body-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            overflow: hidden;
            transition: box-shadow 0.15s, border-color 0.15s;
        }

        .bracket-match.clickable {
            cursor: pointer !important;
        }

        .bracket-match.clickable:hover {
            border-color: var(--bs-primary);
            box-shadow: var(--shadow-md);
        }

        .match-team {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: var(--space-1) var(--space-2);
            color: var(--bs-body-color);
            transition: background-color 0.15s;
        }

        .match-team.winner {
            font-weight: 700;
            color: var(--bs-success);
        }

        .match-team.loser {
            color: var(--bs-danger);
            opacity: 0.7;
        }

        .match-divider {
            height: 1px;
            background: var(--bs-border-color);
        }

        .team-name {
            flex: 1;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            font-size: var(--font-size-sm);
        }

        .team-score {
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            font-size: var(--font-size-sm);
            min-width: 1.5rem;
            text-align: right;
            margin-left: var(--space-2);
        }

        .match-info {
            font-size: 10px;
            color: var(--bs-secondary-color);
            font-style: italic;
            padding: 2px var(--space-2);
            border-top: 1px solid var(--bs-border-color);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        /* ── Connector: bracket lines between rounds ── */
        /*
         * Each connector cell spans 2 match slots from the preceding round.
         * The ::before draws the bracket shape:  ─┐
         *                                         │
         *                                        ─┘
         * The ::after draws the merge line:       ├───
         * Together they produce:  ─┐
         *                          ├───
         *                         ─┘
         */

        .grid-connector {
            position: relative;
        }

        .grid-connector::before {
            content: '';
            position: absolute;
            left: 0;
            top: 25%;
            bottom: 25%;
            width: 50%;
            border-top: 2px solid var(--bs-border-color);
            border-right: 2px solid var(--bs-border-color);
            border-bottom: 2px solid var(--bs-border-color);
            border-top-right-radius: 4px;
            border-bottom-right-radius: 4px;
        }

        .grid-connector::after {
            content: '';
            position: absolute;
            left: 50%;
            top: 50%;
            right: 0;
            height: 2px;
            background: var(--bs-border-color);
            transform: translateY(-50%);
        }
    `]
})
export class BracketsTabComponent {
    brackets = input<DivisionBracketResponse[]>([]);
    canScore = input<boolean>(false);
    isLoading = input<boolean>(false);

    @Output() editBracketScore = new EventEmitter<{
        gid: number;
        t1Name: string;
        t2Name: string;
        t1Score: number | null;
        t2Score: number | null;
    }>();

    readonly Math = Math;

    // ── Shared zoom & pan state (applies to ALL bracket displays) ──
    readonly scale = signal(1);
    readonly translateX = signal(0);
    readonly translateY = signal(0);
    readonly isPanning = signal(false);

    private panStartX = 0;
    private panStartY = 0;
    private panOriginX = 0;
    private panOriginY = 0;

    // ── Parsed bracket structure ──
    readonly parsed = computed<ParsedBracket[]>(() => {
        const data = this.brackets();
        if (!data?.length) return [];

        return data.map(div => {
            // Group matches by roundType
            const roundMap = new Map<string, BracketMatchDto[]>();
            for (const m of div.matches) {
                const rt = m.roundType?.toUpperCase() ?? '';
                if (!roundMap.has(rt)) roundMap.set(rt, []);
                roundMap.get(rt)!.push(m);
            }

            // Build ordered rounds (only rounds that have matches)
            const rounds: { code: string; label: string; matches: BracketMatchDto[] }[] = [];
            for (const code of ROUND_ORDER) {
                const matches = roundMap.get(code);
                if (matches?.length)
                    rounds.push({ code, label: ROUND_LABELS[code] ?? code, matches: [...matches].sort((a, b) => a.gid - b.gid) });
            }
            for (const [code, matches] of roundMap) {
                if (!ROUND_ORDER.includes(code) && matches.length)
                    rounds.push({ code, label: code, matches: [...matches].sort((a, b) => a.gid - b.gid) });
            }

            // Header: show "Agegroup — Division" when per-division, just "Agegroup" otherwise
            const headerLabel = div.divName
                ? `${div.agegroupName} — ${div.divName}`
                : div.agegroupName;

            if (!rounds.length) {
                return { agegroupName: div.agegroupName, divName: div.divName, headerLabel,
                    champion: div.champion ?? null, matches: [], connectors: [], labels: [],
                    gridCols: '', gridRows: '' };
            }

            // Compute total grid rows needed (handles malformed data)
            let totalRows = 0;
            for (let ri = 0; ri < rounds.length; ri++) {
                const needed = rounds[ri].matches.length * Math.pow(2, ri);
                if (needed > totalRows) totalRows = needed;
            }

            // Build grid items
            const gridMatches: GridMatch[] = [];
            const gridConnectors: GridConnector[] = [];
            const labels: RoundLabel[] = [];

            for (let ri = 0; ri < rounds.length; ri++) {
                const round = rounds[ri];
                const matchCol = ri * 2 + 1;      // odd columns: 1, 3, 5, ...
                const connCol = ri * 2 + 2;       // even columns: 2, 4, 6, ...
                const span = Math.pow(2, ri);      // 1, 2, 4, 8, ...
                const isLast = ri === rounds.length - 1;

                labels.push({ label: round.label, gridCol: matchCol });

                for (let mi = 0; mi < round.matches.length; mi++) {
                    gridMatches.push({
                        match: round.matches[mi],
                        gridRow: `${mi * span + 1} / span ${span}`,
                        gridCol: matchCol,
                        isFinal: isLast
                    });
                }

                // Connectors: one per pair of matches (not for last round)
                if (!isLast) {
                    const connSpan = span * 2;
                    const pairCount = Math.floor(round.matches.length / 2);
                    for (let pi = 0; pi < pairCount; pi++) {
                        gridConnectors.push({
                            gridRow: `${pi * connSpan + 1} / span ${connSpan}`,
                            gridCol: connCol
                        });
                    }
                }
            }

            // Grid template strings
            const colParts: string[] = [];
            for (let r = 0; r < rounds.length; r++) {
                colParts.push('260px');
                if (r < rounds.length - 1) colParts.push('40px');
            }

            return {
                agegroupName: div.agegroupName,
                divName: div.divName,
                headerLabel,
                champion: div.champion ?? null,
                matches: gridMatches,
                connectors: gridConnectors,
                labels,
                gridCols: colParts.join(' '),
                gridRows: `repeat(${totalRows}, 80px)`
            };
        });
    });

    // ── Match click ──

    onMatchClick(match: BracketMatchDto): void {
        if (!this.canScore()) return;
        this.editBracketScore.emit({
            gid: match.gid,
            t1Name: match.t1Name,
            t2Name: match.t2Name,
            t1Score: match.t1Score ?? null,
            t2Score: match.t2Score ?? null
        });
    }

    // ── Zoom (shared across all brackets) ──

    zoomIn(): void {
        this.scale.update(s => Math.min(2.0, s + 0.15));
    }

    zoomOut(): void {
        this.scale.update(s => Math.max(0.3, s - 0.15));
    }

    resetZoom(): void {
        this.scale.set(1);
        this.translateX.set(0);
        this.translateY.set(0);
    }

    onWheel(event: WheelEvent): void {
        event.preventDefault();
        const delta = event.deltaY > 0 ? -0.08 : 0.08;
        this.scale.update(s => Math.min(2.0, Math.max(0.3, s + delta)));
    }

    // ── Pan (shared across all brackets, using pointer events for touch + mouse) ──

    onPanStart(event: PointerEvent): void {
        if (event.button !== 0) return;
        this.isPanning.set(true);
        this.panStartX = event.clientX;
        this.panStartY = event.clientY;
        this.panOriginX = this.translateX();
        this.panOriginY = this.translateY();
        (event.target as HTMLElement).setPointerCapture?.(event.pointerId);
    }

    onPanMove(event: PointerEvent): void {
        if (!this.isPanning()) return;
        const s = this.scale();
        this.translateX.set(this.panOriginX + (event.clientX - this.panStartX) / s);
        this.translateY.set(this.panOriginY + (event.clientY - this.panStartY) / s);
    }

    onPanEnd(): void {
        this.isPanning.set(false);
    }
}
