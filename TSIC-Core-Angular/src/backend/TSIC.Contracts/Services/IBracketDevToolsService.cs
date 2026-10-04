using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Dev/sandbox-only helpers to exercise the bracket pipeline against real
/// tournament data: wipe an agegroup (or the whole job) back to "pools scheduled,
/// brackets empty", auto-score its pool games (to watch seeding fill), and auto-score
/// its ready bracket games (to watch winners advance). Scoring routes through the real
/// score path so seeding (R1) and advancement (R2/R3) run exactly as in prod.
///
/// The controller gates every call on <c>IsSandbox()</c> — never reachable in
/// live Production.
/// </summary>
public interface IBracketDevToolsService
{
    /// <summary>
    /// Revert a whole agegroup to unplayed — every pool across its divisions plus its
    /// championship games: clear scores and blank derived bracket occupants. Pool teams
    /// and the brackets.* wiring are left intact so the next auto-score re-seeds/re-advances.
    /// </summary>
    Task<BracketDevActionResult> ClearAgegroupScoresAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Revert an entire job/league to unplayed — every agegroup, pool and bracket.
    /// Same field-level reset as the agegroup scope.
    /// </summary>
    Task<BracketDevActionResult> ClearJobScoresAsync(
        Guid jobId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Job-scope pool seed: give every unscored pool game across the WHOLE event a decisive
    /// score, via the real score path. This is the scope reseeding tournaments need — pools
    /// live in their own agegroup and each scored pool game fires job-wide seed resolution,
    /// so once a pool completes its bracket placeholders (in the separate championship
    /// agegroups) are reseeded automatically.
    /// </summary>
    Task<BracketDevActionResult> AutoScorePoolJobAsync(
        Guid jobId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Job-scope bracket-round seed: give every currently-ready bracket game in the event
    /// (both participants seeded, no score) a decisive score, via the real score path —
    /// advancing winners one round across all championship flights. Call again to advance.
    /// </summary>
    Task<BracketDevActionResult> AutoScoreBracketRoundJobAsync(
        Guid jobId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Agegroup-scope pool seed: give every unscored pool game in ONE agegroup a decisive
    /// score, via the real score path. Each score fires job-wide seed resolution, so a
    /// reseeding tournament's separate championship agegroups still reseed automatically.
    /// </summary>
    Task<BracketDevActionResult> AutoScorePoolAgegroupAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Agegroup-scope bracket-round seed: give every currently-ready bracket game in ONE
    /// agegroup (both participants seeded, no score) a decisive score, via the real score
    /// path — advancing winners one round. Call again to advance further.
    /// </summary>
    Task<BracketDevActionResult> AutoScoreBracketRoundAgegroupAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default);
}
