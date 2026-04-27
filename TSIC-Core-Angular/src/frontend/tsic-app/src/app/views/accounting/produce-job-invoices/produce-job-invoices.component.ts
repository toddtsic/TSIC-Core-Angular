import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';

@Component({
    selector: 'app-produce-job-invoices',
    standalone: true,
    imports: [CommonModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './produce-job-invoices.component.html',
    styleUrls: ['./produce-job-invoices.component.scss'],
})
export class ProduceJobInvoicesComponent {
    private readonly http = inject(HttpClient);
    private readonly endpoint = `${environment.apiUrl}/reporting/Produce_Job_Invoices_LastMonth`;

    readonly isRunning = signal(false);
    readonly success = signal<boolean | null>(null);
    readonly errorMessage = signal('');

    run(): void {
        this.isRunning.set(true);
        this.errorMessage.set('');
        this.success.set(null);

        this.http.get<boolean>(this.endpoint).subscribe({
            next: ok => {
                this.isRunning.set(false);
                this.success.set(ok);
                if (!ok) this.errorMessage.set('Crystal Reports service reported failure. Check server logs.');
            },
            error: err => {
                this.isRunning.set(false);
                this.success.set(false);
                const msg = err.error?.message || 'Failed to invoke invoice production.';
                this.errorMessage.set(msg);
            },
        });
    }
}
