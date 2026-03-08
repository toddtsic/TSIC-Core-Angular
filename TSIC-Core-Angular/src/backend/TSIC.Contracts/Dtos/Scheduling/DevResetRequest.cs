namespace TSIC.Contracts.Dtos.Scheduling;

public record DevResetRequest
{
    public bool Games { get; init; } = true;
    public bool StrategyProfiles { get; init; } = true;
    public bool Pairings { get; init; } = true;
    /// <summary>Legacy combined flag — clears both dates AND field timeslots when true.</summary>
    public bool TimeslotConfig { get; init; }
    /// <summary>Clear date/round assignments only (TimeslotsLeagueSeasonDates).</summary>
    public bool Dates { get; init; }
    /// <summary>Clear field-timeslot config only (TimeslotsLeagueSeasonFields).</summary>
    public bool FieldTimeslots { get; init; }
    public bool FieldAssignments { get; init; }

    /// <summary>
    /// When set, run preconfiguration (colors, dates, fields, pairings) from this
    /// source job after the reset completes. Null = no preconfig.
    /// </summary>
    public Guid? SourceJobId { get; init; }
}
