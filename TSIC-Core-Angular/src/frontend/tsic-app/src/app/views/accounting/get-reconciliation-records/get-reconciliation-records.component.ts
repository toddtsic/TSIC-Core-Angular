import { Component, ChangeDetectionStrategy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { GridAllModule, GridComponent, ColumnModel } from '@syncfusion/ej2-angular-grids';
import { environment } from '@environments/environment';
import {
    AdnImportResult,
    LedgerTab,
    MonthEndArtifactsInfo,
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
    empty: boolean;
}

@Component({
    selector: 'app-get-reconciliation-records',
    standalone: true,
    imports: [CommonModule, DatePipe, CurrencyPipe, GridAllModule],
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

    // Grid config — Search covers filtering, header-click covers sorting, aggregates cover totals.
    readonly ledgerToolbar = ['Search', 'ExcelExport'];
    readonly ledgerPageSettings = { pageSize: 50, pageSizes: [25, 50, 100, 200] };

    // Step 1 — Download ADN Txs
    readonly isImporting = signal(false);
    readonly importResult = signal<AdnImportResult | null>(null);
    readonly importError = signal('');
    readonly probing = signal(true);
    // Probe couldn't reach a working backend — distinct from a genuinely empty month.
    readonly probeFailed = signal(false);
    // The beat between "data received" and the verdict — the match query (stats) running.
    readonly isBuildingStats = signal(false);

    // Reconciliation verdict — Step 1's output statement (loaded after import, and on page init as a probe)
    readonly reconciliation = signal<MonthEndReconciliationResult | null>(null);

    // Eager build: after a download we run the sprocs once and persist the ledger + zip so Step 2/3
    // open instantly. Non-fatal — if it fails, Step 2/3 fall back to build-on-demand.
    readonly isPreparing = signal(false);
    readonly prepareInfo = signal<MonthEndArtifactsInfo | null>(null);

    // Step 2 — Review summary (the ledger tabs)
    readonly isLoadingLedger = signal(false);
    readonly ledger = signal<MonthEndLedger | null>(null);
    readonly ledgerError = signal('');
    readonly activeTabIndex = signal(0);

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

    // Greatest settlement date/time in the loaded month — a data-currency proxy ("settlements through
    // here"), not a pull timestamp (Txs records no import time).
    readonly latestSettlementAt = computed(() => this.reconciliation()?.latestSettlementAt ?? null);

    // The zip is built server-side once (by the eager prepare, or by the Step-2 ledger build). When
    // either has happened this session, Step 3 is a pure download of the prepared file, not a rebuild.
    readonly artifactsReady = computed(() => this.prepareInfo() != null || this.ledger() != null);

    readonly stackViews = computed<ReconciliationStackView[]>(() => {
        const r = this.reconciliation();
        if (!r) return [];
        return [
            { key: 'reg', label: 'Registration', summary: r.reg, matched: r.reg.unmatchedCount === 0, empty: r.reg.transactionCount === 0 },
            { key: 'merch', label: 'Merch', summary: r.merch, matched: r.merch.unmatchedCount === 0, empty: r.merch.transactionCount === 0 },
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

    // QA (passthrough) tabs have dynamic columns — project the string[][] rows into keyed objects and
    // build matching column defs so the Grid can sort/search/export them like any other grid.
    readonly qaColumns = computed<ColumnModel[]>(() => {
        const tab = this.activeTab();
        if (!tab || tab.kind !== 'table') return [];
        return tab.columns.map((h, i) => ({ field: `c${i}`, headerText: h, width: 150 }));
    });
    readonly qaRows = computed<Record<string, string>[]>(() => {
        const tab = this.activeTab();
        if (!tab || tab.kind !== 'table') return [];
        return tab.rows.map(r => {
            const o: Record<string, string> = {};
            r.forEach((c, i) => (o[`c${i}`] = c));
            return o;
        });
    });

    ngOnInit(): void {
        this.probe();
    }

    // Probe: is last month already loaded? Drives step gating without forcing a pull.
    // A failure here (backend down / not deployed) is surfaced, not swallowed into a false "empty".
    probe(): void {
        this.probing.set(true);
        this.probeFailed.set(false);
        this.http
            .get<MonthEndReconciliationResult>(`${this.base}/reconcile${this.monthParams}`)
            .subscribe({
                next: r => {
                    this.reconciliation.set(r);
                    this.probing.set(false);
                },
                error: () => {
                    this.probeFailed.set(true);
                    this.probing.set(false);
                },
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
                    // Data's in — now build the match verdict (Step 1's output statement).
                    this.isBuildingStats.set(true);
                    this.reloadReconciliation();
                    // A fresh pull invalidated the persisted ledger/files server-side — clear ours and
                    // eagerly rebuild them once so Step 2/3 open instantly.
                    this.ledger.set(null);
                    this.filesStats.set(null);
                    this.prepareInfo.set(null);
                    this.prepareArtifacts();
                },
                error: err => {
                    this.isImporting.set(false);
                    this.importError.set(this.friendlyError(err, 'Download failed. Check server logs and try again.'));
                },
            });
    }

    // Eager build (non-fatal): run the sprocs once and persist ledger + zip so Step 2/3 read from disk.
    private prepareArtifacts(): void {
        this.isPreparing.set(true);
        this.http
            .post<MonthEndArtifactsInfo>(`${this.base}/prepare${this.monthParams}`, null)
            .subscribe({
                next: info => {
                    this.prepareInfo.set(info);
                    this.isPreparing.set(false);
                },
                // Swallow — Step 2/3 will build on demand if the eager build didn't finish.
                error: () => this.isPreparing.set(false),
            });
    }

    private reloadReconciliation(): void {
        this.http
            .get<MonthEndReconciliationResult>(`${this.base}/reconcile${this.monthParams}`)
            .subscribe({
                next: r => {
                    this.reconciliation.set(r);
                    this.isBuildingStats.set(false);
                },
                error: () => this.isBuildingStats.set(false),
            });
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

    // Grid Excel export via the toolbar button (grid ref passed from the template).
    onLedgerToolbar(args: { item?: { id?: string } }, grid: GridComponent): void {
        if (args.item?.id?.toLowerCase().includes('excelexport')) {
            grid.excelExport();
        }
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
