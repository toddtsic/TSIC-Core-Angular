namespace TSIC.Contracts.Dtos.Scheduling;

// ── Response DTOs ──

public record FieldDto
{
    public required Guid FieldId { get; init; }
    /// <summary>An address row, not a playable venue (name starts with "*"). Assigning one is
    /// how a director gives the event an insurance location.</summary>
    public bool IsPseudoField { get; init; }

    /// <summary>This row carries the name derived from this job's own jobPath -- the row the
    /// event would ideally use. Any attached pseudo-field will serve; this one wins ties.</summary>
    public bool IsEventLocation { get; init; }
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

public record LeagueSeasonFieldDto
{
    public required Guid FlsId { get; init; }
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public bool? BActive { get; init; }

    /// <summary>0 = Normal, 1 = Preferred, 2 = Avoid</summary>
    public int FieldPreference { get; init; }

    /// <summary>Number of scheduled games on this field. 0 = safe to remove.</summary>
    public int ScheduledGameCount { get; init; }

    /// <summary>
    /// An address row, not a playing field: the name starts with "*". Kept out of every
    /// scheduling surface -- pickers, timeslot creation, the readiness count -- because you
    /// cannot play a game on an address. Shown in Manage Fields, because it was previously
    /// invisible app-wide and could be neither reviewed nor corrected.
    /// </summary>
    public bool IsPseudoField { get; init; }

    /// <summary>
    /// THE row whose address goes on the Vertical Insure team RegSaver payload. Exactly one
    /// attached row carries this, chosen by EventLocationFieldNaming.SelectEventLocation --
    /// the same call the payload builder makes, so the badge cannot promise what the quote
    /// will not deliver.
    /// </summary>
    public bool IsEventLocation { get; init; }
}

public record FieldManagementResponse
{
    /// <summary>Fields NOT assigned to current league-season (available for assignment).</summary>
    public required List<FieldDto> AvailableFields { get; init; }
    /// <summary>Fields assigned to current league-season.</summary>
    public required List<LeagueSeasonFieldDto> AssignedFields { get; init; }

    /// <summary>
    /// The name this job's event-location field must carry ("*" + jobPath less its year
    /// suffix). Null only when the jobPath is too short to derive one.
    /// </summary>
    public string? EventLocationFieldName { get; init; }

    /// <summary>
    /// False when no row matching EventLocationFieldName is attached. A prefix scan can only
    /// describe rows that exist; this is what lets the screen report the row MISSING, which
    /// is the state that actually breaks Team RegSaver.
    /// </summary>
    public bool HasEventLocation { get; init; }
}

// ── Request DTOs ──

public record CreateFieldRequest
{
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

public record UpdateFieldRequest
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

public record AssignFieldsRequest
{
    /// <summary>Field IDs to assign to the current league-season.</summary>
    public required List<Guid> FieldIds { get; init; }
}

public record RemoveFieldsRequest
{
    /// <summary>Field IDs to remove from the current league-season.</summary>
    public required List<Guid> FieldIds { get; init; }
}

public record UpdateFieldPreferenceRequest
{
    /// <summary>0 = Normal, 1 = Preferred, 2 = Avoid</summary>
    public required int FieldPreference { get; init; }
}

/// <summary>
/// One attached pseudo-field row that could serve as the event's insurance location.
/// The caller picks among these with EventLocationFieldNaming.SelectEventLocation.
/// </summary>
public record EventLocationCandidateDto
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }

    /// <summary>Vertical Insure rejects the quote unless all four parts are present.</summary>
    public bool HasCompleteAddress =>
        !string.IsNullOrWhiteSpace(Address)
        && !string.IsNullOrWhiteSpace(City)
        && !string.IsNullOrWhiteSpace(State)
        && !string.IsNullOrWhiteSpace(Zip);
}
