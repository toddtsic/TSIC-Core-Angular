using System.Text;
using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.Options;
using TSIC.API.Configuration;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Sweep;

/// <summary>
/// Daily ADN reconciliation. Walks settled batches, imports ARB recurring transactions,
/// and processes eCheck returns. Mirrors legacy AdnArbSweepService.DoWorkAsync behavior
/// with eCheck handling added on top.
/// </summary>
public sealed class AdnSweepService : IAdnSweepService
{
    // Match legacy hard-coded GUIDs (canonical reference.Accounting_PaymentMethods rows).
    private static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
    // "Failed E-Check Payment" — used for NSF reversal RA rows.
    private static readonly Guid FailedEcheckPaymentMethodId = Guid.Parse("2FECA575-A268-E111-9D56-F04DA202060D");
    // Stamp system-written rows with TSICSuperUser (FK to dbo.AspNetUsers). Legacy
    // wrote _appSettings.TSICParams.SuperUserId here for the same reason.
    private const string SystemUserId = TsicConstants.SuperUserId;

    private readonly IEcheckSettlementRepository _settleRepo;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly IRegistrationRepository _regRepo;
    private readonly IArbSubscriptionRepository _arbRepo;
    private readonly IAdnApiService _adn;
    private readonly IRegistrationFeeAdjustmentService _feeAdj;
    private readonly IEmailService _email;
    private readonly TsicSettings _tsicSettings;
    private readonly AdnSweepOptions _options;
    private readonly ILogger<AdnSweepService> _logger;

    public AdnSweepService(
        IEcheckSettlementRepository settleRepo,
        IRegistrationAccountingRepository accountingRepo,
        IRegistrationRepository regRepo,
        IArbSubscriptionRepository arbRepo,
        IAdnApiService adn,
        IRegistrationFeeAdjustmentService feeAdj,
        IEmailService email,
        IOptions<TsicSettings> tsicSettings,
        IOptions<AdnSweepOptions> options,
        ILogger<AdnSweepService> logger)
    {
        _settleRepo = settleRepo;
        _accountingRepo = accountingRepo;
        _regRepo = regRepo;
        _arbRepo = arbRepo;
        _adn = adn;
        _feeAdj = feeAdj;
        _email = email;
        _tsicSettings = tsicSettings.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdnSweepResult> RunAsync(string triggeredBy, int daysPrior = 0, CancellationToken ct = default)
    {
        if (daysPrior <= 0) daysPrior = _options.DaysPriorWindow;

        var log = await _settleRepo.StartSweepLogAsync(triggeredBy, ct);
        var counts = new Counts();
        string? errorMessage = null;
        var arbRows = new List<ArbDigestRow>();
        var ecRows = new List<EcheckReturnDigestRow>();
        var settledRows = new List<EcheckSettledDigestRow>();

        try
        {
            var creds = await _adn.GetJobAdnCredentials_FromCustomerId(_tsicSettings.DefaultCustomerId, bProdOnly: true);
            var env = _adn.GetADNEnvironment(bProdOnly: true);

            // 1) Walk batches, accumulate flat tx list.
            var allTxs = FetchBatchTransactions(env, creds.AdnLoginId!, creds.AdnTransactionKey!, daysPrior);
            counts.Checked = allTxs.Count;

            // 2) Process ARB transactions (legacy parity).
            foreach (var tx in allTxs.Where(IsArbCandidate))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var row = await ImportArbTransactionAsync(tx, env, creds, ct);
                    if (row != null)
                    {
                        counts.ArbImported++;
                        arbRows.Add(row);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ARB import failed for tx {TxId}", tx.transId);
                    counts.Errored++;
                }
            }

            // 3) Process eCheck Pending → Settled transitions.
            // Walk batch txs that settled successfully and match against our pending Settlement
            // rows. No per-tx API call is needed — presence in a settled batch is the proof of
            // settlement. Settlement.Status flips to "Settled"; money movement already happened
            // at submission time (optimistic credit), so no RegistrationAccounting changes here.
            var settledTxIds = allTxs
                .Where(t => t.transactionStatus == "settledSuccessfully" && !string.IsNullOrEmpty(t.transId))
                .Select(t => t.transId)
                .Distinct()
                .ToList();
            if (settledTxIds.Count > 0)
            {
                var pendingSettlements = (await _settleRepo.GetByAdnTransactionIdsAsync(settledTxIds, ct))
                    .Where(s => s.Status == "Pending")
                    .ToList();
                foreach (var settlement in pendingSettlements)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var row = MarkEcheckSettled(settlement);
                        if (row != null)
                        {
                            counts.EcheckSettled++;
                            settledRows.Add(row);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "eCheck settled processing failed for settlement {Id}",
                            settlement.SettlementId);
                        counts.Errored++;
                    }
                }
                if (counts.EcheckSettled > 0)
                    await _settleRepo.SaveChangesAsync(ct);
            }

