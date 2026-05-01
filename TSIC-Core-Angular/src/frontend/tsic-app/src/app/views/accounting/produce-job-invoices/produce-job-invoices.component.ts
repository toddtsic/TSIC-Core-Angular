import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportingService } from '@infrastructure/services/reporting.service';

type ReportKind = 'full' | 'summaries';

@Component({
    selector: 'app-produce-job-invoices',
    standalone: true,
    imports: [CommonModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './produce-job-invoices.component.html',
    styleUrls: ['./produce-job-invoices.component.scss'],
})
export class ProduceJobInvoicesComponent {
    private readonly reportingService = inject(ReportingService);

    readonly running = signal<ReportKind | null>(null);
    readonly errorMessage = signal('');

    download(kind: ReportKind): void {
        this.errorMessage.set('');
        this.running.set(kind);

        const endpoint = kind === 'full'
            ? 'Get_Invoices_LastMonth'
            : 'Get_Invoices_LastMonthSummariesOnly';

        const fallback = kind === 'full'
            ? 'TSIC-Last-Month-Invoices'
            : 'TSIC-Last-Month-Invoice-Summaries';

        this.reportingService.downloadReport(endpoint).subscribe({
            next: response => {
                this.reportingService.triggerDownload(response, fallback);
                this.running.set(null);
            },
            error: err => {
                this.running.set(null);
                if (err.status === 401) {
                    this.errorMessage.set('You must be logged in to run this report.');
                } else if (err.status === 403) {
                    this.errorMessage.set('You do not have permission to run this report.');
                } else {
                    this.errorMessage.set('Report failed to generate. Please try again.');
                }
            }
        });
    }
}
