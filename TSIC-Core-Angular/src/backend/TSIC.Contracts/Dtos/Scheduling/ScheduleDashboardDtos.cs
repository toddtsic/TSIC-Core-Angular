namespace TSIC.Contracts.Dtos.Scheduling;

/// <summary>
/// Post-build schedule dashboard: whole-schedule aggregates plus coverage denominators
/// (scheduled vs schedulable / active) and the per-day game distribution. Read-only —
/// same contract as the checklist: computing status never writes.
/// </summary>
public record ScheduleDashboardDto
{
    public required ChecklistScheduleStatsDto Stats { get; init; }

    /// <summary>Denominator for Stats.DivisionsScheduled — divisions the build engine would place.</summary>
    public required int SchedulableDivisionCount { get; init; }

    /// <summary>Distinct teams holding at least one scheduled game.</summary>
    public required int TeamsScheduled { get; init; }

    /// <summary>
    /// Active teams across schedulable agegroups — the denominator for TeamsScheduled.
    /// TeamsScheduled falling short of this is the "build silently dropped teams" signal.
    /// </summary>
    public required int ActiveTeamCount { get; init; }

    /// <summary>Games per calendar day, ordered by date.</summary>
    public required List<GamesPerDayDto> GamesPerDay { get; init; }
}

/// <summary>One calendar day's game count.</summary>
public record GamesPerDayDto
{
    public required DateTime Date { get; init; }
    public required int GameCount { get; init; }
}
