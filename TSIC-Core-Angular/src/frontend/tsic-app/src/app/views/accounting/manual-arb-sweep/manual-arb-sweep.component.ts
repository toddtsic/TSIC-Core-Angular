import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import { AdnSweepResult, AdnSweepModeDto, ArbNotifyResultDto, ArbRenderedEmailDto } from '@core/api';

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
    private readonly sanitizer = inject(DomSanitizer);
    private readonly base = `${environment.apiUrl}/admin/adn-sweep`;

    daysPrior = 1;
    readonly isRunning = signal(false);
    readonly result = signal<AdnSweepResult | null>(null);
    readonly errorMessage = signal('');

    /** Which mode this host runs in. Asked for on load so the screen can say so BEFORE the click. */
    readonly mode = signal<AdnSweepModeDto | null>(null);
    readonly isDryRun = computed(() => this.mode()?.dryRun === true);

    readonly expiring = signal<ArbNotifyResultDto | null>(null);
    readonly isExpiringRunning = signal(false);
    readonly expiringError = signal('');

    /**
     * Which rendered email body is expanded. Keyed "<list>:<index>", not a bare index — the sweep and
     * the expiring-card pass render their own lists, and a shared index would open a row in both.
     * null = all collapsed.
     */
    readonly openEmail = signal<string | null>(null);

    constructor() {
        this.http.get<AdnSweepModeDto>(`${this.base}/mode`).subscribe({
            next: m => this.mode.set(m),
            // Mode unknown is not the same as mode live. Leaving it null makes the banner say
            // "could not determine" rather than silently implying the safe answer.
            error: () => this.mode.set(null),
        });
    }

    run(): void {
        if (this.daysPrior < 1 || this.daysPrior > 60) {
            this.errorMessage.set('Days prior must be between 1 and 60.');
            return;
        }
        this.isRunning.set(true);
        this.errorMessage.set('');
        this.result.set(null);
        this.openEmail.set(null);

        this.http.post<AdnSweepResult>(`${this.base}/run?daysPrior=${this.daysPrior}`, null).subscribe({
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

    runExpiring(): void {
        this.isExpiringRunning.set(true);
        this.expiringError.set('');
        this.expiring.set(null);

        this.http.post<ArbNotifyResultDto>(`${this.base}/expiring-cards/dry-run`, null).subscribe({
            next: r => {
                this.isExpiringRunning.set(false);
                this.expiring.set(r);
            },
            error: err => {
                this.isExpiringRunning.set(false);
                const msg = err.error?.message || 'Expiring-card pass failed. Check server logs and try again.';
                this.expiringError.set(msg);
            },
        });
    }

    toggleEmail(key: string): void {
        this.openEmail.set(this.openEmail() === key ? null : key);
    }

    /**
     * Server-authored HTML: the digest and the email bodies are built in AdnSweepService /
     * ArbNotificationService from fixed templates plus DB values, never from anything a user types.
     * Bypassing the sanitizer is what keeps the preview faithful — stripping the inline styles would
     * show a layout nobody actually receives, which defeats reviewing it.
     */
    trust(html: string | null | undefined): SafeHtml {
        return this.sanitizer.bypassSecurityTrustHtml(html ?? '');
    }

    recipients(e: ArbRenderedEmailDto): string {
        return (e.toAddresses ?? []).join(', ');
    }
}
