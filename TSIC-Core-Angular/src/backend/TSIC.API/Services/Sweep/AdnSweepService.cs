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
    // "E-Check Payment" â€” used for settled eCheck / ACH-ARB draft RA rows.
    private static readonly Guid EcheckPaymentMethodId = Guid.Parse("2EECA575-A268-E111-9D56-F04DA202060D");
    // "Failed E-Check Payment" â€” used for NSF reversal RA rows.
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

    // One sweep at a time, process-wide. Every idempotency guard in the sweep is unlocked
    // read-then-write ("already imported? no â†’ book it"), so two concurrent passes could both
    // clear the same guard and double-book an ARB import or double-write an NSF reversal. The
    // triggers (5 AM background service + the manual SuperUser endpoint) share this API process,
    // so an in-process lock covers every real entry path. Static: the service is resolved per
    // scope; the lock must span instances.
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public async Task<AdnSweepResult> RunAsync(
        string triggeredBy,
        int daysPrior = 0,
        bool sendDigest = true,
        CancellationToken ct = default)
    {
        // Refuse, don't queue: a second run would re-scan the same trailing window anyway,
        // so the right behavior for an overlapping request is to not start.
        if (!await RunLock.WaitAsync(0, ct))
        {
            _logger.LogWarning("ADN sweep already running â€” {TriggeredBy} request refused", triggeredBy);
            return new AdnSweepResult
            {
                Checked = 0,
                ArbImported = 0,
                EcheckSettled = 0,
                EcheckReturnsProcessed = 0,
                OrphansFound = 0,
                Errored = 0,
                Succeeded = false,
                ErrorMessage = "Sweep already running â€” request refused.",
                DigestHtml = null
            };
        }

        try
        {
            return await RunCoreAsync(triggeredBy, daysPrior, sendDigest, ct);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<AdnSweepResult> RunCoreAsync(
        string triggeredBy,
        int daysPrior,
        bool sendDigest,
        CancellationToken ct)
    {
        if (daysPrior <= 0) daysPrior = _options.DaysPriorWindow;

        var log = await _settleRepo.StartSweepLogAsync(triggeredBy, ct);
        var counts = new Counts();
        string? errorMessage = null;
        var arbRows = new List<ArbDigestRow>();
        var ecRows = new List<EcheckReturnDigestRow>();
        var settledRows = new List<EcheckSettledDigestRow>();
        var orphanRows = new List<OrphanDigestRow>();
        var watchdogRows = new List<WatchdogDigestRow>();
        var untrackedRows = new List<UntrackedEcheckRaDto>();

        try
        {
            // The scheduled sweep is hard-gated to a Production host (AdnSweepBackgroundService),
            // so the env-bound resolvers return the production account where it actually runs. A
            // manual off-Production trigger harmlessly hits sandbox and finds no prod batches.
            var creds = await _adn.GetJobAdnCredentials_FromCustomerId(_tsicSettings.DefaultCustomerId);
            var env = _adn.GetADNEnvironment();

            // 1) Walk batches, accumulate flat tx list. A batch-list error is NOT an empty day â€” it used
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

            // 3) Process eCheck Pending â†’ Settled transitions.
            // Walk batch txs that settled successfully and match against our pending Settlement
            // rows. No per-tx API call is needed â€” presence in a settled batch is the proof of
            // settlement. Status-only: the money booked at submit (optimistic); this stamp records
            // that the draft entered the banking network, which the return handler and watchdog
            // key on. subscription == null excludes ARB drafts, which book their RA in step 2
            // (ImportArbTransactionAsync) â€” see the ARB/eCheck split there.
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
                        // commit together), so there is no batch save after the loop â€” a batch
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
            // booking write" case). REPORT-ONLY â€” we flag them in the digest for a human to book
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

            // 6) Stale-Pending watchdog: drafts that went silent. Healthy drafts settle in 1â€“2
            // business days; a Settlement still Pending past the threshold gets its status
            // queried at ADN directly and is settled, reversed, or flagged. This is the only
            // detector for a draft that died before origination â€” that failure produces no
            // batch transaction and no return, ever.
            var staleCutoff = DateTime.Now.AddDays(-_options.WatchdogStalePendingDays);
            foreach (var stale in await _settleRepo.GetStalePendingAsync(staleCutoff, ct))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var row = await ProcessStalePendingAsync(stale, env, creds, ct);
                    if (row != null) watchdogRows.Add(row);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Watchdog processing failed for settlement {Id}", stale.SettlementId);
                    counts.Errored++;
                }
            }

            // 7) Integrity net: booked eCheck money with no Settlement return-watcher. The atomic
            // mint makes this unreachable going forward; expected count every run is 0. REPORT-ONLY.
            try
            {
                untrackedRows = await _settleRepo.GetUntrackedEcheckAccountingAsync(EcheckPaymentMethodId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Untracked-eCheck integrity query failed");
                counts.Errored++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ADN sweep run failed");
            // Flatten: this message goes straight into your inbox and into SweepLog.errorMessage. A bare
            // DbUpdateException.Message reads "See the inner exception for details" and shows you none.
            errorMessage = ex.Flatten();
        }

        // The digest is built and sent OUTSIDE the try â€” a failed sweep must still mail, and must say so.
        // It used to be the last statement inside the try, so any throw upstream skipped it entirely and
        // the only signal was the 5am email not arriving. Silence is not a report.
        var html = BuildDigestHtml(arbRows, settledRows, ecRows, orphanRows, watchdogRows, untrackedRows, counts, errorMessage);
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

    // â”€â”€ Batch fetching â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private List<transactionSummaryType> FetchBatchTransactions(
        AuthorizeNet.Environment env, string loginId, string transactionKey, int daysPrior)
    {
        var first = DateTime.Today.Subtract(TimeSpan.FromDays(daysPrior));
        var last = DateTime.Today;

        var batchResp = _adn.GetSettleBatchList_FromDateRange(env, loginId, transactionKey, first, last, true);

        // An error response is a FAILED sweep, not an empty one. Authorize.Net signals "no batches in
        // this window" with an Ok result and a null batchList â€” that is the legitimate quiet day, and it
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
    // (no matching accounting row) is done in DetectOrphanAsync, post-dedup â€” this is
    // just the cheap pre-filter over the batch list.
    private static bool IsOrphanCandidate(transactionSummaryType tx)
    {
        return tx.transactionStatus == "settledSuccessfully"
            && !string.IsNullOrEmpty(tx.transId)
            && tx.subscription == null
            && !string.IsNullOrEmpty(tx.invoiceNumber)
            && tx.invoiceNumber.Split('_').Length == 3;
    }

    // â”€â”€ ARB import (legacy parity) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        //   1. Player ARB (legacy) â€” sub stamped on Registrations.AdnSubscriptionId.
        //   2. Team ARB-Trial      â€” sub stamped on Teams.AdnSubscriptionId (per-team).
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

        // Player ARB subs can be CC or (now) eCheck. Pull last-4 from whichever payment shape applies.
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
        // Tender from the authoritative summary field (also captured to adn.Txs.[Transaction Type]),
        // falling back to the settled tx's payment shape. If neither identifies it, don't guess:
        // booking an eCheck as CC would skip the return-watcher below, leaving a later bounce
        // unreversible. Indeterminate ⇒ skip this pass (logged); the next sweep retries.
        bool? tender = !string.IsNullOrWhiteSpace(tx.accountType)
            ? string.Equals(tx.accountType, "eCheck", StringComparison.OrdinalIgnoreCase)
            : txDetail.transaction.payment?.Item switch
            {
                bankAccountMaskedType => true,
                creditCardMaskedType => false,
                _ => (bool?)null,
            };
        if (tender is null)
        {
            _logger.LogWarning(
                "ARB tx {TxId}: tender indeterminate (accountType='{AccountType}', unrecognized payment shape) — skipping this pass",
                tx.transId, tx.accountType);
            return null;
        }
        var isEcheck = tender.Value;

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
            Paymeth = isEcheck
                ? $"paid by eCheck (****{acctLast4}): {settleAmount:C} on subscriptionId: {tx.subscription.id} on {tx.submitTimeLocal:G} txID: {tx.transId}"
                : $"paid by cc: {settleAmount:C} on subscriptionId: {tx.subscription.id} on {tx.submitTimeLocal:G} txID: {tx.transId}",
            PaymentMethodId = isEcheck ? EcheckPaymentMethodId : CcPaymentMethodId,
            Comment = $"{tx.transactionStatus} (subscriptionId: {tx.subscription.id} {txDetail.transaction.responseReasonDescription})",
            Createdate = DateTime.Now,
            Modified = DateTime.Now,
            LebUserId = SystemUserId
        };

        if (tx.transactionStatus == "settledSuccessfully")
        {
            // eCheck ARB draft: pair the RA with a Settlement row born "Settled" (the money books
            // below). An ACH draft can still be returned days later; ProcessEcheckReturnAsync
            // matches the return to its original via the Settlement table, so without this row an
            // NSF on a plan installment would be logged "not ours, skipping" and never reversed.
            // The subscription drafts autonomously (no submit-time Pending row), so THIS is the
            // only place a plan installment's Settlement key is created. Step-3's
            // subscription==null filter keeps this Settled row out of the Pendingâ†’Settled path.
            // Attached via the navigation property BEFORE the booking save so RA + return-watcher
            // commit in ONE transaction â€” a crash between separate saves would book money no
            // return could ever find.
            if (isEcheck)
            {
                var settledNow = DateTime.Now;
                _settleRepo.Add(new Settlement
                {
                    SettlementId = Guid.NewGuid(),
                    RegistrationAccounting = raRow,
                    AdnTransactionId = tx.transId,
                    Status = "Settled",
                    SubmittedAt = settledNow,
                    NextCheckAt = settledNow,
                    SettledAt = settledNow,
                    LastCheckedAt = settledNow,
                    AccountLast4 = acctLast4,
                    Modified = settledNow,
                    LebUserId = SystemUserId
                });
            }

            // Record the settled installment and re-derive the registration's totals from
            // the ledger in one transaction (the tracked Settlement above flushes with it).
            // The sweep is the actor, so the registration is stamped with the system superuser
            // (matches the audit row and the team-side settle) â€” NOT the registrant's FamilyUserId.
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
        // method for teams â€” direct mutation on the tracked entity).
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
        // Tender from the authoritative summary field (also captured to adn.Txs.[Transaction Type]),
        // falling back to the settled tx's payment shape. If neither identifies it, don't guess:
        // booking an eCheck as CC would skip the return-watcher below, leaving a later bounce
        // unreversible. Indeterminate ⇒ skip this pass (logged); the next sweep retries.
        bool? tender = !string.IsNullOrWhiteSpace(tx.accountType)
            ? string.Equals(tx.accountType, "eCheck", StringComparison.OrdinalIgnoreCase)
            : txDetail.transaction.payment?.Item switch
            {
                bankAccountMaskedType => true,
                creditCardMaskedType => false,
                _ => (bool?)null,
            };
        if (tender is null)
        {
            _logger.LogWarning(
                "ARB-Trial tx {TxId}: tender indeterminate (accountType='{AccountType}', unrecognized payment shape) — skipping this pass",
                tx.transId, tx.accountType);
            return null;
        }
        var isEcheck = tender.Value;

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
            Paymeth = isEcheck
                ? $"paid by eCheck (****{acctLast4}): {settleAmount:C} on subscriptionId: {tx.subscription.id} on {tx.submitTimeLocal:G} txID: {tx.transId}"
                : $"paid by cc: {settleAmount:C} on subscriptionId: {tx.subscription.id} on {tx.submitTimeLocal:G} txID: {tx.transId}",
            // Method-correct bucket: eCheck drafts must land in the eCheck column of the
            // payment-method totals (was hard-coded CC, mis-bucketing team ACH installments).
            PaymentMethodId = isEcheck ? EcheckPaymentMethodId : CcPaymentMethodId,
            Comment = $"{tx.transactionStatus} (team subscriptionId: {tx.subscription.id} {txDetail.transaction.responseReasonDescription})",
            Createdate = DateTime.Now,
            Modified = DateTime.Now,
            LebUserId = SystemUserId
        };

        if (tx.transactionStatus == "settledSuccessfully")
        {
            // eCheck team-ARB draft: pair the RA with a Settlement row born "Settled", exactly
            // like the registration-ARB path â€” without it, an NSF on a team installment hits
            // "not ours, skipping" in ProcessEcheckReturnAsync and the money stays booked
            // forever. Attached via the navigation property so RA + return-watcher commit in
            // ONE transaction with the booking save below.
            if (isEcheck)
            {
                var settledNow = DateTime.Now;
                _settleRepo.Add(new Settlement
                {
                    SettlementId = Guid.NewGuid(),
                    RegistrationAccounting = raRow,
                    AdnTransactionId = tx.transId,
                    Status = "Settled",
                    SubmittedAt = settledNow,
                    NextCheckAt = settledNow,
                    SettledAt = settledNow,
                    LastCheckedAt = settledNow,
                    AccountLast4 = acctLast4,
                    Modified = settledNow,
                    LebUserId = SystemUserId
                });
            }

            // Record the settled installment and re-derive the team's totals from the ledger
            // in one transaction. Other tracked edits on the shared sweep context (the
            // subscription-status sync above, the Settlement row) flush with it.
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

    // â”€â”€ eCheck Pending â†’ Settled â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<EcheckSettledDigestRow?> MarkEcheckSettled(Settlement settlement, CancellationToken ct)
    {
        var ra = settlement.RegistrationAccounting;
        var reg = ra.Registration;
        if (reg == null)
        {
            _logger.LogWarning("Settlement {Id}: no Registration loaded â€” corrupt state, skipping",
                settlement.SettlementId);
            return null;
        }

        // Status-only bookkeeping: the money booked at SUBMIT (optimistic â€” the RA was born
        // Active=true and PaidTotal moved with it). Presence in a settled batch just means the
        // draft entered the banking network; from here the only possible failure is a future
        // return (handled by ProcessEcheckReturnAsync). No Active flip, no recompute, no rep sync.
        var now = DateTime.Now;
        settlement.Status = "Settled";
        settlement.SettledAt = now;
        settlement.LastCheckedAt = now;
        settlement.Modified = now;
        await _settleRepo.SaveChangesAsync(ct);

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

    // â”€â”€ eCheck return processing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Month-end backstop entry (see <see cref="IAdnSweepService.EnsureReturnProcessedAsync"/>).
    /// Same core, same guards as the daily sweep's return path; serialized behind the same
    /// process-wide lock because those guards are unlocked read-then-write. Waits (rather than
    /// refusing like RunAsync) â€” in the close flow the lock is free by construction, and a rare
    /// manual overlap should delay the backstop, never silently skip a missed return.
    /// </summary>
    public async Task<EcheckReturnBackstopOutcome?> EnsureReturnProcessedAsync(
        string returnTransId, CancellationToken ct = default)
    {
        await RunLock.WaitAsync(ct);
        try
        {
            var creds = await _adn.GetJobAdnCredentials_FromCustomerId(_tsicSettings.DefaultCustomerId);
            var env = _adn.GetADNEnvironment();
            var row = await ProcessEcheckReturnByIdAsync(returnTransId, env, creds, ct);
            if (row == null) return null;

            _logger.LogWarning(
                "Month-end backstop: eCheck return {TxId} (original {OrigTxId}) was NOT processed by the daily sweep â€” reversal written now ({Amount:C})",
                row.ReturnTxId, row.OriginalTxId, row.AmountReversed);

            return new EcheckReturnBackstopOutcome
            {
                ReturnTxId = row.ReturnTxId,
                OriginalTxId = row.OriginalTxId,
                JobName = row.JobName,
                AmountReversed = row.AmountReversed,
                Reason = row.Reason,
            };
        }
        finally
        {
            RunLock.Release();
        }
    }

    private Task<EcheckReturnDigestRow?> ProcessEcheckReturnAsync(
        transactionSummaryType returnTx, AuthorizeNet.Environment env, AdnCredentialsViewModel creds, CancellationToken ct)
        => ProcessEcheckReturnByIdAsync(returnTx.transId, env, creds, ct);

    private async Task<EcheckReturnDigestRow?> ProcessEcheckReturnByIdAsync(
        string returnTransId, AuthorizeNet.Environment env, AdnCredentialsViewModel creds, CancellationToken ct)
    {
        // Skip if we already wrote a reversal for this return.
        if (await _accountingRepo.AnyByAdnTransactionIdAsync(returnTransId, ct))
        {
            _logger.LogDebug("eCheck return {TxId} already processed, skipping", returnTransId);
            return null;
        }

        // GetTransactionDetails to find refTransId (the original we submitted).
        var detail = _adn.ADN_GetTransactionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, returnTransId);
        if (detail?.messages?.resultCode != messageTypeEnum.Ok || detail.transaction == null)
        {
            _logger.LogWarning("eCheck return {TxId}: GetTransactionDetails failed", returnTransId);
            return null;
        }

        var originalTxId = detail.transaction.refTransId;
        if (string.IsNullOrEmpty(originalTxId))
        {
            _logger.LogWarning("eCheck return {TxId}: no refTransId â€” cannot link to original", returnTransId);
            return null;
        }

        // Look up Settlement by original tx id.
        var settlements = await _settleRepo.GetByAdnTransactionIdsAsync([originalTxId], ct);
        var settlement = settlements.FirstOrDefault();
        if (settlement == null)
        {
            _logger.LogWarning("eCheck return {TxId}: original {OrigTxId} not in echeck.Settlement â€” not ours, skipping",
                returnTransId, originalTxId);
            return null;
        }

        // Terminal guard: "Returned" is a final state. The sweep re-reads a trailing window of
        // batches, so the same returnedItem tx is re-seen on 2â€“3 consecutive runs â€” without this
        // guard the digest re-counted the return (and re-alerted) every run until it aged out.
        if (settlement.Status == "Returned")
        {
            _logger.LogDebug("eCheck return {TxId}: settlement {Id} already Returned, skipping",
                returnTransId, settlement.SettlementId);
            return null;
        }

        var ra = settlement.RegistrationAccounting;
        var reg = ra.Registration;
        if (reg == null)
        {
            _logger.LogWarning("Settlement {Id}: no Registration loaded â€” corrupt state, skipping",
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

        // One rule under optimistic booking: the money counted at SUBMIT, so EVERY return
        // reverses â€” including originals still "Pending" (bounced before our sweep ever saw
        // them settle). Idempotency is the two guards above: a reversal RA carrying the
        // return's transId, and the terminal "Returned" status. The Kind distinction is
        // digest-facing only â€” the money handling is identical.
        var kind = settlement.Status == "Settled" ? "NSF after settlement" : "returned before settlement recorded";
        var now = DateTime.Now;

        // Mark Settlement returned; flushes with the reversal transaction inside the core.
        settlement.Status = "Returned";
        settlement.ReturnReasonCode = detail.transaction.responseReasonCode.ToString();
        settlement.ReturnReasonText = detail.transaction.responseReasonDescription;
        settlement.LastCheckedAt = now;
        settlement.Modified = now;

        await ReverseEcheckMoneyAsync(settlement, ra, reg, amount,
            reversalTxId: returnTransId,
            reversalComment: $"NSF return â€” original aID {ra.AId}, reason: {settlement.ReturnReasonCode} {settlement.ReturnReasonText}",
            ct);

        return new EcheckReturnDigestRow
        {
            JobName = reg.Job?.DisplayName ?? reg.Job?.JobName ?? "",
            ReturnTxId = returnTransId,
            OriginalTxId = originalTxId,
            Kind = kind,
            Reason = $"{settlement.ReturnReasonText} ({settlement.ReturnReasonCode})",
            AmountReversed = amount,
            Registrant = reg.UserId
        };
    }

    /// <summary>
    /// Shared reversal core â€” the money-undo for a booked eCheck, used by the return handler
    /// (NSF) and the stale-Pending watchdog (draft died / return aged out of the window).
    /// Restores the (CCâˆ’EC) processing-fee credit on the keyed entity, writes the negative
    /// "Failed E-Check Payment" RA row, and re-derives totals â€” one transaction (the caller's
    /// tracked Settlement status flip flushes with it). Team-side rows also roll the delta
    /// onto the rep aggregate. The caller owns idempotency (terminal Settlement status /
    /// reversal-txId guard) and the digest row.
    /// </summary>
    private async Task ReverseEcheckMoneyAsync(
        Settlement settlement,
        RegistrationAccounting ra,
        Registrations reg,
        decimal amount,
        string? reversalTxId,
        string reversalComment,
        CancellationToken ct)
    {
        // Two flavors share this core:
        //   - Player eCheck:    RA.TeamId is null â†’ reverse on the player Registration directly.
        //   - Team eCheck (incl. team-ARB drafts): RA.TeamId is set â†’ reverse on the Teams row,
        //     then re-aggregate onto the rep's Registration via SynchronizeClubRepFinancialsAsync.
        // Restore the processing-fee credit on the reversed entity (save-free; the chokepoint
        // below re-derives FeeTotal/OwedTotal). PaidTotal is recomputed from the ledger once the
        // reversal row lands, so there is no hand-decrement here.
        if (ra.TeamId.HasValue)
        {
            var team = await _teamRepo.GetTeamFromTeamId(ra.TeamId.Value, ct);
            if (team == null)
            {
                _logger.LogWarning("Settlement {Id}: RA.TeamId {TeamId} not found â€” reversal row written, team totals left as-is",
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

        var now = DateTime.Now;

        // Record the reversal row and re-derive the keyed entity's totals from the ledger in one
        // transaction. row.TeamId routes the recompute to the team (else the registration),
        // mirroring the branch above; a missing team is a no-op recompute (the row is still
        // written). Pending edits on the shared sweep context (settlement status, fee restore)
        // flush with it.
        await _accountingRepo.RecordPaymentAndRecomputeAsync(new RegistrationAccounting
        {
            RegistrationId = ra.RegistrationId,
            TeamId = ra.TeamId,
            PaymentMethodId = FailedEcheckPaymentMethodId,
            Payamt = -amount,
            Dueamt = 0,
            Comment = reversalComment,
            AdnTransactionId = reversalTxId,
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
    }

    // â”€â”€ Stale-Pending watchdog â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //
    // The one failure mode with NO signal of its own: a draft the gateway accepted that died
    // before entering the banking network (voided / gateway error at batch time). No settlement
    // will ever come, no return will ever come â€” under optimistic booking, silence means "money
    // is good", so somebody has to check. A healthy draft goes Pending â†’ Settled in 1â€“2 business
    // days; Pending beyond the configured threshold is an anomaly by definition, and the tx id
    // lets us ask ADN point-blank what became of it. (Director notification is deliberately
    // absent â€” everything lands in the support digest; the director-facing NSF alert + inactivate
    // action are one future feature, designed together.)

    // ADN transaction statuses that mean the draft is dead without ever having originated â€”
    // reverse the booked money. Unknown statuses are deliberately NOT here: report-only, a
    // human decides (a wrong reversal is worse than a flagged oddity).
    private static readonly HashSet<string> DeadTransactionStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "voided", "declined", "generalError", "failedReview", "settlementError", "expired"
    };

    private async Task<WatchdogDigestRow?> ProcessStalePendingAsync(
        Settlement settlement, AuthorizeNet.Environment env, AdnCredentialsViewModel creds, CancellationToken ct)
    {
        var ra = settlement.RegistrationAccounting;
        var reg = ra.Registration;
        if (reg == null)
        {
            _logger.LogWarning("Watchdog: settlement {Id} has no Registration loaded â€” corrupt state, skipping",
                settlement.SettlementId);
            return null;
        }

        var row = new WatchdogDigestRow
        {
            JobName = reg.Job?.DisplayName ?? reg.Job?.JobName ?? "",
            TransId = settlement.AdnTransactionId,
            Amount = ra.Payamt ?? 0m,
            Registrant = reg.UserId,
            SubmittedAt = settlement.SubmittedAt,
            Outcome = ""
        };

        var now = DateTime.Now;
        var detail = _adn.ADN_GetTransactionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, settlement.AdnTransactionId);
        if (detail?.messages?.resultCode != messageTypeEnum.Ok || detail.transaction == null)
        {
            settlement.LastCheckedAt = now;
            settlement.Modified = now;
            await _settleRepo.SaveChangesAsync(ct);
            return row with { Outcome = "status check failed at ADN â€” will retry next run" };
        }

        var status = detail.transaction.transactionStatus ?? "";

        if (string.Equals(status, "settledSuccessfully", StringComparison.OrdinalIgnoreCase))
        {
            // Settled but we never saw the batch (sweep outage / window aged out) â€” rejoin path â‘ .
            var settled = await MarkEcheckSettled(settlement, ct);
            return row with { Outcome = settled != null ? "settled â€” batch window missed; stamped Settled" : "settled at ADN but local stamp failed" };
        }

        var amount = ra.Payamt ?? 0m;

        if (string.Equals(status, "returnedItem", StringComparison.OrdinalIgnoreCase))
        {
            // Bounced, and the returnedItem tx aged out of our batch window before a sweep saw
            // it. Same reversal as the return handler; the terminal Returned status keeps a
            // late-arriving batch sighting of the return tx from double-processing.
            settlement.Status = "Returned";
            settlement.ReturnReasonText = "returned (detected by watchdog; return tx outside batch window)";
            settlement.LastCheckedAt = now;
            settlement.Modified = now;

            if (amount <= 0m)
            {
                await _settleRepo.SaveChangesAsync(ct);
                return row with { Outcome = "returned at ADN; original amount non-positive â€” status stamped, nothing to reverse" };
            }

            await ReverseEcheckMoneyAsync(settlement, ra, reg, amount,
                reversalTxId: null,
                reversalComment: $"eCheck returned (watchdog) â€” original aID {ra.AId}, txID {settlement.AdnTransactionId}",
                ct);
            return row with { Outcome = $"returned at ADN â€” reversed {amount:C}" };
        }

        if (DeadTransactionStatuses.Contains(status))
        {
            // Died before origination: no return will ever come â€” THIS is the case only the
            // watchdog can catch. Reverse the submit-time booking.
            settlement.Status = "Failed";
            settlement.ReturnReasonText = $"never originated â€” gateway status '{status}' (watchdog)";
            settlement.LastCheckedAt = now;
            settlement.Modified = now;

            if (amount <= 0m)
            {
                await _settleRepo.SaveChangesAsync(ct);
                return row with { Outcome = $"dead at gateway ({status}); original amount non-positive â€” status stamped, nothing to reverse" };
            }

            await ReverseEcheckMoneyAsync(settlement, ra, reg, amount,
                reversalTxId: null,
                reversalComment: $"eCheck never originated â€” gateway status '{status}' (watchdog), original aID {ra.AId}, txID {settlement.AdnTransactionId}",
                ct);
            return row with { Outcome = $"never originated ({status}) â€” reversed {amount:C}" };
        }

        // Genuinely still in flight (capturedPendingSettlement etc.) or a status we don't
        // recognize â€” stamp the check, report, look again next run. No money is touched on
        // an unrecognized status: report-only, a human decides.
        settlement.LastCheckedAt = now;
        settlement.NextCheckAt = now.AddDays(1);
        settlement.Modified = now;
        await _settleRepo.SaveChangesAsync(ct);
        return row with { Outcome = $"still '{status}' at ADN â€” left Pending, will re-check" };
    }

    // â”€â”€ Orphan charge detection (report-only) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // A settled one-time charge that we can't find a local accounting row for. This is the
    // "card was charged at ADN but the booking write never landed" failure (app pool stop /
    // publish mid-request). We only REPORT it â€” no RegistrationAccounting row is written here.
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
                "ORPHAN ADN charge {TxId}: settled with no accounting row and a malformed invoice '{Invoice}' â€” cannot attribute (report only)",
                tx.transId, tx.invoiceNumber);
            return new OrphanDigestRow
            {
                Resolved = false,
                TransId = tx.transId,
                InvoiceNumber = tx.invoiceNumber,
                SettleAmount = tx.settleAmount,
                SubmittedAt = tx.submitTimeLocal,
                Registrant = null,
                Note = "malformed invoice number â€” cannot map to a registration"
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

        // Genuine orphan: money settled at ADN, no local accounting row. REPORT ONLY â€” we
        // deliberately do NOT write a RegistrationAccounting row. A human reviews the digest
        // and books it by hand if real.
        _logger.LogWarning(
            "ORPHAN ADN charge {TxId}: settled {Amount:C} for registration {RegId} (invoice {Invoice}) with no local accounting row â€” REPORT ONLY, not booked",
            tx.transId, tx.settleAmount, reg.RegistrationId, tx.invoiceNumber);

        return new OrphanDigestRow
        {
            Resolved = true,
            TransId = tx.transId,
            InvoiceNumber = tx.invoiceNumber,
            SettleAmount = tx.settleAmount,
            SubmittedAt = tx.submitTimeLocal,
            Registrant = reg.UserId,
            Note = "settled at ADN, no local accounting row â€” review and book by hand"
        };
    }

    // â”€â”€ Installment math (legacy parity) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Digest email â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private string BuildDigestHtml(
        List<ArbDigestRow> arbRows,
        List<EcheckSettledDigestRow> settledRows,
        List<EcheckReturnDigestRow> ecRows,
        List<OrphanDigestRow> orphanRows,
        List<WatchdogDigestRow> watchdogRows,
        List<UntrackedEcheckRaDto> untrackedRows,
        Counts counts,
        string? errorMessage)
    {
#if DEBUG
        var envType = "DEV";
#else
        var envType = "PROD";
#endif
        var sb = new StringBuilder();
        sb.Append($"<h3 style='margin-bottom:4px;'>ADN Sweep ({envType}, TSIC) â€” {DateTime.Now:dddd, dd MMMM yyyy HH:mm}</h3>");

        // Lead with the failure. A digest of zeros reads like a quiet morning; only this says otherwise.
        if (errorMessage != null)
        {
            sb.Append("<p style='font-size:13px;color:#b00;font-weight:bold;margin:8px 0;'>"
                + "&#9888; SWEEP FAILED â€” this pass did not complete. Payments settled at Authorize.Net may "
                + "NOT be booked in the accounting tables. The counts below are whatever was reached before "
                + "the failure, not a picture of the day.</p>");
            sb.Append($"<p style='font-size:11px;color:#b00;margin:0 0 8px 0;'><b>Error:</b> {errorMessage}</p>");
        }
        else if (counts.Errored > 0)
        {
            sb.Append($"<p style='font-size:13px;color:#b00;font-weight:bold;margin:8px 0;'>"
                + $"&#9888; {counts.Errored} transaction(s) errored â€” the pass completed, but those are not booked.</p>");
        }

        sb.Append($"<p style='font-size:9px;margin-top:0;'>Counts â€” Checked: {counts.Checked}, ARB imported: {counts.ArbImported}, eCheck settled: {counts.EcheckSettled}, eCheck returns: {counts.EcheckReturnsProcessed}, Orphans: {counts.OrphansFound}, Watchdog: {watchdogRows.Count}, Untracked eCheck: {untrackedRows.Count}, Errored: {counts.Errored}</p>");

        // â”€â”€ ARB subscription warnings â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // A suspended/canceled/terminated subscription stops drafting on its own â€” pure absence
        // from our side. The status is synced from ADN during import; this is its alarm.
        var subWarnings = arbRows
            .Where(r => !string.IsNullOrEmpty(r.SubscriptionStatus)
                && !string.Equals(r.SubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.SubscriptionStatus, "expired", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (subWarnings.Count > 0)
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; Subscription(s) not healthy â€” installments will stop arriving on their own:</p>");
            sb.Append("<ul style='font-size:9px;margin-top:0;'>");
            foreach (var w in subWarnings)
            {
                sb.Append($"<li><b>{w.SubscriptionStatus}</b> â€” {w.JobName} Â· sub {w.SubscriptionId} Â· {w.Registrant} ({w.RegistrantAssignment}) Â· owed now {w.OwedNow:C}</li>");
            }
            sb.Append("</ul>");
        }

        // â”€â”€ ARB Activity table â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ eCheck Settled table â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>eCheck Settled (Pending â†’ Settled)</h4>");
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

        // â”€â”€ eCheck Returns table â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>eCheck Returns</h4>");
        if (ecRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(no eCheck returns this run)</p>");
        }
        else
        {
            sb.Append("<table style='border-style:solid;border-collapse:separate;border-spacing:10px;font-size:9px;'>");
            sb.Append("<tr><th>#</th><th>Job</th><th>Original TransId</th><th>Return TransId</th><th>Type</th><th>Reason</th><th>Amount Reversed</th><th>Registrant</th></tr>");
            for (int i = 0; i < ecRows.Count; i++)
            {
                var r = ecRows[i];
                sb.Append("<tr>")
                  .Append($"<td>{i + 1}</td>")
                  .Append($"<td>{r.JobName}</td>")
                  .Append($"<td>{r.OriginalTxId}</td>")
                  .Append($"<td>{r.ReturnTxId}</td>")
                  .Append($"<td>{r.Kind}</td>")
                  .Append($"<td>{r.Reason}</td>")
                  .Append($"<td>{r.AmountReversed:C}</td>")
                  .Append($"<td>{r.Registrant}</td>")
                  .Append("</tr>");
            }
            sb.Append("</table>");
        }

        // â”€â”€ Watchdog table (stale Pending drafts) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>eCheck Watchdog (Pending beyond threshold)</h4>");
        if (watchdogRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(no stale pending drafts â€” every draft settled or resolved on time âœ“)</p>");
        }
        else
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; Draft(s) still Pending past the threshold â€” status was queried at ADN directly; outcome per row.</p>");
            sb.Append("<table style='border-style:solid;border-collapse:separate;border-spacing:10px;font-size:9px;'>");
            sb.Append("<tr><th>#</th><th>Job</th><th>TransId</th><th>Amount</th><th>Registrant</th><th>Submitted</th><th>Outcome</th></tr>");
            for (int i = 0; i < watchdogRows.Count; i++)
            {
                var r = watchdogRows[i];
                sb.Append("<tr>")
                  .Append($"<td>{i + 1}</td>")
                  .Append($"<td>{r.JobName}</td>")
                  .Append($"<td>{r.TransId}</td>")
                  .Append($"<td>{r.Amount:C}</td>")
                  .Append($"<td>{r.Registrant}</td>")
                  .Append($"<td>{r.SubmittedAt:g}</td>")
                  .Append($"<td>{r.Outcome}</td>")
                  .Append("</tr>");
            }
            sb.Append("</table>");
        }

        // â”€â”€ Untracked eCheck payments (integrity net) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>Untracked eCheck Payments (no Settlement return-watcher)</h4>");
        if (untrackedRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(none â€” every booked eCheck is registered for return-watching âœ“)</p>");
        }
        else
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; Booked eCheck money the sweep cannot watch â€” a bounce on these would be silently dropped. Investigate each; likely a partial write.</p>");
            sb.Append("<table style='border-style:solid;border-collapse:separate;border-spacing:10px;font-size:9px;'>");
            sb.Append("<tr><th>#</th><th>RA AId</th><th>TransId</th><th>Amount</th><th>Created</th></tr>");
            for (int i = 0; i < untrackedRows.Count; i++)
            {
                var r = untrackedRows[i];
                sb.Append("<tr>")
                  .Append($"<td>{i + 1}</td>")
                  .Append($"<td>{r.AId}</td>")
                  .Append($"<td>{r.AdnTransactionId}</td>")
                  .Append($"<td>{r.Payamt:C}</td>")
                  .Append($"<td>{r.Createdate:g}</td>")
                  .Append("</tr>");
            }
            sb.Append("</table>");
        }

        // â”€â”€ Orphan ADN Charges table (report-only) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>Orphan ADN Charges (settled at ADN, not booked locally)</h4>");
        if (orphanRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(none â€” every settled charge has a matching accounting row âœ“)</p>");
        }
        else
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>âš ï¸ Money settled at Authorize.Net with no local accounting row. REPORT ONLY â€” nothing was booked. Review each and enter the payment by hand.</p>");
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
            // The verdict rides the subject â€” this is read on a phone, and a failed sweep must be
            // distinguishable from a quiet one without opening the mail.
            Subject = $"AdnSweep AI {DateTime.Now:dddd, dd MMMM yyyy HH:mm}"
                + (errorMessage != null ? " â€” SWEEP FAILED" : errored > 0 ? $" â€” {errored} ERRORED" : ""),
            HtmlBody = html
        }, sendInDevelopment: true, cancellationToken: ct);
    }

    // â”€â”€ Internal types â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        /// <summary>Digest-facing failure type: "NSF after settlement" vs "returned before settlement recorded".</summary>
        public required string Kind { get; init; }
        public required string Reason { get; init; }
        public required decimal AmountReversed { get; init; }
        public required string? Registrant { get; init; }
    }

    // Watchdog finding: a Settlement still Pending past the threshold, with what ADN said and
    // what was done about it ("never originated â€” reversed", "settled â€” window missed", â€¦).
    private sealed record WatchdogDigestRow
    {
        public required string JobName { get; init; }
        public required string TransId { get; init; }
        public required decimal Amount { get; init; }
        public required string? Registrant { get; init; }
        public required DateTime SubmittedAt { get; init; }
        public required string Outcome { get; init; }
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
