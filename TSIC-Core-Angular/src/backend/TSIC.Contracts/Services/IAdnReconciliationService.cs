using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface IAdnReconciliationService
{
    /// <summary>
    /// End-to-end "Get Reconciliation Records" flow for a settlement month:
    /// 1. Pulls TSIC master ADN credentials from the designated Customer record.
    /// 2. Imports settled-batch transactions from Authorize.Net (PRODUCTION env, even on dev box —
    ///    intentional, see project memory <c>project_adn_reconciliation_dev_prod_intentional</c>).
    /// 3. Repopulates <c>Txs</c> for that month (idempotent; mirrors legacy delete-then-insert).
    /// 4. Runs the monthly-reconciliation export and bundles the .xlsx + consolidated .iif into a .zip.
    /// Returns the zip bundle plus import + TRNS counts for surfacing to the UI / logs.
    /// </summary>
    Task<AdnReconciliationRunResult> RunMonthlyAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 1 (the "load"): pull last month's settled ADN batches (reg + merch) into <c>adn.Txs</c>.
    /// Idempotent (delete-then-insert per month key). Writes only <c>Txs</c>; produces no files.
    /// </summary>
    Task<AdnImportResult> ImportSettlementsAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Custodial reconciliation for a settlement month: does every ADN transaction have a matching
    /// accounting row (reg → <c>Registration_Accounting</c>, merch → <c>StoreCartBatchAccounting</c>)?
    /// Reads <c>adn.Txs</c> only — safe to call after the monthly close to verify before importing.
    /// </summary>
    Task<MonthEndReconciliationResult> GetReconciliationAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 2 (the "presenting"): the human-readable month-end ledger — the export workbook's tabs
    /// rendered on screen (IIF double-entry flattened, QA passed through). Reads existing <c>Txs</c>.
    /// </summary>
    Task<MonthEndLedger> GetLedgerAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 3 (the "files"): generate the .zip of two QuickBooks .iif files + backing .xlsx from the
    /// <c>Txs</c> already imported for the month. Pure read of <c>Txs</c> via the sprocs — no ADN pull.
    /// </summary>
    Task<ReconciliationBundleResult> GenerateBundleAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Eager build after a download: runs the sprocs ONCE and persists the month's ledger + zip so the
    /// review (Step 2) and file download (Step 3) are served from disk. Returns the build metadata.
    /// </summary>
    Task<MonthEndArtifactsInfo> PrepareAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unattended month-end close: runs <see cref="RunMonthlyAsync"/> for the month, then emails ONE
    /// message carrying the day's sweep digest, the close summary (accounting-match verdict + IIF TRNS
    /// parity), and the .zip. Driven by the daily ADN sweep on the 1st of the month.
    ///
    /// <para><paramref name="sweep"/> is the sweep that ran minutes earlier, and it is a hard gate: the
    /// sweep books the closing month's final ARB/eCheck rows into the accounting tables the export sprocs
    /// read. If it did not fully succeed (<see cref="AdnSweepResult.IsTrustworthy"/> false), the export is
    /// short those payments — a wrong ledger, not a wrong count — so the mail goes out with the failure
    /// banner and NO attachment. Re-run the close by hand once the sweep is fixed. Pass null (the manual
    /// trigger) to skip the gate and always attach.</para>
    /// </summary>
    Task<AdnReconciliationRunResult> EmailMonthlyCloseAsync(
        int settlementMonth,
        int settlementYear,
        AdnSweepResult? sweep = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// THE 1st-of-the-month path, whole: run the daily sweep with its digest suppressed, then run the
    /// close and mail ONE message — close verdict on top, sweep digest below, .zip attached only if the
    /// sweep was trustworthy. Never throws: if the close fails, the suppressed digest is still mailed
    /// with the failure noted, because losing the morning report to a monthly-close bug is not acceptable.
    ///
    /// <para>This is the single definition of that flow. <c>AdnSweepBackgroundService</c> calls it on the
    /// 1st, and <c>POST /api/adn-reconciliation/email-close?includeSweep=true</c> calls it on demand — so
    /// what you see when you test IS what arrives at 5am, not a reconstruction of it.</para>
    /// </summary>
    Task<MonthEndCloseResult> RunMonthEndCloseWithSweepAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of the combined 1st-of-month run: what the sweep did, what the close did, what shipped.</summary>
public record MonthEndCloseResult
{
    public required AdnSweepResult Sweep { get; init; }

    /// <summary>The close's import + TRNS counts. Null when the close itself failed.</summary>
    public AdnReconciliationRunResult? Close { get; init; }

    /// <summary>Whether the .zip rode the email. False when the sweep was not trustworthy.</summary>
    public required bool FilesAttached { get; init; }

    /// <summary>Set when the close threw and the fallback digest was mailed instead.</summary>
    public string? CloseError { get; init; }
}

public record AdnImportResult
{
    public required int BatchesPulled { get; init; }
    public required int TransactionsPulled { get; init; }
    public required int Imported { get; init; }
    public required int SkippedDuplicates { get; init; }
}

public record AdnReconciliationRunResult
{
    public required ReportExportResult Bundle { get; init; }
    public required int BatchesPulled { get; init; }
    public required int TransactionsPulled { get; init; }
    public required int Imported { get; init; }
    public required int SkippedDuplicates { get; init; }
    public required int RegSourceTrnsCount { get; init; }
    public required int RegConsolidatedTrnsCount { get; init; }
    public required int MerchSourceTrnsCount { get; init; }
    public required int MerchConsolidatedTrnsCount { get; init; }
}
