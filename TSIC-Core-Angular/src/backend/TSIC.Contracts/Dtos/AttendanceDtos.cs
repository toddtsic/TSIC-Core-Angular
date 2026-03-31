namespace TSIC.Contracts.Dtos;

// ══════════════════════════════════════════════════════════════════════
// Attendance Events
// ══════════════════════════════════════════════════════════════════════

public record AttendanceEventDto
{
    public required int EventId { get; init; }
    public required Guid TeamId { get; init; }
    public string? Comment { get; init; }
    public required int EventTypeId { get; init; }
    public string? EventType { get; init; }
    public required DateTime EventDate { get; init; }
    public string? EventLocation { get; init; }
    public int Present { get; init; }
    public int NotPresent { get; init; }
    public int Unknown { get; init; }
    public string? CreatorUserId { get; init; }
}

public record CreateAttendanceEventRequest
{
    public required string Comment { get; init; }
    public required int EventTypeId { get; init; }
    public required DateTime EventDate { get; init; }
    public required string EventLocation { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Attendance Records (per-player RSVP)
// ══════════════════════════════════════════════════════════════════════

public record AttendanceRosterDto
{
    public required int AttendanceId { get; init; }
    public required string PlayerId { get; init; }
    public string? PlayerFirstName { get; init; }
    public string? PlayerLastName { get; init; }
    public required bool Present { get; init; }
    public string? UniformNo { get; init; }
    public string? HeadshotUrl { get; init; }
}

public record UpdateRsvpRequest
{
    public required string PlayerId { get; init; }
    public required bool Present { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Attendance History (per-player)
// ══════════════════════════════════════════════════════════════════════

public record AttendanceHistoryDto
{
    public required DateTime EventDate { get; init; }
    public string? EventType { get; init; }
    public required bool Present { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Event Type Options
// ══════════════════════════════════════════════════════════════════════

public record AttendanceEventTypeDto
{
    public required int Id { get; init; }
    public required string AttendanceType { get; init; }
}
