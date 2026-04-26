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
}
