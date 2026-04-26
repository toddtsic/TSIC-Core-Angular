namespace TSIC.API.Configuration;

/// <summary>
/// Configuration for the daily eCheck settlement sweep BackgroundService.
/// Override via appsettings.json section "EcheckSweep". Defaults below
/// are sensible for production.
/// </summary>
public class EcheckSweepOptions
{
    /// <summary>Master kill switch. Set false to disable the sweep entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hour of day (local server time, 0-23) at which the sweep runs.
    /// Default 4 AM — after ADN's nightly batch settlement window.</summary>
    public int SweepHourLocal { get; set; } = 4;

    /// <summary>Days to wait before first checking a newly-submitted Settlement.
    /// eCheck/ACH cannot settle faster than this, so earlier checks waste API calls.</summary>
    public int InitialGraceDays { get; set; } = 2;

    /// <summary>Days to wait before re-checking a Settlement that came back still
    /// pending. NSF returns can lag the first settle attempt by several days.</summary>
    public int RetryDays { get; set; } = 1;
}
