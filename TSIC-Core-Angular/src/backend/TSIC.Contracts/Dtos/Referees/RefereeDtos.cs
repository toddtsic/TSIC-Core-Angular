namespace TSIC.Contracts.Dtos.Referees;

// ── Referee Summary ──

/// <summary>
/// Summary of a referee registration for the ref assignment dropdown.
/// </summary>
public record RefereeSummaryDto
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string? Email { get; init; }
    public required string? Cellphone { get; init; }
    public required string? CertificationNumber { get; init; }
    public required DateTime? CertificationExpiry { get; init; }
    public required bool IsActive { get; init; }
}

// ── Game Assignment (lightweight mapping) ──

/// <summary>
/// A single ref-to-game assignment. Used to send the full assignment map to the client.
/// </summary>
public record GameRefAssignmentDto
{
    public required int Gid { get; init; }
    public required Guid RefRegistrationId { get; init; }
}

// ── Schedule Game (for the assignment grid) ──

/// <summary>
/// A game with its assigned referees, agegroup color, field info, and team names.
/// Used to render the venue × timeslot assignment matrix.
/// </summary>
public record RefScheduleGameDto
{
    public required int Gid { get; init; }
    public required DateTime GameDate { get; init; }
    public required string? FieldName { get; init; }
    public required Guid? FieldId { get; init; }
    public required string? AgegroupName { get; init; }
    public required string? AgegroupColor { get; init; }
    public required string? DivName { get; init; }
    public required string? T1Name { get; init; }
    public required string? T2Name { get; init; }
    public required string? GameType { get; init; }
    public required List<Guid> AssignedRefIds { get; init; }
}

// ── Game Ref Details (info modal) ──

/// <summary>
/// A referee's game assignments grouped for the info modal.
/// </summary>
public record RefGameDetailsDto
{
    public required string RefName { get; init; }
    public required Guid RegistrationId { get; init; }
    public required List<RefGameDetailRow> Games { get; init; }
}

/// <summary>
/// A single game row within a ref's game detail list.
/// </summary>
public record RefGameDetailRow
{
    public required int Gid { get; init; }
    public required DateTime GameDate { get; init; }
    public required string FieldName { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string T1Name { get; init; }
    public required string T2Name { get; init; }
}

// ── Calendar Events ──

/// <summary>
/// A referee calendar event — one per ref per game.
/// EndTime is inferred from the next game on the same field, or start + 50 minutes.
/// </summary>
public record RefereeCalendarEventDto
{
    public required int Id { get; init; }
    public required int GameId { get; init; }
    public required string Subject { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required string Location { get; init; }
    public required string Description { get; init; }
    public required Guid? FieldId { get; init; }
    public required string? FieldName { get; init; }
    public required string RefereeId { get; init; }
    public required string RefereeFirstName { get; init; }
    public required string RefereeLastName { get; init; }
    public required string? AgegroupName { get; init; }
    public required string? DivName { get; init; }
    public required string? Team1 { get; init; }
    public required string? Team2 { get; init; }
    public required string Color { get; init; }
    public required string RefsWith { get; init; }
}

// ── Filter Options ──

/// <summary>
/// Available filter options for the referee schedule search.
/// </summary>
public record RefScheduleFilterOptionsDto
{
    public required List<FilterOptionDto> GameDays { get; init; }
    public required List<FilterOptionDto> GameTimes { get; init; }
    public required List<FilterOptionDto> Agegroups { get; init; }
    public required List<FilterOptionDto> Fields { get; init; }
}

/// <summary>
/// Generic value/text pair for dropdown options.
/// </summary>
public record FilterOptionDto
{
    public required string Value { get; init; }
    public required string Text { get; init; }
}

// ── Search / Command Requests ──

/// <summary>
/// Filter criteria for referee schedule search.
/// </summary>
public record RefScheduleSearchRequest
{
    public List<string>? GameDays { get; init; }
    public List<string>? GameTimes { get; init; }
    public List<Guid>? AgegroupIds { get; init; }
    public List<Guid>? FieldIds { get; init; }
}

/// <summary>
/// Assign a list of referees to a game. Replaces all existing assignments for that game.
/// </summary>
public record AssignRefsRequest
{
    public required int Gid { get; init; }
    public required List<Guid> RefRegistrationIds { get; init; }
}

/// <summary>
/// Copy the ref assignments from one game to adjacent timeslots on the same field.
/// </summary>
public record CopyGameRefsRequest
{
    public required int Gid { get; init; }
    public required bool CopyDown { get; init; }
    public required int NumberTimeslots { get; init; }
    public required int SkipInterval { get; init; }
}

/// <summary>
/// Result of a CSV referee import.
/// </summary>
public record ImportRefereesResult
{
    public required int Imported { get; init; }
    public required int Skipped { get; init; }
    public required List<string> Errors { get; init; }
}

/// <summary>
/// Request to seed N test referees for development.
/// </summary>
public record SeedTestRefsRequest
{
    public required int Count { get; init; }
}

