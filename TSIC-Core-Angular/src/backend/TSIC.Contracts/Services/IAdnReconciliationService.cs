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
