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

    readonly steps = computed<StepRow[]>(() => {
        const c = this.checklist();
        if (!c) return [];

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
            linkLabel: 'Schedule Hub'
        };

        // ── Post-build steps ──
        // Both operate ON a schedule, so neither means anything until games exist. That is the
        // same gate the Tools grid applied ("available once games are scheduled"); they are
        // promoted out of it because they are ordered work, not a drawer of utilities.
        //
        // Their state is 'info', never 'done'. Nothing on the checklist DTO reports whether a
        // bracket is seeded or whether a reschedule is outstanding, and inventing a green tick
        // from data we do not have is worse than not claiming one. The link is the point.
        const bracketSeeds: StepRow = {
            num: 5,
            title: 'Bracket Seeds',
            icon: 'bi-trophy',
            state: c.gameCount > 0 ? 'info' : 'locked',
            reason: c.gameCount > 0
                ? 'Set who feeds each bracket slot — championship play only'
                : 'Locked until the schedule is built',
            route: '../scheduling/bracket-seeds',
            queryParams: { from: 'scheduling' },
            linkLabel: 'Bracket Seeds'
        };

        const rescheduler: StepRow = {
            num: 6,
            title: 'Rescheduler',
            icon: 'bi-arrow-repeat',
            state: c.gameCount > 0 ? 'info' : 'locked',
            reason: c.gameCount > 0
                ? 'Move, swap or re-time games after the build'
                : 'Locked until the schedule is built',
            route: '../scheduling/rescheduler',
            queryParams: { from: 'scheduling' },
            linkLabel: 'Rescheduler'
        };

        return [pools, pairings, timeslots, build, bracketSeeds, rescheduler];
    });

    // Bracket Seeds and Rescheduler used to live here. They are steps 5 and 6 now — ordered
    // work that follows the build, not utilities you reach for. What is left is genuinely a
    // drawer: ways to look at the schedule, and side tools that sit outside the sequence.
    readonly tools: ToolRow[] = [
        { title: 'View Schedule', icon: 'bi-eye', route: '../scheduling/view-schedule', queryParams: { from: 'scheduling' } },
        { title: 'Master Schedule', icon: 'bi-calendar-week', route: '../scheduling/master-schedule', queryParams: { from: 'scheduling' } },
        { title: 'QA Results', icon: 'bi-check2-square', route: 'qa-results', queryParams: null },
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

    /** First few names inline, the rest folded into "+N more" to keep reasons one line. */
    private nameList(names: string[], max = 4): string {
        if (names.length <= max) return names.join(' · ');
        return `${names.slice(0, max).join(' · ')} +${names.length - max} more`;
    }
}
