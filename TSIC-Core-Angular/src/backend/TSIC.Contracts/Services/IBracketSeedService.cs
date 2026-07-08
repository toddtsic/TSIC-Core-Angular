using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

public interface IBracketSeedService
{
    /// <summary>
    /// Get all bracket games with seed data plus the job's reseed flag. Creates missing
    /// BracketSeeds records and removes orphans as a side effect.
    /// </summary>
    Task<BracketSeedBoardDto> GetBracketGamesAsync(
        Guid jobId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Update seed division + rank for a bracket game. Also updates
    /// Schedule.T1Name/T2Name to show "(DivName#Rank)" annotation.
    /// </summary>
    Task<BracketSeedGameDto> UpdateSeedAsync(
        UpdateBracketSeedRequest request, string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Seed-source divisions for a game. Same-agegroup by default; in reseed mode
    /// (Jobs.bReseedTournament) the job-wide round-robin pools across agegroups.
    /// </summary>
    Task<List<BracketSeedDivisionOptionDto>> GetDivisionsForGameAsync(
        int gid, Guid jobId, CancellationToken ct = default);

    /// <summary>Valid seed-rank ceiling for a pool = its active team count (reseed rank list bound).</summary>
    Task<int> GetRankCeilingAsync(Guid divId, CancellationToken ct = default);
}
