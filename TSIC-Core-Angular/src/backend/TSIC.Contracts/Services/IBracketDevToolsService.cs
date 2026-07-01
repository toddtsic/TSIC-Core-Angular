using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Dev/sandbox-only helpers to exercise the bracket pipeline against real
/// tournament data: wipe a division back to "pools scheduled, brackets empty",
/// auto-score its pool games (to watch seeding fill), and auto-score its ready
/// bracket games (to watch winners advance). Scoring routes through the real
/// score path so seeding (R1) and advancement (R2/R3) run exactly as in prod.
///
/// The controller gates every call on <c>IsSandbox()</c> — never reachable in
/// live Production.
/// </summary>
public interface IBracketDevToolsService
{
    /// <summary>
    /// Clear scores on every game in the division and blank bracket participants,
    /// so the next pool auto-score visibly re-seeds and re-advances. Pool teams are
    /// left intact (they're fixed); only bracket occupants and all scores are reset.
    /// </summary>
    Task<BracketDevActionResult> ClearDivisionScoresAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Give every unscored round-robin game in the division a decisive score, via the
    /// real score path — completing pools locks standings and resolves bracket seeds.
    /// </summary>
    Task<BracketDevActionResult> AutoScorePoolAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Give every currently-ready bracket game (both participants seeded, no score) a
    /// decisive score, via the real score path — advancing winners into the next round.
    /// One call clears exactly one round; call again to advance further.
    /// </summary>
    Task<BracketDevActionResult> AutoScoreBracketRoundAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default);
}
