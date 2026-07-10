using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Turns seed intent into real teams: for each leaf bracket slot, finds the team
/// at (SeedDivId, SeedRank) in the pool-play standings and writes it onto the
/// schedule row — but only once that pool is complete (rank locked). Idempotent
/// and non-destructive: it never overwrites a bracket game that has already been
/// played (that is the director's to correct by hand).
///
/// Standings are supplied by the caller via a provider delegate rather than a
/// direct dependency, to keep this service free of the standings/schedule service
/// (which itself fires resolution — a direct reference would be a DI cycle).
/// </summary>
public interface IBracketSeedResolutionService
{
    /// <param name="standingsProvider">
    /// Lazily computes pool-play standings, SCOPED to the seed divisions handed to it.
    /// Invoked only when the job has seed slots whose pools are final, so a non-bracket
    /// job — and every score before a pool's last one — pays nothing. Standings rank
    /// within a division, so narrowing the division set cannot change any rank; it just
    /// stops a job with N pools from sweeping all N every time one of them completes.
    /// </param>
    /// <returns>The number of slots filled.</returns>
    Task<int> ResolveJobAsync(
        Guid jobId,
        string userId,
        Func<IReadOnlyCollection<Guid>, CancellationToken, Task<StandingsByDivisionResponse>> standingsProvider,
        CancellationToken ct = default);
}
