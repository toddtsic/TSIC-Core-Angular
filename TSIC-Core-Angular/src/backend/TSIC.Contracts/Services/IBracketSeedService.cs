using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

public interface IBracketSeedService
{
    /// <summary>
    /// Get all bracket games with seed data. Creates missing BracketSeeds records
    /// and removes orphans as a side effect.
    /// </summary>
    Task<List<BracketSeedGameDto>> GetBracketGamesAsync(
        Guid jobId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Update seed division + rank for a bracket game. Also updates
    /// Schedule.T1Name/T2Name to show "(DivName#Rank)" annotation.
    /// </summary>
    Task<BracketSeedGameDto> UpdateSeedAsync(
        UpdateBracketSeedRequest request, string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Get available divisions for seed assignment (dropdown options for a specific game).
    /// </summary>
    Task<List<BracketSeedDivisionOptionDto>> GetDivisionsForGameAsync(
        int gid, CancellationToken ct = default);
}
