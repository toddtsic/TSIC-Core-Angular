namespace TSIC.Contracts.Dtos.TeamLink;

/// <summary>
/// One row from [mobile].[Team_Docs] for admin display.
/// Legacy shape preserved: an "all teams" link is stored as one row per team
/// (fan-out), so the grid will show N rows for an N-team link. Edit/delete on
/// any row in a Label+DocUrl group still operates on the whole group server-side.
/// </summary>
public record AdminTeamLinkDto
{
    public required Guid DocId { get; init; }
    public Guid? TeamId { get; init; }
    public required string TeamDisplay { get; init; }
    public required string Label { get; init; }
    public required string DocUrl { get; init; }
    public required DateTime CreateDate { get; init; }
}

/// <summary>
/// Add a team link. TeamId == null means "all active teams" — the service
/// fans the row out to every active team (excluding agegroups
/// "Dropped Teams" and "Registration").
/// </summary>
public record CreateTeamLinkRequest
{
    public Guid? TeamId { get; init; }
    public required string Label { get; init; }
    public required string DocUrl { get; init; }
}

/// <summary>
/// Update a team link. TeamId == null re-fans the row group to every
/// active team. Editing the Label/DocUrl on a non-null TeamId rewrites
/// the single row in place.
/// </summary>
public record UpdateTeamLinkRequest
{
    public Guid? TeamId { get; init; }
    public required string Label { get; init; }
    public required string DocUrl { get; init; }
}

/// <summary>
/// Team dropdown option for the admin team-link form. Active teams only,
/// excluding agegroups "Dropped Teams" and "Registration".
/// </summary>
public record TeamLinkTeamOptionDto
{
    public required Guid TeamId { get; init; }
    public required string Display { get; init; }
}
