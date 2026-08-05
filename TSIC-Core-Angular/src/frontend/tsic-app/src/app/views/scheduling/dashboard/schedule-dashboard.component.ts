import { Component, ChangeDetectionStrategy, computed, inject, OnInit, signal } from '@angular/core';
import { ChecklistBackLinkComponent } from '../shared/components/checklist-back-link/checklist-back-link.component';
import { SchedulingChecklistService, type ScheduleDashboardDto } from '../checklist/services/scheduling-checklist.service';

interface KpiTile {
    icon: string;
    value: string;
    label: string;
    /** Secondary line under the value (date range, shortfall detail). */
    sub: string | null;
    /** Coverage shortfall — pairs an icon + text with the accent so it never rides on color alone. */
    warn: boolean;
}

interface DayRow {
    label: string;
    count: number;
    /** Bar width as a percentage of the busiest day. */
    pct: number;
}

/**
 * Schedule Dashboard — the post-build readout the checklist's stat band clicks through to.
 * Pure display over one GET; the checklist remains the workflow instrument. The coverage
 * tiles (divisions, teams) carry the real QA signal: a shortfall means the build silently
 * placed nothing for someone.
 */
@Component({
    selector: 'app-schedule-dashboard',
    standalone: true,
    imports: [ChecklistBackLinkComponent],
    templateUrl: './schedule-dashboard.component.html',
    styleUrl: './schedule-dashboard.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScheduleDashboardComponent implements OnInit {
    private readonly svc = inject(SchedulingChecklistService);

    readonly dash = signal<ScheduleDashboardDto | null>(null);
    readonly isLoading = signal(false);
    readonly hasError = signal(false);

    readonly gameCount = computed(() => this.dash()?.stats.gameCount ?? 0);
    readonly hasGames = computed(() => this.gameCount() > 0);

    readonly tiles = computed<KpiTile[]>(() => {
        const d = this.dash();
        if (!d || d.stats.gameCount === 0) return [];
        const s = d.stats;

        const tiles: KpiTile[] = [
            {
                icon: 'bi-calendar-check',
                value: `${s.gameCount}`,
                label: s.gameCount === 1 ? 'game' : 'games',
                sub: null,
                warn: false
            },
            {
                icon: 'bi-calendar3',
                value: `${s.playDateCount}`,
                label: s.playDateCount === 1 ? 'play day' : 'play days',
                sub: this.formatDateSpan(s.firstGameDate, s.lastGameDate),
                warn: false
            },
            {
                icon: 'bi-geo-alt',
                value: `${s.fieldsInUse}`,
                label: s.fieldsInUse === 1 ? 'field in use' : 'fields in use',
                sub: null,
                warn: false
            }
        ];

        // Coverage tiles show the denominator only when short — "91" reads cleaner than
        // "91 / 91", and the fraction appearing at all is itself the alarm.
        const divShort = Math.max(0, d.schedulableDivisionCount - s.divisionsScheduled);
        tiles.push({
            icon: 'bi-diagram-3',
            value: divShort > 0 ? `${s.divisionsScheduled} / ${d.schedulableDivisionCount}` : `${s.divisionsScheduled}`,
            label: 'divisions scheduled',
            sub: divShort > 0 ? `${divShort} division${divShort === 1 ? '' : 's'} without games` : null,
            warn: divShort > 0
        });

        const teamShort = Math.max(0, d.activeTeamCount - d.teamsScheduled);
        tiles.push({
            icon: 'bi-people',
            value: teamShort > 0 ? `${d.teamsScheduled} / ${d.activeTeamCount}` : `${d.teamsScheduled}`,
            label: 'teams scheduled',
            sub: teamShort > 0 ? `${teamShort} team${teamShort === 1 ? '' : 's'} without a game` : null,
            warn: teamShort > 0
        });

        return tiles;
    });

    readonly dayRows = computed<DayRow[]>(() => {
        const days = this.dash()?.gamesPerDay ?? [];
        if (days.length === 0) return [];
        const max = Math.max(...days.map(d => d.gameCount));
        const fmt = new Intl.DateTimeFormat('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
        return days.map(d => ({
            label: fmt.format(new Date(d.date)),
            count: d.gameCount,
            pct: max > 0 ? Math.max(2, Math.round((d.gameCount / max) * 100)) : 0
        }));
    });

    ngOnInit(): void {
        this.load();
    }

    load(): void {
        this.isLoading.set(true);
        this.hasError.set(false);
        this.svc.getDashboard().subscribe({
            next: data => {
                this.dash.set(data);
                this.isLoading.set(false);
            },
            error: () => {
                this.hasError.set(true);
                this.isLoading.set(false);
            }
        });
    }

    private formatDateSpan(first: string | null, last: string | null): string | null {
        if (!first) return null;
        const fmt = new Intl.DateTimeFormat('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
        const from = fmt.format(new Date(first));
        if (!last) return from;
        const to = fmt.format(new Date(last));
        return from === to ? from : `${from} – ${to}`;
    }
}
