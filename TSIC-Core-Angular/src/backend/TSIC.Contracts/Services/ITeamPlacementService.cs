using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// Centralized service for resolving team placement into agegroups with automatic waitlist overflow.
/// All entry points that create teams (wizard, LADT admin) must call this service.
/// </summary>
public interface ITeamPlacementService
{
    /// <summary>
    /// Resolve the actual agegroup + division for a team placement.
    /// When there is room, returns the target agegroup and its "Unassigned" holding
    /// division (find-or-created), so a placed team is never left without a division.
    /// If the target agegroup is full, finds-or-creates the WAITLIST mirror
    /// agegroup/division and returns those IDs instead.
    /// If skipCapacityCheck is true (admin path), skips the capacity check and leaves
    /// DivisionId null for the caller to assign.
    /// </summary>
    Task<TeamPlacementResult> ResolvePlacementAsync(
        Guid jobId,
        Guid targetAgegroupId,
        string teamName,
        string? divisionName = null,
        string? userId = null,
        bool skipCapacityCheck = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve where a player should be rostered when a team is full.
    /// If BUseWaitlists=true, finds-or-creates WAITLIST agegroup + division + team mirror.
    /// Uses the same WAITLIST agegroup/division as ResolvePlacementAsync (idempotent).
    /// </summary>
    Task<RosterPlacementResult> ResolveRosterPlacementAsync(
        Guid jobId,
        Guid sourceTeamId,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proactively ensure the WAITLIST team mirror exists for a real team that has just
    /// reached its roster max — minted at fill time so the picker can offer the twin the
    /// instant the real team fills. Gated on the job's BUseWaitlists flag (no-op otherwise),
    /// performs no capacity check, and is idempotent: find-or-creates the WAITLIST agegroup +
    /// division + mirror team and its $0 fee stamp. Safe to call from any post-commit fill
    /// path (registration submit, roster swapper).
    /// </summary>
    Task EnsureWaitlistMirrorAsync(
        Guid jobId,
        Guid realTeamId,
        string? userId = null,
        CancellationToken cancellationToken = default);
}
