using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Single source of truth for the unified CADT/LADT filter tree consumed by
/// view-schedule, rescheduler, search-teams, search-registrations, and public-rosters.
/// One query → both trees with rich metadata flags. Per-surface filtering happens
/// client-side via the shared filter component's input flags.
/// </summary>
public interface IJobFilterTreeRepository
{
    /// <summary>
    /// Returns both CADT and LADT trees for the job, with per-team metadata
    /// (IsScheduled, HasClubRep, PlayerCount) and per-agegroup flags
    /// (IsWaitlist, IsDropped) populated. Includes ALL active teams; consumers
    /// filter as needed.
    /// </summary>
    Task<JobFilterTreeDto> GetForJobAsync(Guid jobId, CancellationToken ct = default);
}
