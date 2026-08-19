namespace TSIC.Contracts.Dtos;

/// <summary>
/// Page payload for the public ASL roster board (legacy route {jobPath}/ASLRosters/Index).
/// Carries the region dropdown and the full team dropdown; rosters load per-region on demand.
/// </summary>
public record AslRostersIndexDto
{
    /// <summary>Distinct region tokens present in this job, ordered by name.</summary>
    public required IReadOnlyList<string> Regions { get; init; }

    /// <summary>Every eligible team, ordered agegroup → name → level of play (legacy order).</summary>
    public required IReadOnlyList<AslTeamMenuItemDto> Teams { get; init; }
}

/// <summary>One entry in the team dropdown.</summary>
public record AslTeamMenuItemDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
}

/// <summary>A team card: header bar (name + coaches) plus its player grid.</summary>
public record AslRegionTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }

    /// <summary>Region token this team resolved to, e.g. "ASL:Texas". Empty when unresolved.</summary>
    public required string TeamRegion { get; init; }

    /// <summary>Trailing 4 characters of the team name — the graduation year.</summary>
    public required string GradYear { get; init; }

    /// <summary>Free-text coaches line, sourced from Teams.team_comments.</summary>
    public required string TeamCoaches { get; init; }

    public required IReadOnlyList<AslTeamPlayerDto> ListTeamPlayers { get; init; }
}

/// <summary>Public-safe player row for an ASL team card — no contact or parent PII.</summary>
public record AslTeamPlayerDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Position { get; init; }
    public string? School { get; init; }
    public string? ClubName { get; init; }

    /// <summary>Uniform number, or "#" when the player has none (legacy placeholder).</summary>
    public required string UniformNumber { get; init; }
}

/// <summary>Repository row: an eligible ASL team before its roster is attached.</summary>
public record AslTeamRowDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string TeamCoaches { get; init; }
}

/// <summary>Repository row: one player carrying the team it belongs to, so a batch fetch can group.</summary>
public record AslTeamPlayerRowDto
{
    public required Guid TeamId { get; init; }
    public required AslTeamPlayerDto Player { get; init; }
}
