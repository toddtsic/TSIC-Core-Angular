using TSIC.Contracts.Dtos.Referees;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for referee assignment operations: search, assign, copy, import, seed, and calendar.
/// </summary>
public interface IRefAssignmentService
{
    // ── Queries ──

    /// <summary>Get all referees for a job.</summary>
    Task<List<RefereeSummaryDto>> GetRefereesAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Get filter options for the schedule search grid.</summary>
    Task<RefScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Search games matching filter criteria with their assigned ref IDs.</summary>
    Task<List<RefScheduleGameDto>> SearchScheduleAsync(Guid jobId, RefScheduleSearchRequest request, CancellationToken ct = default);

    /// <summary>Get all ref-to-game assignment pairs for a job.</summary>
    Task<List<GameRefAssignmentDto>> GetAllAssignmentsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Get detailed ref assignments for a single game.</summary>
    Task<List<RefGameDetailsDto>> GetGameRefDetailsAsync(int gid, Guid jobId, CancellationToken ct = default);

    /// <summary>Get all referee calendar events for a job.</summary>
    Task<List<RefereeCalendarEventDto>> GetCalendarEventsAsync(Guid jobId, CancellationToken ct = default);

    // ── Commands ──

    /// <summary>Replace all ref assignments for a game.</summary>
    Task AssignRefsToGameAsync(AssignRefsRequest request, string auditUserId, CancellationToken ct = default);

    /// <summary>Copy ref assignments from one game to adjacent timeslots on the same field.</summary>
    Task<List<int>> CopyGameRefsAsync(Guid jobId, CopyGameRefsRequest request, string auditUserId, CancellationToken ct = default);

    /// <summary>Import referees from a CSV file.</summary>
    Task<ImportRefereesResult> ImportRefereesAsync(Guid jobId, Stream csvStream, string auditUserId, CancellationToken ct = default);

    /// <summary>Create N test referee registrations for development.</summary>
    Task<List<RefereeSummaryDto>> SeedTestRefereesAsync(Guid jobId, int count, string auditUserId, CancellationToken ct = default);

    /// <summary>Delete all ref assignments AND referee registrations for a job.</summary>
    Task DeleteAllAsync(Guid jobId, CancellationToken ct = default);

}
