namespace TSIC.Contracts.Services;

/// <summary>
/// Daily reconciliation pass over the TSIC customer's settled ADN batches.
///
/// One sweep handles three paths:
///   • ARB recurring billing imports (subscription txs → RegistrationAccounting + status sync)
///   • eCheck Pending → Settled transitions (settledSuccessfully txs that match our pending Settlement rows)
///   • eCheck return processing (returnedItem txs → reverse payment + email director)
///
/// Mirrors the legacy AdnArbSweepService.DoWorkAsync flow with eCheck handling added.
/// Logs to echeck.SweepLog regardless of outcome.
/// </summary>
public interface IAdnSweepService
{
    /// <summary>
    /// Run one sweep pass.
    /// </summary>
    /// <param name="triggeredBy">"Scheduled" for the BackgroundService, "Manual" for the controller.</param>
    /// <param name="daysPrior">How many days back to query settled batches. Defaults to options value if 0.</param>
    Task<AdnSweepResult> RunAsync(string triggeredBy, int daysPrior = 0, CancellationToken ct = default);
}

public sealed record AdnSweepResult
{
    public required int Checked { get; init; }
    public required int ArbImported { get; init; }
    public required int EcheckSettled { get; init; }
    public required int EcheckReturnsProcessed { get; init; }
    public required int Errored { get; init; }
}
