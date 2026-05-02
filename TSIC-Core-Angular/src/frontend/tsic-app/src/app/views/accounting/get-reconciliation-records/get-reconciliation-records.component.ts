import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { environment } from '@environments/environment';

interface ReconciliationStats {
    imported: number;
    skippedDuplicates: number;
    batchesPulled: number;
    transactionsPulled: number;
}

@Component({
    selector: 'app-get-reconciliation-records',
    standalone: true,
    imports: [CommonModule, DatePipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './get-reconciliation-records.component.html',
    styleUrls: ['./get-reconciliation-records.component.scss'],
})
export class GetReconciliationRecordsComponent {
    private readonly http = inject(HttpClient);
    private readonly endpoint = `${environment.apiUrl}/adn-reconciliation/run-monthly`;

    readonly isRunning = signal(false);
    readonly stats = signal<ReconciliationStats | null>(null);
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
        this.stats.set(null);

        this.http
            .post(this.endpoint, null, {
                observe: 'response',
                responseType: 'blob',
            })
            .subscribe({
                next: (response: HttpResponse<Blob>) => {
                    this.isRunning.set(false);
                    this.stats.set({
                        imported: this.readNumberHeader(response, 'X-Imported-Count'),
                        skippedDuplicates: this.readNumberHeader(response, 'X-Skipped-Duplicates'),
                        batchesPulled: this.readNumberHeader(response, 'X-Batches-Pulled'),
                        transactionsPulled: this.readNumberHeader(response, 'X-Transactions-Pulled'),
                    });
                    this.triggerDownload(response);
                },
                error: err => {
                    this.isRunning.set(false);
                    if (err.status === 401) {
                        this.errorMessage.set('You must be logged in to run this report.');
                    } else if (err.status === 403) {
                        this.errorMessage.set('You do not have permission to run this report.');
                    } else {
                        const fallback = 'Reconciliation failed. Check server logs and try again.';
                        this.errorMessage.set(this.readErrorMessage(err) || fallback);
                    }
                },
            });
    }

    private readNumberHeader(response: HttpResponse<Blob>, name: string): number {
        const value = response.headers.get(name);
        const n = value == null ? 0 : Number(value);
        return Number.isFinite(n) ? n : 0;
    }

    private triggerDownload(response: HttpResponse<Blob>): void {
        const blob = response.body;
        if (!blob) return;

        const disposition = response.headers.get('Content-Disposition') ?? '';
        const match = disposition.match(/filename="?([^";]+)"?/i);
        const filename = match?.[1] ?? `TSIC-AdnReconciliation-${this.formatMonthKey()}.xlsx`;

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
