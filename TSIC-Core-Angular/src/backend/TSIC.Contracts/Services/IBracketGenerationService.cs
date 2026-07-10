using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Materializes the brackets.* wiring (BracketInstance + SeedAssignments +
/// AdvancementFeeds) for a division from its placed bracket games and the
/// matching single-elimination template.
///
/// Structural only: it records HOW each slot fills (seed vs feed) — it does
/// NOT resolve seeds from standings or advance winners. That is the
/// score-time / standings-lock concern (R1/R2). Idempotent: safe to re-run
/// whenever the set of placed bracket games changes.
/// </summary>
public interface IBracketGenerationService
{
    /// <returns>
    /// A summary of what was written, or null if the division has no bracket
    /// games placed (or no matching template exists yet).
    /// </returns>
    Task<BracketGenerationResult?> RecomputeDivisionAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Materializes bracket wiring for every division in the job that has bracket
    /// games but no BracketInstance yet — data placed before this system existed,
    /// or a division whose PlaceGameAsync recompute never ran. Idempotent,
    /// non-destructive, cheap-skip once done: call it from anywhere that is about
    /// to READ the wiring. Returns the number of divisions materialized.
    /// </summary>
    Task<int> EnsureJobWiringAsync(Guid jobId, string userId, CancellationToken ct = default);
}
