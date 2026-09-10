using TSIC.Contracts.Dtos.Teams;

namespace TSIC.Contracts.Services;

/// <summary>
/// THE single chokepoint for a team's SEAT — its <c>Teams.DivRank</c>, which on a round-robin game
/// is not a label but the team's position in the pairing matrix. <c>Schedule.T1_No/T2_No</c> are
/// the matrix slots; <c>T1_ID/T1_Name</c> are a cache re-derived from <c>T*_No → DivRank</c>. So a
/// rank write that does not re-seat leaves the board pointing at the previous occupant, and the
/// only thing that reports it is the schedule QA check — days later, if anyone runs it.
///
/// The sibling of <c>ITeamRenameService</c>, and deliberately not merged with it: a rename reaches
/// every row in the job holding that team, any slot type, including bracket and consolation slots
/// that are not rank-seated; a re-seat reaches only the "T" rows of one division. Neither contains
/// the other. What they DO share is an ordering — rename first, then re-seat, so the seat recompute
/// reads the new name — and that ordering used to live in a comment inside a single caller. Callers
/// here make one call and cannot get it wrong.
///
/// Nothing outside this service calls <c>ITeamRepository.RenumberDivRanksAsync</c>. Renumbering a
/// division is a rank change for every team above the gap, which is why removing one team can
/// silently re-seat a whole pool.
/// </summary>
public interface ITeamSeatingService
{
    /// <summary>
    /// THE rule for taking a team out of a pool: <b>a scheduled team may only leave if something
    /// takes its rank.</b> Deleting, dropping and inactivating all leave the rank empty, so all
    /// three are refused — the rank is a slot in the pairing matrix, and an empty slot means the
    /// games built on it resolve to nobody. The symmetrical pool swap supplies a replacement into
    /// that exact rank, which is why it is the one sanctioned way out (and why it carries the
    /// deactivation and club-rep accounting with it).
    ///
    /// One check, called by every exit. Written as a rule rather than copied into each door
    /// because the door that gets added next is the one that would not have been told.
    /// </summary>
    /// <exception cref="InvalidOperationException">The team holds a seat in this job's schedule.</exception>
    Task EnsureTeamMayLeavePoolAsync(Guid teamId, Guid jobId, string action, CancellationToken ct = default);

    /// <summary>
    /// A director moved a team to a rank: positional swap with whoever holds it, renumber 1..N,
    /// optional rename, then re-seat the division. The two teams trade games — that is the point
    /// of the operation, and the returned result says so in words the director can act on.
    /// </summary>
    Task<TeamSeatingResultDto> ApplyRankChangeAsync(
        Guid teamId, Guid jobId, int newRank, string? newName, string userId,
        CancellationToken ct = default);

    /// <summary>
    /// A division's membership changed (team deactivated, deleted, or dropped): compact ranks to
    /// 1..N and re-seat. Returns games re-seated. Callers that removed a SCHEDULED team are in the
    /// wrong place — that team's games would lose their occupant; use the symmetrical pool swap.
    /// </summary>
    Task<int> RenumberAndReseatAsync(
        Guid divId, Guid jobId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Ranks were already set by the caller and must NOT be renumbered — the symmetrical pool swap,
    /// where each incoming team deliberately inherits the outgoing team's rank so the matrix stays
    /// intact. Re-seats the given divisions so the board follows the teams that now hold those ranks.
    /// </summary>
    Task<int> ReseatDivisionsAsync(
        Guid jobId, IReadOnlyCollection<Guid> divIds, string userId, CancellationToken ct = default);
}
