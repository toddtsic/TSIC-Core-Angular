import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { environment } from '@environments/environment';

@Component({
    selector: 'app-merch-reconciliation-records',
    standalone: true,
    imports: [CommonModule, DatePipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './merch-reconciliation-records.component.html',
    styleUrls: ['./merch-reconciliation-records.component.scss'],
})
export class MerchReconciliationRecordsComponent {
    private readonly http = inject(HttpClient);
    private readonly endpoint = `${environment.apiUrl}/reporting/export-monthly-reconciliation-merch`;

    readonly isRunning = signal(false);
    readonly downloaded = signal(false);
    readonly errorMessage = signal('');

    readonly targetMonth = (() => {
        const today = new Date();
        return new Date(today.getFullYear(), today.getMonth() - 1, 1);
    })();

    readonly endOfMonth = (() => {
        const t = this.targetMonth;
        return new Date(t.getFullYear(), t.getMonth() + 1, 0);
    })();

    run(): void {
        this.isRunning.set(true);
        this.errorMessage.set('');
        this.downloaded.set(false);

        const params = new HttpParams()
            .set('settlementMonth', this.targetMonth.getMonth() + 1)
            .set('settlementYear', this.targetMonth.getFullYear());

        this.http
            .get(this.endpoint, {
                params,
                observe: 'response',
                responseType: 'blob',
            })
            .subscribe({
                next: (response: HttpResponse<Blob>) => {
                    this.isRunning.set(false);
                    this.triggerDownload(response);
                    this.downloaded.set(true);
                },
                error: err => {
                    this.isRunning.set(false);
                    if (err.status === 401) {
                        this.errorMessage.set('You must be logged in to run this report.');
                    } else if (err.status === 403) {
                        this.errorMessage.set('You do not have permission to run this report.');
                    } else {
                        const fallback = 'Export failed. Check server logs and try again.';
                        this.errorMessage.set(this.readErrorMessage(err) || fallback);
                    }
                },
            });
    }

    private triggerDownload(response: HttpResponse<Blob>): void {
        const blob = response.body;
        if (!blob) return;

        const disposition = response.headers.get('Content-Disposition') ?? '';
        const match = disposition.match(/filename="?([^";]+)"?/i);
        const filename = match?.[1] ?? `TSIC-AdnReconciliation-Merch-${this.formatMonthKey()}.xlsx`;

        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    }

    private formatMonthKey(): string {
        return `${this.targetMonth.getFullYear()}-${String(this.targetMonth.getMonth() + 1).padStart(2, '0')}`;
    }

    private readErrorMessage(err: { error?: unknown }): string | null {
        const e = err.error;
        if (typeof e === 'string') return e;
        if (e && typeof e === 'object' && 'message' in e && typeof (e as { message?: unknown }).message === 'string') {
            return (e as { message: string }).message;
        }
        return null;
    }
}
