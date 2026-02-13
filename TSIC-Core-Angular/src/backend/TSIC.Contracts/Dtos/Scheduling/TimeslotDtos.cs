namespace TSIC.Contracts.Dtos.Scheduling;

// ── Response DTOs ──

public record TimeslotDateDto
{
    public required int Ai { get; init; }
    public required Guid AgegroupId { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
    public Guid? DivId { get; init; }
    public string? DivName { get; init; }
}

public record TimeslotFieldDto
{
    public required int Ai { get; init; }
    public required Guid AgegroupId { get; init; }
    public required Guid FieldId { get; init; }
    public required string FieldName { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
    public Guid? DivId { get; init; }
    public string? DivName { get; init; }
}

public record TimeslotConfigurationResponse
{
    public required List<TimeslotDateDto> Dates { get; init; }
    public required List<TimeslotFieldDto> Fields { get; init; }
}

public record CapacityPreviewDto
{
    public required string Dow { get; init; }
    public required int FieldCount { get; init; }
    public required int TotalGameSlots { get; init; }
    public required int GamesNeeded { get; init; }
    public required bool IsSufficient { get; init; }
}

// ── Request DTOs ──

public record AddTimeslotDateRequest
{
    public required Guid AgegroupId { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
    public Guid? DivId { get; init; }
}

public record EditTimeslotDateRequest
{
    public required int Ai { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
}

public record AddTimeslotFieldRequest
{
    public required Guid AgegroupId { get; init; }
    /// <summary>null = create for ALL assigned fields (cartesian product).</summary>
    public Guid? FieldId { get; init; }
    /// <summary>null = create for ALL active divisions in agegroup.</summary>
    public Guid? DivId { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
}

public record EditTimeslotFieldRequest
{
    public required int Ai { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
    public Guid? FieldId { get; init; }
    public Guid? DivId { get; init; }
}

public record CloneDatesRequest
{
    public required Guid SourceAgegroupId { get; init; }
    public required Guid TargetAgegroupId { get; init; }
}

public record CloneFieldsRequest
{
    public required Guid SourceAgegroupId { get; init; }
    public required Guid TargetAgegroupId { get; init; }
}

public record CloneByFieldRequest
{
    public required Guid AgegroupId { get; init; }
    public required Guid SourceFieldId { get; init; }
    public required Guid TargetFieldId { get; init; }
}

public record CloneByDivisionRequest
{
    public required Guid AgegroupId { get; init; }
    public required Guid SourceDivId { get; init; }
    public required Guid TargetDivId { get; init; }
}

public record CloneByDowRequest
{
    public required Guid AgegroupId { get; init; }
    public required string SourceDow { get; init; }
    public required string TargetDow { get; init; }
    /// <summary>null = keep source start time.</summary>
    public string? NewStartTime { get; init; }
}

public record CloneDateRecordRequest
{
    public required int Ai { get; init; }
    /// <summary>"day" (+1 day), "week" (+7 days), or "round" (same date, rnd+1).</summary>
    public required string CloneType { get; init; }
}

public record CloneFieldDowRequest
{
    public required int Ai { get; init; }
}

public record DeleteAgegroupTimeslotsRequest
{
    public required Guid AgegroupId { get; init; }
}
