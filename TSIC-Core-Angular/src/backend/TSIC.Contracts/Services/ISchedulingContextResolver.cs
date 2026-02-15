namespace TSIC.Contracts.Services;

/// <summary>
/// Resolves the primary league, season, and year for a given job context.
/// Shared across all scheduling services to eliminate duplicate resolution logic.
/// </summary>
public interface ISchedulingContextResolver
{
    /// <summary>
    /// Resolves job → primary league → season/year.
    /// </summary>
    Task<(Guid LeagueId, string Season, string Year)> ResolveAsync(
        Guid jobId, CancellationToken ct = default);
}
