namespace TSIC.Contracts.Dtos.CustomerJobRevenue;

/// <summary>
/// Team Retention: every active team in every season of the selected tournament events, each
/// marked Returning or New against the event's previous season.
/// </summary>
/// <remarks>
/// Deliberately the raw list and nothing more (Todd, 2026-09-29): the client asked for club and
/// team per job per season, and the one derived fact that answers their question — did this team
/// come back — rides on each row. Counts and percentages are the chart's job, computed from these
/// rows; the Excel export is these rows.
/// </remarks>
public record TeamRetentionResponseDto
{
    /// <summary>Ordered by event, then season, then club, then team.</summary>
    public required List<TeamRetentionRowDto> Rows { get; init; }

    /// <summary>
    /// Selected jobs with no parseable <c>Jobs.year</c> — they cannot be placed in a season, so
    /// they are named rather than silently dropped.
    /// </summary>
    public required List<string> UngroupedJobNames { get; init; }
}

/// <summary>One team in one season of one event.</summary>
public record TeamRetentionRowDto
{
    /// <summary>The event — the job name with its season removed, e.g. <c>"Top Threat Tournaments:Carolina Clash"</c>.</summary>
    public required string EventLabel { get; init; }

    /// <summary>Season, from <c>Jobs.year</c>.</summary>
    public required int Year { get; init; }

    public required string JobName { get; init; }

    /// <summary>The club rep's registration <c>club_name</c>, trimmed. Empty when the team has no club rep.</summary>
    public required string ClubName { get; init; }

    public required string TeamName { get; init; }

    /// <summary>
    /// <c>"Returning"</c> when the same club and team name (trimmed, case-insensitive) played the
    /// event's previous season; <c>"New"</c> otherwise; <c>"First season"</c> in the oldest season
    /// on record, which has nothing to compare against.
    /// </summary>
    public required string Status { get; init; }
}