            // 4) Process eCheck returns.
            foreach (var tx in allTxs.Where(t => t.transactionStatus == "returnedItem"))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var row = await ProcessEcheckReturnAsync(tx, env, creds, ct);
                    if (row != null)
                    {
                        counts.EcheckReturnsProcessed++;
                        ecRows.Add(row);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "eCheck return processing failed for tx {TxId}", tx.transId);
                    counts.Errored++;
                }
            }

            // 5) Build + send digest email.
            var html = BuildDigestHtml(arbRows, settledRows, ecRows, counts);
            await SendDigestAsync(html, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ADN sweep run failed");
            errorMessage = ex.Message;
        }

        await _settleRepo.CompleteSweepLogAsync(
            log, counts.Checked, counts.EcheckSettled, counts.EcheckReturnsProcessed, counts.Errored, errorMessage, ct);

        return new AdnSweepResult
        {
            Checked = counts.Checked,
            ArbImported = counts.ArbImported,
            EcheckSettled = counts.EcheckSettled,
            EcheckReturnsProcessed = counts.EcheckReturnsProcessed,
            Errored = counts.Errored
        };
    }

    // ── Batch fetching ────────────────────────────────────────────────

    private List<transactionSummaryType> FetchBatchTransactions(
        AuthorizeNet.Environment env, string loginId, string transactionKey, int daysPrior)
    {
        var first = DateTime.Today.Subtract(TimeSpan.FromDays(daysPrior));
        var last = DateTime.Today;

        var batchResp = _adn.GetSettleBatchList_FromDateRange(env, loginId, transactionKey, first, last, true);
        if (batchResp?.messages?.resultCode != messageTypeEnum.Ok || batchResp.batchList == null)
        {
            _logger.LogWarning("ADN GetSettleBatchList returned no batches for {Days}d window", daysPrior);
            return [];
        }

        var all = new List<transactionSummaryType>();
        foreach (var batch in batchResp.batchList)
        {
            var txResp = _adn.GetTransactionList_ByBatchId(env, loginId, transactionKey, batch.batchId);
            if (txResp?.messages?.resultCode == messageTypeEnum.Ok && txResp.transactions != null)
            {
                all.AddRange(txResp.transactions);
            }
        }
        return all;
    }

    private static bool IsArbCandidate(transactionSummaryType tx)
    {
        return !string.IsNullOrEmpty(tx.invoiceNumber)
            && tx.subscription != null
            && !string.IsNullOrEmpty(tx.transId)
            && tx.invoiceNumber.Split('_').Length == 3
            && (tx.transactionStatus == "settledSuccessfully"
                || tx.transactionStatus == "declined"
                || tx.transactionStatus == "generalError");
    }

    // ── ARB import (legacy parity) ────────────────────────────────────

    private async Task<ArbDigestRow?> ImportArbTransactionAsync(
        transactionSummaryType tx, AuthorizeNet.Environment env, AdnCredentialsViewModel creds, CancellationToken ct)
    {
        // Skip if already imported.
        if (await _accountingRepo.AnyByAdnTransactionIdAsync(tx.transId, ct))
        {
            _logger.LogDebug("ARB tx {TxId} already imported, skipping", tx.transId);
            return null;
        }

        // Resolve registration via subscription id.
        var reg = await _regRepo.GetByAdnSubscriptionIdAsync(tx.subscription.id.ToString(), ct);
        if (reg == null)
        {
            _logger.LogWarning("ARB tx {TxId} has no matching registration for subscription {SubId}",
                tx.transId, tx.subscription.id);
            return null;
        }

        // Sync ADN-known subscription status to registration record (only on Ok response).
        var subStatusResp = _adn.GetSubscriptionStatus(env, creds.AdnLoginId!, creds.AdnTransactionKey!, reg.AdnSubscriptionId!);
        if (subStatusResp?.messages?.resultCode == messageTypeEnum.Ok)
        {
            var liveSubStatus = subStatusResp.status.ToString();
            if (!string.IsNullOrEmpty(liveSubStatus) && reg.AdnSubscriptionStatus != liveSubStatus)
            {
                await _arbRepo.UpdateSubscriptionStatusAsync(reg.RegistrationId, liveSubStatus, ct);
                reg.AdnSubscriptionStatus = liveSubStatus;
            }
        }
        else
        {
            _logger.LogWarning("ARB tx {TxId}: GetSubscriptionStatus returned non-Ok ({Code}); leaving local status as-is",
                tx.transId, subStatusResp?.messages?.resultCode);
        }

        // Pull tx details for CC last-4 / expiry.
        var txDetail = _adn.ADN_GetTransactionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, tx.transId);
        if (txDetail?.messages?.resultCode != messageTypeEnum.Ok || txDetail.transaction == null)
        {
            _logger.LogWarning("ARB tx {TxId}: GetTransactionDetails returned no data", tx.transId);
            return null;
        }

        string? cc4 = null, ccExp = null;
        if (txDetail.transaction.payment?.Item is creditCardMaskedType cc)
        {
            cc4 = cc.cardNumber?.Length >= 4 ? cc.cardNumber[^4..] : null;
            ccExp = cc.expirationDate;
        }

        var settleAmount = tx.transactionStatus == "settledSuccessfully" ? tx.settleAmount : 0;

        _accountingRepo.Add(new RegistrationAccounting
        {
            RegistrationId = reg.RegistrationId,
            Active = true,
            AdnCc4 = cc4,
            AdnCcexpDate = ccExp,
            AdnInvoiceNo = tx.invoiceNumber,
            AdnTransactionId = tx.transId,
            Dueamt = settleAmount,
            Payamt = settleAmount,
            Paymeth = $"paid by cc: {settleAmount:C} on subscriptionId: {tx.subscription.id} on {tx.submitTimeLocal:G} txID: {tx.transId}",
            PaymentMethodId = CcPaymentMethodId,
            Comment = $"{tx.transactionStatus} (subscriptionId: {tx.subscription.id} {txDetail.transaction.responseReasonDescription})",
            Createdate = DateTime.Now,
            Modified = DateTime.Now,
            LebUserId = SystemUserId
        });

        if (tx.transactionStatus == "settledSuccessfully")
        {
            reg.PaidTotal += settleAmount;
            reg.OwedTotal -= settleAmount;
            reg.Modified = DateTime.Now;
            reg.LebUserId = reg.FamilyUserId;
        }

        await _accountingRepo.SaveChangesAsync(ct);

        // Compute installment math for digest.
        var (owedNow, paymentXofY, nextInstallment) = ComputeInstallmentMath(reg);

        return new ArbDigestRow
        {
            JobName = reg.Job?.DisplayName ?? reg.Job?.JobName ?? "",
            TransId = tx.transId,
            SubscriptionId = tx.subscription.id.ToString(),
            SubscriptionStatus = reg.AdnSubscriptionStatus,
            SettleAmount = settleAmount,
            TransactionStatus = tx.transactionStatus,
            OwedNow = owedNow,
            PaymentXofY = paymentXofY,
            NextInstallment = nextInstallment,
            Registrant = reg.UserId,
            RegistrantAssignment = reg.Assignment
        };
    }

    // ── eCheck Pending → Settled ─────────────────────────────────────

    private EcheckSettledDigestRow? MarkEcheckSettled(Settlement settlement)
    {
        var ra = settlement.RegistrationAccounting;
        var reg = ra.Registration;
        if (reg == null)
        {
            _logger.LogWarning("Settlement {Id}: no Registration loaded — corrupt state, skipping",
                settlement.SettlementId);
            return null;
        }

        var now = DateTime.UtcNow;
        settlement.Status = "Settled";
        settlement.SettledAt = now;
        settlement.LastCheckedAt = now;
        settlement.Modified = now;

        return new EcheckSettledDigestRow
        {
            JobName = reg.Job?.DisplayName ?? reg.Job?.JobName ?? "",
            TransId = settlement.AdnTransactionId,
            Amount = ra.Payamt ?? 0m,
            AccountLast4 = settlement.AccountLast4 ?? "",
            Registrant = reg.UserId,
            SubmittedAt = settlement.SubmittedAt,
            SettledAt = now
        };
    }

    // ── eCheck return processing ──────────────────────────────────────

    private async Task<EcheckReturnDigestRow?> ProcessEcheckReturnAsync(
        transactionSummaryType returnTx, AuthorizeNet.Environment env, AdnCredentialsViewModel creds, CancellationToken ct)
    {
        // Skip if we already wrote a reversal for this return.
        if (await _accountingRepo.AnyByAdnTransactionIdAsync(returnTx.transId, ct))
        {
            _logger.LogDebug("eCheck return {TxId} already processed, skipping", returnTx.transId);
            return null;
        }

        // GetTransactionDetails to find refTransId (the original we submitted).
        var detail = _adn.ADN_GetTransactionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, returnTx.transId);
        if (detail?.messages?.resultCode != messageTypeEnum.Ok || detail.transaction == null)
        {
            _logger.LogWarning("eCheck return {TxId}: GetTransactionDetails failed", returnTx.transId);
            return null;
        }

        var originalTxId = detail.transaction.refTransId;
        if (string.IsNullOrEmpty(originalTxId))
        {
            _logger.LogWarning("eCheck return {TxId}: no refTransId — cannot link to original", returnTx.transId);
            return null;
        }

        // Look up Settlement by original tx id.
        var settlements = await _settleRepo.GetByAdnTransactionIdsAsync([originalTxId], ct);
        var settlement = settlements.FirstOrDefault();
        if (settlement == null)
        {
            _logger.LogWarning("eCheck return {TxId}: original {OrigTxId} not in echeck.Settlement — not ours, skipping",
                returnTx.transId, originalTxId);
            return null;
        }

        var ra = settlement.RegistrationAccounting;
        var reg = ra.Registration;
        if (reg == null)
        {
            _logger.LogWarning("Settlement {Id}: no Registration loaded — corrupt state, skipping",
                settlement.SettlementId);
            return null;
        }

        var amount = ra.Payamt ?? 0m;
        if (amount <= 0m)
        {
            _logger.LogWarning("Settlement {Id} original payamt non-positive; reversal skipped",
                settlement.SettlementId);
            return null;
        }

        var now = DateTime.UtcNow;

        // Mark Settlement returned.
        settlement.Status = "Returned";
        settlement.ReturnReasonCode = detail.transaction.responseReasonCode.ToString();
        settlement.ReturnReasonText = detail.transaction.responseReasonDescription;
        settlement.LastCheckedAt = now;
        settlement.Modified = now;

        // Reverse payment on the registration; recompute totals after fee restore.
        reg.PaidTotal -= amount;
        await _feeAdj.ReverseProcessingFeeForEcheckAsync(reg, amount, reg.JobId, SystemUserId);
        reg.FeeTotal = reg.FeeBase + reg.FeeProcessing - reg.FeeDiscount + reg.FeeDonation + reg.FeeLatefee;
        reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
        reg.Modified = now;
        reg.LebUserId = SystemUserId;

        // Write the reversal RA row.
        _accountingRepo.Add(new RegistrationAccounting
        {
            RegistrationId = ra.RegistrationId,
            TeamId = ra.TeamId,
            PaymentMethodId = FailedEcheckPaymentMethodId,
            Payamt = -amount,
            Dueamt = 0,
            Comment = $"NSF return — original aID {ra.AId}, reason: {settlement.ReturnReasonCode} {settlement.ReturnReasonText}",
            AdnTransactionId = returnTx.transId,
            Active = true,
            Createdate = now,
            Modified = now,
            LebUserId = SystemUserId
        });

        await _settleRepo.SaveChangesAsync(ct);

        // Best-effort director email — capture success for the digest.
        bool directorNotified;
        try
        {
            directorNotified = await SendDirectorAlertAsync(reg.JobId, ra, settlement, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NSF director alert failed for settlement {Id}", settlement.SettlementId);
            directorNotified = false;
        }

        return new EcheckReturnDigestRow
        {
            JobName = reg.Job?.DisplayName ?? reg.Job?.JobName ?? "",
            ReturnTxId = returnTx.transId,
            OriginalTxId = originalTxId,
            Reason = $"{settlement.ReturnReasonText} ({settlement.ReturnReasonCode})",
            AmountReversed = amount,
            Registrant = reg.UserId,
            DirectorNotified = directorNotified
        };
    }

    private async Task<bool> SendDirectorAlertAsync(
        Guid jobId, RegistrationAccounting originalRa, Settlement settlement, CancellationToken ct)
    {
        var director = await _regRepo.GetDirectorContactForJobAsync(jobId, ct);
        if (director == null || string.IsNullOrEmpty(director.Email))
        {
            _logger.LogWarning("No director contact for job {JobId}; NSF alert not sent", jobId);
            return false;
        }

        var amountStr = (originalRa.Payamt ?? 0m).ToString("F2");
        var body = $@"<p>An eCheck has been returned by the bank and the registration's balance was automatically restored.</p>
<ul>
<li><b>Amount:</b> ${amountStr}</li>
<li><b>Reason:</b> {settlement.ReturnReasonText} (code {settlement.ReturnReasonCode})</li>
<li><b>Original transaction ID:</b> {settlement.AdnTransactionId}</li>
</ul>
<p>The registration remains active. You may wish to contact the customer.</p>";

        await _email.SendAsync(new EmailMessageDto
        {
            FromName = "TSIC System",
            FromAddress = TsicConstants.SupportEmail,
            ToAddresses = [director.Email],
            Subject = $"eCheck NSF return — ${amountStr}",
            HtmlBody = body
        }, sendInDevelopment: false, cancellationToken: ct);
        return true;
    }

    // ── Installment math (legacy parity) ──────────────────────────────

    private (decimal OwedNow, string PaymentXofY, DateTime? NextInstallment) ComputeInstallmentMath(Registrations reg)
    {
        if (reg.AdnSubscriptionStartDate == null
            || reg.AdnSubscriptionIntervalLength == null
            || reg.AdnSubscriptionBillingOccurences == null
            || reg.AdnSubscriptionAmountPerOccurence == null)
        {
            return (0m, "", null);
        }

        var totalOcc = reg.AdnSubscriptionBillingOccurences.Value;
        var startDate = reg.AdnSubscriptionStartDate.Value;
        var interval = reg.AdnSubscriptionIntervalLength.Value;
        var amt = reg.AdnSubscriptionAmountPerOccurence.Value;

        var dates = Enumerable.Range(0, totalOcc).Select(i => startDate.AddMonths(i * interval)).ToList();
        var occAsOfNow = dates.Count(d => d.Date <= DateTime.Now.Date);

        var sumAllArbFees = amt * totalOcc;
        var sumAllArbFeesAsOfNow = amt * occAsOfNow;
        var sumAllNonArbFees = reg.FeeTotal - sumAllArbFees;
        var owedNow = (occAsOfNow <= 1)
            ? 0m
            : sumAllArbFeesAsOfNow + sumAllNonArbFees - reg.PaidTotal;

        var paymentXofY = $"{occAsOfNow}/{totalOcc}";
        var nextInstallment = occAsOfNow < dates.Count ? dates[occAsOfNow] : (DateTime?)null;

        return (owedNow > 0 ? owedNow : 0m, paymentXofY, nextInstallment);
    }

    // ── Digest email ──────────────────────────────────────────────────

    private string BuildDigestHtml(
        List<ArbDigestRow> arbRows,
        List<EcheckSettledDigestRow> settledRows,
        List<EcheckReturnDigestRow> ecRows,
        Counts counts)
    {
        var envType = "PROD";
#if DEBUG
        envType = "DEV";
#endif
        var sb = new StringBuilder();
        sb.Append($"<h3 style='margin-bottom:4px;'>ADN Sweep ({envType}, TSIC) — {DateTime.Now:dddd, dd MMMM yyyy HH:mm}</h3>");
        sb.Append($"<p style='font-size:9px;margin-top:0;'>Counts — Checked: {counts.Checked}, ARB imported: {counts.ArbImported}, eCheck settled: {counts.EcheckSettled}, eCheck returns: {counts.EcheckReturnsProcessed}, Errored: {counts.Errored}</p>");

        // ── ARB Activity table ────────────────────────────────────────
        sb.Append("<h4 style='margin-bottom:2px;'>ARB Activity</h4>");
        if (arbRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(no ARB transactions imported this run)</p>");
        }
        else
        {
            sb.Append("<table style='border-style:solid;border-collapse:separate;border-spacing:10px;font-size:9px;'>");
            sb.Append("<tr><th>#</th><th>Job</th><th>TransId</th><th>SubId</th><th>SubStatus</th><th>Amount</th><th>Status</th><th>OwedNow</th><th>PaymentXofY</th><th>NextInstallment</th><th>Registrant</th><th>Assignment</th></tr>");
            for (int i = 0; i < arbRows.Count; i++)
            {
                var r = arbRows[i];
                sb.Append("<tr>")
                  .Append($"<td>{i + 1}</td>")
                  .Append($"<td>{r.JobName}</td>")
                  .Append($"<td>{r.TransId}</td>")
                  .Append($"<td>{r.SubscriptionId}</td>")
                  .Append($"<td>{r.SubscriptionStatus}</td>")
                  .Append($"<td>{r.SettleAmount:C}</td>")
                  .Append($"<td>{r.TransactionStatus}</td>")
                  .Append($"<td>{r.OwedNow:C}</td>")
                  .Append($"<td>{r.PaymentXofY}</td>")
                  .Append($"<td>{(r.NextInstallment.HasValue ? r.NextInstallment.Value.ToString("d") : "")}</td>")
                  .Append($"<td>{r.Registrant}</td>")
                  .Append($"<td>{r.RegistrantAssignment}</td>")
                  .Append("</tr>");
            }
            sb.Append("</table>");
        }

        // ── eCheck Settled table ──────────────────────────────────────
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>eCheck Settled (Pending → Settled)</h4>");
        if (settledRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(no eCheck settlements transitioned this run)</p>");
        }
        else
        {
            sb.Append("<table style='border-style:solid;border-collapse:separate;border-spacing:10px;font-size:9px;'>");
            sb.Append("<tr><th>#</th><th>Job</th><th>TransId</th><th>Amount</th><th>Acct ****</th><th>Registrant</th><th>Submitted</th><th>Settled</th></tr>");
            for (int i = 0; i < settledRows.Count; i++)
            {
                var r = settledRows[i];
                sb.Append("<tr>")
                  .Append($"<td>{i + 1}</td>")
                  .Append($"<td>{r.JobName}</td>")
                  .Append($"<td>{r.TransId}</td>")
                  .Append($"<td>{r.Amount:C}</td>")
                  .Append($"<td>****{r.AccountLast4}</td>")
                  .Append($"<td>{r.Registrant}</td>")
                  .Append($"<td>{r.SubmittedAt:g}</td>")
                  .Append($"<td>{r.SettledAt:g}</td>")
                  .Append("</tr>");
            }
            sb.Append("</table>");
        }

        // ── eCheck Returns table ──────────────────────────────────────
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>eCheck Returns</h4>");
        if (ecRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(no eCheck returns this run)</p>");
        }
        else
        {
            sb.Append("<table style='border-style:solid;border-collapse:separate;border-spacing:10px;font-size:9px;'>");
            sb.Append("<tr><th>#</th><th>Job</th><th>Original TransId</th><th>Return TransId</th><th>Reason</th><th>Amount Reversed</th><th>Registrant</th><th>Director Notified</th></tr>");
            for (int i = 0; i < ecRows.Count; i++)
            {
                var r = ecRows[i];
                sb.Append("<tr>")
                  .Append($"<td>{i + 1}</td>")
                  .Append($"<td>{r.JobName}</td>")
                  .Append($"<td>{r.OriginalTxId}</td>")
                  .Append($"<td>{r.ReturnTxId}</td>")
                  .Append($"<td>{r.Reason}</td>")
                  .Append($"<td>{r.AmountReversed:C}</td>")
                  .Append($"<td>{r.Registrant}</td>")
                  .Append($"<td>{(r.DirectorNotified ? "yes" : "NO")}</td>")
                  .Append("</tr>");
            }
            sb.Append("</table>");
        }

        return sb.ToString();
    }

    private async Task SendDigestAsync(string html, CancellationToken ct)
    {
        await _email.SendAsync(new EmailMessageDto
        {
            FromName = "TSIC System",
            FromAddress = TsicConstants.SupportEmail,
            ToAddresses = [TsicConstants.SupportEmail],
            Subject = $"AdnSweep {DateTime.Now:dddd, dd MMMM yyyy HH:mm}",
            HtmlBody = html
        }, sendInDevelopment: false, cancellationToken: ct);
    }

    // ── Internal types ────────────────────────────────────────────────

    private sealed class Counts
    {
        public int Checked;
        public int ArbImported;
        public int EcheckSettled;
        public int EcheckReturnsProcessed;
        public int Errored;
    }

    private sealed record ArbDigestRow
    {
        public required string JobName { get; init; }
        public required string TransId { get; init; }
        public required string SubscriptionId { get; init; }
        public required string? SubscriptionStatus { get; init; }
        public required decimal SettleAmount { get; init; }
        public required string TransactionStatus { get; init; }
        public required decimal OwedNow { get; init; }
        public required string PaymentXofY { get; init; }
        public required DateTime? NextInstallment { get; init; }
        public required string? Registrant { get; init; }
        public required string? RegistrantAssignment { get; init; }
    }

    private sealed record EcheckReturnDigestRow
    {
        public required string JobName { get; init; }
        public required string ReturnTxId { get; init; }
        public required string OriginalTxId { get; init; }
        public required string Reason { get; init; }
        public required decimal AmountReversed { get; init; }
        public required string? Registrant { get; init; }
        public required bool DirectorNotified { get; init; }
    }

    private sealed record EcheckSettledDigestRow
    {
        public required string JobName { get; init; }
        public required string TransId { get; init; }
        public required decimal Amount { get; init; }
        public required string AccountLast4 { get; init; }
        public required string? Registrant { get; init; }
        public required DateTime SubmittedAt { get; init; }
        public required DateTime SettledAt { get; init; }
    }
}
