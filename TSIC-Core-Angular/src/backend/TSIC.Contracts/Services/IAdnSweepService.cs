namespace TSIC.Contracts.Services;

/// <summary>
/// Daily reconciliation pass over the TSIC customer's settled ADN batches.
///
/// One sweep handles five paths:
///   • ARB recurring billing imports (subscription txs → RegistrationAccounting + status sync)
///   • eCheck Pending → Settled transitions (status-only — money books at submit; this stamp
///     records that the draft entered the banking network)
///   • eCheck return processing (returnedItem txs → reverse the booked payment)
///   • stale-Pending watchdog (drafts that went silent: query ADN directly, settle/reverse/flag)
///   • integrity net (eCheck RA rows with no Settlement return-watcher — report-only)
///
/// All outcomes land in the support digest; there are no per-incident director emails (the
/// director NSF notification + inactivate action are one future feature, designed together).
/// Mirrors the legacy AdnArbSweepService.DoWorkAsync flow with eCheck handling added.
/// Logs to echeck.SweepLog regardless of outcome. Concurrent runs are refused: a second
/// caller while a sweep is in flight gets Succeeded=false with an "already running" message.
/// </summary>
public interface IAdnSweepService
{
    /// <summary>
    /// Run one sweep pass. Always mails the digest — including on failure, where the digest leads with
    /// the error — unless <paramref name="sendDigest"/> is false, in which case the caller owns the send
    /// and gets the rendered HTML back on <see cref="AdnSweepResult.DigestHtml"/> (the 1st-of-month path,
    /// which folds the digest into the month-end close email).
    /// </summary>
    /// <param name="triggeredBy">"Scheduled" for the BackgroundService, "Manual" for the controller.</param>
    /// <param name="daysPrior">How many days back to query settled batches. Defaults to options value if 0.</param>
    /// <param name="sendDigest">False to suppress the send and hand the digest HTML back to the caller.</param>
    Task<AdnSweepResult> RunAsync(
        string triggeredBy,
        int daysPrior = 0,
        bool sendDigest = true,
        CancellationToken ct = default);

    /// <summary>
    /// Month-end backstop: guarantee the Failed eCheck record exists for one returnedItem
    /// transaction the close's ADN pull discovered. Runs the SAME return path as the daily
    /// sweep — refTransId resolution, terminal-Returned guard, reversal-row guard — so a
    /// return the sweep already handled is a no-op, and whichever system fires first, exactly
    /// one reversal ever exists. Null = nothing done (already processed / not ours / could not
    /// resolve at the ambient ADN account); non-null = a missed return was just reversed.
    /// Note the environment asymmetry: the close pulls PRODUCTION by design, but this method
    /// resolves ADN from the ambient host like every money write — off-Production it queries
    /// sandbox, finds nothing, and is inert.
    /// </summary>
    Task<EcheckReturnBackstopOutcome?> EnsureReturnProcessedAsync(
        string returnTransId,
        CancellationToken ct = default);
}

/// <summary>
/// What the month-end backstop did for one returnedItem the daily sweep had missed:
/// the reversal that was just written (negative Failed-eCheck RA + fee restore + recompute).
/// </summary>
public sealed record EcheckReturnBackstopOutcome
{
    public required string ReturnTxId { get; init; }
    public required string OriginalTxId { get; init; }
    public required string JobName { get; init; }
    public required decimal AmountReversed { get; init; }
    public required string Reason { get; init; }
}

public sealed record AdnSweepResult
{
    public required int Checked { get; init; }
    public required int ArbImported { get; init; }
    public required int EcheckSettled { get; init; }
    public required int EcheckReturnsProcessed { get; init; }

    /// <summary>
    /// Orphan ADN charges detected this pass: settled at Authorize.Net but with no matching
    /// RegistrationAccounting row. Report-only — flagged in the digest for manual review,
    /// never auto-booked. Expected to be 0 on virtually every run.
    /// </summary>
    public required int OrphansFound { get; init; }
    public required int Errored { get; init; }

    /// <summary>
    /// False when the pass could not complete: an exception was thrown, OR Authorize.Net answered the
    /// batch-list request with an error. The latter is the dangerous one — it used to collapse into an
    /// empty transaction list, making a broken sweep indistinguishable from a morning with nothing to
    /// settle. Nothing downstream may trust the accounting tables for the swept window when this is false.
    /// </summary>
    public required bool Succeeded { get; init; }

    /// <summary>What went wrong, when <see cref="Succeeded"/> is false. Null on a clean pass.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The rendered digest. Populated on every run; the caller only needs it when it suppressed the send
    /// via <c>sendDigest: false</c> and intends to mail it itself.
    /// </summary>
    public string? DigestHtml { get; init; }

    /// <summary>
    /// The sweep completed AND every transaction in it was processed. This is the gate the month-end
    /// close hangs on: the sweep is what books the closing month's final ARB/eCheck rows into the
    /// accounting tables, so if it did not fully succeed, the QuickBooks export built from those tables
    /// is short those payments — a wrong ledger, not merely a wrong count.
    /// </summary>
    public bool IsTrustworthy => Succeeded && Errored == 0;
}
