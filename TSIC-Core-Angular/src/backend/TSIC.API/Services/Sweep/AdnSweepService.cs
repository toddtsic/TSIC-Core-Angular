using System.Text;
using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.Options;
using TSIC.API.Configuration;
using TSIC.API.Extensions;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Arb;
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
    // "E-Check Payment" — used for settled eCheck / ACH-ARB draft RA rows.
    private static readonly Guid EcheckPaymentMethodId = Guid.Parse("2EECA575-A268-E111-9D56-F04DA202060D");
    // "Failed E-Check Payment" — used for NSF reversal RA rows.
    private static readonly Guid FailedEcheckPaymentMethodId = Guid.Parse("2FECA575-A268-E111-9D56-F04DA202060D");

    /// <summary>
    /// What the eCheck / watchdog / orphan sections say on a dry run, in place of their all-clears.
    /// Those steps do not run at all off Production, so their row lists are empty by construction —
    /// and "(none — every settled charge has a matching accounting row ✓)" then asserts a clean result
    /// for a check nobody performed. A report that cannot tell "nothing wrong" from "nothing looked at"
    /// is the exact defect this feature exists to fix; it must not commit it itself.
    /// </summary>
    /// <summary>
    /// Where a DRY RUN digest goes. Not support@ — that address is a Vade Secure-fronted forwarder that
    /// quarantines silently (see SendDigestAsync). Testing the sweep must not also be a test of a spam
    /// gateway's scoring. Production is untouched and still mails support@.
    /// </summary>
    private const string DryRunDigestRecipient = "toddtsic@gmail.com";

    /// <summary>
    /// Who the PRODUCTION digest goes to as of 2026-08-27. Was support@ alone, from the first sweep
    /// commit until today; support@ stopped delivering and these are the people who read it anyway.
    ///
    /// support@ is deliberately NOT in this list. Every digest is sent FROM support@ (the SES verified
    /// identity, forced in EmailService.NormalizeFromHeader), so mailing support@ meant the address
    /// mailing itself — arriving at its own gateway from an outside IP. Taking it off the To line is
    /// what this change tests.
    /// </summary>
    private static readonly string[] ProductionDigestRecipients =
    [
        "toddtsic@gmail.com",
        "anntsic@gmail.com",
        "chelseatsic@gmail.com"
    ];

    private const string DryRunNotRun =
        "<p style='font-size:9px;color:#888;'>(not run on a dry run — this step moves or reverses money, "
        + "so it is skipped entirely. Nothing here was examined.)</p>";
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
    private readonly IArbNotificationService _arbNotify;
    private readonly TsicSettings _tsicSettings;
    private readonly AdnSweepOptions _options;
    private readonly ILogger<AdnSweepService> _logger;

    /// <summary>
    /// Off Production this sweep REPORTS instead of acting: it reads the real production settled
    /// batches, resolves every failed ARB draft exactly as the live pass would, renders the digest and
    /// each family email, and writes nothing, settles nothing, sends nothing.
    ///
    /// Derived from the environment in the CONSTRUCTOR, and deliberately not a parameter. A dry-run
    /// flag a caller passes is a flag a caller can pass wrongly, and the way to be wrong on Production
    /// is catastrophic and silent: the sweep would import no money, book no settlements, and still
    /// report Succeeded with IsTrustworthy true, so the month-end close would build QuickBooks files
    /// from a ledger missing a day. There is nothing to pass, so there is nothing to pass incorrectly.
    ///
    /// Nothing is lost by this. Before it existed, an off-Production run resolved ADN to SANDBOX,
    /// found no production batches, and did nothing at all — just without showing you anything.
    /// </summary>
    private readonly bool _dryRun;

    public AdnSweepService(
        IEcheckSettlementRepository settleRepo,
        IRegistrationAccountingRepository accountingRepo,
        IRegistrationRepository regRepo,
        ITeamRepository teamRepo,
        IArbSubscriptionRepository arbRepo,
        IAdnApiService adn,
        IRegistrationFeeAdjustmentService feeAdj,
        IEmailService email,
        IArbNotificationService arbNotify,
        IHostEnvironment env,
        IOptions<TsicSettings> tsicSettings,
        IOptions<AdnSweepOptions> options,
        ILogger<AdnSweepService> logger)
    {
        _dryRun = env.IsSandbox();
        _settleRepo = settleRepo;
        _accountingRepo = accountingRepo;
        _regRepo = regRepo;
        _teamRepo = teamRepo;
        _arbRepo = arbRepo;
        _adn = adn;
        _feeAdj = feeAdj;
        _email = email;
        _arbNotify = arbNotify;
        _tsicSettings = tsicSettings.Value;
        _options = options.Value;
        _logger = logger;
    }

    // One sweep at a time, process-wide. Every idempotency guard in the sweep is unlocked
    // read-then-write ("already imported? no → book it"), so two concurrent passes could both
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
            _logger.LogWarning("ADN sweep already running — {TriggeredBy} request refused", triggeredBy);
            return new AdnSweepResult
            {
                Checked = 0,
                ArbImported = 0,
                EcheckSettled = 0,
                EcheckReturnsProcessed = 0,
                OrphansFound = 0,
                Errored = 0,
                Succeeded = false,
                ErrorMessage = "Sweep already running — request refused.",
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

        // Step-boundary logging. Until this existed the only evidence a run had happened at all was the
        // digest arriving — so a run that finished and mailed nothing was indistinguishable from a run
        // that never started, which is exactly the hole that cost a staging afternoon.
        _logger.LogInformation(
            "ADN sweep START: triggeredBy={TriggeredBy} daysPrior={DaysPrior} dryRun={DryRun} sendDigest={SendDigest}",
            triggeredBy, daysPrior, _dryRun, sendDigest);

        // echeck.SweepLog's CHECK constraint permits only "Scheduled" and "Manual", so a dry run has no
        // honest value to write and must not invent a third one. It is not a sweep of record: nothing
        // it observes is booked, so a row claiming a pass ran would misreport the ledger's coverage.
        var log = _dryRun ? null : await _settleRepo.StartSweepLogAsync(triggeredBy, ct);
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
            // The scheduled sweep is hard-gated to a Production host (AdnSweepBackgroundService), so the
            // env-bound resolvers return the production account where it actually runs.
            //
            // A DRY RUN forces the production account instead. Without it the resolvers hand back
            // SANDBOX, the batch list comes back empty, and the run reports a clean morning having
            // examined nothing — which is exactly the failure this whole feature exists to stop being
            // indistinguishable from a real one. Safe because a dry run only READS: the two writes on
            // the failed-draft path are skipped below, steps 3-7 do not run, and nothing is sent. Same
            // read-only exception the month-end reconciliation pull already takes
            // (AdnReconciliationService, "Hardcoded PRODUCTION").
            var creds = _dryRun
                ? await _adn.GetJobAdnProductionCredentials_FromCustomerId(_tsicSettings.DefaultCustomerId)
                : await _adn.GetJobAdnCredentials_FromCustomerId(_tsicSettings.DefaultCustomerId);
            var env = _dryRun ? AuthorizeNet.Environment.PRODUCTION : _adn.GetADNEnvironment();

            // 1) Walk batches, accumulate flat tx list. A batch-list error is NOT an empty day — it used
            // to return [] and sail on, producing a digest of zeros that reads exactly like a quiet
            // morning. Throw instead, so the failure reaches the catch and is reported as a failure.
            var allTxs = FetchBatchTransactions(env, creds.AdnLoginId!, creds.AdnTransactionKey!, daysPrior);
            counts.Checked = allTxs.Count;

            _logger.LogInformation(
                "ADN sweep step 1 (batches): checked={Checked} arbCandidates={Arb} orphanCandidates={Orphans} returns={Returns} env={Env}",
                allTxs.Count,
                allTxs.Count(IsArbCandidate),
                allTxs.Count(IsOrphanCandidate),
                allTxs.Count(t => t.transactionStatus == "returnedItem"),
                env);

            // 2) Process ARB transactions (legacy parity). A dry run resolves EVERY candidate exactly as
            // the live pass does — detail fetch, invoice → registration, installment math, tender
            // resolution, the RA row built in full — and only the writes are skipped.
            //
            // It briefly resolved only the FAILED drafts, to cut Authorize.Net round-trips. That was
            // wrong twice over. It gutted the rehearsal: the settled drafts are the ones that book money,
            // so the money path went unexercised while the run still reported a clean result. And it made
            // ARB Activity a line-for-line copy of Failed ARB Drafts, so a normal morning read as one
            // where every draft failed. The premise was wrong too — the 4am production pass resolves all
            // of them and mails the digest inside a minute.
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

            _logger.LogInformation(
                "ADN sweep step 2 (ARB): imported={Imported} failedDrafts={Failed} errored={Errored}",
                counts.ArbImported,
                arbRows.Count(r => !string.Equals(r.TransactionStatus, "settledSuccessfully", StringComparison.OrdinalIgnoreCase)),
                counts.Errored);

            // Steps 3, 4 and 6 write: a settlement stamp, a payment reversal, and a watchdog that can
            // settle or reverse a silent draft. A dry run skips those three outright rather than teaching
            // three more money paths a don't-write mode — step 2's version of that already produced one
            // subtle bug (a tracked entity flushed by an unrelated SaveChanges), and these are the paths
            // that move real money.
            //
            // Steps 5 and 7 are NOT skipped. Both are report-only — orphan detection is a query plus a
            // list, the integrity net is a single repository read — so there was never a reason to
            // withhold them, and skipping them cost the dry run two genuine findings it could report.
            if (!_dryRun)
            {
            // 3) Process eCheck Pending → Settled transitions.
            // Walk batch txs that settled successfully and match against our pending Settlement
            // rows. No per-tx API call is needed — presence in a settled batch is the proof of
            // settlement. Status-only: the money booked at submit (optimistic); this stamp records
            // that the draft entered the banking network, which the return handler and watchdog
            // key on. subscription == null excludes ARB drafts, which book their RA in step 2
            // (ImportArbTransactionAsync) — see the ARB/eCheck split there.
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
            _logger.LogInformation(
                "ADN sweep steps 3-4 (eCheck): settled={Settled} returnsProcessed={Returns}",
                counts.EcheckSettled, counts.EcheckReturnsProcessed);
            } // end steps 3-4 (live runs only)

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

            _logger.LogInformation("ADN sweep step 5 (orphans): found={Orphans}", counts.OrphansFound);

            if (!_dryRun)
            {
            // 6) Stale-Pending watchdog: drafts that went silent. Healthy drafts settle in 1–2
            // business days; a Settlement still Pending past the threshold gets its status
            // queried at ADN directly and is settled, reversed, or flagged. This is the only
            // detector for a draft that died before origination — that failure produces no
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
            _logger.LogInformation("ADN sweep step 6 (watchdog): staleHandled={Stale}", watchdogRows.Count);
            } // end step 6 (live runs only)

            // 7) Integrity net: booked eCheck money with no Settlement return-watcher. The atomic
            // mint makes this unreachable going forward; expected count every run is 0. REPORT-ONLY.
            try
            {
                untrackedRows = await _settleRepo.GetUntrackedEcheckAccountingAsync(EcheckPaymentMethodId, ct);
                _logger.LogInformation("ADN sweep step 7 (integrity net): untracked={Untracked}", untrackedRows.Count);
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

        // 8) Notify the families behind failed ARB drafts.
        //
        // DELIBERATELY LAST, and deliberately outside the try above. Every step before this one is
        // proven, money-bearing code whose outcome feeds counts.Errored -> AdnSweepResult.IsTrustworthy
        // -> whether the 1st-of-month close is allowed to build the QuickBooks IIF files. If a bounced
        // email could increment Errored, one unreachable family would silently stop month-end close.
        // So this step scores itself separately and can never touch the sweep's verdict.
        //
        // Exactly-once falls out of the import guard: ImportArbTransactionAsync returns null for a
        // transaction already in RegistrationAccounting, so an already-imported failure never reaches
        // arbRows and cannot be emailed twice. A manual re-run the same morning re-mails nobody.
        // Team ARB-Trial rows are excluded by the RegistrationId null check: no registration behind
        // them, so no family to write to.
        var notifyResult = ArbNotifyResultDto.Empty;
        try
        {
            var failedDrafts = arbRows
                .Where(r => r.RegistrationId.HasValue
                    && !string.Equals(r.TransactionStatus, "settledSuccessfully", StringComparison.OrdinalIgnoreCase))
                .Select(r => new ArbFailedDraftDto
                {
                    RegistrationId = r.RegistrationId!.Value,
                    InvoiceNumber = r.InvoiceNumber,
                    TransId = r.TransId,
                    TransactionStatus = r.TransactionStatus,
                    OwedNow = r.OwedNow,
                    SubscriptionStatus = r.SubscriptionStatus,
                    Registrant = r.Registrant,
                    JobName = r.JobName
                })
                .ToList();

            _logger.LogInformation("ADN sweep step 8 (family notify): failedDrafts={Failed} dryRun={DryRun}",
                failedDrafts.Count, _dryRun);

            // ══ FAMILY EMAIL DISABLED 2026-08-27 (Todd) ═════════════════════════════════════════
            // The sweep still FINDS and REPORTS every failed draft; it no longer writes to the
            // family, and writes no per-job emailLogs row. The machinery below is intact and
            // untouched — this is a switch, not a removal.
            //
            // TO RE-ENABLE: uncomment the NotifyFailedDraftsAsync line, delete the Found-only line
            // under it, and uncomment the emailLogs write in ArbNotificationService (marked with
            // the same banner). Then reword the digest labelling in BuildDigestHtml /
            // BuildDigestText, also marked. Four sites, all searchable on "FAMILY EMAIL DISABLED".
            //
            // notifyResult = await _arbNotify.NotifyFailedDraftsAsync(failedDrafts, ct);
            notifyResult = ArbNotifyResultDto.Empty with { Found = failedDrafts.Count };
            // ════════════════════════════════════════════════════════════════════════════════════

            _logger.LogInformation(
                "ADN sweep step 8 complete: found={Found} emailed={Emailed} notEmailed={NotEmailed} (family email DISABLED)",
                notifyResult.Found, notifyResult.Emailed, notifyResult.Found - notifyResult.Emailed);
        }
        catch (Exception ex)
        {
            // Same contract as the digest send below: a notification failure is reported, never
            // allowed to mask or change the sweep's own outcome.
            _logger.LogError(ex, "ARB failed-draft notification step failed");
        }

        // The digest is built and sent OUTSIDE the try — a failed sweep must still mail, and must say so.
        // It used to be the last statement inside the try, so any throw upstream skipped it entirely and
        // the only signal was the 5am email not arriving. Silence is not a report.
        var html = BuildDigestHtml(arbRows, settledRows, ecRows, orphanRows, watchdogRows, untrackedRows, notifyResult, counts, errorMessage);
        // The digest mails on a dry run too, to support only. What the dry run must never do is reach a
        // FAMILY; the support digest is how the delivery path itself gets tested — SES, the transport
        // hop, and how a mail client renders the HTML. Suppressing it meant the transport was the one
        // part no test could reach, which is exactly where the digest was being corrupted.
        if (sendDigest)
        {
            try
            {
                // No ct. See SendDigestAsync.
                await SendDigestAsync(
                    html,
                    BuildDigestText(notifyResult, counts, watchdogRows.Count, untrackedRows.Count, errorMessage),
                    errorMessage,
                    counts.Errored);
            }
            catch (Exception ex)
            {
                // Never let a mail failure mask the sweep's own outcome in SweepLog / the return value.
                _logger.LogError(ex, "ADN sweep digest send failed");
            }
        }
        else
        {
            _logger.LogInformation("ADN sweep digest: not requested by the caller (sendDigest=false)");
        }

        _logger.LogInformation(
            "ADN sweep END: dryRun={DryRun} checked={Checked} arbImported={Arb} errored={Errored} succeeded={Succeeded}",
            _dryRun, counts.Checked, counts.ArbImported, counts.Errored, errorMessage == null);

        // No log row was opened on a dry run, so there is none to complete.
        if (log != null)
        {
            await _settleRepo.CompleteSweepLogAsync(
                log, counts.Checked, counts.EcheckSettled, counts.EcheckReturnsProcessed, counts.Errored, errorMessage, ct);
        }

        return new AdnSweepResult
        {
            Checked = counts.Checked,
            ArbImported = counts.ArbImported,
            EcheckSettled = counts.EcheckSettled,
            EcheckReturnsProcessed = counts.EcheckReturnsProcessed,
            OrphansFound = counts.OrphansFound,
            FailedDraftsFound = notifyResult.Found,
            FailedDraftsEmailed = notifyResult.Emailed,
            FailedDraftsNotEmailed = notifyResult.Skipped,
            Errored = counts.Errored,
            Succeeded = errorMessage == null,
            ErrorMessage = errorMessage,
            DigestHtml = html,
            DryRun = _dryRun,
            RenderedEmails = notifyResult.Rendered,
            NotEmailed = notifyResult.Skips,
            AuditRows = notifyResult.AuditRows,
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
        //
        // The status ADN just reported, which the digest row and the alive-vs-dead email choice read.
        // Held in a LOCAL rather than written onto `reg` when this is a dry run: GetByAdnSubscriptionIdAsync
        // returns a TRACKED entity, and EmailLogRepository.LogAsync commits the shared scoped DbContext —
        // so assigning it here and then writing a [DRY RUN] audit row downstream would flush this status
        // change to the database. A dry run would quietly write.
        var effectiveSubStatus = reg.AdnSubscriptionStatus;
        var subStatusResp = _adn.GetSubscriptionStatus(env, creds.AdnLoginId!, creds.AdnTransactionKey!, reg.AdnSubscriptionId!);
        if (subStatusResp?.messages?.resultCode == messageTypeEnum.Ok)
        {
            var liveSubStatus = subStatusResp.status.ToString();
            if (!string.IsNullOrEmpty(liveSubStatus) && reg.AdnSubscriptionStatus != liveSubStatus)
            {
                effectiveSubStatus = liveSubStatus;
                if (!_dryRun)
                {
                    await _arbRepo.UpdateSubscriptionStatusAsync(reg.RegistrationId, liveSubStatus, ct);
                    reg.AdnSubscriptionStatus = liveSubStatus;
                }
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

        // A dry run books nothing. It still built raRow above, because building it is what proves the
        // tender resolution and the amount are right; it simply never reaches the database. Note this
        // covers the SETTLED branch too — a dry run walks successful drafts as well, to keep the digest
        // counts honest, and must not book their money.
        if (_dryRun)
        {
            // fall through to the digest row
        }
        else if (tx.transactionStatus == "settledSuccessfully")
        {
            // eCheck ARB draft: pair the RA with a Settlement row born "Settled" (the money books
            // below). An ACH draft can still be returned days later; ProcessEcheckReturnAsync
            // matches the return to its original via the Settlement table, so without this row an
            // NSF on a plan installment would be logged "not ours, skipping" and never reversed.
            // The subscription drafts autonomously (no submit-time Pending row), so THIS is the
            // only place a plan installment's Settlement key is created. Step-3's
            // subscription==null filter keeps this Settled row out of the Pending→Settled path.
            // Attached via the navigation property BEFORE the booking save so RA + return-watcher
            // commit in ONE transaction — a crash between separate saves would book money no
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
            // (matches the audit row and the team-side settle) — NOT the registrant's FamilyUserId.
            await _accountingRepo.RecordPaymentAndRecomputeAsync(raRow, SystemUserId, ct);
        }
        else
        {
            // Non-settling transaction: keep the audit row, apply no payment (totals unchanged).
            _accountingRepo.Add(raRow);
            await _accountingRepo.SaveChangesAsync(ct);
        }

        // The sweep is holding the transaction that just failed, so it knows without a lookup.
        var (owedNow, paymentXofY, nextInstallment) = ComputeInstallmentMath(
            reg,
            currentDraftFailed: !string.Equals(
                tx.transactionStatus, "settledSuccessfully", StringComparison.OrdinalIgnoreCase));

        return new ArbDigestRow
        {
            JobName = reg.Job?.JobName ?? reg.Job?.DisplayName ?? "",
            TransId = tx.transId,
            SubscriptionId = tx.subscription.id.ToString(),
            SubscriptionStatus = effectiveSubStatus,
            SettleAmount = settleAmount,
            TransactionStatus = tx.transactionStatus,
            OwedNow = owedNow,
            PaymentXofY = paymentXofY,
            NextInstallment = nextInstallment,
            Registrant = RegistrantDisplay(reg),
            RegistrantAssignment = reg.Assignment,
            RegistrationId = reg.RegistrationId,
            InvoiceNumber = tx.invoiceNumber
        };
    }

    private async Task<ArbDigestRow?> ImportTeamArbTransactionAsync(
        transactionSummaryType tx, Domain.Entities.Teams team, AuthorizeNet.Environment env, AdnCredentialsViewModel creds, CancellationToken ct)
    {
        // Sync ADN-known subscription status onto the team row (no separate ARB repo
        // method for teams — direct mutation on the tracked entity).
        //
        // Same dry-run rule as the player path above, and here it matters more because the mutation IS
        // the persistence: the entity is tracked, so these three assignments become an UPDATE at the
        // next SaveChanges on the scoped context — which the [DRY RUN] emailLogs write would trigger.
        var effectiveSubStatus = team.AdnSubscriptionStatus;
        var subStatusResp = _adn.GetSubscriptionStatus(env, creds.AdnLoginId!, creds.AdnTransactionKey!, team.AdnSubscriptionId!);
        if (subStatusResp?.messages?.resultCode == messageTypeEnum.Ok)
        {
            var liveSubStatus = subStatusResp.status.ToString();
            if (!string.IsNullOrEmpty(liveSubStatus) && team.AdnSubscriptionStatus != liveSubStatus)
            {
                effectiveSubStatus = liveSubStatus;
                if (!_dryRun)
                {
                    team.AdnSubscriptionStatus = liveSubStatus;
                    team.Modified = DateTime.Now;
                    team.LebUserId = SystemUserId;
                }
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

        // Dry run books nothing — same rule as the registration path.
        if (_dryRun)
        {
            // fall through to the digest row
        }
        else if (tx.transactionStatus == "settledSuccessfully")
        {
            // eCheck team-ARB draft: pair the RA with a Settlement row born "Settled", exactly
            // like the registration-ARB path — without it, an NSF on a team installment hits
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

        var registrant = team.Clubrep is { } rep
            ? $"{rep.FirstName} {rep.LastName}".Trim()
            : team.ClubrepId ?? "";
        var assignment = $"{team.TeamName ?? team.DisplayName ?? team.TeamFullName}";

        return new ArbDigestRow
        {
            JobName = team.Job?.JobName ?? team.Job?.DisplayName ?? "",
            TransId = tx.transId,
            SubscriptionId = tx.subscription.id.ToString(),
            SubscriptionStatus = effectiveSubStatus,
            SettleAmount = settleAmount,
            TransactionStatus = tx.transactionStatus,
            OwedNow = owedNow,
            PaymentXofY = paymentXofY,
            NextInstallment = nextInstallment,
            Registrant = registrant,
            RegistrantAssignment = assignment,
            // Team ARB-Trial: no registration, so no family to notify.
            RegistrationId = null,
            InvoiceNumber = tx.invoiceNumber
        };
    }

    // Digest-facing registrant cell: the human name (legacy sweep parity — it printed
    // User.FirstName/LastName); the UserId FK only as a last resort when no User row loads.
    private static string RegistrantDisplay(Registrations reg)
        => reg.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : reg.UserId ?? "";

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

        // Status-only bookkeeping: the money booked at SUBMIT (optimistic — the RA was born
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
            JobName = reg.Job?.JobName ?? reg.Job?.DisplayName ?? "",
            TransId = settlement.AdnTransactionId,
            Amount = ra.Payamt ?? 0m,
            AccountLast4 = settlement.AccountLast4 ?? "",
            Registrant = RegistrantDisplay(reg),
            SubmittedAt = settlement.SubmittedAt,
            SettledAt = now
        };
    }

    // ── eCheck return processing ──────────────────────────────────────

    /// <summary>
    /// Month-end backstop entry (see <see cref="IAdnSweepService.EnsureReturnProcessedAsync"/>).
    /// Same core, same guards as the daily sweep's return path; serialized behind the same
    /// process-wide lock because those guards are unlocked read-then-write. Waits (rather than
    /// refusing like RunAsync) — in the close flow the lock is free by construction, and a rare
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
                "Month-end backstop: eCheck return {TxId} (original {OrigTxId}) was NOT processed by the daily sweep — reversal written now ({Amount:C})",
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
            _logger.LogWarning("eCheck return {TxId}: no refTransId — cannot link to original", returnTransId);
            return null;
        }

        // Look up Settlement by original tx id.
        var settlements = await _settleRepo.GetByAdnTransactionIdsAsync([originalTxId], ct);
        var settlement = settlements.FirstOrDefault();
        if (settlement == null)
        {
            _logger.LogWarning("eCheck return {TxId}: original {OrigTxId} not in echeck.Settlement — not ours, skipping",
                returnTransId, originalTxId);
            return null;
        }

        // Terminal guard: "Returned" is a final state. The sweep re-reads a trailing window of
        // batches, so the same returnedItem tx is re-seen on 2–3 consecutive runs — without this
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

        // One rule under optimistic booking: the money counted at SUBMIT, so EVERY return
        // reverses — including originals still "Pending" (bounced before our sweep ever saw
        // them settle). Idempotency is the two guards above: a reversal RA carrying the
        // return's transId, and the terminal "Returned" status. The Kind distinction is
        // digest-facing only — the money handling is identical.
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
            reversalComment: $"NSF return — original aID {ra.AId}, reason: {settlement.ReturnReasonCode} {settlement.ReturnReasonText}",
            ct);

        return new EcheckReturnDigestRow
        {
            JobName = reg.Job?.JobName ?? reg.Job?.DisplayName ?? "",
            ReturnTxId = returnTransId,
            OriginalTxId = originalTxId,
            Kind = kind,
            Reason = $"{settlement.ReturnReasonText} ({settlement.ReturnReasonCode})",
            AmountReversed = amount,
            Registrant = RegistrantDisplay(reg)
        };
    }

    /// <summary>
    /// Shared reversal core — the money-undo for a booked eCheck, used by the return handler
    /// (NSF) and the stale-Pending watchdog (draft died / return aged out of the window).
    /// Restores the (CCâˆ’EC) processing-fee credit on the keyed entity, writes the negative
    /// "Failed E-Check Payment" RA row, and re-derives totals — one transaction (the caller's
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
        //   - Player eCheck:    RA.TeamId is null → reverse on the player Registration directly.
        //   - Team eCheck (incl. team-ARB drafts): RA.TeamId is set → reverse on the Teams row,
        //     then re-aggregate onto the rep's Registration via SynchronizeClubRepFinancialsAsync.
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

    // ── Stale-Pending watchdog ────────────────────────────────────────
    //
    // The one failure mode with NO signal of its own: a draft the gateway accepted that died
    // before entering the banking network (voided / gateway error at batch time). No settlement
    // will ever come, no return will ever come — under optimistic booking, silence means "money
    // is good", so somebody has to check. A healthy draft goes Pending → Settled in 1–2 business
    // days; Pending beyond the configured threshold is an anomaly by definition, and the tx id
    // lets us ask ADN point-blank what became of it. (Director notification is deliberately
    // absent — everything lands in the support digest; the director-facing NSF alert + inactivate
    // action are one future feature, designed together.)

    // ADN transaction statuses that mean the draft is dead without ever having originated —
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
            _logger.LogWarning("Watchdog: settlement {Id} has no Registration loaded — corrupt state, skipping",
                settlement.SettlementId);
            return null;
        }

        var row = new WatchdogDigestRow
        {
            JobName = reg.Job?.JobName ?? reg.Job?.DisplayName ?? "",
            TransId = settlement.AdnTransactionId,
            Amount = ra.Payamt ?? 0m,
            Registrant = RegistrantDisplay(reg),
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
            return row with { Outcome = "status check failed at ADN — will retry next run" };
        }

        var status = detail.transaction.transactionStatus ?? "";

        if (string.Equals(status, "settledSuccessfully", StringComparison.OrdinalIgnoreCase))
        {
            // Settled but we never saw the batch (sweep outage / window aged out) — rejoin path ①.
            var settled = await MarkEcheckSettled(settlement, ct);
            return row with { Outcome = settled != null ? "settled — batch window missed; stamped Settled" : "settled at ADN but local stamp failed" };
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
                return row with { Outcome = "returned at ADN; original amount non-positive — status stamped, nothing to reverse" };
            }

            await ReverseEcheckMoneyAsync(settlement, ra, reg, amount,
                reversalTxId: null,
                reversalComment: $"eCheck returned (watchdog) — original aID {ra.AId}, txID {settlement.AdnTransactionId}",
                ct);
            return row with { Outcome = $"returned at ADN — reversed {amount:C}" };
        }

        if (DeadTransactionStatuses.Contains(status))
        {
            // Died before origination: no return will ever come — THIS is the case only the
            // watchdog can catch. Reverse the submit-time booking.
            settlement.Status = "Failed";
            settlement.ReturnReasonText = $"never originated — gateway status '{status}' (watchdog)";
            settlement.LastCheckedAt = now;
            settlement.Modified = now;

            if (amount <= 0m)
            {
                await _settleRepo.SaveChangesAsync(ct);
                return row with { Outcome = $"dead at gateway ({status}); original amount non-positive — status stamped, nothing to reverse" };
            }

            await ReverseEcheckMoneyAsync(settlement, ra, reg, amount,
                reversalTxId: null,
                reversalComment: $"eCheck never originated — gateway status '{status}' (watchdog), original aID {ra.AId}, txID {settlement.AdnTransactionId}",
                ct);
            return row with { Outcome = $"never originated ({status}) — reversed {amount:C}" };
        }

        // Genuinely still in flight (capturedPendingSettlement etc.) or a status we don't
        // recognize — stamp the check, report, look again next run. No money is touched on
        // an unrecognized status: report-only, a human decides.
        settlement.LastCheckedAt = now;
        settlement.NextCheckAt = now.AddDays(1);
        settlement.Modified = now;
        await _settleRepo.SaveChangesAsync(ct);
        return row with { Outcome = $"still '{status}' at ADN — left Pending, will re-check" };
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
            Registrant = RegistrantDisplay(reg),
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

    /// <summary>
    /// Installment position and arrears for a registration ARB plan.
    /// </summary>
    /// <param name="currentDraftFailed">
    /// True when the caller is looking at a draft for the CURRENT installment that did not settle.
    /// The first-installment suppression below assumes the opening payment may still be settling and
    /// PaidTotal simply has not caught up; a known decline says otherwise. Without this, a family
    /// whose FIRST installment declined was emailed - and shown - $0.00 owed, permanently.
    /// Mirrors ArbDefensiveService.CalculateOwedNow, which applies the same override to its
    /// 48-hour grace. The two must agree: this figure is the number in the family email, that one is
    /// the number on the page the email links to.
    /// </param>
    private (decimal OwedNow, string PaymentXofY, DateTime? NextInstallment) ComputeInstallmentMath(
        Registrations reg, bool currentDraftFailed = false)
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
        var owedNow = (occAsOfNow <= 1 && !currentDraftFailed)
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
        List<WatchdogDigestRow> watchdogRows,
        List<UntrackedEcheckRaDto> untrackedRows,
        ArbNotifyResultDto notifyResult,
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

        // A dry run's ONLY output is this report — it writes nothing and sends nothing. So the report
        // has to be exact about what it did and did not do. Say it before any number is read.
        if (_dryRun)
        {
            sb.Append("<p style='font-size:12px;font-weight:bold;color:#0b5;margin:8px 0 2px 0;'>"
                + "DRY RUN — nothing was written, settled, or sent.</p>");
            sb.Append("<p style='font-size:10px;margin:0 0 8px 0;'>Real production Authorize.Net batches were read and "
                + "every recurring draft resolved in full, exactly as the 4am pass does — only the writes and the family "
                + "sends were withheld. Orphan detection and the eCheck integrity net ran too; both are report-only. "
                + "The three steps that move money — eCheck settlement, return reversal, the stale-draft watchdog — did "
                + "not run, and their sections say so rather than reporting zero.</p>");
        }

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

        // "imported" is a claim about the database. On a dry run nothing was imported, and the eCheck /
        // orphan counters are structurally zero because their steps never ran — printing them as 0 next
        // to real numbers reads as "nothing to report", which is a different statement from "not checked".
        if (_dryRun)
        {
            sb.Append($"<p style='font-size:9px;margin-top:0;'>Counts — Checked: {counts.Checked}, "
                + $"ARB resolved: {counts.ArbImported}, Orphans: {counts.OrphansFound}, "
                + $"Untracked eCheck: {untrackedRows.Count}, "
                // FAMILY EMAIL DISABLED 2026-08-27 — original: (would email {Emailed}, NOT emailed {Skipped})
                + $"Failed drafts: {notifyResult.Found} (no family emailed), "
                + $"Errored: {counts.Errored}. eCheck settled / returns / watchdog: not run.</p>");
        }
        else
        {
            sb.Append($"<p style='font-size:9px;margin-top:0;'>Counts — Checked: {counts.Checked}, ARB imported: {counts.ArbImported}, eCheck settled: {counts.EcheckSettled}, eCheck returns: {counts.EcheckReturnsProcessed}, Orphans: {counts.OrphansFound}, Watchdog: {watchdogRows.Count}, Untracked eCheck: {untrackedRows.Count}, Failed drafts: {notifyResult.Found} (no family emailed), Errored: {counts.Errored}</p>");
        }

        // ── Failed ARB drafts ─────────────────────────────────────────
        // The section this digest was missing for six years. A declined card leaves the subscription
        // ACTIVE at ADN (it retries), so these registrations never appeared in the subscription-status
        // warnings below — 67 of 81 failing registrations were invisible by construction. Paired counts:
        // "found" without "emailed" is the number that still needs a human.
        var failedRows = arbRows
            .Where(r => !string.Equals(r.TransactionStatus, "settledSuccessfully", StringComparison.OrdinalIgnoreCase))
            .ToList();
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>Failed ARB Drafts</h4>");
        if (failedRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(none — every recurring draft settled ✓)</p>");
        }
        else
        {
            // ══ FAMILY EMAIL DISABLED 2026-08-27 (Todd) ═════════════════════════════════════════
            // TO RE-ENABLE: delete the two Appends below and uncomment the original pair under them.
            // (Tense follows the run: the original read "26 famil(ies) emailed automatically" on a
            // dry run, which emailed nobody — a report claiming an action it did not take, in the
            // one section a human acts on. Keep the _dryRun ternary if you restore it.)
            sb.Append($"<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; {failedRows.Count} recurring draft(s) did not settle. "
                + "No family was emailed — this notification is currently turned OFF.</p>");
            sb.Append("<p style='font-size:9px;margin-top:0;'>Automatic emails to these families are disabled, "
                + "not removed, and can be switched back on. Until then each family below needs a person: "
                + "contact them, or send from the job's ARB Health screen.</p>");
            // + $"{notifyResult.Emailed} famil(ies) {(_dryRun ? "WOULD BE emailed" : "emailed")} automatically, "
            // + $"{notifyResult.Skipped} NOT emailed.</p>");
            // ════════════════════════════════════════════════════════════════════════════════════
            var teamFailures = failedRows.Count(r => r.RegistrationId == null);
            if (teamFailures > 0)
            {
                sb.Append($"<p style='font-size:9px;'>({teamFailures} of these are team ARB-Trial drafts, which have no family behind them and are not counted in the email totals.)</p>");
            }
            sb.Append("<table style='border-style:solid;border-collapse:separate;border-spacing:10px;font-size:9px;'>");
            sb.Append("<tr><th>#</th><th>Job</th><th>Status</th><th>TransId</th><th>SubId</th><th>SubStatus</th><th>OwedNow</th><th>PaymentXofY</th><th>Registrant</th><th>Assignment</th></tr>");
            for (int i = 0; i < failedRows.Count; i++)
            {
                var r = failedRows[i];
                sb.Append("<tr>")
                  .Append($"<td>{i + 1}</td>")
                  .Append($"<td>{r.JobName}</td>")
                  .Append($"<td><b>{r.TransactionStatus}</b></td>")
                  .Append($"<td>{r.TransId}</td>")
                  .Append($"<td>{r.SubscriptionId}</td>")
                  .Append($"<td>{r.SubscriptionStatus}</td>")
                  .Append($"<td>{r.OwedNow:C}</td>")
                  .Append($"<td>{r.PaymentXofY}</td>")
                  .Append($"<td>{r.Registrant}</td>")
                  .Append($"<td>{r.RegistrantAssignment}</td>")
                  .Append("</tr>\n");
            }
            sb.Append("</table>");
        }

        // Families the notifier deliberately did not write to. These are the ones needing a person:
        // no reachable address, or no family username to put in the login instructions.
        if (notifyResult.Skips.Count > 0)
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;margin-top:8px;'>&#9888; NOT emailed — contact these by hand:</p>");
            sb.Append("<ul style='font-size:9px;margin-top:0;'>");
            foreach (var s in notifyResult.Skips)
            {
                sb.Append($"<li>{s.JobName} · {s.Registrant} — {s.Reason}</li>");
            }
            sb.Append("</ul>");
        }

        // ── ARB subscription warnings ─────────────────────────────────
        // A suspended/canceled/terminated subscription stops drafting on its own — pure absence
        // from our side. The status is synced from ADN during import; this is its alarm.
        var subWarnings = arbRows
            .Where(r => !string.IsNullOrEmpty(r.SubscriptionStatus)
                && !string.Equals(r.SubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.SubscriptionStatus, "expired", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (subWarnings.Count > 0)
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; Subscription(s) not healthy — installments will stop arriving on their own:</p>");
            sb.Append("<ul style='font-size:9px;margin-top:0;'>");
            foreach (var w in subWarnings)
            {
                sb.Append($"<li><b>{w.SubscriptionStatus}</b> — {w.JobName} · sub {w.SubscriptionId} · {w.Registrant} ({w.RegistrantAssignment}) · owed now {w.OwedNow:C}</li>");
            }
            sb.Append("</ul>");
        }

        // ── ARB Activity table ────────────────────────────────────────
        // Every ARB transaction in the window, dry run included — the settled ones are the bulk of a
        // normal morning and the reason this table means anything.
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
                  .Append("</tr>\n");
            }
            sb.Append("</table>");
        }

        // ── eCheck Settled table ──────────────────────────────────────
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>eCheck Settled (Pending → Settled)</h4>");
        if (_dryRun)
        {
            sb.Append(DryRunNotRun);
        }
        else if (settledRows.Count == 0)
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
                  .Append("</tr>\n");
            }
            sb.Append("</table>");
        }

        // ── eCheck Returns table ──────────────────────────────────────
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>eCheck Returns</h4>");
        if (_dryRun)
        {
            sb.Append(DryRunNotRun);
        }
        else if (ecRows.Count == 0)
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
                  .Append("</tr>\n");
            }
            sb.Append("</table>");
        }

        // ── Watchdog table (stale Pending drafts) ─────────────────────
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>eCheck Watchdog (Pending beyond threshold)</h4>");
        if (_dryRun)
        {
            sb.Append(DryRunNotRun);
        }
        else if (watchdogRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(no stale pending drafts — every draft settled or resolved on time ✓)</p>");
        }
        else
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; Draft(s) still Pending past the threshold — status was queried at ADN directly; outcome per row.</p>");
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
                  .Append("</tr>\n");
            }
            sb.Append("</table>");
        }

        // ── Untracked eCheck payments (integrity net) ─────────────────
        sb.Append("<h4 style='margin-bottom:2px;margin-top:14px;'>Untracked eCheck Payments (no Settlement return-watcher)</h4>");
        if (untrackedRows.Count == 0)
        {
            sb.Append("<p style='font-size:9px;'>(none — every booked eCheck is registered for return-watching ✓)</p>");
        }
        else
        {
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; Booked eCheck money the sweep cannot watch — a bounce on these would be silently dropped. Investigate each; likely a partial write.</p>");
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
                  .Append("</tr>\n");
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
            sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; Money settled at Authorize.Net with no local accounting row. REPORT ONLY — nothing was booked. Review each and enter the payment by hand.</p>");
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
                  .Append("</tr>\n");
            }
            sb.Append("</table>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// TAKES NO CancellationToken, deliberately, and must not be given one.
    ///
    /// The digest is the report ABOUT the run, which is why it is built and sent outside the try — a
    /// failed sweep must still mail, and must say so. A cancellation token defeats exactly that: the
    /// caller's token is the request's, a dry run forces production ADN and makes two synchronous
    /// round trips per transaction so it is slow, and any abort — the browser, IIS, an admin
    /// navigating away — cancels it. SendRawEmailAsync then throws instantly, the wrapper logs
    /// "digest send failed", and the one artifact that would have explained the run is the thing the
    /// failure destroyed. Silence is not a report.
    ///
    /// The parameter is removed rather than ignored, on the same reasoning as <see cref="_dryRun"/>
    /// not being a parameter: there is nothing to pass, so there is nothing to pass wrongly. Matches
    /// PaymentService's receipt send, which takes CancellationToken.None for the same reason.
    ///
    /// Safe on shutdown too — the SES call is a single short round trip, and a sweep that ran during
    /// a recycle is precisely the one whose digest you want.
    /// </summary>
    /// <summary>
    /// The plain-text half of the digest's multipart/alternative body.
    ///
    /// The digest was text/html with NO text alternative, which is both a spam signal in its own right
    /// and leaves nothing to show when a gateway strips HTML. support@teamsportsinfo.com routes through
    /// a mail security gateway (fwd.oxsus-vadesecure.net) before it reaches a mailbox: on 2026-08-25 SES
    /// ACCEPTED three digests with message ids and none was delivered, while a 120-byte plain-text test
    /// to the same address one minute apart landed. Every real message this system sends is HTML, so
    /// this is not only about the digest — but the digest is where it was caught.
    ///
    /// Deliberately the counts only, not a text rendering of the tables. The verdict is what has to
    /// survive; the detail is what the HTML part is for.
    /// </summary>
    private string BuildDigestText(
        ArbNotifyResultDto notifyResult, Counts counts, int watchdogCount, int untrackedCount, string? errorMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ADN Sweep ({(_dryRun ? "DRY RUN" : "LIVE")}) - {DateTime.Now:dddd, dd MMMM yyyy HH:mm}");
        sb.AppendLine();

        if (_dryRun)
        {
            sb.AppendLine("DRY RUN - nothing was written, settled, or sent.");
            sb.AppendLine();
        }

        // Lead with the failure, same rule as the HTML part.
        if (errorMessage != null)
        {
            sb.AppendLine($"SWEEP FAILED: {errorMessage}");
            sb.AppendLine();
        }
        else if (counts.Errored > 0)
        {
            sb.AppendLine($"WARNING: {counts.Errored} transaction(s) errored - the pass completed, but those are not booked.");
            sb.AppendLine();
        }

        sb.AppendLine($"Checked:              {counts.Checked}");
        sb.AppendLine($"ARB {(_dryRun ? "resolved" : "imported"),-17} {counts.ArbImported}");
        if (!_dryRun)
        {
            sb.AppendLine($"eCheck settled:       {counts.EcheckSettled}");
            sb.AppendLine($"eCheck returns:       {counts.EcheckReturnsProcessed}");
            sb.AppendLine($"Watchdog:             {watchdogCount}");
        }
        sb.AppendLine($"Orphans:              {counts.OrphansFound}");
        sb.AppendLine($"Untracked eCheck:     {untrackedCount}");
        // FAMILY EMAIL DISABLED 2026-08-27 — TO RE-ENABLE, restore the commented line below.
        sb.AppendLine($"Failed drafts:        {notifyResult.Found} (family email OFF - nobody contacted)");
        // sb.AppendLine($"Failed drafts:        {notifyResult.Found} "
        //     + $"({(_dryRun ? "would email" : "emailed")} {notifyResult.Emailed}, NOT emailed {notifyResult.Skipped})");
        sb.AppendLine($"Errored:              {counts.Errored}");
        if (_dryRun)
        {
            sb.AppendLine();
            sb.AppendLine("eCheck settled / returns / watchdog: not run on a dry run.");
        }

        // The one list that always needs a human, so it must survive an HTML strip.
        if (notifyResult.Skips.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("NOT emailed - contact these by hand:");
            foreach (var s in notifyResult.Skips)
            {
                sb.AppendLine($"  - {s.JobName} / {s.Registrant} - {s.Reason}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Full detail is in the HTML version of this message.");
        return sb.ToString();
    }

    private async Task SendDigestAsync(string html, string text, string? errorMessage, int errored)
    {
        // Instrumented deliberately. The digest send used to report nothing at all on success and only a
        // warning on failure, so "no email arrived" was indistinguishable from "no email was attempted",
        // from "SES refused it", and from "it sent and the mail path ate it". Three of those need
        // different fixes. Seq now separates them.
        // support@teamsportsinfo.com is NOT a mailbox. It is
        //     SES -> Vade Secure -> Netsol -> Sieve forward -> toddtsic@gmail.com,
        // and Vade quarantines silently: SES reports 200 OK with a MessageId and zero bounces while
        // the mail never lands. This burned a day on 2026-05-10 and again on 2026-08-25.
        //
        // A DRY RUN therefore goes to BOTH: direct to the person running it, AND to support@.
        //
        // Both, not either. Direct delivery means the tester always gets their report regardless of
        // Vade's mood -- nobody testing the sweep should be testing a spam gateway's scoring at the
        // same time. But sending ONLY direct would silently remove the ability to prove the subject
        // fix worked, and support@ is the address PRODUCTION uses: if the two copies stop agreeing
        // -- gmail arrives, support@ does not -- that divergence IS the Vade signal, on a dry run,
        // months before it could matter at 4am.
        //
        // 2026-08-27: PRODUCTION NO LONGER MAILS support@. It mails the three people who read it.
        // On this date support@ stopped delivering anything sent from our SES: the 04:00 digest, a
        // 09:08 manual sweep digest and a live E-Mail Troubleshooter test all returned an SES 200 with
        // a MessageId and never landed, while the SAME box sending to toddtsic@gmail.com in the same
        // minute arrived, and Ann's ordinary Gmail message to support@ was forwarded through in 18
        // seconds. So the mailbox, the Sieve forward and Vade are all up; what does not survive is our
        // SES mail to that address specifically.
        // Revert to [TsicConstants.SupportEmail] once Netsol/Vade explain and fix it.
        string[] recipients = _dryRun
            ? [DryRunDigestRecipient, TsicConstants.SupportEmail]
            : ProductionDigestRecipients;

        _logger.LogInformation(
            "ADN sweep digest: sending to {Recipients} (dryRun={DryRun}, bytes={Bytes})",
            string.Join(",", recipients), _dryRun, html.Length);

        var accepted = await _email.SendAsync(new EmailMessageDto
        {
            FromName = "",
            ToAddresses = [.. recipients],
            // The verdict rides the subject -- this is read on a phone, and a failed sweep must be
            // distinguishable from a quiet one without opening the mail. The marker rides the subject
            // too: two digests can land the same day and the 4am one is the only one that means
            // anything about the ledger.
            //
            // NO BRACKET PREFIX AND NO EM-DASH, deliberately. Both are textbook spam tells and both
            // are named in reference_email_routing.md as the top-ranked reason Vade silently dropped
            // this exact digest before. Legacy's subject that always got through was a plain
            // "ArbSweep {date}". Keep this shape plain: no [brackets], no U+2014, ASCII hyphen only.
            Subject = (_dryRun ? "DRY RUN " : "")
                + $"AdnSweep AI {DateTime.Now:dddd, dd MMMM yyyy HH:mm}"
                + (errorMessage != null ? " - SWEEP FAILED" : errored > 0 ? $" - {errored} ERRORED" : ""),
            HtmlBody = html,
            // multipart/alternative. HTML-only is itself a filter signal, and leaves nothing to show
            // when a gateway strips HTML. See BuildDigestText.
            TextBody = text
        }, sendInDevelopment: true, cancellationToken: CancellationToken.None);

        if (accepted)
        {
            _logger.LogInformation("ADN sweep digest: accepted by the mail service (dryRun={DryRun})", _dryRun);
        }
        else
        {
            // SendAsync swallows its own exception and returns false. Without this the only trace was a
            // warning inside EmailService that says nothing about which message it belonged to.
            _logger.LogError(
                "ADN sweep digest: NOT accepted by the mail service — no digest was delivered (dryRun={DryRun})",
                _dryRun);
        }
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

        /// <summary>
        /// Dry run only: settled ARB drafts seen in the batches but deliberately NOT fetched in full.
        /// Reported so the run never looks like it examined them.
        /// </summary>
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
        /// <summary>Null for team ARB-Trial drafts, which have no registration behind them.</summary>
        public required Guid? RegistrationId { get; init; }
        public required string InvoiceNumber { get; init; }
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
    // what was done about it ("never originated — reversed", "settled — window missed", …).
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
