import { Component, inject, signal, computed, effect, ChangeDetectionStrategy, ViewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { GridAllModule, GridComponent, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { EmailLogService } from './services/email-log.service';
import { JobService } from '@infrastructure/services/job.service';
import type { EmailLogSummaryDto, EmailLogDetailDto } from '@core/api';

@Component({
    selector: 'app-email-log',
    standalone: true,
    imports: [DecimalPipe, GridAllModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './email-log.component.html',
    styleUrl: './email-log.component.scss'
})
export class EmailLogComponent {
    private readonly emailLogService = inject(EmailLogService);
    private readonly jobService = inject(JobService);

    @ViewChild('grid') grid!: GridComponent;

    // Data
    readonly emails = signal<EmailLogSummaryDto[]>([]);
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);

    // Detail
    readonly selectedEmailId = signal<number | null>(null);
    readonly selectedDetail = signal<EmailLogDetailDto | null>(null);
    readonly isDetailLoading = signal(false);
    readonly copied = signal(false);

    // Grid settings
    sortSettings: SortSettingsModel = { columns: [{ field: 'sendTs', direction: 'Descending' }] };

    // Parsed recipients from detail
    readonly recipients = computed(() => {
        const detail = this.selectedDetail();
        if (!detail?.sendTo) return [];
        return detail.sendTo
            .split(';')
            .map(e => e.trim().toLowerCase())
            .filter(e => e.length > 0)
            .sort();
    });

    // Load on job change
    private readonly loadOnJobChange = effect(() => {
        const job = this.jobService.currentJob();
        if (job?.jobPath) {
            this.loadEmails();
        }
    });

    loadEmails() {
        this.isLoading.set(true);
        this.errorMessage.set(null);
        this.selectedEmailId.set(null);
        this.selectedDetail.set(null);

        this.emailLogService.getEmailLogs().subscribe({
            next: emails => {
                this.emails.set(emails);
                this.isLoading.set(false);
            },
            error: err => {
                this.errorMessage.set(err?.error?.message || 'Failed to load email log.');
                this.isLoading.set(false);
            }
        });
    }

    // Row click → show detail
    onRowSelected(args: any): void {
        if (args.data) {
            this.selectEmail(args.data as EmailLogSummaryDto);
        }
    }

    // Row numbers
    refreshRowNumbers(): void {
        if (!this.grid) return;
        const rows = this.grid.getRows();
        const page = this.grid.pageSettings?.currentPage ?? 1;
        const size = this.grid.pageSettings?.pageSize ?? rows.length;
        const offset = (page - 1) * size;
        rows.forEach((row, i) => {
            const cell = row.querySelector('td');
            if (cell) cell.textContent = String(offset + i + 1);
        });
    }

    onActionComplete(args: any): void {
        if (args.requestType === 'sorting' || args.requestType === 'paging') {
            this.refreshRowNumbers();
        }
    }

    selectEmail(email: EmailLogSummaryDto) {
        if (this.selectedEmailId() === email.emailId) return;

        this.selectedEmailId.set(email.emailId);
        this.isDetailLoading.set(true);
        this.selectedDetail.set(null);

        this.emailLogService.getEmailDetail(email.emailId).subscribe({
            next: detail => {
                this.selectedDetail.set(detail);
                this.isDetailLoading.set(false);
            },
            error: () => {
                this.isDetailLoading.set(false);
            }
        });
    }

    closeDetail() {
        this.selectedEmailId.set(null);
        this.selectedDetail.set(null);
    }

    copyMessageHtml() {
        const html = this.selectedDetail()?.msg;
        if (!html) return;

        navigator.clipboard.writeText(html).then(() => {
            this.copied.set(true);
            setTimeout(() => this.copied.set(false), 2000);
        });
    }
}
