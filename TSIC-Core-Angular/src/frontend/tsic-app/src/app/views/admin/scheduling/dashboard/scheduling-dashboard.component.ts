import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterModule } from '@angular/router';
import { SchedulingDashboardService, type SchedulingDashboardStatusDto } from './services/scheduling-dashboard.service';

@Component({
    selector: 'app-scheduling-dashboard',
    standalone: true,
    imports: [RouterModule],
    templateUrl: './scheduling-dashboard.component.html',
    styleUrl: './scheduling-dashboard.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class SchedulingDashboardComponent implements OnInit {
    private readonly svc = inject(SchedulingDashboardService);

    readonly status = signal<SchedulingDashboardStatusDto | null>(null);
    readonly isLoading = signal(false);
    readonly hasError = signal(false);

    readonly cards = computed(() => {
        const s = this.status();
        if (!s) return [];

        return [
            {
                title: 'LADT Setup',
                icon: 'bi-diagram-3',
                route: '/ladt/admin',
                isExternal: true,
                metric: `${s.totalAgegroups} agegroups \u00b7 ${s.totalDivisions} divisions`,
                detail: s.totalDivisions > 0
                    ? (s.divisionsAreThemed ? 'themed' : 'not themed')
                    : null,
                severity: s.totalDivisions > 0 ? 'success' as const : 'empty' as const
            },
            {
                title: 'Pool Assignment',
                icon: 'bi-people',
                route: '/admin/pool-assignment',
                isExternal: true,
                metric: `${s.agegroupsPoolComplete}/${s.totalAgegroups} agegroups`,
                detail: 'complete',
                severity: s.agegroupsPoolComplete >= s.totalAgegroups ? 'success' as const
                    : s.agegroupsPoolComplete > 0 ? 'partial' as const : 'warn' as const
            },
            {
                title: 'Fields',
                icon: 'bi-geo-alt',
                route: 'fields',
                isExternal: false,
                metric: `${s.fieldCount} fields`,
                detail: null,
                severity: s.fieldCount > 0 ? 'success' as const : 'empty' as const
            },
            {
                title: 'Pairings',
                icon: 'bi-arrow-left-right',
                route: 'pairings',
                isExternal: false,
                metric: `${s.poolSizesWithPairings}/${s.totalDistinctPoolSizes} pool sizes`,
                detail: 'with pairings',
                severity: s.totalDistinctPoolSizes === 0 ? 'empty' as const
                    : s.poolSizesWithPairings >= s.totalDistinctPoolSizes ? 'success' as const
                    : s.poolSizesWithPairings > 0 ? 'partial' as const : 'empty' as const
            },
            {
                title: 'Timeslots',
                icon: 'bi-clock',
                route: 'timeslots',
                isExternal: false,
                metric: `${s.agegroupsReady}/${s.totalAgegroups} agegroups`,
                detail: 'ready to schedule',
                severity: s.agegroupsReady >= s.totalAgegroups ? 'success' as const
                    : s.agegroupsReady > 0 ? 'partial' as const : 'empty' as const
            },
            {
                title: 'Schedule',
                icon: 'bi-calendar-check',
                route: 'schedule-division',
                isExternal: false,
                metric: `${s.agegroupsScheduled}/${s.totalAgegroups} agegroups`,
                detail: 'scheduled',
                severity: s.agegroupsScheduled >= s.totalAgegroups ? 'success' as const
                    : s.agegroupsScheduled > 0 ? 'partial' as const : 'empty' as const
            }
        ];
    });

    ngOnInit(): void {
        this.loadStatus();
    }

    loadStatus(): void {
        this.isLoading.set(true);
        this.hasError.set(false);
        this.svc.getStatus().subscribe({
            next: (data) => {
                this.status.set(data);
                this.isLoading.set(false);
            },
            error: () => {
                this.hasError.set(true);
                this.isLoading.set(false);
            }
        });
    }
}
