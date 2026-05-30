namespace TSIC.Contracts.Dtos.CampGroups;

/// <summary>
/// Team summary for the camp-groups admin left pane: one row per active team
/// in the job with its current rostered Player count.
/// </summary>
public record TeamRosterCountDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required int PlayerCount { get; init; }
}

/// <summary>
/// Registrant row for the camp-groups admin right pane: one row per active
/// Player on the selected team, with current DayGroup/NightGroup values.
/// </summary>
public record CampPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? SchoolName { get; init; }
    public string? GradYear { get; init; }
    public string? Position { get; init; }
    public string? ClubName { get; init; }
    public string? DayGroup { get; init; }
    public string? NightGroup { get; init; }
}

/// <summary>
/// PATCH body for single-registrant Day/Night group update. Both fields are
/// optional — only provided fields are written. Empty string is normalized to null.
/// </summary>
public record UpdateCampGroupsRequest
{
    public string? DayGroup { get; init; }
    public string? NightGroup { get; init; }
    public bool UpdateDayGroup { get; init; }
    public bool UpdateNightGroup { get; init; }
}

/// <summary>
/// PATCH body for bulk Day/Night group update across many registrants.
/// </summary>
public record BulkUpdateCampGroupsRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public string? DayGroup { get; init; }
    public string? NightGroup { get; init; }
    public bool UpdateDayGroup { get; init; }
    public bool UpdateNightGroup { get; init; }
}

/// <summary>
/// Response for bulk update: how many rows were touched.
/// </summary>
public record BulkUpdateCampGroupsResponse
{
    public required int UpdatedCount { get; init; }
}

/// <summary>
/// Day/Night group dropdown options for the camp-groups admin screen, sourced from
/// Jobs.JsonOptions. Lets non-SuperUser admins read the lists without granting them
/// access to the broader DDL Options editor.
/// </summary>
public record CampGroupOptionsDto
{
    public required List<string> DayGroups { get; init; }
    public required List<string> NightGroups { get; init; }
}
