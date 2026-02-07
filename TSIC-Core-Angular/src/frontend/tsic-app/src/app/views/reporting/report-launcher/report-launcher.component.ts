import { Component, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ReportingService } from '@infrastructure/services/reporting.service';

@Component({
    selector: 'app-report-launcher',
    standalone: true,
    templateUrl: './report-launcher.component.html',
    styleUrl: './report-launcher.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportLauncherComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly reportingService = inject(ReportingService);

    readonly loading = signal(false);
    readonly error = signal<string | null>(null);
    readonly reportAction = signal<string>('');

    ngOnInit(): void {
        const action = this.route.snapshot.paramMap.get('action');
        if (!action) {
            this.error.set('No report action specified.');
            return;
        }

        this.reportAction.set(action);
        this.downloadReport(action);
    }

    private downloadReport(action: string): void {
        this.loading.set(true);
        this.error.set(null);

        // Collect any query params from the URL (e.g., exportFormat)
        const queryParams: Record<string, string> = {};
        const snapshot = this.route.snapshot.queryParamMap;
        for (const key of snapshot.keys) {
            const value = snapshot.get(key);
            if (value) {
                queryParams[key] = value;
            }
        }

        this.reportingService.downloadReport(action, queryParams).subscribe({
            next: (response) => {
                const fallback = action.toLowerCase().includes('excel')
                    ? `TSIC-${action}.xlsx`
                    : action.toLowerCase().includes('ical')
                        ? `TSIC-${action}.ics`
                        : `TSIC-${action}.pdf`;
                this.reportingService.triggerDownload(response, fallback);
                this.loading.set(false);
            },
            error: (err) => {
                this.loading.set(false);
                if (err.status === 401) {
                    this.error.set('You must be logged in to access this report.');
                } else if (err.status === 403) {
                    this.error.set('You do not have permission to access this report.');
                } else {
                    this.error.set('An error occurred while generating the report. Please try again.');
                }
            }
        });
    }

    retry(): void {
        const action = this.reportAction();
        if (action) {
            this.downloadReport(action);
        }
    }

    goBack(): void {
        window.history.back();
    }
}
