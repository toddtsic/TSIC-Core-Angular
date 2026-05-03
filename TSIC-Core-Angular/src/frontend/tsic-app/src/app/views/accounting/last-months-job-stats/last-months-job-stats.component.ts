import { Component, ChangeDetectionStrategy, inject, signal, ViewChild } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { GridAllModule, GridComponent, EditSettingsModel, ToolbarItems } from '@syncfusion/ej2-angular-grids';
import { environment } from '@environments/environment';
import type { LastMonthsJobStatRowDto, UpdateLastMonthsJobStatRequest } from '@core/api';

@Component({
    selector: 'app-last-months-job-stats',
    standalone: true,
    imports: [CommonModule, DatePipe, GridAllModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './last-months-job-stats.component.html',
    styleUrls: ['./last-months-job-stats.component.scss'],
})
export class LastMonthsJobStatsComponent {
    private readonly http = inject(HttpClient);
    private readonly endpoint = `${environment.apiUrl}/last-months-job-stats`;

    readonly isLoading = signal(false);
    readonly errorMessage = signal('');
    readonly rows = signal<LastMonthsJobStatRowDto[]>([]);

    readonly targetMonth = (() => {
        const today = new Date();
        return new Date(today.getFullYear(), today.getMonth() - 1, 1);
    })();

    readonly editSettings: EditSettingsModel = {
        allowEditing: true,
        allowAdding: false,
        allowDeleting: false,
    };

    readonly toolbar: ToolbarItems[] = ['Edit', 'Cancel', 'Update', 'ExcelExport'];

    @ViewChild('grid') grid!: GridComponent;

    constructor() {
        this.load();
    }

    load(): void {
        this.isLoading.set(true);
        this.errorMessage.set('');

        this.http.get<LastMonthsJobStatRowDto[]>(this.endpoint).subscribe({
            next: rows => {
                this.rows.set(rows);
                this.isLoading.set(false);
            },
            error: err => {
                this.isLoading.set(false);
                if (err.status === 401) {
                    this.errorMessage.set('You must be logged in to view this report.');
                } else if (err.status === 403) {
                    this.errorMessage.set('You do not have permission to view this report.');
                } else {
                    this.errorMessage.set(err.error?.message || 'Failed to load last months job stats.');
                }
            },
        });
    }

    onToolbarClick(args: { item?: { id?: string } }): void {
        if (args.item?.id?.includes('excelexport')) {
            this.grid.excelExport();
        }
    }

    onActionComplete(args: { requestType?: string; action?: string; data?: LastMonthsJobStatRowDto }): void {
        if (args.requestType === 'save' && args.action === 'edit' && args.data) {
            const row = args.data;
            const request: UpdateLastMonthsJobStatRequest = {
                countActivePlayersToDate: row.countActivePlayersToDate,
                countActivePlayersToDateLastMonth: row.countActivePlayersToDateLastMonth,
                countNewPlayersThisMonth: row.countNewPlayersThisMonth,
                countActiveTeamsToDate: row.countActiveTeamsToDate,
                countActiveTeamsToDateLastMonth: row.countActiveTeamsToDateLastMonth,
                countNewTeamsThisMonth: row.countNewTeamsThisMonth,
            };

            this.http.put(`${this.endpoint}/${row.aid}`, request).subscribe({
                error: err => {
                    this.errorMessage.set(err.error?.message || 'Failed to save row update.');
                },
            });
        }
    }
}
