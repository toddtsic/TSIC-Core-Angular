import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import type { TeamResultDto, TeamResultsResponse } from '@core/api';

/**
 * Team schedule fly-in — opened from a team's record pill (games/brackets) or
 * name (standings); reopens recursively via opponent taps.
 *
 * Visual language ported from TSIC-Events-2025 feature/visual-refresh
 * (teamschedule-summary): the games split into Upcoming/Results ×
 * Round-Robin/Championship sections; played games render as a fixed-column
 * "results ledger" whose verdict is a glyph — gold trophy = won, red
 * thumbs-down = lost, blank = tie. Black-tie otherwise: no green/red badges,
 * no row tints (both retired here), scores plain bold.
 *
 * Shell is the canonical .detail-panel fly-in (src/styles/_flyin.scss), not a
 * centered dialog — same surface as the search/registrations/teams detail
 * panels, full-bleed under the mobile header below 768px.
 */
interface ResultGroup {
    key: string;
    label: string;
    played: boolean;
    games: TeamResultDto[];
}

@Component({
    selector: 'app-team-results-modal',
    standalone: true,
    imports: [DatePipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (visible()) {
            <div class="detail-backdrop" (click)="close.emit()"></div>
        }
        <div class="detail-panel" [class.open]="visible()" [style.--ag-tint]="agColor()">
            <div class="panel-header">
                <div class="header-top-row">
                    <div class="title-stack">
                        <!-- Age-group identity — dot + label, the same language as the
                             schedule rows (a FILLED color chip was already rejected in
                             games-tab: the stored palette is dominated by near-white,
                             which floods a chip into invisibility; the dot carries the
                             color where contrast is not load-bearing). -->
                        @if (response()?.agegroupName) {
                            <span class="ag-chip">
                                <span class="ag-dot" aria-hidden="true"
                                      [class.ag-dot--empty]="!agColor()"></span>
                                <span class="ag-label">{{ response()?.agegroupName }}</span>
                            </span>
                        }
                        <h3 class="panel-title">{{ response()?.teamName || 'Team Schedule' }}</h3>
                        <div class="title-sub">
                            @if (response()?.clubName) {
                                <span class="club-name">{{ response()?.clubName }}</span>
                            }
                            @if (headerRecord()) {
                                <span class="record-chip"
                                      [attr.aria-label]="'Record ' + headerRecord()">{{ headerRecord() }}</span>
                            }
                        </div>
                    </div>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close">&times;</button>
                </div>
            </div>

            <div class="panel-body">
                @if (loading()) {
                    <div class="body-state">
                        <div class="spinner-border spinner-border-sm text-primary" role="status">
                            <span class="visually-hidden">Loading...</span>
                        </div>
                        <span class="ms-2">Loading schedule...</span>
                    </div>
                } @else if (groups().length === 0) {
                    <div class="body-state">
                        <i class="bi bi-calendar-x me-2" aria-hidden="true"></i>No games scheduled yet.
                    </div>
                } @else {
                    @for (group of groups(); track group.key) {
                        <div class="section-header">{{ group.label }}</div>

                        @for (g of group.games; track g.gid) {
                            <div class="result-card" [class.card-dimmed]="g.gStatusCode === 5">
                                @if (group.played) {
                                    <!-- Results ledger row. Fixed rem columns so the glyph,
                                         score, and opponent align down the whole section —
                                         the ledger read. Glyph column is always reserved:
                                         a tie renders an empty slot, not a shifted row. -->
                                    <div class="line-primary">
                                        <span class="glyph-slot" aria-hidden="true">
                                            @if (g.outcome === 'W') {
                                                <i class="bi bi-trophy-fill winner-glyph"></i>
                                            } @else if (g.outcome === 'L') {
                                                <i class="bi bi-hand-thumbs-down-fill loss-glyph"></i>
                                            }
                                        </span>
                                        <span class="score-text">{{ g.teamScore }}&ndash;{{ g.opponentScore }}</span>
                                        <span class="vs-text">vs</span>
                                        @if (g.opponentTeamId) {
                                            <button type="button" class="team-name-link"
                                                    [attr.aria-label]="'View ' + g.opponentName + ' schedule'"
                                                    (click)="viewOpponent.emit(g.opponentTeamId!)">{{ g.opponentName }}</button>
                                        } @else {
                                            <span class="opp-name">{{ g.opponentName }}</span>
                                        }
                                        @if (g.opponentRecord) {
                                            <span class="record-chip">{{ g.opponentRecord }}</span>
                                        }
                                    </div>
                                } @else {
                                    <!-- Upcoming: when + opponent (no verdict, no score) -->
                                    <div class="line-upcoming">
                                        <span class="when-text">{{ g.gDate | date:'EEE M/d' }} <span class="when-time">{{ g.gDate | date:'h:mm a' }}</span></span>
                                        <span class="vs-text">vs</span>
                                        @if (g.opponentTeamId) {
                                            <button type="button" class="team-name-link"
                                                    [attr.aria-label]="'View ' + g.opponentName + ' schedule'"
                                                    (click)="viewOpponent.emit(g.opponentTeamId!)">{{ g.opponentName }}</button>
                                        } @else {
                                            <span class="opp-name">{{ g.opponentName }}</span>
                                        }
                                        @if (g.opponentRecord) {
                                            <span class="record-chip">{{ g.opponentRecord }}</span>
                                        }
                                    </div>
                                }

                                <!-- Meta line: date (results only — upcoming leads with it),
                                     venue, round label, exceptional status. -->
                                <div class="line-meta">
                                    @if (group.played) {
                                        <span>{{ g.gDate | date:'EEE M/d' }}</span>
                                    }
                                    @if (g.location) {
                                        <span class="meta-sep" aria-hidden="true">&middot;</span>
                                        @if (mapsUrl(g)) {
                                            <a [href]="mapsUrl(g)" target="_blank" rel="noopener" class="loc-link">{{ g.location }}</a>
                                        } @else {
                                            <span>{{ g.location }}</span>
                                        }
                                    }
                                    @if (roundLabel(g)) {
                                        <span class="meta-sep" aria-hidden="true">&middot;</span>
                                        <span class="round-label">{{ roundLabel(g) }}</span>
                                    }
                                    @if (showStatus(g)) {
                                        <span class="meta-sep" aria-hidden="true">&middot;</span>
                                        <span class="status-text">{{ g.gStatusText }}</span>
                                    }
                                </div>
                            </div>
                        }
                    }
                }
            </div>
        </div>
    `,
    styles: [`
        /* Shell classes (.detail-backdrop/.detail-panel/.panel-*) are global — _flyin.scss.
           Everything below is the interior. */

        .title-stack {
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
            min-width: 0;
        }

        /* Same derived ink as the schedule rows: hue/chroma pass through untouched,
           lightness clamped just enough to stay visible (near-white directors' colors
           are the common case). Identical recipe to games-tab so the dot here and the
           rail there can never disagree about an age group's color. */
        .detail-panel {
            --ag-ink-lo: 0.35;
            --ag-ink-hi: 0.75;
            --ag-ink: oklch(from var(--ag-tint) clamp(var(--ag-ink-lo), l, var(--ag-ink-hi)) c h);
        }

        :host-context([data-bs-theme='dark']) .detail-panel {
            --ag-ink-lo: 0.45;
            --ag-ink-hi: 0.85;
        }

        .ag-chip {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
        }

        .ag-dot {
            display: inline-block;
            box-sizing: border-box;
            width: 10px;
            height: 10px;
            flex-shrink: 0;
            border-radius: 50%;
            border: 1px solid var(--bs-border-color);
            background: var(--ag-ink);
        }

        .ag-dot--empty {
            background: transparent;
            border-style: dashed;
        }

        .ag-label {
            font-size: var(--font-size-xs);
            font-weight: 600;
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            letter-spacing: 0.03em;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .title-sub {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
        }

        .club-name {
            font-size: var(--font-size-sm);
            color: var(--bs-secondary-color);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        /* Neutral pill — same object as the games-tab record button, minus the
           affordance (here it is a label, not a control). Never green/red: the
           record is a season stat, not a verdict. */
        .record-chip {
            flex-shrink: 0;
            padding: 0 var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            background: transparent;
            font-size: var(--font-size-xs);
            font-variant-numeric: tabular-nums;
            font-weight: 400;
            line-height: 1.5;
            color: var(--score-muted);
            white-space: nowrap;
        }

        .body-state {
            display: flex;
            align-items: center;
            justify-content: center;
            padding: var(--space-8) var(--space-4);
            font-size: var(--font-size-sm);
            color: var(--bs-secondary-color);
        }

        /* Section divider — quiet uppercase, room above between sections. */
        .section-header {
            font-size: var(--font-size-xs);
            font-weight: 600;
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            letter-spacing: 0.06em;
            text-align: center;
            margin: var(--space-4) 0 var(--space-2);
        }

        .section-header:first-child { margin-top: 0; }

        .result-card {
            background: var(--bs-card-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            padding: var(--space-2) var(--space-3);
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
        }

        .result-card + .result-card { margin-top: var(--space-2); }

        .card-dimmed { opacity: 0.5; }

        /* ── Results ledger ──
           Fixed rem columns (not auto) so the glyph, score, and opponent line up
           across SEPARATE cards — the whole section reads as one ledger. The glyph
           column reserves its width even when empty (tie), so no row ever shifts. */
        .line-primary {
            display: grid;
            grid-template-columns: 1.7rem 3.2rem 1.5rem minmax(0, 1fr) auto;
            align-items: baseline;
            column-gap: var(--space-1);
        }

        .glyph-slot {
            place-self: center center;
            display: inline-flex;
            align-items: center;
            justify-content: center;
        }

        /* Verdict glyphs — the ONE place the loss glyph exists. On the main schedule
           both teams are present, so the trophy alone tells the story; here a single
           team's history is being read, and "no trophy" would be ambiguous between
           loss and tie. Gold = won (--winner-gold, palette-invariant), red = lost.
           Two-class selectors on purpose: they must out-specify any blanket rule. */
        .glyph-slot .winner-glyph {
            font-size: var(--font-size-lg);
            color: var(--winner-gold);
        }

        .glyph-slot .loss-glyph {
            font-size: var(--font-size-lg);
            color: var(--bs-danger);
        }

        .score-text {
            justify-self: center;
            font-size: var(--font-size-sm);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            color: var(--score-strong);
            white-space: nowrap;
        }

        .vs-text {
            justify-self: start;
            font-size: var(--font-size-xs);
            color: var(--score-muted);
        }

        .team-name-link {
            appearance: none;
            border: none;
            background: transparent;
            padding: 0;
            font: inherit;
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
            text-align: left;
            min-width: 0;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            cursor: pointer;
            text-decoration: underline dotted;
            text-decoration-color: var(--bs-border-color);
            text-underline-offset: 3px;
        }

        .team-name-link:hover {
            color: var(--bs-primary);
            text-decoration-color: currentColor;
        }

        .team-name-link:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
            border-radius: var(--radius-sm);
        }

        .opp-name {
            font-size: var(--font-size-sm);
            min-width: 0;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .line-primary .record-chip { place-self: center end; }

        /* ── Upcoming row — no verdict, no score; the date leads. ── */
        .line-upcoming {
            display: flex;
            align-items: baseline;
            gap: var(--space-2);
            min-width: 0;
        }

        .when-text {
            font-size: var(--font-size-sm);
            font-weight: 600;
            color: var(--bs-body-color);
            white-space: nowrap;
        }

        .when-time {
            font-weight: 400;
            color: var(--bs-secondary-color);
        }

        .line-upcoming .record-chip { margin-left: auto; }

        /* ── Meta line ── */
        .line-meta {
            display: flex;
            align-items: baseline;
            flex-wrap: wrap;
            gap: var(--space-1);
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
        }

        .meta-sep { color: var(--bs-border-color); }

        .loc-link {
            color: var(--bs-primary);
            text-decoration: none;
        }

        .loc-link:hover { text-decoration: underline; }

        .loc-link:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
            border-radius: var(--radius-sm);
        }

        .round-label { font-style: italic; }

        /* Status words are exceptional info (Rescheduled/Forfeit/Cancelled) — strong
           ink, no color, per the black-tie status treatment on the games tab. */
        .status-text {
            color: var(--score-strong);
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.03em;
        }
    `]
})
export class TeamResultsModalComponent {
    /** Full team-results payload; null while loading a fresh team. */
    response = input<TeamResultsResponse | null>(null);
    /** Raw director hex for the team's age group (Leagues.agegroups.color); null = unset. */
    agColor = input<string | null>(null);
    visible = input<boolean>(false);
    loading = input<boolean>(false);

    readonly close = output<void>();
    readonly viewOpponent = output<string>();

    /** Played = a real result exists: both scores present and the game wasn't
     *  cancelled. Everything else (unscored, cancelled) lists as upcoming, where
     *  the status word explains itself. */
    private isPlayed(g: TeamResultDto): boolean {
        return g.teamScore != null && g.opponentScore != null && g.gStatusCode !== 5;
    }

    /** gameType is the backend's friendly label ("Pool Play", "Semifinals", ...).
     *  Consolation is NOT a bracket game (GameRoundTypes doctrine) — it rides the
     *  Round-Robin section carrying its "Consolation" label in the meta line. */
    private isChampionship(g: TeamResultDto): boolean {
        return g.gameType !== 'Pool Play' && g.gameType !== 'Consolation';
    }

    readonly groups = computed<ResultGroup[]>(() => {
        const games = this.response()?.games ?? [];
        const defs = [
            { key: 'up-rr', label: 'Upcoming · Round-Robin', played: false, champ: false },
            { key: 'up-ch', label: 'Upcoming · Championship', played: false, champ: true },
            { key: 'res-rr', label: 'Results · Round-Robin', played: true, champ: false },
            { key: 'res-ch', label: 'Results · Championship', played: true, champ: true }
        ];
        return defs
            .map(d => ({
                key: d.key,
                label: d.label,
                played: d.played,
                games: games
                    .filter(g => this.isPlayed(g) === d.played && this.isChampionship(g) === d.champ)
                    .sort((a, b) => a.gDate.localeCompare(b.gDate))
            }))
            .filter(g => g.games.length > 0);
    });

    /** Header record chip — suppressed for a 0-0-0 (nothing played yet). */
    readonly headerRecord = computed(() => {
        const r = this.response()?.teamRecord;
        return r && r !== '0-0-0' ? r : null;
    });

    /** Round label for the meta line; Pool Play is the section's default and says nothing. */
    roundLabel(g: TeamResultDto): string | null {
        return g.gameType === 'Pool Play' ? null : g.gameType;
    }

    /** Exceptional statuses only — Scheduled(1) is the quiet default and Final(6)
     *  is implied by the score sitting right there. */
    showStatus(g: TeamResultDto): boolean {
        return g.gStatusCode != null && g.gStatusCode !== 1 && g.gStatusCode !== 6 && !!g.gStatusText;
    }

    mapsUrl(g: TeamResultDto): string | null {
        if (g.fAddress) {
            return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(g.fAddress)}`;
        }
        if (g.latitude && g.longitude) {
            return `https://www.google.com/maps/search/?api=1&query=${g.latitude},${g.longitude}`;
        }
        return null;
    }
}
