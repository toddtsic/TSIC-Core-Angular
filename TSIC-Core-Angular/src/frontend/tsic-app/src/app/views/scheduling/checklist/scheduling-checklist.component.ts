import { Component, ChangeDetectionStrategy, computed, inject, OnInit, signal } from '@angular/core';
import { RouterModule } from '@angular/router';
import { SchedulingChecklistService, type SchedulingChecklistDto } from './services/scheduling-checklist.service';

type StepState = 'done' | 'todo' | 'locked' | 'info';

interface StepRow {
    num: number;
    title: string;
    icon: string;
    state: StepState;
    reason: string | null;
    route: string;
    queryParams: Record<string, string> | null;
    linkLabel: string;
}

interface ToolRow {
    title: string;
    icon: string;
    route: string;
    /** Standalone tools (outside the scheduling shell) get ?from=scheduling so they can render a back chip. */
    queryParams: Record<string, string> | null;
}

interface StatChip {
    icon: string;
    value: string;
    label: string;
}

/**
 * Scheduling Checklist — the single front door for scheduling. Ordered steps with live
 * readiness + a deep link per step; operational tools unlock once games exist.
 */
@Component({
    selector: 'app-scheduling-checklist',
    standalone: true,
    imports: [RouterModule],
    templateUrl: './scheduling-checklist.component.html',
    styleUrl: './scheduling-checklist.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class SchedulingChecklistComponent implements OnInit {
    private readonly svc = inject(SchedulingChecklistService);

    readonly checklist = signal<SchedulingChecklistDto | null>(null);
    readonly isLoading = signal(false);
    readonly hasError = signal(false);

    readonly gameCount = computed(() => this.checklist()?.gameCount ?? 0);
    readonly hasGames = computed(() => this.gameCount() > 0);

    // Post-build stats band. Pre-build, the checklist itself is the dashboard — readiness is
    // the only stat that matters — so the band only exists once games are on the books. The
    // games count stays in the title's badge-count chip; these chips add what it can't say.
    readonly statChips = computed<StatChip[]>(() => {
        const st = this.checklist()?.scheduleStats;
        if (!st || st.gameCount === 0) return [];

        const chips: StatChip[] = [];
        const span = this.formatDateSpan(st.firstGameDate, st.lastGameDate);
        if (span) {
            chips.push({
                icon: 'bi-calendar3',
                value: span,
                // "play days" not "days": weekend-only events have date-span gaps, and the
                // distinct-day count is the honest number next to a first–last range.
                label: st.playDateCount > 1 ? `${st.playDateCount} play days` : ''
            });
        }
        chips.push({
            icon: 'bi-geo-alt',
            value: `${st.fieldsInUse}`,
            label: st.fieldsInUse === 1 ? 'field' : 'fields'
        });
        chips.push({
            icon: 'bi-diagram-3',
            value: `${st.divisionsScheduled}`,
            label: st.divisionsScheduled === 1 ? 'division scheduled' : 'divisions scheduled'
        });
        return chips;
    });

    readonly steps = computed<StepRow[]>(() => {
        const c = this.checklist();
        if (!c) return [];

        // Step ZERO by design (Todd, 08-12): the prerequisite, not part of the yearly flow.
        // Fields depend on nothing else and everything downstream is authored against them.
        // The signal is deliberately the cheapest honest one — ≥1 active field assigned to
        // the league-season; whether every division's timeslots resolve a field stays the
        // timeslots step's question.
        const fieldsSetup: StepRow = {
            num: 0,
            title: 'Set Up Fields',
            icon: 'bi-geo-alt',
            state: c.fieldsSetup.complete ? 'done' : 'todo',
            reason: c.fieldsSetup.complete
                ? `${c.fieldsSetup.activeFieldCount} field${c.fieldsSetup.activeFieldCount === 1 ? '' : 's'} assigned for this season`
                : 'No fields assigned to this season yet',
            route: 'fields',
            queryParams: null,
            linkLabel: 'Manage Fields'
        };

        const pools: StepRow = {
            num: 1,
            title: 'Assign Teams To Pools',
            icon: 'bi-people',
            state: c.pools.complete ? 'done' : 'todo',
            reason: c.pools.complete
                ? 'Every team is in a pool'
                : `${c.pools.totalUnpooledTeams} unpooled team${c.pools.totalUnpooledTeams === 1 ? '' : 's'} — `
                    + this.nameList(c.pools.offenders.map(o => `${o.agegroupName}: ${o.unpooledCount} of ${o.teamCount}`)),
            route: '../ladt/pool-assignment',
            queryParams: { from: 'scheduling' },
            linkLabel: 'Pool Assignment'
        };

        const pairings: StepRow = {
            num: 2,
            title: 'Pairings',
            icon: 'bi-arrow-left-right',
            state: c.pairings.complete ? 'done' : 'info',
            reason: c.pairings.complete
                ? 'All pool sizes have pairings'
                : `Missing for pool size${c.pairings.missingPoolSizes.length === 1 ? '' : 's'} `
                    + `${c.pairings.missingPoolSizes.join(', ')} — generated automatically at build`,
            route: 'pairings',
            queryParams: null,
            linkLabel: 'Pairings'
        };

        // One step, two grains — play dates belong to the agegroup, field timeslots to the
        // division. Both are set on the same page, as they were in legacy.
        const timeslots: StepRow = {
            num: 3,
            title: 'Assign Timeslots',
            icon: 'bi-calendar-range',
            state: c.dates.complete && c.fields.complete ? 'done' : 'todo',
            reason: this.timeslotsReason(c),
            route: 'timeslots',
            queryParams: null,
            linkLabel: 'Assign Timeslots'
        };

        // Game guarantees used to be step 4 in their own right, with Build Schedule at 5. Both
        // rows sent the user to the same place — the hub — because the guarantee is one field on
        // its Build Rules tab.
        //
        // Worse, it gated nothing. AutoBuildScheduleService derives a missing guarantee from the
        // pairing table (even teams: max round; odd: max round − 1) and writes the derived value
        // back to the agegroup profile. A red step demanding an answer the engine works out for
        // itself is the same shape as step 2, which has always said so instead of blocking.
        //
        // So it folds in here as a note, not a gate. What still locks this step is the work that
        // lives in the steps ABOVE it — pools, dates, timeslots — which nothing can derive.
        const upstreamBlockers = [
            !c.fieldsSetup.complete ? 'fields' : null,
            !c.pools.complete ? 'pools' : null,
            !c.dates.complete ? 'play dates' : null,
            !c.fields.complete ? 'timeslots' : null
        ].filter((b): b is string => b !== null);

        const guaranteeNote = c.rules.complete
            ? ''
            : ` · ${c.rules.divisionsWithoutGuarantee.length} division`
                + `${c.rules.divisionsWithoutGuarantee.length === 1 ? '' : 's'} without a game `
                + `guarantee — derived from pairings unless you set one on Build Rules`;

        const build: StepRow = {
            num: 4,
            title: 'Build Schedule',
            icon: 'bi-calendar-check',
            state: c.gameCount > 0
                ? 'done'
                : upstreamBlockers.length > 0 ? 'locked' : 'todo',
            reason: c.gameCount > 0
                ? `${c.gameCount} game${c.gameCount === 1 ? '' : 's'} scheduled`
                : upstreamBlockers.length > 0
                    ? `Locked until complete: ${upstreamBlockers.join(', ')}`
                    : `All prerequisites met — ready to build${guaranteeNote}`,
            route: 'schedule-hub',
            queryParams: null,
            linkLabel: 'Schedule'
        };

        const rows = [fieldsSetup, pools, pairings, timeslots, build];

        // A row earns a step number only when it is finishable and its completion is computable —
        // everything else belongs in Tools. Bracket Seeds is the one post-build row that
        // qualifies: coverage (every non-pool slot carries a seed or an advancement feed) is
        // reported by the DTO. Hidden outright when the event has no bracket games — a green
        // check for work that never applied would be a hollow claim.
        if (c.bracketSeeds.hasBracketGames) {
            const bs = c.bracketSeeds;
            rows.push({
                num: 5,
                title: 'Bracket Seeds',
                icon: 'bi-trophy',
                state: bs.complete ? 'done' : 'todo',
                reason: bs.complete
                    ? 'Every bracket slot has a seed or feed source'
                    : `${bs.uncoveredSlotCount} slot${bs.uncoveredSlotCount === 1 ? '' : 's'} without a seed or feed — `
                        + this.nameList(bs.uncoveredByAgegroup.map(a => `${a.agegroupName}: ${a.slotLabels.join(', ')}`)),
                route: '../scheduling/bracket-seeds',
                queryParams: { from: 'scheduling' },
                linkLabel: 'Bracket Seeds'
            });
        }

        return rows;
    });

    // Operations on a built schedule — reached for repeatedly, never "finished", so none of
    // them can hold a step number. The grid unlocks once games exist.
    //
    // QA Results and Master Schedule are hub segments, so their tools deep-link the hub with
    // that segment preselected (?mode=) — one door, one chrome — instead of the standalone
    // twins those routes still serve for direct URLs.
    readonly tools: ToolRow[] = [
        { title: 'Master Schedule', icon: 'bi-calendar-week', route: 'schedule-hub', queryParams: { mode: 'master' } },
        { title: 'Rescheduler', icon: 'bi-arrow-repeat', route: '../scheduling/rescheduler', queryParams: { from: 'scheduling' } },
        { title: 'Schedule Viewer', icon: 'bi-eye', route: '../scheduling/view-schedule', queryParams: { from: 'scheduling' } },
        // Straight to the Schedules tab rather than the library's front page — game cards and
        // team schedules, not a catalogue. The library falls back to All when this event has
        // no schedule reports, so the link is never a dead end.
        { title: 'Schedule Reports', icon: 'bi-file-earmark-text', route: '../reporting/reports-library', queryParams: { tab: 'Schedules', from: 'scheduling' } },
        { title: 'QA Results', icon: 'bi-check2-square', route: 'schedule-hub', queryParams: { mode: 'qa' } },
        { title: 'Tournament Parking', icon: 'bi-car-front', route: '../scheduling/tournament-parking', queryParams: { from: 'scheduling' } },
        { title: 'Mobile Scorers', icon: 'bi-phone', route: '../scheduling/mobile-scorers', queryParams: { from: 'scheduling' } }
    ];

    ngOnInit(): void {
        this.load();
    }

    load(): void {
        this.isLoading.set(true);
        this.hasError.set(false);
        this.svc.getChecklist().subscribe({
            next: data => {
                this.checklist.set(data);
                this.isLoading.set(false);
            },
            error: () => {
                this.hasError.set(true);
                this.isLoading.set(false);
            }
        });
    }

    /**
     * Step 3 reports two grains. Play dates are missing by agegroup ("2030"); timeslots are
     * missing by division, grouped under their agegroup ("2030: White, Red") so a single
     * unconfigured pool is named instead of hiding behind a green agegroup.
     */
    private timeslotsReason(c: SchedulingChecklistDto): string {
        const clauses: string[] = [];

        if (!c.dates.complete) {
            clauses.push(`No play dates: ${this.nameList(c.dates.missingAgegroups)}`);
        }
        if (!c.fields.complete) {
            const groups = c.fields.missingByAgegroup
                .map(a => `${a.agegroupName}: ${a.divNames.join(', ')}`);
            const pools = c.fields.missingDivisionCount;
            clauses.push(
                `${pools} pool${pools === 1 ? '' : 's'} without timeslots — ${this.nameList(groups)}`);
        }

        return clauses.length > 0
            ? clauses.join(' · ')
            : 'Play dates and timeslots are set';
    }

    /** "Sat, Jun 20 – Sun, Jun 21"; collapses to one date when the span is a single day. */
    private formatDateSpan(first: string | null, last: string | null): string | null {
        if (!first) return null;
        const fmt = new Intl.DateTimeFormat('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
        const from = fmt.format(new Date(first));
        if (!last) return from;
        const to = fmt.format(new Date(last));
        return from === to ? from : `${from} – ${to}`;
    }

    /** First few names inline, the rest folded into "+N more" to keep reasons one line. */
    private nameList(names: string[], max = 4): string {
        if (names.length <= max) return names.join(' · ');
        return `${names.slice(0, max).join(' · ')} +${names.length - max} more`;
    }
}
