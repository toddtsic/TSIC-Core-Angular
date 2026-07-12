using System.Text;
using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.Logging;
using TSIC.API.Services.Reporting;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
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
    private readonly IEmailService _email;
    private readonly ILogger<AdnReconciliationService> _logger;

    public AdnReconciliationService(
        IAdnReconciliationRepository repo,
        IAdnApiService adnApi,
        IReportingService reportingService,
        IEmailService email,
        ILogger<AdnReconciliationService> logger)
    {
        _repo = repo;
        _adnApi = adnApi;
        _reportingService = reportingService;
        _email = email;
        _logger = logger;
    }

    public Task<MonthEndReconciliationResult> GetReconciliationAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
        => _repo.GetMonthEndReconciliationAsync(settlementMonth, settlementYear, cancellationToken);

    public async Task<AdnImportResult> ImportSettlementsAsync(
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

        var loginId = creds.AdnLoginId
            ?? throw new InvalidOperationException("TSIC master Authorize.Net AdnLoginId is null.");
        var transactionKey = creds.AdnTransactionKey
            ?? throw new InvalidOperationException("TSIC master Authorize.Net AdnTransactionKey is null.");
        var batchResponse = _adnApi.GetSettleBatchList_FromDateRange(
            env: env,
            adnLoginId: loginId,
            adnTransactionKey: transactionKey,
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

        // The month's Txs just changed — drop any persisted close artifacts so the next build/read
        // (the eager Prepare that follows, or a later Step 2/3) regenerates from the fresh data.
        _reportingService.InvalidateMonthEnd(settlementMonth, settlementYear);

        return new AdnImportResult
        {
            BatchesPulled = batches.Length,
            TransactionsPulled = pulled.Count,
            Imported = toInsert.Count,
            SkippedDuplicates = skippedDuplicates,
        };
    }

    public async Task<ReconciliationBundleResult> GenerateBundleAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        // Reads the Txs already imported for the month via the reconciliation sprocs — no ADN pull.
        var bundle = await _reportingService.ExportMonthEndCloseBundleAsync(
            settlementMonth, settlementYear, cancellationToken);

        if (bundle.RegSourceTrnsCount != bundle.RegConsolidatedTrnsCount
            || bundle.MerchSourceTrnsCount != bundle.MerchConsolidatedTrnsCount)
        {
            _logger.LogWarning(
                "AdnReconciliation: IIF TRNS count mismatch for {Year}-{Month:D2} — " +
                "reg source {RegSource}/consolidated {RegConsolidated}, merch source {MerchSource}/consolidated {MerchConsolidated}. " +
                "The .iif files were still produced; verify before importing to QuickBooks.",
                settlementYear, settlementMonth,
                bundle.RegSourceTrnsCount, bundle.RegConsolidatedTrnsCount,
                bundle.MerchSourceTrnsCount, bundle.MerchConsolidatedTrnsCount);
        }

        return bundle;
    }

    public Task<MonthEndLedger> GetLedgerAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
        => _reportingService.GetMonthEndLedgerAsync(settlementMonth, settlementYear, cancellationToken);

    public Task<MonthEndArtifactsInfo> PrepareAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
        => _reportingService.BuildAndPersistMonthEndAsync(settlementMonth, settlementYear, cancellationToken);

    public async Task<AdnReconciliationRunResult> RunMonthlyAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        // Combined legacy flow (Step 1 + Step 3 in one call): pull, then bundle.
        var import = await ImportSettlementsAsync(settlementMonth, settlementYear, cancellationToken);
        var bundle = await GenerateBundleAsync(settlementMonth, settlementYear, cancellationToken);

        return new AdnReconciliationRunResult
        {
            Bundle = bundle.Zip,
            BatchesPulled = import.BatchesPulled,
            TransactionsPulled = import.TransactionsPulled,
            Imported = import.Imported,
            SkippedDuplicates = import.SkippedDuplicates,
            RegSourceTrnsCount = bundle.RegSourceTrnsCount,
            RegConsolidatedTrnsCount = bundle.RegConsolidatedTrnsCount,
            MerchSourceTrnsCount = bundle.MerchSourceTrnsCount,
            MerchConsolidatedTrnsCount = bundle.MerchConsolidatedTrnsCount,
        };
    }

    public async Task<AdnReconciliationRunResult> EmailMonthlyCloseAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        // Same close the operator runs by hand from Accounting → ADN End-of-Month / IIF, just unattended:
        // pull the month, build the bundle, then read back the custodial match so the covering email can
        // carry the verdict. Sequential awaits — one scoped DbContext.
        var run = await RunMonthlyAsync(settlementMonth, settlementYear, cancellationToken);
        var reconciliation = await GetReconciliationAsync(settlementMonth, settlementYear, cancellationToken);

        var monthName = new DateTime(settlementYear, settlementMonth, 1).ToString("MMMM yyyy");
        var unmatched = reconciliation.Reg.UnmatchedCount + reconciliation.Merch.UnmatchedCount;
        var parityOk = run.RegSourceTrnsCount == run.RegConsolidatedTrnsCount
            && run.MerchSourceTrnsCount == run.MerchConsolidatedTrnsCount;

        var sent = await _email.SendAsync(new EmailMessageDto
        {
            FromName = "TSIC System",
            ToAddresses = [TsicConstants.SupportEmail],
            Subject = $"AdnMonthEndClose {monthName}"
                + (unmatched > 0 ? $" — {unmatched} UNMATCHED" : "")
                + (parityOk ? "" : " — IIF PARITY MISMATCH"),
            HtmlBody = BuildCloseEmailHtml(monthName, run, reconciliation, unmatched, parityOk),
            Attachments =
            [
                new EmailAttachmentDto
                {
                    FileName = run.Bundle.FileName,
                    Content = run.Bundle.FileBytes,
                    ContentType = run.Bundle.ContentType,
                },
            ],
        }, sendInDevelopment: true, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "AdnReconciliation: month-end close emailed for {Month} (sent={Sent}, zipBytes={Bytes}, " +
            "unmatched={Unmatched}, parityOk={ParityOk})",
            monthName, sent, run.Bundle.FileBytes.Length, unmatched, parityOk);

        return run;
    }

    /// <summary>
    /// Covering note for the close email: the two things that decide whether the .iif can be imported
    /// as-is — does every ADN dollar map to a client (the custodial match), and did every source TRNS
    /// survive consolidation (IIF parity). Everything else is in the attached workbook.
    /// </summary>
    private static string BuildCloseEmailHtml(
        string monthName,
        AdnReconciliationRunResult run,
        MonthEndReconciliationResult reconciliation,
        int unmatched,
        bool parityOk)
    {
        var sb = new StringBuilder();
        sb.Append($"<h3 style='margin-bottom:4px;'>ADN Month-End Close — {monthName}</h3>");
        sb.Append("<p style='font-size:11px;margin-top:0;'>The attached .zip holds both QuickBooks .iif files "
            + "(registration + merch), their backing .xlsx, and the human-readable summary.</p>");

        sb.Append(unmatched == 0
            ? "<p style='font-size:12px;color:#0a0;font-weight:bold;'>&#10003; Accounting match clean — every ADN transaction maps to a client.</p>"
            : $"<p style='font-size:12px;color:#b00;font-weight:bold;'>&#9888; {unmatched} ADN transaction(s) have no matching accounting row. Review before importing.</p>");

        if (!parityOk)
        {
            sb.Append("<p style='font-size:12px;color:#b00;font-weight:bold;'>&#9888; IIF TRNS parity mismatch — "
                + "a source transaction did not survive consolidation. Verify the .iif before importing.</p>");
        }

        sb.Append("<table style='border-collapse:separate;border-spacing:10px;font-size:11px;'>");
        sb.Append("<tr><th align='left'>Stack</th><th align='right'>ADN Txs</th><th align='right'>Matched</th>"
            + "<th align='right'>Unmatched</th><th align='right'>Paid</th><th align='right'>Credited</th>"
            + "<th align='right'>IIF TRNS</th></tr>");
        AppendStackRow(sb, "Registration", reconciliation.Reg, run.RegConsolidatedTrnsCount, run.RegSourceTrnsCount);
        AppendStackRow(sb, "Merch", reconciliation.Merch, run.MerchConsolidatedTrnsCount, run.MerchSourceTrnsCount);
        sb.Append("</table>");

        sb.Append($"<p style='font-size:9px;color:#666;'>Pulled {run.BatchesPulled} batch(es) / {run.TransactionsPulled} "
            + $"transaction(s); imported {run.Imported} new, skipped {run.SkippedDuplicates} duplicate(s).");
        if (reconciliation.LatestSettlementAt.HasValue)
        {
            sb.Append($" Settlements through {reconciliation.LatestSettlementAt.Value:g}.");
        }
        sb.Append("</p>");

        return sb.ToString();
    }

    private static void AppendStackRow(
        StringBuilder sb, string label, ReconciliationStackSummary s, int consolidatedTrns, int sourceTrns)
    {
        sb.Append("<tr>")
          .Append($"<td>{label}</td>")
          .Append($"<td align='right'>{s.TransactionCount}</td>")
          .Append($"<td align='right'>{s.MatchedCount}</td>")
          .Append($"<td align='right'>{(s.UnmatchedCount > 0 ? $"<b style='color:#b00;'>{s.UnmatchedCount}</b>" : "0")}</td>")
          .Append($"<td align='right'>{s.PaidTotal:C}</td>")
          .Append($"<td align='right'>{s.CreditTotal:C}</td>")
          .Append($"<td align='right'>{consolidatedTrns} / {sourceTrns}</td>")
          .Append("</tr>");
    }

    private static string MapStatus(string adnStatus) => adnStatus switch
    {
        "settledSuccessfully" => "Settled Successfully",
        "refundSettledSuccessfully" => "Credited",
        "voided" => "Voided",
        _ => string.Empty,
    };
}
