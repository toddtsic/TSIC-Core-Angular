namespace TSIC.Contracts.Services;

/// <summary>
/// Daily reconciliation pass that walks pending eCheck settlements and asks
/// Authorize.Net for current status. Settled rows are marked Cleared; bounced
/// rows trigger an automated reversal (negative RA row + fee credit reversal +
/// director alert email). Registration stays active on bounce — director handles.
/// </summary>
public interface IEcheckSweepService
{
    /// <summary>
    /// Run one sweep pass. Logs to echeck.SweepLog regardless of outcome.
    /// Safe to invoke concurrently with itself (idempotent per record), though
    /// scheduling typically prevents that.
    /// </summary>
    /// <param name="triggeredBy">"Scheduled" for the BackgroundService, "Manual" for ad-hoc.</param>
    Task<EcheckSweepResult> RunAsync(string triggeredBy, CancellationToken ct = default);
}

public sealed record EcheckSweepResult
{
    public required int Checked { get; init; }
    public required int Settled { get; init; }
    public required int Returned { get; init; }
    public required int Errored { get; init; }
}
