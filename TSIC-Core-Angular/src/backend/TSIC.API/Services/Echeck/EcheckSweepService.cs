using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.Options;
using TSIC.API.Configuration;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Echeck;

public sealed class EcheckSweepService : IEcheckSweepService
{
    private static readonly Guid EcheckReturnMethodId = Guid.Parse("2FECA575-A268-E111-9D56-F04DA202060D");
    private const string SystemUserId = "system-echeck-sweep";

    private readonly IEcheckSettlementRepository _settleRepo;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly IRegistrationRepository _regRepo;
    private readonly IAdnApiService _adn;
    private readonly IRegistrationFeeAdjustmentService _feeAdj;
    private readonly IEmailService _email;
    private readonly EcheckSweepOptions _options;
    private readonly ILogger<EcheckSweepService> _logger;

    public EcheckSweepService(
        IEcheckSettlementRepository settleRepo,
        IRegistrationAccountingRepository accountingRepo,
        IRegistrationRepository regRepo,
        IAdnApiService adn,
        IRegistrationFeeAdjustmentService feeAdj,
        IEmailService email,
        IOptions<EcheckSweepOptions> options,
        ILogger<EcheckSweepService> logger)
    {
        _settleRepo = settleRepo;
        _accountingRepo = accountingRepo;
        _regRepo = regRepo;
        _adn = adn;
        _feeAdj = feeAdj;
        _email = email;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EcheckSweepResult> RunAsync(string triggeredBy, CancellationToken ct = default)
    {
        var log = await _settleRepo.StartSweepLogAsync(triggeredBy, ct);
        var counts = new Counts();
        string? errorMessage = null;

        try
        {
            var now = DateTime.UtcNow;
            var pending = await _settleRepo.GetPendingDueAsync(now, ct);
            _logger.LogInformation("eCheck sweep: {Count} pending records due", pending.Count);

            foreach (var settlement in pending)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var outcome = await ProcessSettlementAsync(settlement, now, ct);
                    counts.Checked++;
                    switch (outcome)
                    {
                        case Outcome.Settled: counts.Settled++; break;
                        case Outcome.Returned: counts.Returned++; break;
                        case Outcome.Errored: counts.Errored++; break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sweep failed for settlement {SettlementId} (tx {TxId})",
                        settlement.SettlementId, settlement.AdnTransactionId);
                    counts.Errored++;
                }
            }

            // Persist all the per-record mutations from the loop in one batch.
            await _settleRepo.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "eCheck sweep run failed");
            errorMessage = ex.Message;
        }

        await _settleRepo.CompleteSweepLogAsync(
            log, counts.Checked, counts.Settled, counts.Returned, counts.Errored, errorMessage, ct);

        return new EcheckSweepResult
        {
            Checked = counts.Checked,
            Settled = counts.Settled,
            Returned = counts.Returned,
            Errored = counts.Errored
        };
    }

    private async Task<Outcome> ProcessSettlementAsync(Settlement settlement, DateTime now, CancellationToken ct)
    {
        var ra = settlement.RegistrationAccounting;
        var reg = ra.Registration;
        if (reg == null)
        {
            _logger.LogWarning("Settlement {Id} missing registration; cannot resolve credentials",
                settlement.SettlementId);
            PushNextCheck(settlement, now);
            return Outcome.Errored;
        }

        var creds = await _adn.GetJobAdnCredentials_FromJobId(reg.JobId, bProdOnly: true);
        var env = _adn.GetADNEnvironment(bProdOnly: true);

        var resp = _adn.ADN_GetTransactionDetails(env, creds.AdnLoginId, creds.AdnTransactionKey, settlement.AdnTransactionId);
        if (resp == null || resp.messages.resultCode != messageTypeEnum.Ok || resp.transaction == null)
        {
            _logger.LogWarning("ADN returned no transaction details for tx {TxId}", settlement.AdnTransactionId);
            PushNextCheck(settlement, now);
            return Outcome.Errored;
        }

        switch (resp.transaction.transactionStatus)
        {
            case "settledSuccessfully":
                settlement.Status = "Settled";
                settlement.SettledAt = now;
                settlement.LastCheckedAt = now;
                settlement.Modified = now;
                return Outcome.Settled;

            case "settlementError":
            case "returnedItem":
            case "voided":
                await ApplyReturnedAsync(settlement, ra, reg, resp.transaction, now, ct);
                return Outcome.Returned;

            case "capturedPendingSettlement":
                PushNextCheck(settlement, now);
                return Outcome.Checked;

            default:
                _logger.LogWarning("Unrecognized ADN status {Status} for tx {TxId}; leaving Pending",
                    resp.transaction.transactionStatus, settlement.AdnTransactionId);
                PushNextCheck(settlement, now);
                return Outcome.Errored;
        }
    }

    private async Task ApplyReturnedAsync(
        Settlement settlement, RegistrationAccounting originalRa, Registrations reg,
        transactionDetailsType txn, DateTime now, CancellationToken ct)
    {
        settlement.Status = "Returned";
        settlement.ReturnReasonCode = txn.responseReasonCode.ToString();
        settlement.ReturnReasonText = txn.responseReasonDescription;
        settlement.LastCheckedAt = now;
        settlement.Modified = now;

        var amount = originalRa.Payamt ?? 0m;
        if (amount <= 0m)
        {
            _logger.LogWarning("Settlement {Id} original payamt non-positive; reversal skipped",
                settlement.SettlementId);
            return;
        }

        // Reverse payment on the registration.
        reg.PaidTotal -= amount;
        reg.OwedTotal += amount;
        reg.Modified = now;
        reg.LebUserId = SystemUserId;

        // Reverse the eCheck fee credit applied at submission.
        await _feeAdj.ReverseProcessingFeeForEcheckAsync(reg, amount, reg.JobId, SystemUserId);

        // Recompute totals after the fee restore.
        reg.FeeTotal = reg.FeeBase + reg.FeeProcessing - reg.FeeDiscount + reg.FeeDonation + reg.FeeLatefee;
        reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;

        // Insert reversal RA row (E-Check Return method, negative amount, links to original via comment).
        _accountingRepo.Add(new RegistrationAccounting
        {
            RegistrationId = originalRa.RegistrationId,
            TeamId = originalRa.TeamId,
            PaymentMethodId = EcheckReturnMethodId,
            Payamt = -amount,
            Dueamt = 0,
            Comment = $"NSF return — original aID {originalRa.AId}, reason: {settlement.ReturnReasonCode} {settlement.ReturnReasonText}",
            AdnTransactionId = settlement.AdnTransactionId,
            Active = true,
            Createdate = now,
            Modified = now,
            LebUserId = SystemUserId
        });

        // Best-effort director alert — never fail the sweep on email problems.
        try
        {
            await SendDirectorAlertAsync(reg.JobId, originalRa, settlement, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send NSF director alert for settlement {Id}", settlement.SettlementId);
        }
    }

    private async Task SendDirectorAlertAsync(
        Guid jobId, RegistrationAccounting originalRa, Settlement settlement, CancellationToken ct)
    {
        var director = await _regRepo.GetDirectorContactForJobAsync(jobId, ct);
        if (director == null || string.IsNullOrEmpty(director.Email))
        {
            _logger.LogWarning("No director contact for job {JobId}; NSF alert not sent", jobId);
            return;
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
            FromAddress = "noreply@teamsportsinfo.com",
            ToAddresses = new List<string> { director.Email },
            Subject = $"eCheck NSF return — ${amountStr}",
            HtmlBody = body
        }, sendInDevelopment: false, cancellationToken: ct);
    }

    private void PushNextCheck(Settlement settlement, DateTime now)
    {
        settlement.LastCheckedAt = now;
        settlement.NextCheckAt = now.AddDays(_options.RetryDays);
        settlement.Modified = now;
    }

    private enum Outcome { Checked, Settled, Returned, Errored }

    private sealed class Counts
    {
        public int Checked;
        public int Settled;
        public int Returned;
        public int Errored;
    }
}
