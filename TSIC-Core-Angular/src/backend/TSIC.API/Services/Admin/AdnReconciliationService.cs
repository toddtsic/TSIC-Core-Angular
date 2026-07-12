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
    private readonly IAdnSweepService _sweep;
    private readonly IEmailService _email;
    private readonly ILogger<AdnReconciliationService> _logger;

    public AdnReconciliationService(
        IAdnReconciliationRepository repo,
        IAdnApiService adnApi,
        IReportingService reportingService,
        IAdnSweepService sweep,
        IEmailService email,
        ILogger<AdnReconciliationService> logger)
    {
        _repo = repo;
        _adnApi = adnApi;
        _reportingService = reportingService;
        _sweep = sweep;
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

    public async Task<MonthEndCloseResult> RunMonthEndCloseWithSweepAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        // 1) The day's sweep, digest SUPPRESSED — from here on we own that send, and every path below
        //    must mail it. RunAsync reports its own failures on the result rather than throwing, so a
        //    throw here means something outside the sweep proper broke; treat that as untrustworthy.
        AdnSweepResult sweep;
        try
        {
            sweep = await _sweep.RunAsync("MonthEndClose", daysPrior: 0, sendDigest: false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Month-end close: the sweep threw before reporting");
            sweep = UnreportedSweep(ex.Message);
        }

        // 2) The close, gated on that sweep.
        try
        {
            var run = await EmailMonthlyCloseAsync(settlementMonth, settlementYear, sweep, cancellationToken);
            return new MonthEndCloseResult
            {
                Sweep = sweep,
                Close = run,
                FilesAttached = sweep.IsTrustworthy,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Month-end close for {Year}-{Month:D2} failed; mailing the sweep digest alone",
                settlementYear, settlementMonth);

            // The close blew up, but we are still holding the day's suppressed digest. Mail it.
            await SendDigestFallbackAsync(sweep, settlementMonth, settlementYear, ex, cancellationToken);

            return new MonthEndCloseResult
            {
                Sweep = sweep,
                Close = null,
                FilesAttached = false,
                CloseError = ex.Message,
            };
        }
    }

    /// <summary>Stand-in for a sweep that never reported. Fails the trust gate, so no files ship.</summary>
    private static AdnSweepResult UnreportedSweep(string error) => new()
    {
        Checked = 0,
        ArbImported = 0,
        EcheckSettled = 0,
        EcheckReturnsProcessed = 0,
        OrphansFound = 0,
        Errored = 0,
        Succeeded = false,
        ErrorMessage = $"The sweep failed before reporting: {error}. Nothing about this morning's booking "
            + "can be assumed.",
    };

    /// <summary>
    /// Last resort when the close itself throws: mail the digest the sweep handed us, with the close
    /// failure on top. Without this the suppressed digest would simply evaporate and the morning would
    /// go silent on the one day of the month that matters most.
    /// </summary>
    private async Task SendDigestFallbackAsync(
        AdnSweepResult sweep, int month, int year, Exception closeFailure, CancellationToken ct)
    {
        try
        {
            var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");
            var body =
                "<p style='font-size:13px;color:#b00;font-weight:bold;'>&#9888; The month-end close for "
                + $"{monthName} failed to run. No QuickBooks files were produced. Re-run it from "
                + "Accounting &rarr; ADN End-of-Month / IIF once the cause is fixed.</p>"
                + $"<p style='font-size:11px;color:#b00;'><b>Close error:</b> {closeFailure.Message}</p>"
                + "<hr style='margin:20px 0;border:0;border-top:1px solid #ccc;' />"
                + (sweep.DigestHtml ?? "<p style='font-size:11px;'>The sweep produced no digest either.</p>");

            await _email.SendAsync(new EmailMessageDto
            {
                FromName = "TSIC System",
                ToAddresses = [TsicConstants.SupportEmail],
                Subject = $"AdnSweep — {monthName} MONTH-END CLOSE FAILED",
                HtmlBody = body,
            }, sendInDevelopment: true, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Month-end fallback digest send failed — the morning report is LOST");
        }
    }

    public async Task<AdnReconciliationRunResult> EmailMonthlyCloseAsync(
        int settlementMonth,
        int settlementYear,
        AdnSweepResult? sweep = null,
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

        // THE GATE. The sweep books the closing month's last ARB/eCheck rows into the very tables the
        // export sprocs read. A sweep that did not fully succeed leaves those payments unbooked, so the
        // .iif is short them — importable, plausible, and wrong. Withhold the file; send the alarm.
        // A null sweep is the manual trigger: the operator is driving, so attach.
        var sweepTrustworthy = sweep?.IsTrustworthy ?? true;

        var subject = sweep != null
            ? $"AdnSweep + MonthEndClose — {monthName}"
            : $"AdnMonthEndClose {monthName}";
        if (!sweepTrustworthy) subject += " — SWEEP FAILED, NO FILES";
        if (unmatched > 0) subject += $" — {unmatched} UNMATCHED";
        if (!parityOk) subject += " — IIF PARITY MISMATCH";

        var message = new EmailMessageDto
        {
            FromName = "TSIC System",
            ToAddresses = [TsicConstants.SupportEmail],
            Subject = subject,
            HtmlBody = BuildCloseEmailHtml(monthName, run, reconciliation, unmatched, parityOk, sweep, sweepTrustworthy),
        };

        if (sweepTrustworthy)
        {
            message.Attachments.Add(new EmailAttachmentDto
            {
                FileName = run.Bundle.FileName,
                Content = run.Bundle.FileBytes,
                ContentType = run.Bundle.ContentType,
            });
        }

        var sent = await _email.SendAsync(message, sendInDevelopment: true, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "AdnReconciliation: month-end close emailed for {Month} (sent={Sent}, attached={Attached}, " +
            "zipBytes={Bytes}, unmatched={Unmatched}, parityOk={ParityOk}, sweepTrustworthy={SweepOk})",
            monthName, sent, sweepTrustworthy, run.Bundle.FileBytes.Length, unmatched, parityOk, sweepTrustworthy);

        return run;
    }

    /// <summary>
    /// The one email for the 1st: the day's sweep digest on top (when the close is riding the sweep),
    /// then the close verdict — does every ADN dollar map to a client, did every source TRNS survive
    /// consolidation, and is the attached export trustworthy at all.
    /// </summary>
    private static string BuildCloseEmailHtml(
        string monthName,
        AdnReconciliationRunResult run,
        MonthEndReconciliationResult reconciliation,
        int unmatched,
        bool parityOk,
        AdnSweepResult? sweep,
        bool sweepTrustworthy)
    {
        var sb = new StringBuilder();
        sb.Append($"<h3 style='margin-bottom:4px;'>ADN Month-End Close — {monthName}</h3>");

        // The headline. Everything below is unreliable if this fired, so it goes first.
        if (!sweepTrustworthy)
        {
            sb.Append("<p style='font-size:13px;color:#b00;font-weight:bold;margin:8px 0;'>"
                + "&#9888; NO FILES ATTACHED — this morning's sweep did not fully succeed, so the closing "
                + "month's final payments may not be booked. Any QuickBooks export built now would be short "
                + "those transactions. Fix the sweep, then re-run the close from "
                + "Accounting &rarr; ADN End-of-Month / IIF (or POST /api/adn-reconciliation/email-close).</p>");
            if (sweep?.ErrorMessage != null)
            {
                sb.Append($"<p style='font-size:11px;color:#b00;margin:0 0 8px 0;'><b>Sweep error:</b> {sweep.ErrorMessage}</p>");
            }
            else if (sweep is { Errored: > 0 })
            {
                sb.Append($"<p style='font-size:11px;color:#b00;margin:0 0 8px 0;'>"
                    + $"<b>Sweep:</b> completed, but {sweep.Errored} transaction(s) errored and are not booked.</p>");
            }
        }
        else
        {
            sb.Append("<p style='font-size:11px;margin-top:0;'>The attached .zip holds both QuickBooks .iif files "
                + "(registration + merch), their backing .xlsx, and the human-readable summary.</p>");
        }

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

        // The day's sweep digest, folded in below the close — one email on the 1st, not two. The sweep
        // suppressed its own send and handed us the rendered HTML, so this is the identical report you
        // get on the other 30 mornings, verbatim.
        if (!string.IsNullOrEmpty(sweep?.DigestHtml))
        {
            sb.Append("<hr style='margin:20px 0;border:0;border-top:1px solid #ccc;' />");
            sb.Append(sweep.DigestHtml);
        }

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
