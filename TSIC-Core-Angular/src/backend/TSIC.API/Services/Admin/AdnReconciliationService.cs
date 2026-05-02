using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.Logging;
using TSIC.API.Services.Reporting;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Implements the legacy "Get Reconciliation Records" flow:
/// pull ADN settled batches for the target month, repopulate <c>Txs</c>, then return
/// the existing monthly-reconciliation Excel export. Mirrors
/// <c>TSIC_Unify.Controllers.AdnReconciliation.AdnReconciliationController.GetTransactionsFromLastMonth</c>
/// behavior (delete-then-insert idempotency, status filter, invoice-number shape filter).
/// Hardcodes <c>AuthorizeNet.Environment.PRODUCTION</c> — see
/// project_adn_reconciliation_dev_prod_intentional memory for rationale.
/// </summary>
public class AdnReconciliationService : IAdnReconciliationService
{
    private static readonly HashSet<string> AllowedTransactionStatuses = new(StringComparer.Ordinal)
    {
        "settledSuccessfully",
        "refundSettledSuccessfully",
        "voided",
    };

    private readonly IAdnReconciliationRepository _repo;
    private readonly IAdnApiService _adnApi;
    private readonly IReportingService _reportingService;
    private readonly ILogger<AdnReconciliationService> _logger;

    public AdnReconciliationService(
        IAdnReconciliationRepository repo,
        IAdnApiService adnApi,
        IReportingService reportingService,
        ILogger<AdnReconciliationService> logger)
    {
        _repo = repo;
        _adnApi = adnApi;
        _reportingService = reportingService;
        _logger = logger;
    }

    public async Task<AdnReconciliationRunResult> RunMonthlyAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        var startDate = new DateTime(settlementYear, settlementMonth, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var creds = await _repo.GetTsicMasterAdnCredentialsAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "TSIC master Authorize.Net credentials not found. Expected a Customer record with " +
                "CustomerName = 'TeamSportsInfo.com' having AdnLoginId / AdnTransactionKey populated.");

        // Match legacy: delete-first idempotency. Re-running for the same month wipes that month's
        // rows then repopulates from a fresh ADN pull.
        var monthKey = startDate.ToString("-MMM-yyyy ");
        var deleted = await _repo.DeleteTxsForMonthKeyAsync(monthKey, cancellationToken);
        _logger.LogInformation(
            "AdnReconciliation: cleared {Deleted} existing Txs rows for month key {MonthKey}",
            deleted, monthKey);

        // Hardcoded PRODUCTION — see project_adn_reconciliation_dev_prod_intentional memory.
        // Reconciliation is read-only against ADN; pulling prod data into dev's Txs is desired
        // for debugging.
        var env = AuthorizeNet.Environment.PRODUCTION;

        // Look-back includes the day before to catch settlements that crossed midnight.
        var lookBackStart = startDate.AddDays(-1);

        var batchResponse = _adnApi.GetSettleBatchList_FromDateRange(
            env: env,
            adnLoginId: creds.AdnLoginId,
            adnTransactionKey: creds.AdnTransactionKey,
            firstSettlementDate: lookBackStart,
            lastSettlementDate: endDate,
            includeStatistics: true);

        if (batchResponse?.messages?.resultCode != messageTypeEnum.Ok)
        {
            var msg = batchResponse?.messages?.message?[0]?.text ?? "Unknown error from Authorize.Net";
            throw new InvalidOperationException($"Authorize.Net batch list request failed: {msg}");
        }

        var batches = batchResponse.batchList ?? Array.Empty<batchDetailsType>();
        var pulled = new List<(batchDetailsType Batch, transactionSummaryType Tx)>();

        foreach (var batch in batches)
        {
            var txResponse = _adnApi.GetTransactionList_ByBatchId(
                env: env,
                adnLoginId: creds.AdnLoginId,
                adnTransactionKey: creds.AdnTransactionKey,
                batchId: batch.batchId);

            if (txResponse?.messages?.resultCode != messageTypeEnum.Ok || txResponse.transactions == null)
            {
                continue;
            }

            foreach (var tx in txResponse.transactions)
            {
                pulled.Add((batch, tx));
            }
        }

        // Filter: only known-good statuses, and invoice numbers shaped like our 3- or 4-segment IDs
        // (4 segments = merch with trailing "_M"). Anything else is foreign settlement traffic.
        var importable = pulled
            .Where(p =>
                !string.IsNullOrEmpty(p.Tx.invoiceNumber)
                && (p.Tx.invoiceNumber.Split('_').Length is 3 or 4)
                && AllowedTransactionStatuses.Contains(p.Tx.transactionStatus))
            .ToList();

        var existingIds = await _repo.GetExistingTransactionIdsAsync(
            importable.Select(p => p.Tx.transId),
            cancellationToken);

        var toInsert = new List<Txs>();
        var skippedDuplicates = 0;

        foreach (var (batch, tx) in importable)
        {
            if (existingIds.Contains(tx.transId))
            {
                skippedDuplicates++;
                continue;
            }

            toInsert.Add(new Txs
            {
                BOldSysTx = 0,
                TransactionId = tx.transId,
                TransactionStatus = MapStatus(tx.transactionStatus),
                SettlementAmount = tx.settleAmount.ToString(),
                SettlementDateTime = batch.settlementTimeLocal.ToString("dd-MMM-yyyy hh:mm:ss tt") + " EDT",
                InvoiceNumber = tx.invoiceNumber,
            });
        }

        if (toInsert.Count > 0)
        {
            await _repo.AddRangeAsync(toInsert, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "AdnReconciliation: imported {Imported} new Txs ({Skipped} duplicates skipped) " +
            "from {Batches} batches / {Transactions} transactions for {Year}-{Month:D2}",
            toInsert.Count, skippedDuplicates, batches.Length, pulled.Count,
            settlementYear, settlementMonth);

        // Now generate the Excel export by running the existing reconciliation report.
        var excel = await _reportingService.ExportMonthlyReconciliationAsync(
            settlementMonth, settlementYear, isMerchandise: false, cancellationToken);

        return new AdnReconciliationRunResult
        {
            Excel = excel,
            BatchesPulled = batches.Length,
            TransactionsPulled = pulled.Count,
            Imported = toInsert.Count,
            SkippedDuplicates = skippedDuplicates,
        };
    }

    private static string MapStatus(string adnStatus) => adnStatus switch
    {
        "settledSuccessfully" => "Settled Successfully",
        "refundSettledSuccessfully" => "Credited",
        "voided" => "Voided",
        _ => string.Empty,
    };
}
