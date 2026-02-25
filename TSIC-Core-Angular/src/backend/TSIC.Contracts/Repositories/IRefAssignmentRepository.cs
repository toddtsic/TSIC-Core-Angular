using TSIC.Contracts.Dtos.Referees;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for referee game assignment data access.
/// Covers referee roster, game assignments, calendar events, and filter options.
/// </summary>
public interface IRefAssignmentRepository
{
    // ── Referee Roster ──

    /// <summary>
    /// Get all referee registrations for a job, ordered by LastName, FirstName.
    /// </summary>
    Task<List<RefereeSummaryDto>> GetRefereesForJobAsync(Guid jobId, CancellationToken ct = default);

    // ── Assignments ──

    /// <summary>
    /// Get all ref-to-game assignments for a job (lightweight Gid + RegId pairs).
    /// </summary>
    Task<List<GameRefAssignmentDto>> GetAllAssignmentsForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get the RefGameAssigments records for a single game (tracked for mutation).
    /// </summary>
    Task<List<RefGameAssigments>> GetAssignmentsForGameAsync(int gid, CancellationToken ct = default);

    /// <summary>
    /// Delete all existing assignments for a game, insert new ones, update Schedule.RefCount.
    /// </summary>
    Task ReplaceAssignmentsForGameAsync(int gid, List<Guid> refRegistrationIds, string auditUserId, CancellationToken ct = default);

    /// <summary>
    /// Delete all referee assignments for a job.
    /// </summary>
    Task DeleteAllAssignmentsForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Delete all referee registrations (role = Referee) for a job.
    /// </summary>
    Task DeleteAllRefereeRegistrationsForJobAsync(Guid jobId, CancellationToken ct = default);

    // ── Schedule Search (referee-specific) ──

    /// <summary>
    /// Get filter options (distinct game days, times, agegroups, fields) for the referee schedule grid.
    /// </summary>
    Task<RefScheduleFilterOptionsDto> GetRefScheduleFilterOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Search games matching filter criteria, returning each game with its assigned ref IDs.
    /// </summary>
    Task<List<RefScheduleGameDto>> SearchScheduleAsync(Guid jobId, RefScheduleSearchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get detailed ref assignments for a single game — grouped by referee with their game list.
    /// </summary>
    Task<List<RefGameDetailsDto>> GetGameRefDetailsAsync(int gid, Guid jobId, CancellationToken ct = default);

    // ── Calendar ──

    /// <summary>
    /// Get all referee calendar events for a job.
    /// Each ref on each game = one event. EndTime inferred from next game on same field or +50min.
    /// </summary>
    Task<List<RefereeCalendarEventDto>> GetCalendarEventsAsync(Guid jobId, CancellationToken ct = default);

    // ── Copy Support ──

    /// <summary>
    /// Get all games on a specific field for a specific date, ordered by GDate.
    /// Used by copy-refs logic to find adjacent timeslots.
    /// </summary>
    Task<List<Schedule>> GetGamesOnFieldForDateAsync(Guid fieldId, DateTime gameDate, Guid jobId, CancellationToken ct = default);

    // ── Persistence ──

    /// <summary>Persist pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
