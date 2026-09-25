import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, filter, map } from 'rxjs';
import { DecimalPipe } from '@angular/common';
import { GridAllModule, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { GridRowNumbersDirective } from '@shared-ui/directives/grid-row-numbers.directive';
import { EmailLogService } from './services/email-log.service';
import { JobService } from '@infrastructure/services/job.service';
import type { EmailLogSummaryDto, EmailLogDetailDto } from '@core/api';

@Component({
    selector: 'app-email-log',
    standalone: true,
    imports: [DecimalPipe, GridAllModule, GridRowNumbersDirective],
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
    /** Transient copy outcome: null | 'copied' | 'failed'. A copy button that silently
     *  does nothing is worse than no button, so failure is SHOWN. */
    readonly copyState = signal<'copied' | 'failed' | null>(null);
    readonly copied = computed(() => this.copyState() === 'copied');
    readonly copyFailed = computed(() => this.copyState() === 'failed');
    readonly copyTitle = computed(() => {
        switch (this.copyState()) {
            case 'copied': return 'Copied — paste into a new email';
            case 'failed': return 'Copy blocked — the clipboard needs a secure (https) page';
            default: return 'Copy message';
        }
    });

    private copyResetTimer?: ReturnType<typeof setTimeout>;

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

    constructor() {
        // Load on job change. distinctUntilChanged keys on jobPath so an unrelated
        // currentJob.set() (e.g. a metadata refetch) doesn't refire the request.
        toObservable(this.jobService.currentJob).pipe(
            map(job => job?.jobPath),
            filter((jobPath): jobPath is string => !!jobPath),
            distinctUntilChanged(),
            takeUntilDestroyed(),
        ).subscribe(() => this.loadEmails());
    }

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

    /**
     * Copy the message so it pastes as FORMATTED text, not as source.
     *
     * `writeText()` puts the markup on the clipboard as text/plain only, so a rich-text
     * target (the compose editor, Gmail, Outlook) has nothing but the tags to paste and
     * shows them literally. The clipboard is a multi-flavor container: write text/html
     * for rich targets and a tag-stripped text/plain alongside it for plain ones. The
     * paste target picks the flavor it wants.
     */
    async copyMessageHtml(): Promise<void> {
        const html = this.selectedDetail()?.msg;
        if (!html) return;

        try {
            // navigator.clipboard is UNDEFINED outside a secure context (plain http) and
            // ClipboardItem is missing on older browsers — both throw rather than reject.
            if (typeof ClipboardItem === 'undefined' || !navigator.clipboard?.write) {
                await navigator.clipboard.writeText(html);
            } else {
                await navigator.clipboard.write([new ClipboardItem({
                    'text/html': new Blob([html], { type: 'text/html' }),
                    'text/plain': new Blob([this.htmlToPlainText(html)], { type: 'text/plain' }),
                })]);
            }
            this.setCopyState('copied');
        } catch {
            this.setCopyState('failed');
        }
    }

    /**
     * Tag-stripped fallback for plain-text paste targets. DOMParser, not a detached div:
     * an inert document never fetches the `<img src>`s the message body carries.
     */
    private htmlToPlainText(html: string): string {
        const withBreaks = html
            .replace(/<br\s*\/?>/gi, '\n')
            .replace(/<\/(p|div|tr|li|h[1-6])>/gi, '\n');
        const text = new DOMParser()
            .parseFromString(withBreaks, 'text/html')
            .body.textContent ?? '';
        return text.replace(/\n{3,}/g, '\n\n').trim();
    }

    private setCopyState(state: 'copied' | 'failed'): void {
        clearTimeout(this.copyResetTimer);
        this.copyState.set(state);
        this.copyResetTimer = setTimeout(() => this.copyState.set(null), 2000);
    }
}
