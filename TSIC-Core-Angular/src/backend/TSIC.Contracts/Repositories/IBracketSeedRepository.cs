using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IBracketSeedRepository
{
    /// <summary>
    /// Get all non-round-robin (bracket) games for the job with their bracket seed data.
    /// Includes left join to BracketSeeds + division names.
    /// </summary>
    Task<List<BracketSeedGameDto>> GetBracketGamesAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get the BracketSeeds record for a game (tracked, for update).
    /// </summary>
    Task<BracketSeeds?> GetByGidTrackedAsync(
        int gid, CancellationToken ct = default);

    /// <summary>
    /// Get all BracketSeeds GIDs for a job (for orphan cleanup).
    /// </summary>
    Task<List<BracketSeeds>> GetAllForJobAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Create a new BracketSeeds record.
    /// </summary>
    Task AddAsync(BracketSeeds entity, CancellationToken ct = default);

    /// <summary>
    /// Remove orphaned BracketSeeds records.
    /// </summary>
    void RemoveRange(IEnumerable<BracketSeeds> entities);

    /// <summary>
    /// Get divisions in the same agegroup as a game (for seed assignment dropdown).
    /// Excludes "Unassigned" division.
    /// </summary>
    Task<List<BracketSeedDivisionOptionDto>> GetDivisionsForGameAsync(
        int gid, CancellationToken ct = default);

    /// <summary>
    /// Reseed mode: every round-robin pool in the job (divisions with "T" games), across
    /// agegroups, labeled with the owning agegroup. These are the valid cross-agegroup seed sources.
    /// </summary>
    Task<List<BracketSeedDivisionOptionDto>> GetSeedSourceDivisionsForJobAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>Active team count in a pool — the valid seed-rank ceiling (reseed rank list bound).</summary>
    Task<int> GetActiveTeamCountByDivAsync(Guid divId, CancellationToken ct = default);

    /// <summary>
    /// Get the Schedule record (tracked) for updating T1Name/T2Name after seed assignment.
    /// </summary>
    Task<Schedule?> GetScheduleTrackedAsync(
        int gid, CancellationToken ct = default);

    /// <summary>
    /// Get division name by ID (for T1Name/T2Name annotation).
    /// </summary>
    Task<string?> GetDivisionNameAsync(
        Guid divId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
