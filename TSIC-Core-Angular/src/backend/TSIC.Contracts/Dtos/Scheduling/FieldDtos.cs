namespace TSIC.Contracts.Dtos.Scheduling;

// ── Response DTOs ──

public record FieldDto
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
}

public record FieldManagementResponse
{
    /// <summary>Fields NOT assigned to current league-season (available for assignment).</summary>
    public required List<FieldDto> AvailableFields { get; init; }
    /// <summary>Fields assigned to current league-season.</summary>
    public required List<LeagueSeasonFieldDto> AssignedFields { get; init; }
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
