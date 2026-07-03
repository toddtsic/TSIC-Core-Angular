import { Component, ChangeDetectionStrategy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import {
    AdnImportResult,
    LedgerTab,
    MonthEndLedger,
    MonthEndReconciliationResult,
    ReconciliationStackSummary,
} from '@core/api';

interface FilesStats {
    regTrnsSource: number;
    regTrnsConsolidated: number;
    merchTrnsSource: number;
    merchTrnsConsolidated: number;
}

interface ReconciliationStackView {
    key: string;
    label: string;
    summary: ReconciliationStackSummary;
    matched: boolean;
}

@Component({
    selector: 'app-get-reconciliation-records',
    standalone: true,
    imports: [CommonModule, DatePipe, CurrencyPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './get-reconciliation-records.component.html',
    styleUrls: ['./get-reconciliation-records.component.scss'],
})
export class GetReconciliationRecordsComponent implements OnInit {
    private readonly http = inject(HttpClient);
    private readonly base = `${environment.apiUrl}/adn-reconciliation`;

    // Month is fixed to last month — the close is monotonic, no picker.
    readonly targetMonth = (() => {
        const today = new Date();
        return new Date(today.getFullYear(), today.getMonth() - 1, 1);
    })();
    readonly endOfMonth = new Date(this.targetMonth.getFullYear(), this.targetMonth.getMonth() + 1, 0);
    private readonly settlementMonth = this.targetMonth.getMonth() + 1;
    private readonly settlementYear = this.targetMonth.getFullYear();
    private get monthParams(): string {
        return `?settlementMonth=${this.settlementMonth}&settlementYear=${this.settlementYear}`;
    }

    readonly steps = [
        { n: 1, label: 'Download ADN Txs' },
        { n: 2, label: 'Review summary' },
        { n: 3, label: 'Download files' },
    ];

    readonly activeStep = signal(1);

    // Step 1 — Download ADN Txs
    readonly isImporting = signal(false);
    readonly importResult = signal<AdnImportResult | null>(null);
    readonly importError = signal('');
    readonly probing = signal(true);

    // Reconciliation verdict — Step 1's output statement (loaded after import, and on page init as a probe)
    readonly reconciliation = signal<MonthEndReconciliationResult | null>(null);

    // Step 2 — Review summary (the ledger tabs)
    readonly isLoadingLedger = signal(false);
    readonly ledger = signal<MonthEndLedger | null>(null);
    readonly ledgerError = signal('');
    readonly activeTabIndex = signal(0);
    private readonly expanded = signal<ReadonlySet<string>>(new Set());

    // Step 3 — Download files
    readonly isGeneratingFiles = signal(false);
    readonly filesStats = signal<FilesStats | null>(null);
    readonly filesError = signal('');

    // Data is "in place" (last month already loaded) when Txs holds rows — only the download writes Txs,
    // so a nonzero count means the pull has run and later steps are reachable without repeating Step 1.
    readonly loadedTransactionCount = computed(() => {
        const r = this.reconciliation();
        return r ? r.reg.transactionCount + r.merch.transactionCount : 0;
    });
    readonly dataInPlace = computed(() => this.loadedTransactionCount() > 0);

    readonly stackViews = computed<ReconciliationStackView[]>(() => {
        const r = this.reconciliation();
        if (!r) return [];
        return [
            { key: 'reg', label: 'Registration', summary: r.reg, matched: r.reg.unmatchedCount === 0 },
            { key: 'merch', label: 'Merch', summary: r.merch, matched: r.merch.unmatchedCount === 0 },
        ];
    });
    readonly allMatched = computed(() => {
        const v = this.stackViews();
        return v.length > 0 && v.every(s => s.matched);
    });

    readonly activeTab = computed<LedgerTab | null>(() => {
        const l = this.ledger();
        if (!l || l.tabs.length === 0) return null;
        return l.tabs[Math.min(this.activeTabIndex(), l.tabs.length - 1)] ?? null;
    });

    ngOnInit(): void {
        // Probe: is last month already loaded? Drives step gating without forcing a pull.
        this.http
            .get<MonthEndReconciliationResult>(`${this.base}/reconcile${this.monthParams}`)
            .subscribe({
                next: r => {
                    this.reconciliation.set(r);
                    this.probing.set(false);
                },
                error: () => this.probing.set(false),
            });
    }

    // ----- Step navigation -------------------------------------------------

    canEnter(step: number): boolean {
        return step === 1 || this.dataInPlace();
    }

    isStepDone(step: number): boolean {
        switch (step) {
            case 1: return this.dataInPlace();
            case 2: return this.ledger() != null;
            case 3: return this.filesStats() != null;
            default: return false;
        }
    }

    goStep(step: number): void {
        if (!this.canEnter(step)) return;
        this.activeStep.set(step);
        if (step === 2 && !this.ledger() && !this.isLoadingLedger()) {
            this.loadLedger();
        }
    }

    // ----- Step 1: Download ADN Txs ---------------------------------------

    downloadTxs(): void {
        this.isImporting.set(true);
        this.importError.set('');
        this.importResult.set(null);

        this.http
            .post<AdnImportResult>(`${this.base}/import${this.monthParams}`, null)
            .subscribe({
                next: result => {
                    this.importResult.set(result);
                    this.isImporting.set(false);
                    // Re-probe so the match verdict (Step 1's output statement) reflects the fresh import.
                    this.reloadReconciliation();
                    // A fresh pull invalidates any previously loaded ledger/files.
                    this.ledger.set(null);
                    this.filesStats.set(null);
                },
                error: err => {
                    this.isImporting.set(false);
                    this.importError.set(this.friendlyError(err, 'Download failed. Check server logs and try again.'));
                },
            });
    }

    private reloadReconciliation(): void {
        this.http
            .get<MonthEndReconciliationResult>(`${this.base}/reconcile${this.monthParams}`)
            .subscribe({ next: r => this.reconciliation.set(r) });
    }

    // ----- Step 2: Review summary (ledger tabs) ---------------------------

    loadLedger(): void {
        this.isLoadingLedger.set(true);
        this.ledgerError.set('');
        this.http
            .get<MonthEndLedger>(`${this.base}/ledger${this.monthParams}`)
            .subscribe({
                next: l => {
                    this.ledger.set(l);
                    this.activeTabIndex.set(0);
                    this.isLoadingLedger.set(false);
                },
                error: err => {
                    this.isLoadingLedger.set(false);
                    this.ledgerError.set(this.friendlyError(err, 'Could not load the ledger.'));
                },
            });
    }

    selectTab(index: number): void {
        this.activeTabIndex.set(index);
    }

    toggleSplits(key: string): void {
        const next = new Set(this.expanded());
        if (next.has(key)) {
            next.delete(key);
        } else {
            next.add(key);
        }
        this.expanded.set(next);
    }

    isExpanded(key: string): boolean {
        return this.expanded().has(key);
    }

    // ----- Step 3: Download files -----------------------------------------

    downloadFiles(): void {
        this.isGeneratingFiles.set(true);
        this.filesError.set('');
        this.http
            .get(`${this.base}/files${this.monthParams}`, { observe: 'response', responseType: 'blob' })
            .subscribe({
                next: (response: HttpResponse<Blob>) => {
                    this.isGeneratingFiles.set(false);
                    this.filesStats.set({
                        regTrnsSource: this.readNumberHeader(response, 'X-Iif-Reg-Trns-Source'),
                        regTrnsConsolidated: this.readNumberHeader(response, 'X-Iif-Reg-Trns-Consolidated'),
                        merchTrnsSource: this.readNumberHeader(response, 'X-Iif-Merch-Trns-Source'),
                        merchTrnsConsolidated: this.readNumberHeader(response, 'X-Iif-Merch-Trns-Consolidated'),
                    });
                    this.triggerDownload(response);
                },
                error: err => {
                    this.isGeneratingFiles.set(false);
                    this.filesError.set(this.friendlyError(err, 'File generation failed. Check server logs.'));
                },
            });
    }

    readonly regFilesMismatch = computed(() => {
        const s = this.filesStats();
        return s != null && s.regTrnsSource !== s.regTrnsConsolidated;
    });
    readonly merchFilesMismatch = computed(() => {
        const s = this.filesStats();
        return s != null && s.merchTrnsSource !== s.merchTrnsConsolidated;
    });
    readonly filesMismatch = computed(() => this.regFilesMismatch() || this.merchFilesMismatch());

    // ----- helpers ---------------------------------------------------------

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
        const filename = match?.[1] ?? `TSIC-AdnReconciliation-${this.settlementYear}-${String(this.settlementMonth).padStart(2, '0')}.zip`;

        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    }

    private friendlyError(err: { status?: number; error?: unknown }, fallback: string): string {
        if (err.status === 401) return 'You must be logged in to run this.';
        if (err.status === 403) return 'You do not have permission to run this.';
        const e = err.error;
        if (typeof e === 'string') return e;
        if (e && typeof e === 'object' && 'message' in e && typeof (e as { message?: unknown }).message === 'string') {
            return (e as { message: string }).message;
        }
        return fallback;
    }
}
