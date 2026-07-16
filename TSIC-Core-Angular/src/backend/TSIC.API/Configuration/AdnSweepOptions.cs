namespace TSIC.API.Configuration;

/// <summary>
/// Daily sweep BackgroundService configuration. Bound from appsettings "AdnSweep".
/// </summary>
public class AdnSweepOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Local hour to run the daily sweep (0-23). Default 4 AM.</summary>
    public int SweepHourLocal { get; set; } = 4;

    /// <summary>How many days back to query ADN settled batches. Legacy default = 2.</summary>
    public int DaysPriorWindow { get; set; } = 2;

    /// <summary>
    /// Grace minutes after the target hour before deferring to tomorrow.
    /// If service starts at 4:30 AM and grace is 60, it runs immediately; if at 5:30, it waits.
    /// </summary>
    public int StartupGraceMinutes { get; set; } = 60;

    /// <summary>
    /// On the 1st of the month, run the month-end close for the month just ended and email the .zip
    /// (both QuickBooks .iif + backing .xlsx) to support. Off switch for the unattended close only —
    /// the daily sweep keeps running either way.
    /// </summary>
    public bool EmailMonthEndClose { get; set; } = true;

    /// <summary>
    /// Watchdog threshold: an eCheck Settlement still "Pending" this many days after submission is
    /// an anomaly (healthy drafts settle in 1–2 business days) — the sweep asks ADN directly what
    /// became of it. Generous default so weekends/holidays never false-positive.
    /// </summary>
    public int WatchdogStalePendingDays { get; set; } = 5;
}
