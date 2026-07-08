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
    /// Revert a division to unplayed: clear scores and blank derived bracket
    /// occupants. Because championship games seed cross-pool from the division, this
    /// ALSO resets every bracket game in the division's agegroup (the caveat) — else
    /// teams seeded off the erased standings would be stranded. Pool teams and the
    /// brackets.* wiring are left intact so the next auto-score re-seeds/re-advances.
    /// </summary>
    Task<BracketDevActionResult> ClearDivisionScoresAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Revert a whole agegroup to unplayed — every pool across its divisions plus its
    /// championship games. Same field-level reset as the division scope.
    /// </summary>
    Task<BracketDevActionResult> ClearAgegroupScoresAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Revert an entire job/league to unplayed — every agegroup, pool and bracket.
    /// Same field-level reset as the division scope.
    /// </summary>
    Task<BracketDevActionResult> ClearJobScoresAsync(
        Guid jobId, string userId, CancellationToken ct = default);

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

    /// <summary>
    /// Agegroup-scope pool seed: give every unscored pool game across ALL divisions in
    /// the agegroup a decisive score, via the real score path. This is the correct scope
    /// for testing bracket seeding — championship games seed cross-pool from the agegroup's
    /// divisions, so all their pools must complete before seeds resolve.
    /// </summary>
    Task<BracketDevActionResult> AutoScorePoolAgegroupAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Agegroup-scope bracket-round seed: give every currently-ready bracket game in the
    /// agegroup (both participants seeded, no score) a decisive score, via the real score
    /// path — advancing winners one round. Call again to advance further.
    /// </summary>
    Task<BracketDevActionResult> AutoScoreBracketRoundAgegroupAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default);
}
