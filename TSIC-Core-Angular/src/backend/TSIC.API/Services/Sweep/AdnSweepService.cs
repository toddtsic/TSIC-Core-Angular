using System.Text;
using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.Options;
using TSIC.API.Configuration;
using TSIC.API.Extensions;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Extensions;
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
    private readonly ITeamRepository _teamRepo;
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
        ITeamRepository teamRepo,
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
        _teamRepo = teamRepo;
        _arbRepo = arbRepo;
        _adn = adn;
        _feeAdj = feeAdj;
        _email = email;
        _tsicSettings = tsicSettings.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdnSweepResult> RunAsync(
        string triggeredBy,
        int daysPrior = 0,
        bool sendDigest = true,
        CancellationToken ct = default)
    {
        if (daysPrior <= 0) daysPrior = _options.DaysPriorWindow;

        var log = await _settleRepo.StartSweepLogAsync(triggeredBy, ct);
        var counts = new Counts();
        string? errorMessage = null;
        var arbRows = new List<ArbDigestRow>();
        var ecRows = new List<EcheckReturnDigestRow>();
        var settledRows = new List<EcheckSettledDigestRow>();
        var orphanRows = new List<OrphanDigestRow>();

        try
        {
            // The scheduled sweep is hard-gated to a Production host (AdnSweepBackgroundService),
            // so the env-bound resolvers return the production account where it actually runs. A
            // manual off-Production trigger harmlessly hits sandbox and finds no prod batches.
            var creds = await _adn.GetJobAdnCredentials_FromCustomerId(_tsicSettings.DefaultCustomerId);
            var env = _adn.GetADNEnvironment();

            // 1) Walk batches, accumulate flat tx list. A batch-list error is NOT an empty day — it used
            // to return [] and sail on, producing a digest of zeros that reads exactly like a quiet
            // morning. Throw instead, so the failure reaches the catch and is reported as a failure.
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
            // settlement. This is the MONEY EVENT under pessimistic eCheck: MarkEcheckSettled flips
            // the submit-time RA (born Active=false) to Active=true and re-derives PaidTotal from
            // the ledger, atomically per settlement. subscription == null excludes ARB drafts, which
            // book their RA in step 2 (ImportArbTransactionAsync) — see the ARB/eCheck split there.
            var settledTxIds = allTxs
                .Where(t => t.transactionStatus == "settledSuccessfully" && t.subscription == null && !string.IsNullOrEmpty(t.transId))
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
                        // Each call owns its transaction (status flip + RA Active flip + recompute
                        // commit together), so there is no batch save after the loop — a batch
                        // re-save could re-commit a rolled-back in-memory status without its money.
                        var row = await MarkEcheckSettled(settlement, ct);
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

            // 5) Detect orphan charges: one-time txs that settled at ADN but have no local
            // RegistrationAccounting row (the rare "charged the card, app pool died before the
            // booking write" case). REPORT-ONLY — we flag them in the digest for a human to book
            // by hand; the sweep never writes accounting rows here. In ~26 years this has happened
            // about once, so the expected count every run is 0.
            foreach (var tx in allTxs.Where(IsOrphanCandidate))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var row = await DetectOrphanAsync(tx, ct);
                    if (row != null)
                    {
                        counts.OrphansFound++;
                        orphanRows.Add(row);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Orphan detection failed for tx {TxId}", tx.transId);
                    counts.Errored++;
                }
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ADN sweep run failed");
            // Flatten: this message goes straight into your inbox and into SweepLog.errorMessage. A bare
            // DbUpdateException.Message reads "See the inner exception for details" and shows you none.
            errorMessage = ex.Flatten();
        }

        // The digest is built and sent OUTSIDE the try — a failed sweep must still mail, and must say so.
        // It used to be the last statement inside the try, so any throw upstream skipped it entirely and
        // the only signal was the 5am email not arriving. Silence is not a report.
        var html = BuildDigestHtml(arbRows, settledRows, ecRows, orphanRows, counts, errorMessage);
        if (sendDigest)
        {
            try
            {
                await SendDigestAsync(html, errorMessage, counts.Errored, ct);
            }
            catch (Exception ex)
            {
                // Never let a mail failure mask the sweep's own outcome in SweepLog / the return value.
                _logger.LogError(ex, "ADN sweep digest send failed");
            }
        }

        await _settleRepo.CompleteSweepLogAsync(
            log, counts.Checked, counts.EcheckSettled, counts.EcheckReturnsProcessed, counts.Errored, errorMessage, ct);

        return new AdnSweepResult
        {
            Checked = counts.Checked,
            ArbImported = counts.ArbImported,
            EcheckSettled = counts.EcheckSettled,
            EcheckReturnsProcessed = counts.EcheckReturnsProcessed,
            OrphansFound = counts.OrphansFound,
            Errored = counts.Errored,
            Succeeded = errorMessage == null,
            ErrorMessage = errorMessage,
            DigestHtml = html,
        };
    }

    // ── Batch fetching ────────────────────────────────────────────────

    private List<transactionSummaryType> FetchBatchTransactions(
        AuthorizeNet.Environment env, string loginId, string transactionKey, int daysPrior)
    {
        var first = DateTime.Today.Subtract(TimeSpan.FromDays(daysPrior));
        var last = DateTime.Today;

        var batchResp = _adn.GetSettleBatchList_FromDateRange(env, loginId, transactionKey, first, last, true);

        // An error response is a FAILED sweep, not an empty one. Authorize.Net signals "no batches in
        // this window" with an Ok result and a null batchList — that is the legitimate quiet day, and it
        // returns []. Anything else (credentials rejected, service error) throws: nothing downstream may
        // conclude "nothing settled" from an answer Authorize.Net never actually gave.
        if (batchResp?.messages?.resultCode != messageTypeEnum.Ok)
        {
            var reason = batchResp?.messages?.message?[0]?.text ?? "no response from Authorize.Net";
            throw new InvalidOperationException(
                $"ADN GetSettleBatchList failed for the {daysPrior}d window: {reason}");
        }

        if (batchResp.batchList == null)
        {
            _logger.LogInformation("ADN GetSettleBatchList: no settled batches in the {Days}d window", daysPrior);
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

    // Orphan candidate = a settled, one-time charge that carries our invoice format.
    // subscription == null excludes ARB txs (handled in step 2). The real orphan test
    // (no matching accounting row) is done in DetectOrphanAsync, post-dedup — this is
    // just the cheap pre-filter over the batch list.
    private static bool IsOrphanCandidate(transactionSummaryType tx)
    {
        return tx.transactionStatus == "settledSuccessfully"
            && !string.IsNullOrEmpty(tx.transId)
            && tx.subscription == null
            && !string.IsNullOrEmpty(tx.invoiceNumber)
            && tx.invoiceNumber.Split('_').Length == 3;
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

        var subId = tx.subscription.id.ToString();

        // Two ARB sub flavors share the same subscription-id namespace at ADN:
        //   1. Player ARB (legacy) — sub stamped on Registrations.AdnSubscriptionId.
        //   2. Team ARB-Trial      — sub stamped on Teams.AdnSubscriptionId (per-team).
        // Try registration first (covers the long-standing player flow), then team.
        var reg = await _regRepo.GetByAdnSubscriptionIdAsync(subId, ct);
        if (reg != null)
        {
            return await ImportRegistrationArbTransactionAsync(tx, reg, env, creds, ct);
        }

        var team = await _teamRepo.GetByAdnSubscriptionIdAsync(subId, ct);
        if (team != null)
        {
            return await ImportTeamArbTransactionAsync(tx, team, env, creds, ct);
        }

        _logger.LogWarning("ARB tx {TxId} has no matching registration or team for subscription {SubId}",
            tx.transId, subId);
        return null;
    }

    private async Task<ArbDigestRow?> ImportRegistrationArbTransactionAsync(
        transactionSummaryType tx, Registrations reg, AuthorizeNet.Environment env, AdnCredentialsViewModel creds, CancellationToken ct)
    {
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

        var raRow = new RegistrationAccounting
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
        };

        if (tx.transactionStatus == "settledSuccessfully")
        {
            // Record the settled installment and re-derive the registration's totals from
            // the ledger in one transaction. The sweep is the actor, so the registration is
            // stamped with the system superuser (matches the audit row and the team-side
            // settle) — NOT the registrant's FamilyUserId.
            await _accountingRepo.RecordPaymentAndRecomputeAsync(raRow, SystemUserId, ct);
        }
        else
        {
            // Non-settling transaction: keep the audit row, apply no payment (totals unchanged).
            _accountingRepo.Add(raRow);
            await _accountingRepo.SaveChangesAsync(ct);
        }

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

    private async Task<ArbDigestRow?> ImportTeamArbTransactionAsync(
        transactionSummaryType tx, Domain.Entities.Teams team, AuthorizeNet.Environment env, AdnCredentialsViewModel creds, CancellationToken ct)
    {
        // Sync ADN-known subscription status onto the team row (no separate ARB repo
        // method for teams — direct mutation on the tracked entity).
        var subStatusResp = _adn.GetSubscriptionStatus(env, creds.AdnLoginId!, creds.AdnTransactionKey!, team.AdnSubscriptionId!);
        if (subStatusResp?.messages?.resultCode == messageTypeEnum.Ok)
        {
            var liveSubStatus = subStatusResp.status.ToString();
            if (!string.IsNullOrEmpty(liveSubStatus) && team.AdnSubscriptionStatus != liveSubStatus)
            {
                team.AdnSubscriptionStatus = liveSubStatus;
                team.Modified = DateTime.Now;
                team.LebUserId = SystemUserId;
            }
        }
        else
        {
            _logger.LogWarning("ARB-Trial tx {TxId}: GetSubscriptionStatus returned non-Ok ({Code}); leaving local status as-is",
                tx.transId, subStatusResp?.messages?.resultCode);
        }

        var txDetail = _adn.ADN_GetTransactionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, tx.transId);
        if (txDetail?.messages?.resultCode != messageTypeEnum.Ok || txDetail.transaction == null)
        {
            _logger.LogWarning("ARB-Trial tx {TxId}: GetTransactionDetails returned no data", tx.transId);
            return null;
        }

        // ARB-Trial subs can be CC or eCheck. Pull last-4 from whichever payment shape applies.
        string? cc4 = null, ccExp = null, acctLast4 = null;
        switch (txDetail.transaction.payment?.Item)
        {
            case creditCardMaskedType cc:
                cc4 = cc.cardNumber?.Length >= 4 ? cc.cardNumber[^4..] : null;
                ccExp = cc.expirationDate;
                break;
            case bankAccountMaskedType ba:
                acctLast4 = ba.accountNumber?.Length >= 4 ? ba.accountNumber[^4..] : null;
                break;
        }

        var settleAmount = tx.transactionStatus == "settledSuccessfully" ? tx.settleAmount : 0;
        var clubRepRegId = team.ClubrepRegistrationid;

        // Write the per-tx accounting row against the rep's Registrations row but
        // tag it with TeamId so refunds/audits can resolve back to the originating
        // team without a DB scan.
        var raRow = new RegistrationAccounting
        {
            RegistrationId = clubRepRegId ?? Guid.Empty,
            TeamId = team.TeamId,
            Active = true,
            AdnCc4 = cc4,
            AdnCcexpDate = ccExp,
            AdnInvoiceNo = tx.invoiceNumber,
            AdnTransactionId = tx.transId,
            Dueamt = settleAmount,
            Payamt = settleAmount,
            Paymeth = cc4 != null
                ? $"paid by cc: {settleAmount:C} on subscriptionId: {tx.subscription.id} on {tx.submitTimeLocal:G} txID: {tx.transId}"
                : $"paid by eCheck (****{acctLast4}): {settleAmount:C} on subscriptionId: {tx.subscription.id} on {tx.submitTimeLocal:G} txID: {tx.transId}",
            PaymentMethodId = CcPaymentMethodId,
            Comment = $"{tx.transactionStatus} (team subscriptionId: {tx.subscription.id} {txDetail.transaction.responseReasonDescription})",
            Createdate = DateTime.Now,
            Modified = DateTime.Now,
            LebUserId = SystemUserId
        };

        if (tx.transactionStatus == "settledSuccessfully")
        {
            // Record the settled installment and re-derive the team's totals from the ledger
            // in one transaction. Other tracked edits on the shared sweep context (e.g. the
            // subscription-status sync above) flush with it.
            await _accountingRepo.RecordPaymentAndRecomputeAsync(raRow, SystemUserId, ct);
        }
        else
        {
            // Non-settling transaction: keep the audit row, apply no payment (totals unchanged).
            _accountingRepo.Add(raRow);
            await _accountingRepo.SaveChangesAsync(ct);
        }

        // Roll team-level deltas (PaidTotal, OwedTotal, status changes) onto the rep's
        // Registrations row so search/balance UI shows the post-sweep aggregate.
        if (clubRepRegId.HasValue && clubRepRegId.Value != Guid.Empty)
        {
            await _registrations_SyncRep(clubRepRegId.Value, ct);
        }

        var (owedNow, paymentXofY, nextInstallment) = ComputeTeamInstallmentMath(team);

        var registrant = team.ClubrepId ?? "";
        var assignment = $"{team.TeamName ?? team.DisplayName ?? team.TeamFullName}";

        return new ArbDigestRow
        {
            JobName = team.Job?.DisplayName ?? team.Job?.JobName ?? "",
            TransId = tx.transId,
            SubscriptionId = tx.subscription.id.ToString(),
            SubscriptionStatus = team.AdnSubscriptionStatus,
            SettleAmount = settleAmount,
            TransactionStatus = tx.transactionStatus,
            OwedNow = owedNow,
            PaymentXofY = paymentXofY,
            NextInstallment = nextInstallment,
            Registrant = registrant,
            RegistrantAssignment = assignment
        };
    }

    // Small wrapper to keep the team ARB / NSF paths agnostic about which Registrations
    // method does the rep aggregation. SynchronizeClubRepFinancialsAsync is the single
    // canonical roll-up point.
    private Task _registrations_SyncRep(Guid clubRepRegistrationId, CancellationToken ct)
        => _regRepo.SynchronizeClubRepFinancialsAsync(clubRepRegistrationId, SystemUserId, ct);

    // ── eCheck Pending → Settled ─────────────────────────────────────

    private async Task<EcheckSettledDigestRow?> MarkEcheckSettled(Settlement settlement, CancellationToken ct)
    {
        var ra = settlement.RegistrationAccounting;
        var reg = ra.Registration;
        if (reg == null)
        {
            _logger.LogWarning("Settlement {Id}: no Registration loaded — corrupt state, skipping",
                settlement.SettlementId);
            return null;
        }

        var now = DateTime.Now;
        settlement.Status = "Settled";
        settlement.SettledAt = now;
        settlement.LastCheckedAt = now;
        settlement.Modified = now;

        // Pessimistic booking: the submit-time RA was born Active=false (excluded from the PaidTotal
        // ledger sum). Settlement is the money event — flip it Active=true and re-derive the keyed
        // entity's total from the ledger. RecomputeForRowAsync flushes this flip AND the settlement
        // status change (both tracked on the shared sweep context) inside one transaction, so the
        // row and the total it implies commit together. ra.TeamId routes team vs registration.
        ra.Active = true;
        ra.Modified = now;
        await _accountingRepo.RecomputeForRowAsync(ra, SystemUserId, ct);

        // Team eCheck: roll the team's freshly-credited total onto the rep's Registration aggregate,
        // mirroring the NSF path (SynchronizeClubRepFinancialsAsync). Player eCheck (TeamId null)
        // reconciles the registration directly in the recompute above and needs no roll-up.
        if (ra.TeamId.HasValue && ra.RegistrationId.HasValue && ra.RegistrationId.Value != Guid.Empty)
        {
            await _registrations_SyncRep(ra.RegistrationId.Value, ct);
        }

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

        // Capture BEFORE flipping the status below. Under pessimistic eCheck the money was booked
        // at SETTLEMENT (Status→"Settled" + RA Active→true), not at submit. A return can arrive for
        // a settlement we never marked Settled (the item bounced before our sweep saw it clear): its
        // RA is still Active=false and was never counted in PaidTotal, so there is nothing to
        // reverse. Only a previously-settled item needs the reversal row + fee restore below.
        var wasCounted = settlement.Status == "Settled";

        var now = DateTime.Now;

        // Mark Settlement returned.
        settlement.Status = "Returned";
        settlement.ReturnReasonCode = detail.transaction.responseReasonCode.ToString();
        settlement.ReturnReasonText = detail.transaction.responseReasonDescription;
        settlement.LastCheckedAt = now;
        settlement.Modified = now;

        if (!wasCounted)
        {
            // Never credited — persist the Returned status only (RA stays Active=false, PaidTotal
            // untouched). Reg stays active; the director alert below carries the decision to
            // inactivate. Save here because the reversal chokepoint that would normally flush the
            // status is skipped in this branch.
            _logger.LogInformation(
                "eCheck return {TxId}: settlement {Id} never settled (still Pending) — status→Returned, no reversal (never credited)",
                returnTx.transId, settlement.SettlementId);
            await _settleRepo.SaveChangesAsync(ct);

            bool notified;
            try
            {
                notified = await SendDirectorAlertAsync(reg.JobId, ra, settlement, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NSF director alert failed for settlement {Id}", settlement.SettlementId);
                notified = false;
            }

            return new EcheckReturnDigestRow
            {
                JobName = reg.Job?.DisplayName ?? reg.Job?.JobName ?? "",
                ReturnTxId = returnTx.transId,
                OriginalTxId = originalTxId,
                Reason = $"{settlement.ReturnReasonText} ({settlement.ReturnReasonCode})",
                AmountReversed = 0m,
                Registrant = reg.UserId,
                DirectorNotified = notified
            };
        }

        // Two NSF flavors share this method:
        //   - Player eCheck:    RA.TeamId is null → reverse on the player Registration directly.
        //   - Team eCheck/ARB-Trial fallback: RA.TeamId is set → reverse on the Teams row,
        //     then re-aggregate onto the rep's Registration via SynchronizeClubRepFinancialsAsync.
        // The earlier ReduceTeamProcessingFeeForEcheckAsync (applied at submit) is mirrored
        // here by ReverseTeamProcessingFeeForEcheckAsync. ARB-Trial fallback follows the same
        // pattern, so this single branch covers both.
        // Restore the processing-fee credit on the reversed entity (save-free; the chokepoint
        // below re-derives FeeTotal/OwedTotal). PaidTotal is recomputed from the ledger once the
        // reversal row lands, so there is no hand-decrement here.
        if (ra.TeamId.HasValue)
        {
            var team = await _teamRepo.GetTeamFromTeamId(ra.TeamId.Value, ct);
            if (team == null)
            {
                _logger.LogWarning("Settlement {Id}: RA.TeamId {TeamId} not found — reversal row written, team totals left as-is",
                    settlement.SettlementId, ra.TeamId);
            }
            else
            {
                await _feeAdj.ReverseTeamProcessingFeeForEcheckAsync(team, amount, reg.JobId, SystemUserId);
            }
        }
        else
        {
            await _feeAdj.ReverseProcessingFeeForEcheckAsync(reg, amount, reg.JobId, SystemUserId);
        }

        // Record the reversal row and re-derive the keyed entity's totals from the ledger in one
        // transaction. row.TeamId routes the recompute to the team (else the registration),
        // mirroring the branch above; a missing team is a no-op recompute (the row is still
        // written). Pending edits on the shared sweep context (settlement status, fee restore)
        // flush with it, so the separate settlement save is no longer needed.
        await _accountingRepo.RecordPaymentAndRecomputeAsync(new RegistrationAccounting
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
        }, SystemUserId, ct);

        // For team-side reversals, roll the team delta onto the rep aggregate. Doing
        // this AFTER SaveChanges so the team's reversed PaidTotal/OwedTotal are visible
        // to SynchronizeClubRepFinancialsAsync's sum query.
        if (ra.TeamId.HasValue && ra.RegistrationId.HasValue && ra.RegistrationId.Value != Guid.Empty)
        {
            await _registrations_SyncRep(ra.RegistrationId.Value, ct);
        }

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
            ToAddresses = [director.Email],
            Subject = $"eCheck NSF return — ${amountStr}",
            HtmlBody = body
        }, sendInDevelopment: false, cancellationToken: ct);
        return true;
    }

    // ── Orphan charge detection (report-only) ─────────────────────────

    // A settled one-time charge that we can't find a local accounting row for. This is the
    // "card was charged at ADN but the booking write never landed" failure (app pool stop /
    // publish mid-request). We only REPORT it — no RegistrationAccounting row is written here.
    // A human reads the digest and books it by hand if it's real.
    private async Task<OrphanDigestRow?> DetectOrphanAsync(transactionSummaryType tx, CancellationToken ct)
    {
        // Already booked? Then it isn't an orphan. This is the common case for every settled
        // charge (the payment flow writes the row synchronously), so it filters ~everything
        // cheaply before we touch the registration tables.
        if (await _accountingRepo.AnyByAdnTransactionIdAsync(tx.transId, ct))
            return null;

        // Parse the invoice's three AIs (customer_job_registration).
        var parts = tx.invoiceNumber.Split('_');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var custAi)
            || !int.TryParse(parts[1], out var jobAi)
            || !int.TryParse(parts[2], out var regAi))
        {
            _logger.LogWarning(
                "ORPHAN ADN charge {TxId}: settled with no accounting row and a malformed invoice '{Invoice}' — cannot attribute (report only)",
                tx.transId, tx.invoiceNumber);
            return new OrphanDigestRow
            {
                Resolved = false,
                TransId = tx.transId,
                InvoiceNumber = tx.invoiceNumber,
                SettleAmount = tx.settleAmount,
                SubmittedAt = tx.submitTimeLocal,
                Registrant = null,
                Note = "malformed invoice number — cannot map to a registration"
            };
        }

        var reg = await _regRepo.GetByInvoiceAisAsync(custAi, jobAi, regAi, ct);
        if (reg == null)
        {
            _logger.LogWarning(
                "ORPHAN ADN charge {TxId}: settled with no accounting row; invoice '{Invoice}' matches no registration (report only)",
                tx.transId, tx.invoiceNumber);
            return new OrphanDigestRow
            {
                Resolved = false,
                TransId = tx.transId,
                InvoiceNumber = tx.invoiceNumber,
                SettleAmount = tx.settleAmount,
                SubmittedAt = tx.submitTimeLocal,
                Registrant = null,
                Note = "no registration matches this invoice's customer/job/registration AIs"
            };
        }

        // Genuine orphan: money settled at ADN, no local accounting row. REPORT ONLY — we
        // deliberately do NOT write a RegistrationAccounting row. A human reviews the digest
        // and books it by hand if real.
        _logger.LogWarning(
            "ORPHAN ADN charge {TxId}: settled {Amount:C} for registration {RegId} (invoice {Invoice}) with no local accounting row — REPORT ONLY, not booked",
            tx.transId, tx.settleAmount, reg.RegistrationId, tx.invoiceNumber);

        return new OrphanDigestRow
        {
            Resolved = true,
            TransId = tx.transId,
            InvoiceNumber = tx.invoiceNumber,
            SettleAmount = tx.settleAmount,
            SubmittedAt = tx.submitTimeLocal,
            Registrant = reg.UserId,
            Note = "settled at ADN, no local accounting row — review and book by hand"
        };
    }

    // ── Installment math (legacy parity) ──────────────────────────────

    // Team ARB-Trial subs run on day-based intervals (deposit today+1, balance on
    // AdnStartDateAfterTrial), so the schedule is always exactly two charges and
    // the next-installment math operates on AddDays, not AddMonths.
    private (decimal OwedNow, string PaymentXofY, DateTime? NextInstallment) ComputeTeamInstallmentMath(Domain.Entities.Teams team)
    {
        if (team.AdnSubscriptionStartDate == null
            || team.AdnSubscriptionIntervalLength == null
            || team.AdnSubscriptionBillingOccurences == null
            || team.AdnSubscriptionAmountPerOccurence == null)
        {
            return (Math.Max(team.OwedTotal ?? 0m, 0m), "", null);
        }

        var totalOcc = team.AdnSubscriptionBillingOccurences.Value;
        var startDate = team.AdnSubscriptionStartDate.Value;
        var intervalDays = team.AdnSubscriptionIntervalLength.Value;

        // Deposit happens at startDate; subsequent occurrences at startDate + N*intervalDays.
        var dates = Enumerable.Range(0, totalOcc).Select(i => startDate.AddDays(i * intervalDays)).ToList();
        var occAsOfNow = dates.Count(d => d.Date <= DateTime.Now.Date);

        var paymentXofY = $"{occAsOfNow}/{totalOcc}";
        var nextInstallment = occAsOfNow < dates.Count ? dates[occAsOfNow] : (DateTime?)null;
        var owedNow = Math.Max(team.OwedTotal ?? 0m, 0m);
        return (owedNow, paymentXofY, nextInstallment);
    }

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
        List<OrphanDigestRow> orphanRows,
        Counts counts,
        string? errorMessage)
    {
#if DEBUG
        var envType = "DEV";
#else
        var envType = "PROD";
#endif
        var sb = new StringBuilder();
        sb.Append($"<h3 style='margin-bottom:4px;'>ADN Sweep ({envType}, TSIC) — {DateTime.Now:dddd, dd MMMM yyyy HH:mm}</h3>");

        // Lead with the failure. A digest of zeros reads like a quiet morning; only this says otherwise.
        if (errorMessage != null)
        {
            sb.Append("<p style='font-size:13px;color:#b00;font-weight:bold;margin:8px 0;'>"
                + "&#9888; SWEEP FAILED — this pass did not complete. Payments settled at Authorize.Net may "
                + "NOT be booked in the accounting tables. The counts below are whatever was reached before "
                + "the failure, not a picture of the day.</p>");
            sb.Append($"<p style='font-size:11px;color:#b00;margin:0 0 8px 0;'><b>Error:</b> {errorMessage}</p>");
        }
        else if (counts.Errored > 0)
        {
            sb.Append($"<p style='font-size:13px;color:#b00;font-weight:bold;margin:8px 0;'>"
                + $"&#9888; {counts.Errored} transaction(s) errored — the pass completed, but those are not booked.</p>");
        }

        sb.Append($"<p style='font-size:9px;margin-top:0;'>Counts — Checked: {counts.Checked}, ARB imported: {counts.ArbImported}, eCheck settled: {counts.EcheckSettled}, eCheck returns: {counts.EcheckReturnsProcessed}, Orphans: {counts.OrphansFound}, Errored: {counts.Errored}</p>");

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

        // ── Orphan ADN Charges table (report-only) ────────────────────
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>Orphan ADN Charges (settled at ADN, not booked locally)</h4>");
        if (orphanRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(none — every settled charge has a matching accounting row ✓)</p>");
        }
        else
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>⚠️ Money settled at Authorize.Net with no local accounting row. REPORT ONLY — nothing was booked. Review each and enter the payment by hand.</p>");
            sb.Append("<table style='border-style:solid;border-collapse:separate;border-spacing:10px;font-size:9px;'>");
            sb.Append("<tr><th>#</th><th>Resolved</th><th>TransId</th><th>Invoice</th><th>Settle Amount</th><th>Submitted (ADN)</th><th>Registrant</th><th>Note</th></tr>");
            for (int i = 0; i < orphanRows.Count; i++)
            {
                var r = orphanRows[i];
                sb.Append("<tr>")
                  .Append($"<td>{i + 1}</td>")
                  .Append($"<td>{(r.Resolved ? "yes" : "NO")}</td>")
                  .Append($"<td>{r.TransId}</td>")
                  .Append($"<td>{r.InvoiceNumber}</td>")
                  .Append($"<td>{r.SettleAmount:C}</td>")
                  .Append($"<td>{r.SubmittedAt:g}</td>")
                  .Append($"<td>{r.Registrant}</td>")
                  .Append($"<td>{r.Note}</td>")
                  .Append("</tr>");
            }
            sb.Append("</table>");
        }

        return sb.ToString();
    }

    private async Task SendDigestAsync(string html, string? errorMessage, int errored, CancellationToken ct)
    {
        await _email.SendAsync(new EmailMessageDto
        {
            FromName = "",
            ToAddresses = [TsicConstants.SupportEmail],
            // The verdict rides the subject — this is read on a phone, and a failed sweep must be
            // distinguishable from a quiet one without opening the mail.
            Subject = $"AdnSweep AI {DateTime.Now:dddd, dd MMMM yyyy HH:mm}"
                + (errorMessage != null ? " — SWEEP FAILED" : errored > 0 ? $" — {errored} ERRORED" : ""),
            HtmlBody = html
        }, sendInDevelopment: true, cancellationToken: ct);
    }

    // ── Internal types ────────────────────────────────────────────────

    private sealed class Counts
    {
        public int Checked;
        public int ArbImported;
        public int EcheckSettled;
        public int EcheckReturnsProcessed;
        public int OrphansFound;
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

    // Report-only: a settled ADN charge with no matching RegistrationAccounting row.
    // Resolved = we mapped the invoice to a registration; !Resolved = couldn't attribute it.
    // SubmittedAt is ADN's submitTimeLocal (local batch time of the charge).
    private sealed record OrphanDigestRow
    {
        public required bool Resolved { get; init; }
        public required string TransId { get; init; }
        public required string InvoiceNumber { get; init; }
        public required decimal SettleAmount { get; init; }
        public required DateTime SubmittedAt { get; init; }
        public required string? Registrant { get; init; }
        public required string Note { get; init; }
    }
}
