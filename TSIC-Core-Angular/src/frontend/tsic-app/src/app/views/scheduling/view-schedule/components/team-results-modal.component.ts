import {
    ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, OnChanges, output, signal,
    SimpleChanges
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { catchError, map, of, Subject, switchMap } from 'rxjs';
import type {
    DivisionBracketResponse, DivisionStandingsDto, PublicRosterPlayerDto, TeamResultDto, TeamResultsResponse
} from '@core/api';
import { ResizablePanelDirective } from '@shared-ui/directives/resizable-panel.directive';
import { AgeGroupPickerComponent, type AgePickerItem } from '../../shared/components/age-group-picker/age-group-picker.component';
import { BracketViewComponent } from './bracket-view.component';
import { ClubMenuComponent } from './club-menu.component';
import { TEAM_VIEWS_SOURCE } from '../services/team-views-source';
import { splitTeamName } from '../utils/team-name';

type TeamView = 'schedule' | 'standings' | 'bracket' | 'roster';

interface ViewTab { key: TeamView; label: string; icon: string; }

/** Standings for a team: its age group's pools (its own pool alone when the age group is unknown). */
interface TeamStandings {
    teamId: string;
    agegroupId: string | null;
    divisions: DivisionStandingsDto[];
    failed: boolean;
}

interface RosterRow {
    name: string;
    uniformNo: string | null;
    position: string | null;
    isStaff: boolean;
}

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
    imports: [DatePipe, NgTemplateOutlet, ResizablePanelDirective, ClubMenuComponent, AgeGroupPickerComponent, BracketViewComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (visible()) {
            <div class="detail-backdrop" (click)="close.emit()"></div>
        }
        <div class="detail-panel" [class.open]="visible()" [style.--ag-tint]="agColor()"
             appResizablePanel storageKey="teamResultsPanelWidth" panelSide="right">
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
                        <!-- Club over team — the same order as the games grid's two-line name.
                             "2031 Blue" alone names nothing (most clubs field one); the club is
                             the identifying part, so it is the heading and the team sits under
                             it in semibold body ink (the subject, not demoted to grey). No club
                             → the team is the heading and the sub-line holds only the record. -->
                        @let club = response()?.clubName;
                        @let subjectId = response()?.teamId;
                        <h3 class="panel-title">{{ club || response()?.teamName || 'Team Schedule' }}</h3>
                        <div class="title-sub">
                            @if (club && subjectId) {
                                <!-- Team picker: the club's other teams, this one marked; picking
                                     one reloads this panel, the same path as an opponent tap. -->
                                <span class="team-sub">
                                    <app-club-menu pill [club]="club" [label]="response()?.teamName ?? ''"
                                                   [teamId]="subjectId" (pick)="viewOpponent.emit($event)" />
                                </span>
                            } @else if (club) {
                                <span class="team-sub">{{ response()?.teamName }}</span>
                            }
                            @if (headerRecord()) {
                                <span class="record-chip"
                                      [attr.aria-label]="'Record ' + headerRecord()">{{ headerRecord() }}</span>
                            }
                        </div>
                    </div>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close">&times;</button>
                </div>
                <!-- The event's tabs, for this one team (TSIC-Events team view). Only the views
                     the team has: no pool play → no Standings, no bracket in its age group → no
                     Bracket, rosters kept private → no Roster. -->
                @if (views().length > 1) {
                    <div class="view-switch" role="tablist" aria-label="Team views">
                        @for (v of views(); track v.key) {
                            <button type="button" role="tab" class="view-option"
                                    [class.active]="activeView() === v.key"
                                    [attr.aria-selected]="activeView() === v.key"
                                    (click)="selectView(v.key)">
                                <i [class]="'bi ' + v.icon" aria-hidden="true"></i>{{ v.label }}
                            </button>
                        }
                    </div>
                }
            </div>

            <div class="panel-body">
                @switch (activeView()) {
                @case ('standings') {
                    <ng-container *ngTemplateOutlet="standingsTpl" />
                }
                @case ('bracket') {
                    <ng-container *ngTemplateOutlet="bracketTpl" />
                }
                @case ('roster') {
                    <ng-container *ngTemplateOutlet="rosterTpl" />
                }
                @default {
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
                                        <ng-container *ngTemplateOutlet="opponentTpl; context: { $implicit: g }" />
                                        @if (g.opponentRecord) {
                                            <span class="record-chip">{{ g.opponentRecord }}</span>
                                        }
                                    </div>
                                } @else {
                                    <!-- Upcoming: when + opponent (no verdict, no score) -->
                                    <div class="line-upcoming">
                                        <span class="when-text">{{ g.gDate | date:'EEE M/d' }} <span class="when-time">{{ g.gDate | date:'h:mm a' }}</span></span>
                                        <span class="vs-text">vs</span>
                                        <ng-container *ngTemplateOutlet="opponentTpl; context: { $implicit: g }" />
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
                }
                }
            </div>
        </div>

        <!-- Standings: one pool at a time, the team's own first. ‹ [pool ▾] › walks the age
             group's pools (all of them decide who advances). The team's row is bold. -->
        <ng-template #standingsTpl>
            @let st = teamStandings();
            @if (!st) {
                <ng-container *ngTemplateOutlet="loadingTpl; context: { $implicit: 'standings' }" />
            } @else if (st.failed) {
                <div class="body-state">Couldn't load the standings.</div>
            } @else if (shownPool(); as pool) {
                <div class="view-head">
                    @if (poolItems().length > 1) {
                        <app-age-group-picker compact stepper noun="pool"
                            [items]="poolItems()" [selectedId]="pool.divId"
                            (selectionChange)="selectedPool.set($event)" />
                    } @else {
                        <span class="view-head__name">{{ pool.divName }}</span>
                    }
                </div>
                <table class="pool-table">
                    <thead>
                        <tr>
                            <th class="col-rank">#</th>
                            <th class="col-team">Team</th>
                            <th class="col-num">GP</th>
                            <th class="col-num">W</th>
                            <th class="col-num">L</th>
                            <th class="col-num">T</th>
                            <th class="col-num">GF</th>
                            <th class="col-num">GA</th>
                            <th class="col-num">GD</th>
                        </tr>
                    </thead>
                    <tbody>
                        @for (t of pool.teams; track t.teamId; let i = $index) {
                            <tr [class.is-self]="t.teamId === teamId()">
                                <td class="col-rank">{{ t.rankOrder ?? (i + 1) }}</td>
                                <td class="col-team">
                                    @if (t.teamId === teamId()) {
                                        {{ t.teamName }}
                                    } @else {
                                        <button type="button" class="team-link"
                                                (click)="switchTeam(t.teamId)">{{ t.teamName }}</button>
                                    }
                                </td>
                                <td class="col-num">{{ t.games }}</td>
                                <td class="col-num">{{ t.wins }}</td>
                                <td class="col-num">{{ t.losses }}</td>
                                <td class="col-num">{{ t.ties }}</td>
                                <td class="col-num">{{ t.goalsFor }}</td>
                                <td class="col-num">{{ t.goalsAgainst }}</td>
                                <td class="col-num">{{ goalDiff(t.goalDiffMax9) }}</td>
                            </tr>
                        }
                    </tbody>
                </table>
            } @else {
                <div class="body-state">No standings yet.</div>
            }
        </ng-template>

        <!-- Bracket: the age group's, opened on the team's own (bold in the ladder). A picker
             only when the age group has more than one. Read-only here: scores are entered
             from the Brackets tab. -->
        <ng-template #bracketTpl>
            @let list = teamBrackets();
            @if (!list) {
                <ng-container *ngTemplateOutlet="loadingTpl; context: { $implicit: 'bracket' }" />
            } @else if (bracketsFailed()) {
                <div class="body-state">Couldn't load the bracket.</div>
            } @else if (shownBracket(); as b) {
                @if (bracketItems().length > 1) {
                    <div class="view-head">
                        <app-age-group-picker compact stepper noun="bracket"
                            [items]="bracketItems()" [selectedId]="shownBracketId()"
                            (selectionChange)="selectedBracket.set($event)" />
                    </div>
                }
                <app-bracket-view [bracket]="b" [agColor]="agColor()" [followedTeamIds]="selfIds()"
                                  [fieldLinks]="false" (viewTeamResults)="switchTeam($event)" />
            } @else {
                <div class="body-state">No bracket yet.</div>
            }
        </ng-template>

        <!-- Roster: players by number, staff after. -->
        <ng-template #rosterTpl>
            @let rows = rosterRows();
            @if (!rows) {
                <ng-container *ngTemplateOutlet="loadingTpl; context: { $implicit: 'roster' }" />
            } @else if (rosterFailed()) {
                <div class="body-state">Couldn't load the roster.</div>
            } @else if (rows.length === 0) {
                <div class="body-state">No roster posted yet.</div>
            } @else {
                <table class="roster-table">
                    <thead>
                        <tr>
                            <th class="col-jersey">#</th>
                            <th>Name</th>
                            <th>Position</th>
                        </tr>
                    </thead>
                    <tbody>
                        @for (r of rows; track $index) {
                            <tr [class.staff-row]="r.isStaff">
                                <td class="col-jersey">{{ r.isStaff ? '' : (r.uniformNo || '—') }}</td>
                                <td>
                                    {{ r.name }}
                                    @if (r.isStaff) { <span class="staff-tag">Staff</span> }
                                </td>
                                <td class="col-pos">{{ r.position || '' }}</td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        </ng-template>

        <ng-template #loadingTpl let-what>
            <div class="body-state">
                <div class="spinner-border spinner-border-sm text-primary" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
                <span class="ms-2">Loading {{ what }}...</span>
            </div>
        </ng-template>

        <!-- Opponent name, shared by Results and Upcoming rows. Club (semibold, emphasis ink)
             over the team as a picker pill — the opponent's club's teams, the opponent marked,
             so opening it or a sibling is one pick. No club to split off → the pill alone. -->
        <ng-template #opponentTpl let-g>
            @let op = splitName(g.opponentName);
            <span class="opp-block">
                @if (op.club) {
                    <span class="opp-club">{{ op.club }}</span>
                }
                @if (g.opponentTeamId) {
                    <span class="opp-name" [class.opp-team]="!!op.club">
                        <app-club-menu pill [club]="op.club || op.team" [label]="op.team"
                                       [teamId]="g.opponentTeamId" (pick)="viewOpponent.emit($event)" />
                    </span>
                } @else {
                    <span class="opp-name" [class.opp-team]="!!op.club">{{ op.team }}</span>
                }
            </span>
        </ng-template>
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

        /* No overflow clipping: it would cut off the team pill's focus ring. A long name
           wraps inside the pill instead. */
        .team-sub {
            min-width: 0;
            font-size: var(--font-size-base);
            font-weight: 600;
            color: var(--bs-body-color);
            overflow-wrap: anywhere;
        }

        /* Same object as the games-tab record button, and it has to STAY the same object —
           a W-L-T reads identically wherever it appears or it stops being one token. Both
           carry the pill border; if one ever loses it, the other is wrong too, not right.
           The only difference is the affordance: there it is a control, here a label.
           Never green/red: the record is a season stat, not a verdict. */
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
            /* Horizontally centered in its reserved column, but baseline-aligned like
               score/vs/name so it stays on the name's FIRST line. place-self: center
               would vertically centre it in the row, and a wrapped (2-line) opponent name
               grows the row — dropping the glyph to the middle. Baseline pins it up top. */
            justify-self: center;
            align-self: baseline;
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

        /* Two-line opponent (club over team), as in the games grid. A flex column in the
           ledger's name track: its baseline is the FIRST line, so the club lines up with the
           score and "vs", and the team sits under it. */
        .opp-block {
            display: flex;
            flex-direction: column;
            align-items: flex-start;
            min-width: 0;
        }
        .opp-club {
            font-size: var(--font-size-sm);
            font-weight: 600;
            color: var(--bs-emphasis-color);
            overflow-wrap: anywhere;
        }
        /* Team line under a club: secondary ink, like the grid. The pill inherits it; its
           own hover/focus promote to primary. */
        .opp-name.opp-team {
            color: var(--bs-secondary-color);
        }

        /* Wraps within the minmax(0, 1fr) column instead of truncating; the grid baseline-
           aligns glyph/score/vs to the FIRST line, so a wrap grows the row downward. */
        .opp-name {
            font-size: var(--font-size-sm);
            min-width: 0;
            max-width: 100%;
            overflow-wrap: anywhere;
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

        /* ── View switch (segmented) ── */
        .view-switch {
            display: flex;
            margin-top: var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            overflow: hidden;
        }

        .view-option {
            flex: 1;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: var(--space-1);
            min-width: 0;
            padding: var(--space-1) var(--space-2);
            border: none;
            background: var(--bs-body-bg);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
            font-weight: 500;
            white-space: nowrap;
            cursor: pointer;
            transition: background-color 0.15s, color 0.15s;
        }
        .view-option + .view-option { border-left: 1px solid var(--bs-border-color); }
        .view-option:hover:not(.active) {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }
        /* Brand primary, as the standings RR/All toggle — gold is the trophy's alone. */
        .view-option.active {
            background: var(--bs-primary);
            color: var(--brand-primary-contrast, var(--bs-white));
            font-weight: 600;
        }
        .view-option:focus-visible,
        .team-link:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        @media (prefers-reduced-motion: reduce) {
            .view-option { transition: none !important; }
        }

        /* ── Standings / bracket heading row (the picker) ── */
        .view-head {
            display: flex;
            align-items: center;
            min-width: 0;
            margin-bottom: var(--space-3);
        }
        .view-head__name {
            font-size: var(--font-size-sm);
            font-weight: 600;
            color: var(--bs-body-color);
        }

        /* ── Pool table ── */
        .pool-table,
        .roster-table {
            width: 100%;
            border-collapse: collapse;
            font-size: var(--font-size-sm);
        }
        .pool-table th,
        .roster-table th {
            padding: var(--space-1);
            border-bottom: 2px solid var(--bs-border-color);
            background: var(--bs-tertiary-bg);
            font-weight: 600;
            white-space: nowrap;
            text-align: left;
        }
        .pool-table td,
        .roster-table td {
            padding: var(--space-1);
            border-bottom: 1px solid var(--bs-border-color);
            color: var(--bs-body-color);
        }
        .pool-table .col-rank { width: 1.75rem; text-align: center; }
        .pool-table .col-num {
            width: 2.1rem;
            text-align: right;
            font-variant-numeric: tabular-nums;
        }
        .pool-table .col-team { overflow-wrap: anywhere; }
        /* The panel's own team — bold, so it is found at a glance. */
        .pool-table tr.is-self td { font-weight: 700; }

        .team-link {
            padding: 0;
            border: none;
            background: none;
            color: inherit;
            font: inherit;
            text-align: left;
            cursor: pointer;
            text-decoration: underline dotted;
            text-decoration-color: color-mix(in srgb, currentColor 35%, transparent);
            text-underline-offset: 2px;
        }
        .team-link:hover {
            color: var(--bs-primary);
            text-decoration: underline solid;
        }

        /* ── Roster ── */
        .roster-table .col-jersey {
            width: 2.5rem;
            text-align: center;
            font-variant-numeric: tabular-nums;
        }
        .roster-table .col-pos { color: var(--bs-secondary-color); }
        .roster-table tr.staff-row td { color: var(--bs-secondary-color); }
        .staff-tag {
            margin-left: var(--space-1);
            padding: 0 var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            font-size: var(--font-size-xs);
        }

        @media (max-width: 767.98px) {
            .pool-table { font-size: var(--font-size-xs); }
            .pool-table .col-num { width: 1.6rem; }
        }

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
export class TeamResultsModalComponent implements OnChanges {
    /** Full team-results payload; null while loading a fresh team. */
    response = input<TeamResultsResponse | null>(null);
    /** Raw director hex for the team's age group (Leagues.agegroups.color); null = unset. */
    agColor = input<string | null>(null);
    visible = input<boolean>(false);
    loading = input<boolean>(false);

    readonly close = output<void>();
    readonly viewOpponent = output<string>();

    /** The page's data for the Standings / Bracket / Roster views; none → Schedule alone. */
    private readonly source = inject(TEAM_VIEWS_SOURCE, { optional: true });

    readonly teamId = computed(() => this.response()?.teamId ?? null);
    /** The panel's team, for the ladder's bold "followed" marker. */
    readonly selfIds = computed(() => { const id = this.teamId(); return id ? [id] : []; });

    // ── Views ──

    /** Loaded standings — kept until the next team's arrive; read through teamStandings. */
    private readonly standings = signal<TeamStandings | null>(null);
    /** The shown team's standings; null until they arrive. */
    readonly teamStandings = computed(() => {
        const s = this.standings();
        return s && s.teamId === this.teamId() ? s : null;
    });
    /** The pool the team plays in. */
    readonly teamPool = computed(() =>
        this.teamStandings()?.divisions.find(d => d.teams.some(t => t.teamId === this.teamId())) ?? null);

    /** Standings exist where the team has a pool table (until they load: where it plays pool play). */
    private readonly hasStandings = computed(() => {
        const s = this.teamStandings();
        return s ? !!this.teamPool() : !!this.response()?.games.some(g => g.gameType === 'Pool Play');
    });
    /**
     * A bracket is the age group's, not the pool's: one pool can feed it while another doesn't, so
     * the flag is any bracket game anywhere in the age group (agegroupHasBrackets). A bracket game
     * of the team's own settles it before the standings arrive.
     */
    private readonly hasBracket = computed(() =>
        !!this.response()?.games.some(g => this.isChampionship(g))
        || !!this.teamStandings()?.divisions.some(d => d.agegroupHasBrackets));
    private readonly rosterAllowed = computed(() => !!this.source && !this.source.rostersRestricted());

    private readonly availableViews = computed<ViewTab[]>(() => {
        const views: ViewTab[] = [{ key: 'schedule', label: 'Schedule', icon: 'bi-calendar3' }];
        if (!this.source) return views;
        if (this.hasStandings()) views.push({ key: 'standings', label: 'Standings', icon: 'bi-list-ol' });
        if (this.hasBracket()) views.push({ key: 'bracket', label: 'Bracket', icon: 'bi-diagram-3' });
        if (this.rosterAllowed()) views.push({ key: 'roster', label: 'Roster', icon: 'bi-people' });
        return views;
    });
    /** Held steady while the next team loads, so the switch does not collapse and come back. */
    readonly views = linkedSignal<{ loading: boolean; views: ViewTab[] }, ViewTab[]>({
        source: () => ({ loading: !this.response(), views: this.availableViews() }),
        computation: (s, prev) => s.loading && prev ? prev.value : s.views
    });

    /** The view asked for. */
    private readonly view = signal<TeamView>('schedule');
    /** The view shown: the one asked for while the team has it, else its schedule. */
    readonly activeView = computed<TeamView>(() =>
        this.views().some(v => v.key === this.view()) ? this.view() : 'schedule');

    // ── Standings: page through the age group's pools ──

    /** The pool being looked at: the team's own, until another is picked; a team switch goes back to its pool. */
    readonly selectedPool = linkedSignal<{ team: string | null; pool: string | null }, string | null>({
        source: () => ({ team: this.teamId(), pool: this.teamPool()?.divId ?? null }),
        computation: s => s.pool
    });
    readonly shownPool = computed(() => {
        const divisions = this.teamStandings()?.divisions ?? [];
        return divisions.find(d => d.divId === this.selectedPool()) ?? this.teamPool() ?? divisions[0] ?? null;
    });
    /** The age group's color marks the team's own pool, and only it. */
    readonly poolItems = computed<AgePickerItem[]>(() => {
        const home = this.teamPool();
        return (this.teamStandings()?.divisions ?? []).map(d => ({
            id: d.divId, label: d.divName, color: d === home ? this.agColor() : null
        }));
    });

    // ── Bracket ──

    private readonly brackets = signal<{ key: string; list: DivisionBracketResponse[]; failed: boolean } | null>(null);
    /** Brackets are the age group's: a teammate in the same age group reuses them. */
    private readonly bracketsKey = computed(() => {
        const id = this.teamId();
        if (!id) return null;
        const agId = this.teamStandings()?.agegroupId;
        return agId ? `ag:${agId}` : `team:${id}`;
    });
    readonly teamBrackets = computed(() => {
        const b = this.brackets();
        return b && b.key === this.bracketsKey() ? b.list : null;
    });
    readonly bracketsFailed = computed(() => !!this.teamBrackets() && !!this.brackets()?.failed);
    /** The bracket the team plays in; -1 before it is seeded into one. */
    private readonly homeBracket = computed(() => {
        const id = this.teamId();
        return (this.teamBrackets() ?? []).findIndex(b =>
            b.matches.some(m => m.t1Id === id || m.t2Id === id)
            || (b.consolationGames ?? []).some(c => c.t1Id === id || c.t2Id === id));
    });
    readonly selectedBracket = linkedSignal<{ team: string | null; home: number }, string>({
        source: () => ({ team: this.teamId(), home: this.homeBracket() }),
        computation: s => String(Math.max(s.home, 0))
    });
    readonly shownBracketId = computed(() =>
        (this.teamBrackets() ?? [])[Number(this.selectedBracket())] ? this.selectedBracket() : '0');
    readonly shownBracket = computed(() => (this.teamBrackets() ?? [])[Number(this.shownBracketId())] ?? null);
    readonly bracketItems = computed<AgePickerItem[]>(() => {
        const home = this.homeBracket();
        return (this.teamBrackets() ?? []).map((b, i) => ({
            id: String(i), label: b.divName || b.agegroupName, color: i === home ? this.agColor() : null
        }));
    });

    // ── Roster ──

    private readonly roster = signal<{ teamId: string; players: PublicRosterPlayerDto[]; failed: boolean } | null>(null);
    private readonly teamRoster = computed(() => {
        const r = this.roster();
        return r && r.teamId === this.teamId() ? r : null;
    });
    readonly rosterFailed = computed(() => !!this.teamRoster()?.failed);
    /** Players by number (then name), staff after them. Null until loaded. */
    readonly rosterRows = computed<RosterRow[] | null>(() => {
        const r = this.teamRoster();
        if (!r) return null;
        const rows = r.players.map(p => {
            const isStaff = p.roleLabel === 'Staff';
            return {
                name: isStaff ? p.displayName.replace(/^Staff:\s*/i, '') : p.displayName,
                uniformNo: p.uniformNo ?? null,
                position: p.position ?? null,
                isStaff
            };
        });
        const byNumber = (a: RosterRow, b: RosterRow) =>
            (parseInt(a.uniformNo ?? '', 10) || Infinity) - (parseInt(b.uniformNo ?? '', 10) || Infinity)
            || a.name.localeCompare(b.name);
        return [
            ...rows.filter(x => !x.isStaff).sort(byNumber),
            ...rows.filter(x => x.isStaff).sort((a, b) => a.name.localeCompare(b.name))
        ];
    });

    // ── Loading: switchMap drops a slower response for a team since switched away from ──

    private readonly standings$ = new Subject<string>();
    private readonly brackets$ = new Subject<{ key: string; teamId: string; agegroupId: string | null }>();
    private readonly roster$ = new Subject<string>();
    /** What was last asked for, so a re-render does not ask again while a load is in flight. */
    private requested = { standings: '', brackets: '', roster: '' };

    constructor() {
        const src = this.source;
        if (!src) return;

        this.standings$.pipe(
            switchMap(teamId => src.loadTeamStandings(teamId).pipe(
                switchMap(own => {
                    const agegroupId = own.divisions[0]?.agegroupId ?? null;
                    if (!agegroupId) return of<TeamStandings>({ teamId, agegroupId, divisions: own.divisions, failed: false });
                    // a teammate in the same age group keeps its standings
                    const held = this.standings();
                    if (held && !held.failed && held.agegroupId === agegroupId) return of<TeamStandings>({ ...held, teamId });
                    return src.loadAgegroupStandings(agegroupId).pipe(
                        map((all): TeamStandings => ({ teamId, agegroupId, divisions: all.divisions, failed: false })));
                }),
                catchError(() => of<TeamStandings>({ teamId, agegroupId: null, divisions: [], failed: true })))),
            takeUntilDestroyed()
        ).subscribe(s => {
            this.standings.set(s);
            // the age group is known now — the bracket can load
            this.loadActiveView();
        });

        this.brackets$.pipe(
            switchMap(({ key, teamId, agegroupId }) => src.loadBrackets(teamId, agegroupId).pipe(
                map(list => ({ key, list, failed: false })),
                catchError(() => of({ key, list: [] as DivisionBracketResponse[], failed: true })))),
            takeUntilDestroyed()
        ).subscribe(b => this.brackets.set(b));

        this.roster$.pipe(
            switchMap(teamId => src.loadTeamRoster(teamId).pipe(
                map(players => ({ teamId, players, failed: false })),
                catchError(() => of({ teamId, players: [] as PublicRosterPlayerDto[], failed: true })))),
            takeUntilDestroyed()
        ).subscribe(r => this.roster.set(r));
    }

    ngOnChanges(changes: SimpleChanges): void {
        // Closed: the next open starts on Schedule with fresh data (scores may have moved).
        if (changes['visible'] && !this.visible()) {
            this.view.set('schedule');
            this.standings.set(null);
            this.brackets.set(null);
            this.roster.set(null);
            this.requested = { standings: '', brackets: '', roster: '' };
            return;
        }
        if (changes['response']) {
            const teamId = this.teamId();
            if (this.source && teamId && this.requested.standings !== teamId) {
                this.requested.standings = teamId;
                this.standings$.next(teamId);
            }
            this.loadActiveView();
        }
    }

    selectView(view: TeamView): void {
        this.view.set(view);
        this.loadActiveView();
    }

    /** Another team, from a standings row or the ladder: the panel switches to it in place. */
    switchTeam(teamId: string): void {
        if (teamId && teamId !== this.teamId()) this.viewOpponent.emit(teamId);
    }

    /** Loads the shown view's data unless it is already for this team. Standings load with every team. */
    private loadActiveView(): void {
        const teamId = this.teamId();
        if (!this.source || !teamId) return;
        switch (this.activeView()) {
            case 'bracket': {
                // wait for the standings: they name the age group the bracket belongs to
                const st = this.teamStandings();
                const key = this.bracketsKey();
                if (!st || !key || this.requested.brackets === key) return;
                this.requested.brackets = key;
                this.brackets$.next({ key, teamId, agegroupId: st.agegroupId });
                return;
            }
            case 'roster':
                if (this.requested.roster === teamId) return;
                this.requested.roster = teamId;
                this.roster$.next(teamId);
                return;
        }
    }

    goalDiff(gd: number): string {
        return gd > 0 ? `+${gd}` : String(gd);
    }

    /** Opponent names split club / team — the games grid's rule (utils/team-name). */
    protected readonly splitName = splitTeamName;

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
