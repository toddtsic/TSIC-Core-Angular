using TSIC.Domain.Entities;

namespace TSIC.Contracts.Services;

/// <summary>
/// Runtime bracket behavior driven by score entry (R2): reject ties on bracket
/// games, and advance the winner (and loser, for a bronze feed) forward into
/// the next game by following the materialized AdvancementFeeds edges.
/// </summary>
public interface IBracketAdvancementService
{
    /// <summary>
    /// Rejects a tie on a single-elimination bracket game. No-op for
    /// round-robin games or games without both scores.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// If <paramref name="game"/> is a bracket game with equal scores.
    /// </exception>
    void EnsureBracketScoreValid(Schedule game);

    /// <summary>
    /// Writes the decided game's winner/loser into the slots its feeds point at.
    /// No-op unless the game is a bracket game with a decisive score. Returns
    /// the number of downstream slots filled.
    /// </summary>
    Task<int> AdvanceWinnerAsync(int gid, string userId, CancellationToken ct = default);
}
