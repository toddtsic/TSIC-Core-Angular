import { Component, ChangeDetectionStrategy, inject, signal, computed, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { GridAllModule, GridComponent, ToolbarItems } from '@syncfusion/ej2-angular-grids';
import { environment } from '@environments/environment';
import type { StpClubRepDto } from '@core/api';

/**
 * Stay-to-Play club rep summary. Ports the one legacy STP admin screen that carried
 * real function (Controllers/STP/Admin/STPClubRepsController + Views/STPClubReps).
 *
 * The grid IS the deliverable: a housing vendor reads the team counts to size room
 * blocks, and takes the data away with the Excel toolbar button. Legacy's batch-email
 * half is deliberately not ported — Stay-to-Play is a data transfer to a third party,
 * not a mailing service we run on their behalf.
 */
@Component({
    selector: 'app-stp-club-reps',
    standalone: true,
    imports: [CommonModule, GridAllModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './stp-club-reps.component.html',
    styleUrls: ['./stp-club-reps.component.scss'],
})
export class StpClubRepsComponent {
    private readonly http = inject(HttpClient);
    private readonly endpoint = `${environment.apiUrl}/stp/club-reps`;

    readonly isLoading = signal(false);
    readonly errorMessage = signal('');
    readonly rows = signal<StpClubRepDto[]>([]);

    readonly jobName = computed(() => this.rows()[0]?.jobName ?? '');
    readonly totalActiveTeams = computed(() =>
        this.rows().reduce((sum, r) => sum + r.activeTeamCount, 0));

    readonly toolbar: ToolbarItems[] = ['ExcelExport'];
    readonly grid = viewChild.required<GridComponent>('grid');

    constructor() {
        this.load();
    }

    load(): void {
        this.isLoading.set(true);
        this.errorMessage.set('');

        this.http.get<StpClubRepDto[]>(this.endpoint).subscribe({
            next: rows => {
                this.rows.set(rows);
                this.isLoading.set(false);
            },
            error: err => {
                this.isLoading.set(false);
                if (err.status === 401) {
                    this.errorMessage.set('You must be logged in to view this screen.');
                } else if (err.status === 403) {
                    this.errorMessage.set('You do not have permission to view this event\'s club reps.');
                } else {
                    this.errorMessage.set(err.error?.message || 'Failed to load club reps.');
                }
            },
        });
    }

    onToolbarClick(args: { item?: { id?: string } }): void {
        if (args.item?.id?.includes('excelexport')) {
            this.grid().excelExport();
        }
    }
}
