import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import { AdnSweepResult } from '@core/api';

@Component({
    selector: 'app-manual-arb-sweep',
    standalone: true,
    imports: [CommonModule, FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './manual-arb-sweep.component.html',
    styleUrls: ['./manual-arb-sweep.component.scss'],
})
export class ManualArbSweepComponent {
    private readonly http = inject(HttpClient);
    private readonly endpoint = `${environment.apiUrl}/admin/adn-sweep/run`;

    daysPrior = 1;
    readonly isRunning = signal(false);
    readonly result = signal<AdnSweepResult | null>(null);
    readonly errorMessage = signal('');

    run(): void {
        if (this.daysPrior < 1 || this.daysPrior > 60) {
            this.errorMessage.set('Days prior must be between 1 and 60.');
            return;
        }
        this.isRunning.set(true);
        this.errorMessage.set('');
        this.result.set(null);

        this.http.post<AdnSweepResult>(`${this.endpoint}?daysPrior=${this.daysPrior}`, null).subscribe({
            next: r => {
                this.isRunning.set(false);
                this.result.set(r);
            },
            error: err => {
                this.isRunning.set(false);
                const msg = err.error?.message || 'Sweep failed. Check server logs and try again.';
                this.errorMessage.set(msg);
            },
        });
    }
}
