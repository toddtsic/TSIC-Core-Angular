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
                title: 'Pool Assignment',
                icon: 'bi-people',
                route: '/admin/pool-assignment',
                isExternal: true,
                metric: `${s.teamsAssigned} assigned`,
                detail: s.teamsUnassigned > 0 ? `${s.teamsUnassigned} unassigned` : null,
                severity: s.teamsUnassigned > 0 ? 'warn' : 'success'
            },
            {
                title: 'Fields',
                icon: 'bi-geo-alt',
                route: 'fields',
                isExternal: false,
                metric: `${s.fieldCount} fields`,
                detail: null,
                severity: s.fieldCount > 0 ? 'success' : 'empty'
            },
            {
                title: 'Pairings',
                icon: 'bi-arrow-left-right',
                route: 'pairings',
                isExternal: false,
                metric: `${s.divisionsWithPairings}/${s.totalDivisions} divisions`,
                detail: 'with pairings',
                severity: s.divisionsWithPairings >= s.totalDivisions ? 'success'
                    : s.divisionsWithPairings > 0 ? 'partial' : 'empty'
            },
            {
                title: 'Timeslots',
                icon: 'bi-clock',
                route: 'timeslots',
                isExternal: false,
                metric: `${s.agegroupsWithTimeslots}/${s.totalAgegroups} agegroups`,
                detail: 'configured',
                severity: s.agegroupsWithTimeslots >= s.totalAgegroups ? 'success'
                    : s.agegroupsWithTimeslots > 0 ? 'partial' : 'empty'
            },
            {
                title: 'Schedule',
                icon: 'bi-calendar-check',
                route: 'schedule-division',
                isExternal: false,
                metric: `${s.scheduledGameCount} games`,
                detail: `${s.divisionsScheduled}/${s.totalDivisions} divisions scheduled`,
                severity: s.divisionsScheduled >= s.totalDivisions ? 'success'
                    : s.divisionsScheduled > 0 ? 'partial' : 'empty'
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
