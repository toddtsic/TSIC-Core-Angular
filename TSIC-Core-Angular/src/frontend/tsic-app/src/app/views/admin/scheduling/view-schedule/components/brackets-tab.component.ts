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

interface ParsedBracket {
    agegroupName: string;
    divName: string;
    champion: string | null;
    rounds: { roundType: string; label: string; matches: BracketMatchDto[] }[];
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
            @for (bracket of parsed(); track bracket.divName) {
                <div class="bracket-division">
                    <div class="division-header">
                        <span class="division-title">{{ bracket.agegroupName }} {{ bracket.divName }}</span>
                        @if (bracket.champion) {
                            <span class="champion-badge">
                                <i class="bi bi-trophy-fill"></i>
                                {{ bracket.champion }}
                            </span>
                        }
                    </div>

                    <div class="bracket-viewport"
                         (wheel)="onWheel($event)"
                         (mousedown)="onPanStart($event)"
                         (mousemove)="onPanMove($event)"
                         (mouseup)="onPanEnd()"
                         (mouseleave)="onPanEnd()"
                         [style.cursor]="isPanning() ? 'grabbing' : 'grab'">

                        <!-- Zoom controls -->
                        <div class="zoom-controls">
                            <button class="zoom-btn" (click)="zoomIn()" title="Zoom in"
                                    (mousedown)="$event.stopPropagation()">
                                <i class="bi bi-plus-lg"></i>
                            </button>
                            <button class="zoom-btn" (click)="zoomOut()" title="Zoom out"
                                    (mousedown)="$event.stopPropagation()">
                                <i class="bi bi-dash-lg"></i>
                            </button>
                            <button class="zoom-btn" (click)="resetZoom()" title="Reset zoom"
                                    (mousedown)="$event.stopPropagation()">
                                <i class="bi bi-arrows-angle-contract"></i>
                            </button>
                            <span class="zoom-label">{{ Math.round(scale() * 100) }}%</span>
                        </div>

                        <div class="bracket-canvas"
                             [style.transform]="'scale(' + scale() + ') translate(' + translateX() + 'px, ' + translateY() + 'px)'">
                            @for (round of bracket.rounds; track round.roundType; let ri = $index) {
                                <div class="bracket-round"
                                     [style.--round-index]="ri">
                                    <div class="round-label">{{ round.label }}</div>
                                    <div class="round-matches"
                                         [style.gap]="matchGap(ri)">
                                        @for (match of round.matches; track match.gid) {
                                            <div class="bracket-match-wrapper">
                                                <div class="bracket-match"
                                                     [class.clickable]="canScore()"
                                                     (click)="onMatchClick(match)">
                                                    <div class="match-team"
                                                         [class.winner]="match.t1Css === 'winner'"
                                                         [class.loser]="match.t1Css === 'loser'">
                                                        <span class="team-name">{{ match.t1Name }}</span>
                                                        <span class="team-score">{{ match.t1Score ?? '' }}</span>
                                                    </div>
                                                    <div class="match-divider"></div>
                                                    <div class="match-team"
                                                         [class.winner]="match.t2Css === 'winner'"
                                                         [class.loser]="match.t2Css === 'loser'">
                                                        <span class="team-name">{{ match.t2Name }}</span>
                                                        <span class="team-score">{{ match.t2Score ?? '' }}</span>
                                                    </div>
                                                    @if (match.locationTime) {
                                                        <div class="match-info">{{ match.locationTime }}</div>
                                                    }
                                                </div>
                                                <!-- Connector line to next round -->
                                                @if (ri < bracket.rounds.length - 1) {
                                                    <div class="connector-line"></div>
                                                }
                                            </div>
                                        }
                                    </div>
                                </div>
                            }
                        </div>
                    </div>
                </div>
            }
        }
    `,
    styles: [`
        :host {
            display: block;
        }

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

        /* ── Division wrapper ── */

        .bracket-division {
            margin-bottom: var(--space-4);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            overflow: hidden;
        }

        .division-header {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            background: var(--bs-secondary-bg);
            padding: var(--space-2) var(--space-3);
            border-bottom: 1px solid var(--bs-border-color);
        }

        .division-title {
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

        /* ── Viewport (pan & zoom container) ── */

        .bracket-viewport {
            position: relative;
            overflow: hidden;
            background: var(--bs-body-bg);
            min-height: 300px;
            user-select: none;
        }

        .zoom-controls {
            position: absolute;
            top: var(--space-2);
            right: var(--space-2);
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

        /* ── Canvas (transformed layer) ── */

        .bracket-canvas {
            display: flex;
            align-items: flex-start;
            gap: 0;
            padding: var(--space-6);
            transform-origin: 0 0;
            will-change: transform;
        }

        /* ── Round column ── */

        .bracket-round {
            display: flex;
            flex-direction: column;
            align-items: center;
            min-width: 280px;
        }

        .round-label {
            font-size: var(--font-size-xs);
            font-weight: 600;
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            letter-spacing: 0.05em;
            padding-bottom: var(--space-3);
            white-space: nowrap;
        }

        .round-matches {
            display: flex;
            flex-direction: column;
            justify-content: space-around;
            flex: 1;
            min-height: 100%;
        }

        /* ── Match wrapper (card + connector) ── */

        .bracket-match-wrapper {
            display: flex;
            align-items: center;
            position: relative;
        }

        /* ── Match card ── */

        .bracket-match {
            background: var(--bs-body-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            min-width: 240px;
            max-width: 280px;
            overflow: hidden;
            box-shadow: var(--shadow-xs);
            transition: box-shadow 0.15s, border-color 0.15s;
        }

        .bracket-match.clickable {
            cursor: pointer;
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
            font-size: var(--font-size-2xs);
            color: var(--bs-secondary-color);
            font-style: italic;
            padding: var(--space-1) var(--space-2);
            border-top: 1px solid var(--bs-border-color);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        /* ── Connector lines ── */

        .connector-line {
            width: 40px;
            height: 2px;
            background: var(--bs-border-color);
            flex-shrink: 0;
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

    // Expose Math for template
    readonly Math = Math;

    // ── Zoom & Pan state ──
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
        if (!data || data.length === 0) return [];

        return data.map(div => {
            // Group matches by roundType
            const roundMap = new Map<string, BracketMatchDto[]>();
            for (const match of div.matches) {
                const rt = match.roundType?.toUpperCase() ?? '';
                if (!roundMap.has(rt)) roundMap.set(rt, []);
                roundMap.get(rt)!.push(match);
            }

            // Build ordered rounds array (only rounds that have matches)
            const rounds: ParsedBracket['rounds'] = [];
            for (const code of ROUND_ORDER) {
                const matches = roundMap.get(code);
                if (matches && matches.length > 0) {
                    rounds.push({
                        roundType: code,
                        label: ROUND_LABELS[code] ?? code,
                        matches: [...matches].sort((a, b) => a.gid - b.gid)
                    });
                }
            }

            // Include any unrecognized round types at the end
            for (const [code, matches] of roundMap.entries()) {
                if (!ROUND_ORDER.includes(code) && matches.length > 0) {
                    rounds.push({
                        roundType: code,
                        label: code,
                        matches: [...matches].sort((a, b) => a.gid - b.gid)
                    });
                }
            }

            return {
                agegroupName: div.agegroupName,
                divName: div.divName,
                champion: div.champion ?? null,
                rounds
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

    // ── Zoom ──

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

    // ── Pan ──

    onPanStart(event: MouseEvent): void {
        // Ignore right-click
        if (event.button !== 0) return;
        this.isPanning.set(true);
        this.panStartX = event.clientX;
        this.panStartY = event.clientY;
        this.panOriginX = this.translateX();
        this.panOriginY = this.translateY();
    }

    onPanMove(event: MouseEvent): void {
        if (!this.isPanning()) return;
        const currentScale = this.scale();
        const dx = (event.clientX - this.panStartX) / currentScale;
        const dy = (event.clientY - this.panStartY) / currentScale;
        this.translateX.set(this.panOriginX + dx);
        this.translateY.set(this.panOriginY + dy);
    }

    onPanEnd(): void {
        this.isPanning.set(false);
    }

    // ── Helpers ──

    /** Returns a CSS gap value that increases with round depth for visual bracket fanning. */
    matchGap(roundIndex: number): string {
        // First round: tight spacing; each subsequent round doubles the gap
        const base = 8;
        const gap = base * Math.pow(2, roundIndex);
        return `${gap}px`;
    }
}
