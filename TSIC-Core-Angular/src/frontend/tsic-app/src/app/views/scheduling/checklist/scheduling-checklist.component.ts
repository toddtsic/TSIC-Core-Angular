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

        const dates: StepRow = {
            num: 2,
            title: 'Assign Play Dates',
            icon: 'bi-calendar3',
            state: c.dates.complete ? 'done' : 'todo',
            reason: c.dates.complete
                ? 'Every agegroup has play dates'
                : `No dates for: ${this.nameList(c.dates.missingAgegroups)}`,
            route: 'schedule-hub',
            queryParams: null,
            linkLabel: 'Configure Dates'
        };

        const fields: StepRow = {
            num: 3,
            title: 'Assign Fields',
            icon: 'bi-geo-alt',
            state: c.fields.complete ? 'done' : 'todo',
            reason: c.fields.complete
                ? 'Every agegroup has field assignments'
                : `No fields assigned for: ${this.nameList(c.fields.missingAgegroups)}`,
            route: 'schedule-hub',
            queryParams: null,
            linkLabel: 'Configure Fields'
        };

        const rules: StepRow = {
            num: 4,
            title: 'Set Game Guarantees',
            icon: 'bi-sliders',
            state: c.rules.complete ? 'done' : 'todo',
            reason: c.rules.complete
                ? 'Every division has a game guarantee'
                : `No game guarantee for: ${this.nameList(c.rules.divisionsWithoutGuarantee)}`,
            route: 'schedule-hub',
            queryParams: null,
            linkLabel: 'Build Rules'
        };

        const pairings: StepRow = {
            num: 5,
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

        const blockers = [
            !c.pools.complete ? 'pools' : null,
            !c.dates.complete ? 'dates' : null,
            !c.fields.complete ? 'fields' : null,
            !c.rules.complete ? 'game guarantees' : null
        ].filter((b): b is string => b !== null);

        const build: StepRow = {
            num: 6,
            title: 'Build Schedule',
            icon: 'bi-calendar-check',
            state: c.gameCount > 0 ? 'done' : c.buildUnlocked ? 'todo' : 'locked',
            reason: c.gameCount > 0
                ? `${c.gameCount} game${c.gameCount === 1 ? '' : 's'} scheduled`
                : c.buildUnlocked
                    ? 'All prerequisites met — ready to build'
                    : `Locked until complete: ${blockers.join(', ')}`,
            route: 'schedule-hub',
            queryParams: null,
            linkLabel: 'Schedule Hub'
        };

        return [pools, dates, fields, rules, pairings, build];
    });

    readonly tools: ToolRow[] = [
        { title: 'View Schedule', icon: 'bi-eye', route: '../scheduling/view-schedule' },
        { title: 'Master Schedule', icon: 'bi-calendar-week', route: '../scheduling/master-schedule' },
        { title: 'Rescheduler', icon: 'bi-arrow-repeat', route: '../scheduling/rescheduler' },
        { title: 'QA Results', icon: 'bi-check2-square', route: 'qa-results' },
        { title: 'Bracket Seeds', icon: 'bi-trophy', route: '../scheduling/bracket-seeds' },
        { title: 'Tournament Parking', icon: 'bi-car-front', route: '../scheduling/tournament-parking' },
        { title: 'Mobile Scorers', icon: 'bi-phone', route: '../scheduling/mobile-scorers' }
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

    /** First few names inline, the rest folded into "+N more" to keep reasons one line. */
    private nameList(names: string[], max = 4): string {
        if (names.length <= max) return names.join(' · ');
        return `${names.slice(0, max).join(' · ')} +${names.length - max} more`;
    }
}
