import { Component, inject, signal, computed, effect, ChangeDetectionStrategy } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { EmailLogService } from './services/email-log.service';
import { JobService } from '@infrastructure/services/job.service';
import type { EmailLogSummaryDto, EmailLogDetailDto } from '@core/api';

type SortColumn = 'sendTs' | 'sendFrom' | 'count' | 'subject';
type SortDirection = 'asc' | 'desc';

@Component({
    selector: 'app-email-log',
    standalone: true,
    imports: [DatePipe, DecimalPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './email-log.component.html',
    styleUrl: './email-log.component.scss'
})
export class EmailLogComponent {
    private readonly emailLogService = inject(EmailLogService);
    private readonly jobService = inject(JobService);

    // Data
    readonly emails = signal<EmailLogSummaryDto[]>([]);
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);

    // Detail
    readonly selectedEmailId = signal<number | null>(null);
    readonly selectedDetail = signal<EmailLogDetailDto | null>(null);
    readonly isDetailLoading = signal(false);
    readonly copied = signal(false);

    // Sorting (default: newest first)
    readonly sortColumn = signal<SortColumn>('sendTs');
    readonly sortDirection = signal<SortDirection>('desc');

    readonly sortedEmails = computed(() => {
        const list = [...this.emails()];
        const col = this.sortColumn();
        const dir = this.sortDirection() === 'asc' ? 1 : -1;

        return list.sort((a, b) => {
            let aVal: string | number;
            let bVal: string | number;

            switch (col) {
                case 'sendTs':
                    aVal = new Date(a.sendTs).getTime();
                    bVal = new Date(b.sendTs).getTime();
                    break;
                case 'sendFrom':
                    aVal = (a.sendFrom ?? '').toLowerCase();
                    bVal = (b.sendFrom ?? '').toLowerCase();
                    break;
                case 'count':
                    aVal = a.count ?? 0;
                    bVal = b.count ?? 0;
                    break;
                case 'subject':
                    aVal = (a.subject ?? '').toLowerCase();
                    bVal = (b.subject ?? '').toLowerCase();
                    break;
                default:
                    return 0;
            }

            if (typeof aVal === 'string') {
                return dir * aVal.localeCompare(bVal as string);
            }
            return dir * ((aVal as number) - (bVal as number));
        });
    });

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

    sort(column: SortColumn) {
        if (this.sortColumn() === column) {
            this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
        } else {
            this.sortColumn.set(column);
            this.sortDirection.set(column === 'sendTs' ? 'desc' : 'asc');
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
